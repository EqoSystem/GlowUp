package com.glowup.photo;

import android.app.Activity;
import android.app.Fragment;
import android.content.Intent;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Matrix;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.MediaStore;
import android.util.Base64;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;

/**
 * Profile picture (New22, 20 November 2026): opens the phone's own photo picker and answers with ONE small square JPEG.
 *
 * Privacy: the system picker (Android 13+: the Photo Picker; older: the document picker) hands the app only the photo the person
 * chose, so the app needs NO storage or media permission. The app asks the person first in its own words (ProfilePhoto.cs).
 * The photo is read once, turned upright (EXIF), cropped to a centred square, scaled to at most `maxSide` pixels and returned as
 * Base64 JPEG text. Nothing else is read and nothing is kept here.
 *
 * Used by Assets/_Project/Scripts/Settings/ProfilePhoto.cs through an AndroidJavaProxy for Callback. The answer arrives on a
 * background thread; the C# side moves it to Unity's main thread.
 */
public final class GlowUpPhotoPicker {

    public interface Callback {
        void onPicked(String base64Jpeg);
        void onFailed(String reason);          // "cancelled", "no-picker", "unreadable"
    }

    private static final int REQUEST = 47111;
    private static final String TAG = "glowup_photo_picker";
    private static Callback pending;
    private static int pendingSide = 256;

    private GlowUpPhotoPicker() { }

    public static void pick(final Activity activity, final int maxSide, final Callback callback) {
        if (activity == null || callback == null) return;
        pending = callback;
        pendingSide = Math.max(64, Math.min(1024, maxSide));
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                try {
                    android.app.FragmentManager fm = activity.getFragmentManager();
                    Fragment old = fm.findFragmentByTag(TAG);
                    if (old != null) fm.beginTransaction().remove(old).commitAllowingStateLoss();
                    fm.beginTransaction().add(new PickerFragment(), TAG).commitAllowingStateLoss();
                } catch (Exception e) { answer(null, "no-picker"); }
            }
        });
    }

    private static void answer(String base64, String reason) {
        Callback cb = pending;
        pending = null;
        if (cb == null) return;
        if (base64 != null) cb.onPicked(base64); else cb.onFailed(reason == null ? "unreadable" : reason);
    }

    /** A view-less fragment that starts the picker and receives its answer. */
    public static final class PickerFragment extends Fragment {
        public PickerFragment() { }

        @Override public void onCreate(Bundle state) {
            super.onCreate(state);
            if (state != null) return;                        // recreated after the picker: the answer still comes to onActivityResult
            Intent intent;
            if (Build.VERSION.SDK_INT >= 33) intent = new Intent(MediaStore.ACTION_PICK_IMAGES);
            else { intent = new Intent(Intent.ACTION_GET_CONTENT); intent.addCategory(Intent.CATEGORY_OPENABLE); }
            intent.setType("image/*");
            try { startActivityForResult(intent, REQUEST); }
            catch (Exception e) { close(); answer(null, "no-picker"); }
        }

        @Override public void onActivityResult(int requestCode, int resultCode, Intent data) {
            if (requestCode != REQUEST) return;
            final Activity activity = getActivity();
            close();
            if (resultCode != Activity.RESULT_OK || data == null || data.getData() == null || activity == null) { answer(null, "cancelled"); return; }
            final Uri uri = data.getData();
            final int side = pendingSide;
            new Thread(new Runnable() {
                @Override public void run() {
                    try { answer(encode(activity, uri, side), null); }
                    catch (Throwable t) { answer(null, "unreadable"); }
                }
            }).start();
        }

        private void close() {
            try { getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss(); } catch (Exception ignored) { }
        }
    }

    static String encode(Activity activity, Uri uri, int side) throws Exception {
        BitmapFactory.Options bounds = new BitmapFactory.Options();
        bounds.inJustDecodeBounds = true;
        try (InputStream in = activity.getContentResolver().openInputStream(uri)) { BitmapFactory.decodeStream(in, null, bounds); }
        if (bounds.outWidth <= 0 || bounds.outHeight <= 0) throw new IllegalArgumentException("not an image");
        int sample = 1;
        while (Math.min(bounds.outWidth, bounds.outHeight) / (sample * 2) >= side) sample *= 2;
        BitmapFactory.Options opts = new BitmapFactory.Options();
        opts.inSampleSize = sample;
        Bitmap bmp;
        try (InputStream in = activity.getContentResolver().openInputStream(uri)) { bmp = BitmapFactory.decodeStream(in, null, opts); }
        if (bmp == null) throw new IllegalArgumentException("not an image");

        int degrees = 0;
        if (Build.VERSION.SDK_INT >= 24) {
            try (InputStream in = activity.getContentResolver().openInputStream(uri)) {
                android.media.ExifInterface exif = new android.media.ExifInterface(in);
                int o = exif.getAttributeInt(android.media.ExifInterface.TAG_ORIENTATION, android.media.ExifInterface.ORIENTATION_NORMAL);
                degrees = o == android.media.ExifInterface.ORIENTATION_ROTATE_90 ? 90 : o == android.media.ExifInterface.ORIENTATION_ROTATE_180 ? 180 : o == android.media.ExifInterface.ORIENTATION_ROTATE_270 ? 270 : 0;
            } catch (Exception ignored) { }
        }
        int crop = Math.min(bmp.getWidth(), bmp.getHeight());
        int x = (bmp.getWidth() - crop) / 2, y = (bmp.getHeight() - crop) / 2;
        Matrix m = new Matrix();
        float scale = Math.min(1f, (float) side / crop);
        m.postScale(scale, scale);
        if (degrees != 0) m.postRotate(degrees);
        Bitmap square = Bitmap.createBitmap(bmp, x, y, crop, crop, m, true);
        if (square != bmp) bmp.recycle();
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        square.compress(Bitmap.CompressFormat.JPEG, 82, out);
        square.recycle();
        return Base64.encodeToString(out.toByteArray(), Base64.NO_WRAP);
    }
}
