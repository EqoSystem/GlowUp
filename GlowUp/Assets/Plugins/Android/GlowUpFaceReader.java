package com.glowup.ocr;

import android.content.Context;
import android.graphics.Bitmap;
import android.os.Build;
import android.os.SystemClock;

import com.google.mediapipe.framework.image.BitmapImageBuilder;
import com.google.mediapipe.framework.image.MPImage;
import com.google.mediapipe.tasks.components.containers.NormalizedLandmark;
import com.google.mediapipe.tasks.core.BaseOptions;
import com.google.mediapipe.tasks.vision.core.RunningMode;
import com.google.mediapipe.tasks.vision.facelandmarker.FaceLandmarker;
import com.google.mediapipe.tasks.vision.facelandmarker.FaceLandmarkerResult;

import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Finds a face in a picture on the phone itself (no internet) with Google's MediaPipe Face Landmarker and returns the
 * 468 points of the standard face mesh (the same numbering as ARCore's face mesh). The guided AR Try-On draws its make-up
 * on those points, so the colour follows the real face on EVERY phone, not only on phones with ARCore.
 *
 * The picture is only looked at here; it is not saved and not sent anywhere.
 *
 * The library comes from the line "com.google.mediapipe:tasks-vision" in Assets/Plugins/Android/mainTemplate.gradle (the same one the
 * finger signs use), the model is Assets/StreamingAssets/face_landmarker.task. Android 7.0 (API 24) or newer is needed: on an older
 * phone isAvailable() says no and the app keeps the old guided preview.
 *
 * The model runs in VIDEO mode: after the first picture it follows the face from picture to picture, which is quicker and steadier
 * than looking for the face again every time. Everything slow (loading the model, looking at a picture) happens on one thread of
 * its own, never on the thread that draws the app. The answer is not pushed to the app: the app asks for it with take(), once per
 * picture, so no callback object has to cross from Java to Unity 30 times a second.
 */
public final class GlowUpFaceReader {

    /** What state() answers. */
    public static final int NOT_STARTED = 0, STARTING = 1, READY = 2, FAILED = 3;

    /** Points of the standard face mesh (MediaPipe also gives 10 iris points after them; they are not used). */
    private static final int POINTS = 468;

    private static final Object lock = new Object();
    private static FaceLandmarker landmarker;
    private static ExecutorService worker;
    private static volatile int state = NOT_STARTED;
    private static volatile String failure = "";
    private static volatile boolean busy;

    // the newest answer: 468 x 3 numbers (x and y as shares of the picture 0 to 1, z in the same scale), or an empty list when no face was seen
    private static volatile float[] latest;
    private static volatile boolean fresh;
    private static volatile int lastMillis;
    private static long lastStamp = -1;

    private GlowUpFaceReader() { }

    /** True when the face library is part of this build and the phone's Android is new enough for it. */
    public static boolean isAvailable() {
        if (Build.VERSION.SDK_INT < 24) return false;
        try {
            Class.forName("com.google.mediapipe.tasks.vision.facelandmarker.FaceLandmarker");
            return true;
        } catch (Throwable t) {
            return false;
        }
    }

    /** 0 = not started, 1 = getting ready (the model is being loaded), 2 = ready, 3 = could not start: failure() says why. */
    public static int state() {
        return state;
    }

    /** Why the face finder could not start ("" when it did, or was never tried). */
    public static String failure() {
        return failure;
    }

    /** True while a picture is still being looked at (the next one would only wait). */
    public static boolean isBusy() {
        return busy;
    }

    /** How long the last picture took to look at, in milliseconds (0 before the first one). The app slows down or speeds up with it. */
    public static int lastMillis() {
        return lastMillis;
    }

    /**
     * Starts getting the face finder ready and returns at once: loading the model takes a moment, so it is done on the reader's own
     * thread. Ask state() until it says READY or FAILED. modelAsset = the model's file name inside the app's assets ("face_landmarker.task").
     */
    public static void start(Context context, final String modelAsset) {
        final Context app;
        synchronized (lock) {
            if (state == STARTING || state == READY) return;
            if (!isAvailable()) {
                failure = "This phone's Android version is too old for face finding (Android 7 or newer is needed).";
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
                    if (state != STARTING) return;          // stopped again while waiting
                }
                FaceLandmarker made = null;
                String problem = "";
                try {
                    FaceLandmarker.FaceLandmarkerOptions options = FaceLandmarker.FaceLandmarkerOptions.builder()
                            .setBaseOptions(BaseOptions.builder().setModelAssetPath(modelAsset).build())
                            .setRunningMode(RunningMode.VIDEO)
                            .setNumFaces(1)
                            .setMinFaceDetectionConfidence(0.5f)
                            .setMinFacePresenceConfidence(0.5f)
                            .setMinTrackingConfidence(0.5f)
                            .setOutputFaceBlendshapes(false)
                            .setOutputFacialTransformationMatrixes(false)
                            .build();
                    made = FaceLandmarker.createFromOptions(app, options);
                } catch (Throwable t) {
                    problem = String.valueOf(t.getMessage() != null ? t.getMessage() : t);
                }
                boolean keep = false;
                synchronized (lock) {
                    if (state == STARTING) {
                        if (made != null) {
                            landmarker = made;
                            busy = false;
                            fresh = false;
                            latest = null;
                            lastStamp = -1;
                            state = READY;
                            keep = true;
                        } else {
                            failure = problem;
                            state = FAILED;
                        }
                    }
                }
                if (!keep && made != null) {
                    try { made.close(); } catch (Throwable ignored) { }
                }
            }
        });
    }

    /**
     * Looks for a face in an UPRIGHT picture: pixels = row by row from the top, each pixel as one ARGB number.
     * Returns false when the finder is not ready or still busy with the picture before: then nothing happens.
     * The answer comes later through take().
     */
    public static boolean look(final int[] pixels, final int width, final int height) {
        final FaceLandmarker finder;
        synchronized (lock) {
            finder = landmarker;
            if (finder == null || worker == null || state != READY || busy) return false;
            if (pixels == null || width <= 0 || height <= 0 || pixels.length < width * height) return false;
            busy = true;
        }
        worker.execute(new Runnable() {
            @Override
            public void run() {
                Bitmap bitmap = null;
                long began = SystemClock.uptimeMillis();
                try {
                    bitmap = Bitmap.createBitmap(pixels, width, height, Bitmap.Config.ARGB_8888);
                    MPImage image = new BitmapImageBuilder(bitmap).build();
                    long stamp = began > lastStamp ? began : lastStamp + 1;      // VIDEO mode needs a time that only grows
                    lastStamp = stamp;
                    FaceLandmarkerResult result = finder.detectForVideo(image, stamp);

                    List<List<NormalizedLandmark>> faces = result.faceLandmarks();
                    float[] points;
                    if (faces == null || faces.isEmpty() || faces.get(0).size() < POINTS) {
                        points = new float[0];
                    } else {
                        List<NormalizedLandmark> face = faces.get(0);
                        points = new float[POINTS * 3];
                        for (int i = 0; i < POINTS; i++) {
                            NormalizedLandmark p = face.get(i);
                            points[i * 3] = p.x();
                            points[i * 3 + 1] = p.y();
                            points[i * 3 + 2] = p.z();
                        }
                    }
                    latest = points;
                    fresh = true;
                } catch (Throwable t) {
                    latest = new float[0];            // a picture that could not be read counts as "no face"; the app carries on
                    fresh = true;
                } finally {
                    if (bitmap != null) bitmap.recycle();
                    lastMillis = (int) (SystemClock.uptimeMillis() - began);
                    busy = false;
                }
            }
        });
        return true;
    }

    /** True when an answer has come that take() has not given yet. */
    public static boolean hasNew() {
        return fresh;
    }

    /**
     * The answer for the newest picture: 468 x 3 numbers (x, y as shares of the picture from its top left, z), or an EMPTY list when the
     * picture showed no face (and when nothing has come yet). Never null, so nothing null has to cross over to Unity. Ask hasNew() first.
     */
    public static float[] take() {
        fresh = false;
        float[] points = latest;
        return points != null ? points : new float[0];
    }

    /** Lets go of the face finder (the model leaves the phone's memory). It can be started again later. */
    public static void stop() {
        final FaceLandmarker finder;
        final ExecutorService thread;
        synchronized (lock) {
            finder = landmarker;
            thread = worker;
            landmarker = null;
            failure = "";
            fresh = false;
            latest = null;
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
