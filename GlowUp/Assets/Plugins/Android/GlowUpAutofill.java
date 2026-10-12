package com.glowup.input;

import android.app.Activity;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.view.View;
import android.view.Window;
import android.view.autofill.AutofillManager;
import android.widget.EditText;

import java.lang.reflect.Field;

/**
 * Keeps Android's own "saved accounts" list (autofill) from opening by itself over the app.
 *
 * Why: Unity does not type into the app's own text boxes. It opens the keyboard with a hidden text box of its own,
 * in a separate small window. Android ties its list of saved e-mails and passwords to THAT box, so the list showed
 * up at a place that has nothing to do with the field the user tapped ("out of nowhere"). The app now shows its own
 * list of e-mails under the e-mail field instead (Assets/_Project/Scripts/Auth/SavedAccountsList.cs).
 *
 * Two things are done, and each one alone is enough on most phones:
 *   1. switchOff: every text box that is created later in this app is marked "not for autofill" by default
 *      (a setting added to the app's look, the style GlowUpNoAutofill, and the same mark on the app's window).
 *   2. quiet: when a keyboard opens, Unity's hidden text box is marked directly, and a list that has already
 *      opened is closed.
 *
 * Autofill exists from Android 8 (API 26). On older phones there is no such list and nothing is done.
 * Used by Assets/_Project/Scripts/Common/SystemAutofill.cs.
 */
public final class GlowUpAutofill {

    private static final String STYLE = "GlowUpNoAutofill";      // res/values-v26/styles.xml of GlowUpRes.androidlib
    private static final int[] CHECK_AFTER_MS = { 0, 120, 350, 800 };

    private GlowUpAutofill() { }

    /** Once, when the app starts. Returns a short line for the log. */
    public static String switchOff(final Activity activity) {
        if (activity == null) return "no activity";
        if (Build.VERSION.SDK_INT < 26) return "not needed before Android 8";
        final int style = activity.getResources().getIdentifier(STYLE, "style", activity.getPackageName());
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    if (style != 0) activity.getTheme().applyStyle(style, true);
                    Window window = activity.getWindow();
                    if (window != null) window.getDecorView().setImportantForAutofill(View.IMPORTANT_FOR_AUTOFILL_NO_EXCLUDE_DESCENDANTS);
                } catch (Throwable ignored) {
                    // the second step (quiet) still marks the keyboard's own box
                }
            }
        });
        return style != 0 ? "switched off" : "the style " + STYLE + " is missing: only the keyboard's own box is marked";
    }

    /** When one of the app's input fields is tapped: marks the keyboard's text box as soon as it exists. */
    public static void quiet(final Activity activity) {
        if (activity == null || Build.VERSION.SDK_INT < 26) return;
        Handler handler = new Handler(Looper.getMainLooper());
        for (int delay : CHECK_AFTER_MS) {
            handler.postDelayed(new Runnable() {
                @Override
                public void run() { markKeyboardBox(activity); }
            }, delay);
        }
    }

    private static void markKeyboardBox(Activity activity) {
        try {
            EditText box = keyboardBox(activity);
            if (box == null || box.getImportantForAutofill() == View.IMPORTANT_FOR_AUTOFILL_NO) return;
            box.setImportantForAutofill(View.IMPORTANT_FOR_AUTOFILL_NO);
            AutofillManager manager = activity.getSystemService(AutofillManager.class);
            if (manager != null) manager.cancel();              // a list that opened before the mark is closed
        } catch (Throwable ignored) {
            // another Unity version keeps the box somewhere else: step 1 (switchOff) still applies
        }
    }

    /** Unity's hidden text box of the keyboard that is open now, or null when there is none. */
    private static EditText keyboardBox(Activity activity) throws Exception {
        Object player = valueOfTypeNamed(activity, "com.unity3d.player.UnityPlayer");
        if (player == null) return null;
        Object softInput = valueOfFieldNamed(player, "mSoftInput");
        if (softInput == null) return null;
        for (Class<?> c = softInput.getClass(); c != null && c != Object.class; c = c.getSuperclass())
            for (Field field : c.getDeclaredFields()) {
                if (!EditText.class.isAssignableFrom(field.getType())) continue;
                field.setAccessible(true);
                Object value = field.get(softInput);
                if (value != null) return (EditText) value;
            }
        return null;
    }

    private static Object valueOfFieldNamed(Object owner, String name) throws Exception {
        for (Class<?> c = owner.getClass(); c != null && c != Object.class; c = c.getSuperclass())
            for (Field field : c.getDeclaredFields()) {
                if (!field.getName().equals(name)) continue;
                field.setAccessible(true);
                return field.get(owner);
            }
        return null;
    }

    /** The value of the first field of `owner` whose type has this name (the class is not on the compile path). */
    private static Object valueOfTypeNamed(Object owner, String typeName) throws Exception {
        for (Class<?> c = owner.getClass(); c != null && c != Object.class; c = c.getSuperclass())
            for (Field field : c.getDeclaredFields()) {
                boolean match = false;
                for (Class<?> t = field.getType(); t != null; t = t.getSuperclass()) if (t.getName().equals(typeName)) { match = true; break; }
                if (!match) continue;
                field.setAccessible(true);
                Object value = field.get(java.lang.reflect.Modifier.isStatic(field.getModifiers()) ? null : owner);
                if (value != null) return value;
            }
        return null;
    }
}
