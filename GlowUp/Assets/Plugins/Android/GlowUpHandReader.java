package com.glowup.ocr;

import android.content.Context;
import android.graphics.Bitmap;
import android.os.Build;

import com.google.mediapipe.framework.image.BitmapImageBuilder;
import com.google.mediapipe.framework.image.MPImage;
import com.google.mediapipe.tasks.components.containers.Landmark;
import com.google.mediapipe.tasks.components.containers.NormalizedLandmark;
import com.google.mediapipe.tasks.core.BaseOptions;
import com.google.mediapipe.tasks.vision.core.ImageProcessingOptions;
import com.google.mediapipe.tasks.vision.core.RunningMode;
import com.google.mediapipe.tasks.vision.handlandmarker.HandLandmarker;
import com.google.mediapipe.tasks.vision.handlandmarker.HandLandmarkerResult;

import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Finds a hand in a picture on the phone itself (no internet) with Google's MediaPipe Hand Landmarker, and
 * returns the 21 points of the hand (wrist, and four points per finger). The app counts the raised fingers
 * from those points (Assets/_Project/Scripts/Common/FingerCounter.cs) to change screen by a finger sign.
 *
 * The picture is only looked at here; it is not saved and not sent anywhere.
 *
 * The library comes from the line "com.google.mediapipe:tasks-vision" in Assets/Plugins/Android/mainTemplate.gradle,
 * the model is Assets/StreamingAssets/hand_landmarker.task. The library needs Android 7.0 (API 24): on an older
 * phone isAvailable() says no and the finger signs simply stay off.
 * It is a class of its own so that the rest of the app keeps working in a build without this library.
 *
 * Everything slow (loading the model, looking at a picture) happens on one thread of its own, never on the
 * thread that draws the app.
 */
public final class GlowUpHandReader {

    /** Told when a picture has been looked at. NOT called on the phone's main thread. */
    public interface Callback {
        /**
         * points      = 63 numbers separated by spaces: x, y, z of the 21 points, as shares of the picture (0 to 1); "" when no hand is seen.
         * worldPoints = 63 numbers separated by spaces: the same 21 points in metres, measured from the middle of the hand; "" when not known.
         */
        void onResult(String points, String worldPoints);
        void onError(String message);
    }

    /** What state() answers. */
    public static final int NOT_STARTED = 0, STARTING = 1, READY = 2, FAILED = 3;

    private static final Object lock = new Object();
    private static HandLandmarker landmarker;
    private static ExecutorService worker;
    private static volatile int state = NOT_STARTED;
    private static volatile String failure = "";
    private static volatile boolean busy;

    private GlowUpHandReader() { }

    /** True when the hand library is part of this build and the phone's Android is new enough for it. */
    public static boolean isAvailable() {
        if (Build.VERSION.SDK_INT < 24) return false;
        try {
            Class.forName("com.google.mediapipe.tasks.vision.handlandmarker.HandLandmarker");
            return true;
        } catch (Throwable t) {
            return false;
        }
    }

    /** 0 = not started, 1 = getting ready (the model is being loaded), 2 = ready, 3 = could not start: failure() says why. */
    public static int state() {
        return state;
    }

    /** Why the hand finder could not start ("" when it did, or was never tried). */
    public static String failure() {
        return failure;
    }

    /**
     * Starts getting the hand finder ready and returns at once: loading the model takes a moment, so it is done on
     * the reader's own thread. Ask state() until it says READY or FAILED.
     * modelAsset = the model's file name inside the app's assets ("hand_landmarker.task").
     */
    public static void start(Context context, final String modelAsset) {
        final Context app;
        synchronized (lock) {
            if (state == STARTING || state == READY) return;
            if (!isAvailable()) {
                failure = "This phone's Android version is too old for hand detection (Android 7 or newer is needed).";
                state = FAILED;
                return;
            }
            if (context == null) {
                failure = "The app could not be reached.";
                state = FAILED;
                return;
            }
            app = context.getApplicationContext();
            if (worker == null) worker = Executors.newSingleThreadExecutor();
            failure = "";
            state = STARTING;
        }
        worker.execute(new Runnable() {
            @Override
            public void run() {
                synchronized (lock) {
                    if (state != STARTING) return;          // stopped again (or made ready by a start before this one)
                }
                HandLandmarker made = null;
                String problem = "";
                try {
                    HandLandmarker.HandLandmarkerOptions options = HandLandmarker.HandLandmarkerOptions.builder()
                            .setBaseOptions(BaseOptions.builder().setModelAssetPath(modelAsset).build())
                            .setRunningMode(RunningMode.IMAGE)
                            .setNumHands(1)
                            .setMinHandDetectionConfidence(0.6f)
                            .setMinHandPresenceConfidence(0.6f)
                            .build();
                    made = HandLandmarker.createFromOptions(app, options);
                } catch (Throwable t) {
                    problem = String.valueOf(t.getMessage() != null ? t.getMessage() : t);
                }
                boolean keep = false;
                synchronized (lock) {
                    if (state == STARTING) {
                        if (made != null) {
                            landmarker = made;
                            busy = false;
                            state = READY;
                            keep = true;
                        } else {
                            failure = problem;
                            state = FAILED;
                        }
                    }
                }
                if (!keep && made != null) {
                    try { made.close(); } catch (Throwable ignored) { }     // it was stopped while the model loaded
                }
            }
        });
    }

    /** True while a picture is still being looked at (the next one would only wait). */
    public static boolean isBusy() {
        return busy;
    }

    /**
     * Looks for a hand in a picture. pixels = the picture row by row from the top, each pixel as one ARGB number.
     * rotationDegrees = how far the picture must be turned clockwise to stand upright (0, 90, 180 or 270).
     * Returns false when the finder is not ready or still busy with the picture before: then nothing happens.
     */
    public static boolean read(final int[] pixels, final int width, final int height, final int rotationDegrees, final Callback callback) {
        final HandLandmarker finder;
        synchronized (lock) {
            finder = landmarker;
            if (finder == null || worker == null || state != READY || busy || callback == null) return false;
            if (pixels == null || width <= 0 || height <= 0 || pixels.length < width * height) return false;
            busy = true;
        }
        worker.execute(new Runnable() {
            @Override
            public void run() {
                Bitmap bitmap = null;
                try {
                    bitmap = Bitmap.createBitmap(pixels, width, height, Bitmap.Config.ARGB_8888);
                    MPImage image = new BitmapImageBuilder(bitmap).build();
                    ImageProcessingOptions turn = ImageProcessingOptions.builder().setRotationDegrees(rotationDegrees).build();
                    HandLandmarkerResult result = finder.detect(image, turn);

                    List<List<NormalizedLandmark>> hands = result.landmarks();
                    List<List<Landmark>> worldHands = result.worldLandmarks();
                    if (hands == null || hands.isEmpty() || hands.get(0).size() < 21) {
                        callback.onResult("", "");
                        return;
                    }
                    List<NormalizedLandmark> hand = hands.get(0);
                    StringBuilder points = new StringBuilder(700);
                    for (int i = 0; i < 21; i++) {
                        NormalizedLandmark p = hand.get(i);
                        points.append(p.x()).append(' ').append(p.y()).append(' ').append(p.z()).append(' ');
                    }
                    StringBuilder world = new StringBuilder(700);
                    if (worldHands != null && !worldHands.isEmpty() && worldHands.get(0).size() >= 21) {
                        List<Landmark> worldHand = worldHands.get(0);
                        for (int i = 0; i < 21; i++) {
                            Landmark p = worldHand.get(i);
                            world.append(p.x()).append(' ').append(p.y()).append(' ').append(p.z()).append(' ');
                        }
                    }
                    callback.onResult(points.toString().trim(), world.toString().trim());
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

    /** Lets go of the hand finder (the model leaves the phone's memory). It can be started again later. */
    public static void stop() {
        final HandLandmarker finder;
        final ExecutorService thread;
        synchronized (lock) {
            finder = landmarker;
            thread = worker;
            landmarker = null;
            failure = "";
            state = NOT_STARTED;
        }
        if (finder == null) return;
        try {
            if (thread != null) {
                // closed on the reader's own thread, so never in the middle of a picture
                thread.execute(new Runnable() {
                    @Override
                    public void run() {
                        try { finder.close(); } catch (Throwable ignored) { }
                    }
                });
            } else {
                finder.close();
            }
        } catch (Throwable ignored) { }
    }
}
