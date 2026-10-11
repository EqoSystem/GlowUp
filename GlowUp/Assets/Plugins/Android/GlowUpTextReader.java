package com.glowup.ocr;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Rect;

import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.text.Text;
import com.google.mlkit.vision.text.TextRecognition;
import com.google.mlkit.vision.text.TextRecognizer;
import com.google.mlkit.vision.text.latin.TextRecognizerOptions;

/**
 * Reads the text in a picture on the phone itself (no internet), with Google's ML Kit text recognition.
 * Used by the scan screen (Assets/_Project/Scripts/Scan/MlKitTextReader.cs). It works on 32-bit and
 * 64-bit phones; the library comes from the line "com.google.mlkit:text-recognition" in
 * Assets/Plugins/Android/mainTemplate.gradle.
 */
public final class GlowUpTextReader {

    /** Told when a picture has been read. Called on the phone's main thread. */
    public interface Callback {
        /**
         * text = everything that was read. boxes = where each line of text is in the picture, as
         * "left,top,right,bottom;left,top,right,bottom;..." with every number from 0 to 1
         * (0,0 = the picture's top-left corner). boxes is "" when nothing was read.
         */
        void onResult(String text, String boxes);
        void onError(String message);
    }

    private static TextRecognizer recognizer;

    private GlowUpTextReader() { }

    /** True when the text recognition library is part of this build. */
    public static boolean isAvailable() {
        try {
            Class.forName("com.google.mlkit.vision.text.TextRecognition");
            return true;
        } catch (Throwable t) {
            return false;
        }
    }

    /** Reads an upright picture given as JPG or PNG bytes. The answer goes to the callback. */
    public static void read(byte[] picture, final Callback callback) {
        try {
            final Bitmap bitmap = BitmapFactory.decodeByteArray(picture, 0, picture.length);
            if (bitmap == null) {
                callback.onError("The picture could not be opened.");
                return;
            }
            final float width = Math.max(1, bitmap.getWidth());
            final float height = Math.max(1, bitmap.getHeight());
            if (recognizer == null) {
                recognizer = TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS);
            }
            recognizer.process(InputImage.fromBitmap(bitmap, 0))
                    .addOnSuccessListener(text -> {
                        bitmap.recycle();
                        callback.onResult(text.getText(), boxesOf(text, width, height));
                    })
                    .addOnFailureListener(e -> {
                        bitmap.recycle();
                        callback.onError(String.valueOf(e.getMessage()));
                    });
        } catch (Throwable t) {
            callback.onError(String.valueOf(t));
        }
    }

    /** Where the lines of text are, written as numbers from 0 to 1 (at most 40 lines). */
    private static String boxesOf(Text text, float width, float height) {
        StringBuilder out = new StringBuilder();
        int count = 0;
        try {
            for (Text.TextBlock block : text.getTextBlocks()) {
                for (Text.Line line : block.getLines()) {
                    Rect box = line.getBoundingBox();
                    if (box == null || count >= 40) continue;
                    if (out.length() > 0) out.append(';');
                    out.append(box.left / width).append(',').append(box.top / height).append(',')
                       .append(box.right / width).append(',').append(box.bottom / height);
                    count++;
                }
            }
        } catch (Throwable t) {
            return "";
        }
        return out.toString();
    }
}
