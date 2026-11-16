package com.glowup.input;

import android.app.Activity;
import android.view.KeyEvent;
import android.view.Window;

import java.lang.reflect.InvocationHandler;
import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Method;
import java.lang.reflect.Proxy;

/**
 * VOLUME BUTTONS AS A SHUTTER (phase 2A, 11 November 2026).
 *
 * Unity does not pass the phone's volume keys to the app: Android changes the volume with them. While the AI Smart Scan screen asks
 * for them (Settings > Camera Helpers > "Volume button does:" is not "Do nothing"), this class takes a press of volume up or volume
 * down for the app instead, and the scan screen reads how many presses came (takePresses) once per frame.
 *
 * How: the activity's window passes every key to its Window.Callback. A stand-in callback (a java.lang.reflect.Proxy of the same
 * interface) is put in front of the original once; it hands every call to the original, except volume keys while active. When the
 * scan screen is left (or the setting is "Do nothing") active is false and the volume keys change the volume again.
 * Used by Assets/_Project/Scripts/Common/VolumeKeyInput.cs. Nothing is recorded or sent anywhere.
 */
public final class GlowUpVolumeKeys {

    private static volatile boolean active;
    private static int upPresses, downPresses;
    private static boolean installed;

    private GlowUpVolumeKeys() { }

    /** On = volume keys are taken by the app; off = they change the volume as usual. Installs the stand-in callback the first time. */
    public static void setActive(final Activity activity, boolean on) {
        active = on;
        if (!on) { synchronized (GlowUpVolumeKeys.class) { upPresses = 0; downPresses = 0; } return; }
        if (installed || activity == null) return;
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() { install(activity); }
        });
    }

    /** The volume-up presses since the last call (a key held down counts once). */
    public static synchronized int takeUpPresses() {
        int n = upPresses;
        upPresses = 0;
        return n;
    }

    /** The volume-down presses since the last call. */
    public static synchronized int takeDownPresses() {
        int n = downPresses;
        downPresses = 0;
        return n;
    }

    private static synchronized void install(Activity activity) {
        if (installed) return;
        try {
            final Window window = activity.getWindow();
            if (window == null) return;
            final Window.Callback original = window.getCallback();
            if (original == null) return;
            Window.Callback standIn = (Window.Callback) Proxy.newProxyInstance(
                Window.Callback.class.getClassLoader(),
                new Class<?>[] { Window.Callback.class },
                new InvocationHandler() {
                    @Override
                    public Object invoke(Object proxy, Method method, Object[] args) throws Throwable {
                        if (active && "dispatchKeyEvent".equals(method.getName()) && args != null && args.length == 1 && args[0] instanceof KeyEvent) {
                            KeyEvent key = (KeyEvent) args[0];
                            int code = key.getKeyCode();
                            if (code == KeyEvent.KEYCODE_VOLUME_UP || code == KeyEvent.KEYCODE_VOLUME_DOWN) {
                                if (key.getAction() == KeyEvent.ACTION_DOWN && key.getRepeatCount() == 0) {
                                    synchronized (GlowUpVolumeKeys.class) { if (code == KeyEvent.KEYCODE_VOLUME_UP) upPresses++; else downPresses++; }
                                }
                                return Boolean.TRUE;               // taken: the volume does not change, no volume panel
                            }
                        }
                        try {
                            return method.invoke(original, args);
                        } catch (InvocationTargetException e) {
                            throw e.getCause() != null ? e.getCause() : e;
                        }
                    }
                });
            window.setCallback(standIn);
            installed = true;
        } catch (Throwable ignored) {
            // another Android or Unity version: the volume keys simply keep changing the volume
        }
    }
}
