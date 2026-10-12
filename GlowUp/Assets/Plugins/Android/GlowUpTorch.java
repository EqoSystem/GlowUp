package com.glowup.ocr;

import android.app.Activity;
import android.content.Context;
import android.hardware.camera2.CameraCaptureSession;
import android.hardware.camera2.CameraCharacteristics;
import android.hardware.camera2.CameraManager;
import android.hardware.camera2.CaptureRequest;
import android.os.Handler;

import java.lang.reflect.Field;

/**
 * The flashlight WHILE THE CAMERA IS ON.
 *
 * Android does not let an app switch the flashlight on through the phone's flashlight service while that app's own
 * camera is open: the light belongs to the camera then. The right way is to ask the open camera itself to keep its
 * light on ("torch"). Unity opens the camera for the app and does not offer that, so this class reaches the camera
 * session Unity opened and adds the torch to the request that is already running. No extra permission is needed:
 * the camera permission the app has covers its light.
 *
 * It looks the session up by the TYPE of Unity's fields, not by their names (the names are scrambled and change
 * between Unity versions). If Unity ever keeps its camera differently, set() answers with the reason and the app
 * tells the user, exactly as before this class existed.
 *
 * Used by Assets/_Project/Scripts/Common/Flashlight.cs.
 */
public final class GlowUpTorch {

    private GlowUpTorch() { }

    /** True when the phone has a flashlight next to a back camera. */
    public static boolean hasFlash(Context context) {
        try {
            CameraManager manager = (CameraManager) context.getSystemService(Context.CAMERA_SERVICE);
            for (String id : manager.getCameraIdList()) {
                CameraCharacteristics about = manager.getCameraCharacteristics(id);
                Boolean flash = about.get(CameraCharacteristics.FLASH_INFO_AVAILABLE);
                Integer facing = about.get(CameraCharacteristics.LENS_FACING);
                if (flash != null && flash && (facing == null || facing == CameraCharacteristics.LENS_FACING_BACK)) return true;
            }
        } catch (Throwable ignored) { }
        return false;
    }

    /** True when Unity has a camera open right now (then the light must be switched through that camera). */
    public static boolean cameraIsOpen(Activity activity) {
        try {
            Object holder = sessionHolder(activity);
            return holder != null && valueOfType(holder, CameraCaptureSession.class) != null;
        } catch (Throwable t) {
            return false;
        }
    }

    /**
     * Switches the light of the open camera on or off. Returns "" when the camera took the change, otherwise the
     * reason it could not be done.
     */
    public static String set(Activity activity, final boolean on) {
        try {
            Object holder = sessionHolder(activity);
            if (holder == null) return "The camera is not running.";
            final CaptureRequest.Builder builder = (CaptureRequest.Builder) valueOfType(holder, CaptureRequest.Builder.class);
            final CameraCaptureSession session = (CameraCaptureSession) valueOfType(holder, CameraCaptureSession.class);
            final CameraCaptureSession.CaptureCallback callback = (CameraCaptureSession.CaptureCallback) valueOfType(holder, CameraCaptureSession.CaptureCallback.class);
            final Handler handler = (Handler) valueOfType(holder, Handler.class);
            if (builder == null || session == null) return "The camera is not running.";

            // does THIS camera have a light? (the selfie camera usually has none)
            try {
                CameraManager manager = (CameraManager) activity.getSystemService(Context.CAMERA_SERVICE);
                Boolean flash = manager.getCameraCharacteristics(session.getDevice().getId()).get(CameraCharacteristics.FLASH_INFO_AVAILABLE);
                if (flash != null && !flash) return "This camera has no flashlight.";
            } catch (Throwable ignored) { }

            final String[] problem = { "" };
            Runnable change = new Runnable() {
                @Override
                public void run() {
                    try {
                        builder.set(CaptureRequest.FLASH_MODE, on ? CaptureRequest.FLASH_MODE_TORCH : CaptureRequest.FLASH_MODE_OFF);
                        session.setRepeatingRequest(builder.build(), callback, handler);
                    } catch (Throwable t) {
                        problem[0] = String.valueOf(t.getMessage() != null ? t.getMessage() : t);
                    }
                }
            };
            // on the camera's own thread, in line with what Unity itself asks of the camera
            if (handler != null && handler.getLooper() != null && handler.getLooper().getThread() != Thread.currentThread()) {
                final Object done = new Object();
                final boolean[] finished = { false };
                synchronized (done) {
                    handler.post(new Runnable() {
                        @Override
                        public void run() {
                            change.run();
                            synchronized (done) { finished[0] = true; done.notifyAll(); }
                        }
                    });
                    long until = System.currentTimeMillis() + 1500;
                    while (!finished[0] && System.currentTimeMillis() < until) done.wait(100);
                }
                if (!finished[0]) return "The camera did not answer.";
            } else {
                change.run();
            }
            return problem[0];
        } catch (Throwable t) {
            return String.valueOf(t.getMessage() != null ? t.getMessage() : t);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Finding the camera session Unity opened
    // ---------------------------------------------------------------------------------------------
    /** The object of Unity that holds the open camera's request and session, or null when no camera is open. */
    private static Object sessionHolder(Activity activity) throws Exception {
        Object player = valueOfTypeNamed(activity, "com.unity3d.player.UnityPlayer");
        if (player == null) return null;
        Object wrapper = valueOfTypeNamed(player, "com.unity3d.player.Camera2Wrapper");
        if (wrapper == null) return null;
        // the wrapper's field whose object has both a request builder and a session
        for (Class<?> c = wrapper.getClass(); c != null && c != Object.class; c = c.getSuperclass()) {
            for (Field field : c.getDeclaredFields()) {
                if (field.getType().isPrimitive() || field.getType().getName().startsWith("android.") || field.getType().getName().startsWith("java.")) continue;
                field.setAccessible(true);
                Object value = field.get(wrapper);
                if (value != null && hasFieldOfType(value, CaptureRequest.Builder.class) && hasFieldOfType(value, CameraCaptureSession.class)) return value;
            }
        }
        return null;
    }

    private static boolean hasFieldOfType(Object owner, Class<?> type) {
        for (Class<?> c = owner.getClass(); c != null && c != Object.class; c = c.getSuperclass())
            for (Field field : c.getDeclaredFields())
                if (type.isAssignableFrom(field.getType())) return true;
        return false;
    }

    /** The value of the first field of `owner` whose type is `type` (or a subclass of it). */
    private static Object valueOfType(Object owner, Class<?> type) throws Exception {
        for (Class<?> c = owner.getClass(); c != null && c != Object.class; c = c.getSuperclass())
            for (Field field : c.getDeclaredFields()) {
                if (!type.isAssignableFrom(field.getType())) continue;
                field.setAccessible(true);
                Object value = field.get(java.lang.reflect.Modifier.isStatic(field.getModifiers()) ? null : owner);
                if (value != null) return value;
            }
        return null;
    }

    /** The value of the first field of `owner` whose type has this name (the class may not be on the compile path). */
    private static Object valueOfTypeNamed(Object owner, String typeName) throws Exception {
        for (Class<?> c = owner.getClass(); c != null && c != Object.class; c = c.getSuperclass())
            for (Field field : c.getDeclaredFields()) {
                if (!typeNamed(field.getType(), typeName)) continue;
                field.setAccessible(true);
                Object value = field.get(java.lang.reflect.Modifier.isStatic(field.getModifiers()) ? null : owner);
                if (value != null) return value;
            }
        return null;
    }

    private static boolean typeNamed(Class<?> type, String name) {
        for (Class<?> c = type; c != null; c = c.getSuperclass()) if (c.getName().equals(name)) return true;
        return false;
    }
}
