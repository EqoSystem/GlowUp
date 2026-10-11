package com.glowup.screen;

import android.app.Activity;
import android.view.Window;
import android.view.WindowManager;

/**
 * The brightness of this app's own window (the phone's brightness setting is never changed).
 * Used by the camera screens (Assets/_Project/Scripts/Common/ScreenBrightnessBoost.cs).
 * The change is made on the phone's main thread, where Android wants it.
 */
public final class GlowUpScreen {

    private GlowUpScreen() { }

    /**
     * value from 0.05 to 1 = that brightness for this app's window.
     * A negative value = follow the phone's own brightness again (also its automatic brightness).
     * The window is never set to 0: on some phones that switches the screen's light off.
     */
    public static void setBrightness(final Activity activity, final float value) {
        if (activity == null) return;
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    Window window = activity.getWindow();
                    if (window == null) return;
                    WindowManager.LayoutParams attributes = window.getAttributes();
                    float wanted = value < 0f ? WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE : Math.max(0.05f, Math.min(1f, value));
                    if (Math.abs(attributes.screenBrightness - wanted) < 0.004f) return;      // it is so already
                    attributes.screenBrightness = wanted;
                    window.setAttributes(attributes);
                } catch (Throwable ignored) {
                    // a window that is going away: nothing to brighten
                }
            }
        });
    }
}
