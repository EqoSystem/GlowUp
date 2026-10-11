package com.glowup.ocr;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;

import com.google.mlkit.vision.barcode.BarcodeScanner;
import com.google.mlkit.vision.barcode.BarcodeScannerOptions;
import com.google.mlkit.vision.barcode.BarcodeScanning;
import com.google.mlkit.vision.barcode.common.Barcode;
import com.google.mlkit.vision.common.InputImage;

/**
 * Reads the barcode in a picture on the phone itself (no internet), with Google's ML Kit barcode scanning.
 * Used by the scan screen (Assets/_Project/Scripts/Scan/MlKitBarcodeReader.cs) to find a product in the
 * inventory by the number under its barcode. Only shop barcodes are read (EAN-13, EAN-8, UPC-A, UPC-E).
 * The library comes from the line "com.google.mlkit:barcode-scanning" in Assets/Plugins/Android/mainTemplate.gradle.
 * It is a class of its own so that the text reader (GlowUpTextReader) keeps working in a build without this library.
 */
public final class GlowUpBarcodeReader {

    /** Told when a picture has been looked at. Called on the phone's main thread. */
    public interface Callback {
        /** code = the digits under the barcode, or "" when the picture shows no barcode. */
        void onResult(String code);
        void onError(String message);
    }

    private static BarcodeScanner scanner;

    private GlowUpBarcodeReader() { }

    /** True when the barcode library is part of this build. */
    public static boolean isAvailable() {
        try {
            Class.forName("com.google.mlkit.vision.barcode.BarcodeScanning");
            return true;
        } catch (Throwable t) {
            return false;
        }
    }

    /** Looks for a barcode in an upright picture given as JPG or PNG bytes. The answer goes to the callback. */
    public static void read(byte[] picture, final Callback callback) {
        try {
            final Bitmap bitmap = BitmapFactory.decodeByteArray(picture, 0, picture.length);
            if (bitmap == null) {
                callback.onError("The picture could not be opened.");
                return;
            }
            if (scanner == null) {
                scanner = BarcodeScanning.getClient(new BarcodeScannerOptions.Builder()
                        .setBarcodeFormats(Barcode.FORMAT_EAN_13, Barcode.FORMAT_EAN_8, Barcode.FORMAT_UPC_A, Barcode.FORMAT_UPC_E)
                        .build());
            }
            scanner.process(InputImage.fromBitmap(bitmap, 0))
                    .addOnSuccessListener(codes -> {
                        bitmap.recycle();
                        String found = "";
                        for (Barcode code : codes) {
                            String value = code.getRawValue();
                            if (value != null && value.length() >= 8) { found = value; break; }
                        }
                        callback.onResult(found);
                    })
                    .addOnFailureListener(e -> {
                        bitmap.recycle();
                        callback.onError(String.valueOf(e.getMessage()));
                    });
        } catch (Throwable t) {
            callback.onError(String.valueOf(t));
        }
    }
}
