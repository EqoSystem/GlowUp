package com.glowup.ocr;

import android.graphics.Bitmap;
import android.graphics.Rect;

import com.google.android.gms.tasks.Tasks;
import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.text.Text;
import com.google.mlkit.vision.text.TextRecognition;
import com.google.mlkit.vision.text.TextRecognizer;
import com.google.mlkit.vision.text.latin.TextRecognizerOptions;

import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;

/**
 * Reads ONE camera picture for hold-to-scan: the text in it (line by line, with how sure the reader is of each
 * line and where it is) and, when asked, the barcode. Everything happens on the phone (Google ML Kit, no internet)
 * and on a thread of its own, never on the thread that draws the app.
 *
 * While the scan button is held the app offers several pictures a second. Only one is read at a time: while the
 * reader is busy, isBusy() says so and the app keeps the newest picture for the next turn ("always the latest
 * frame"), so pictures never queue up.
 *
 * The picture comes as plain numbers (one ARGB number per pixel, row by row from the top), not as a JPG: nothing
 * has to be packed and unpacked for every picture. It is not saved and not sent anywhere.
 *
 * Used by Assets/_Project/Scripts/Scan/FrameReader.cs. The libraries are the lines "com.google.mlkit:text-recognition"
 * and "com.google.mlkit:barcode-scanning" in Assets/Plugins/Android/mainTemplate.gradle.
 */
public final class GlowUpFrameReader {

    /** Told when a picture has been read. NOT called on the phone's main thread. */
    public interface Callback {
        /**
         * lines   = one row per line of text, the parts separated by tabs:
         *           confidence (0 to 1), left, top, right, bottom (each 0 to 1 of the picture, from its top-left corner), text.
         *           "" when no text was read.
         * barcode = the digits under a barcode in the picture, or "". When its place in the picture is known, the digits
         *           are followed by a tab and left, top, right, bottom (each 0 to 1, separated by tabs).
         */
        void onResult(String lines, String barcode);
        void onError(String message);
    }

    private static final int MOST_LINES = 80;
    private static TextRecognizer recognizer;
    private static ExecutorService worker;
    private static volatile boolean busy;

    private GlowUpFrameReader() { }

    /** True when the text recognition library is part of this build. */
    public static boolean isAvailable() {
        try {
            Class.forName("com.google.mlkit.vision.text.TextRecognition");
            return true;
        } catch (Throwable t) {
            return false;
        }
    }

    /** True when the barcode library is part of this build. */
    public static boolean hasBarcodes() {
        try {
            Class.forName("com.google.mlkit.vision.barcode.BarcodeScanning");
            return true;
        } catch (Throwable t) {
            return false;
        }
    }

    /** True while a picture is still being read (the next one would only wait). */
    public static boolean isBusy() {
        return busy;
    }

    /**
     * Reads an upright picture. pixels = the picture row by row from the top, each pixel as one ARGB number.
     * wantBarcode = also look for a barcode. Returns false when the reader is still busy with the picture before
     * (then nothing happens and the callback is not called).
     */
    public static synchronized boolean read(final int[] pixels, final int width, final int height, final boolean wantBarcode, final Callback callback) {
        if (busy || callback == null || pixels == null || width <= 0 || height <= 0 || pixels.length < width * height) return false;
        if (worker == null) worker = Executors.newSingleThreadExecutor();
        busy = true;
        worker.execute(new Runnable() {
            @Override
            public void run() {
                Bitmap bitmap = null;
                try {
                    bitmap = Bitmap.createBitmap(pixels, width, height, Bitmap.Config.ARGB_8888);
                    InputImage image = InputImage.fromBitmap(bitmap, 0);
                    if (recognizer == null) recognizer = TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS);
                    Text text = Tasks.await(recognizer.process(image), 8, TimeUnit.SECONDS);
                    String barcode = "";
                    if (wantBarcode) {
                        try { barcode = Barcodes.read(image, width, height); } catch (Throwable ignored) { }      // a build without the barcode library still reads text
                    }
                    callback.onResult(linesOf(text, width, height), barcode);
                } catch (Throwable t) {
                    try { callback.onError(String.valueOf(t.getMessage() != null ? t.getMessage() : t)); } catch (Throwable ignored) { }
                } finally {
                    if (bitmap != null) bitmap.recycle();
                    busy = false;
                }
            }
        });
        return true;
    }

    /** The lines of text as rows of tab-separated parts (see Callback.onResult). */
    private static String linesOf(Text text, float width, float height) {
        StringBuilder out = new StringBuilder(2048);
        int count = 0;
        for (Text.TextBlock block : text.getTextBlocks()) {
            for (Text.Line line : block.getLines()) {
                if (count >= MOST_LINES) return out.toString();
                String words = line.getText();
                if (words == null) continue;
                words = words.replace('\t', ' ').replace('\n', ' ').replace('\r', ' ').trim();
                if (words.length() == 0) continue;
                Rect box = line.getBoundingBox();
                float confidence;
                try { confidence = line.getConfidence(); } catch (Throwable t) { confidence = 1f; }
                if (out.length() > 0) out.append('\n');
                out.append(confidence).append('\t');
                if (box != null) {
                    out.append(box.left / width).append('\t').append(box.top / height).append('\t')
                       .append(box.right / width).append('\t').append(box.bottom / height).append('\t');
                } else {
                    out.append("0\t0\t0\t0\t");
                }
                out.append(words);
                count++;
            }
        }
        return out.toString();
    }

    /** Kept apart so that this reader also loads in a build without the barcode library. */
    private static final class Barcodes {
        private static com.google.mlkit.vision.barcode.BarcodeScanner scanner;

        static String read(InputImage image, float width, float height) throws Exception {
            if (scanner == null) {
                scanner = com.google.mlkit.vision.barcode.BarcodeScanning.getClient(
                        new com.google.mlkit.vision.barcode.BarcodeScannerOptions.Builder()
                                .setBarcodeFormats(
                                        com.google.mlkit.vision.barcode.common.Barcode.FORMAT_EAN_13,
                                        com.google.mlkit.vision.barcode.common.Barcode.FORMAT_EAN_8,
                                        com.google.mlkit.vision.barcode.common.Barcode.FORMAT_UPC_A,
                                        com.google.mlkit.vision.barcode.common.Barcode.FORMAT_UPC_E)
                                .build());
            }
            List<com.google.mlkit.vision.barcode.common.Barcode> codes = Tasks.await(scanner.process(image), 4, TimeUnit.SECONDS);
            for (com.google.mlkit.vision.barcode.common.Barcode code : codes) {
                String value = code.getRawValue();
                if (value == null || value.length() < 8) continue;
                Rect box = code.getBoundingBox();
                if (box == null || width <= 0 || height <= 0) return value;
                return value + '\t' + (box.left / width) + '\t' + (box.top / height) + '\t' + (box.right / width) + '\t' + (box.bottom / height);
            }
            return "";
        }
    }
}
