package com.glowup.files;

import android.app.Activity;
import android.app.Fragment;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;
import android.util.Base64;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;

/**
 * "Import file" on the staff scanner (24 Nov 2026): the phone's own document picker (no storage permission: only the chosen file is handed over).
 * The file is read once (at most 12 MB) and returned as its name plus Base64 text; nothing is kept here. The C# side (ProductFileImport)
 * reads Word (.docx), Excel (.xlsx), PDF (text), JSON, CSV and TXT. The answer arrives on a background thread.
 */
public final class GlowUpFilePicker {

    public interface Callback {
        void onFile(String name, String base64);
        void onFailed(String reason);          // "cancelled", "no-picker", "unreadable", "too-big"
    }

    private static final int REQUEST = 47131;
    private static final String TAG = "glowup_file_picker";
    private static final int MAX_BYTES = 12 * 1024 * 1024;
    private static Callback pending;

    private GlowUpFilePicker() { }

    public static void pick(final Activity activity, final Callback callback) {
        if (activity == null || callback == null) return;
        pending = callback;
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                try {
                    android.app.FragmentManager fm = activity.getFragmentManager();
                    Fragment old = fm.findFragmentByTag(TAG);
                    if (old != null) fm.beginTransaction().remove(old).commitAllowingStateLoss();
                    fm.beginTransaction().add(new PickerFragment(), TAG).commitAllowingStateLoss();
                } catch (Exception e) { fail("no-picker"); }
            }
        });
    }

    private static void fail(String reason) {
        Callback cb = pending;
        pending = null;
        if (cb != null) cb.onFailed(reason);
    }

    public static final class PickerFragment extends Fragment {
        public PickerFragment() { }

        @Override public void onCreate(Bundle state) {
            super.onCreate(state);
            if (state != null) return;
            Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
            intent.addCategory(Intent.CATEGORY_OPENABLE);
            intent.setType("*/*");
            intent.putExtra(Intent.EXTRA_MIME_TYPES, new String[] {
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "application/pdf", "application/json", "text/csv", "text/comma-separated-values", "text/plain", "application/csv",
                "application/msword", "application/vnd.ms-excel", "application/octet-stream" });
            try { startActivityForResult(intent, REQUEST); }
            catch (Exception e) { close(); fail("no-picker"); }
        }

        @Override public void onActivityResult(int requestCode, int resultCode, Intent data) {
            super.onActivityResult(requestCode, resultCode, data);
            if (requestCode != REQUEST) return;
            final Uri uri = resultCode == Activity.RESULT_OK && data != null ? data.getData() : null;
            final Activity activity = getActivity();
            close();
            if (uri == null || activity == null) { fail("cancelled"); return; }
            new Thread(new Runnable() {
                @Override public void run() {
                    try {
                        String name = "file";
                        Cursor c = activity.getContentResolver().query(uri, null, null, null, null);
                        if (c != null) { try { if (c.moveToFirst()) { int i = c.getColumnIndex(OpenableColumns.DISPLAY_NAME); if (i >= 0) name = c.getString(i); } } finally { c.close(); } }
                        InputStream in = activity.getContentResolver().openInputStream(uri);
                        if (in == null) { fail("unreadable"); return; }
                        ByteArrayOutputStream out = new ByteArrayOutputStream();
                        byte[] buf = new byte[16384];
                        int n, total = 0;
                        try {
                            while ((n = in.read(buf)) > 0) { total += n; if (total > MAX_BYTES) { fail("too-big"); return; } out.write(buf, 0, n); }
                        } finally { in.close(); }
                        Callback cb = pending;
                        pending = null;
                        if (cb != null) cb.onFile(name, Base64.encodeToString(out.toByteArray(), Base64.NO_WRAP));
                    } catch (Throwable t) { fail("unreadable"); }
                }
            }).start();
        }

        private void close() {
            try { getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss(); } catch (Exception e) { }
        }
    }
}
