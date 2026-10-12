package com.glowup.ocr;

import android.content.Context;
import android.graphics.Bitmap;
import android.os.Build;
import android.util.Base64;

import com.google.mediapipe.framework.image.BitmapImageBuilder;
import com.google.mediapipe.framework.image.MPImage;
import com.google.mediapipe.tasks.core.BaseOptions;
import com.google.mediapipe.tasks.vision.core.RunningMode;
import com.google.mediapipe.tasks.vision.imageembedder.ImageEmbedder;
import com.google.mediapipe.tasks.vision.imageembedder.ImageEmbedderResult;

import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Turns a picture of a product's pack into about a thousand numbers that say WHAT THE PACK LOOKS LIKE, on the
 * phone itself (no internet), with a small picture model (MobileNet V3 small, through Google's MediaPipe Image
 * Embedder). Two pictures of the same pack give numbers that point the same way, also when the light, the
 * angle and the distance differ; two different packs do not. The app keeps these numbers for the products it
 * has scanned (Assets/_Project/Scripts/Scan/PackagingMemory.cs), so it knows a pack again without asking the AI.
 *
 * The picture itself is only looked at here: it is not saved and not sent anywhere. The numbers cannot be
 * turned back into the picture.
 *
 * The library is the line "com.google.mediapipe:tasks-vision" in Assets/Plugins/Android/mainTemplate.gradle (the
 * same one the finger signs use), the model is Assets/StreamingAssets/pack_embedder.tflite. The library needs
 * Android 7.0 (API 24): on an older phone isAvailable() says no and the app falls back to its simpler
 * colour-and-pattern signature.
 *
 * Everything slow (loading the model, looking at a picture) happens on one thread of its own.
 * Used by Assets/_Project/Scripts/Scan/PackEmbedder.cs.
 */
public final class GlowUpPackEmbedder {

    /** Told when a picture has been looked at. NOT called on the phone's main thread. */
    public interface Callback {
        /** numbers = the look of the pack: one byte per number (-127 to 127), written as Base64 text. */
        void onResult(String numbers);
        void onError(String message);
    }

    /** What state() answers. */
    public static final int NOT_STARTED = 0, STARTING = 1, READY = 2, FAILED = 3;

    private static final Object lock = new Object();
    private static ImageEmbedder embedder;
    private static ExecutorService worker;
    private static volatile int state = NOT_STARTED;
    private static volatile String failure = "";
    private static volatile boolean busy;

    private GlowUpPackEmbedder() { }

    /** True when the picture library is part of this build and the phone's Android is new enough for it. */
    public static boolean isAvailable() {
        if (Build.VERSION.SDK_INT < 24) return false;
        try {
            Class.forName("com.google.mediapipe.tasks.vision.imageembedder.ImageEmbedder");
            return true;
        } catch (Throwable t) {
            return false;
        }
    }

    /** 0 = not started, 1 = getting ready (the model is being loaded), 2 = ready, 3 = could not start: failure() says why. */
    public static int state() {
        return state;
    }

    /** Why it could not start ("" when it did, or was never tried). */
    public static String failure() {
        return failure;
    }

    /** True while a picture is still being looked at (the next one would only wait). */
    public static boolean isBusy() {
        return busy;
    }

    /**
     * Starts loading the model and returns at once. Ask state() until it says READY or FAILED.
     * modelAsset = the model's file name inside the app's assets ("pack_embedder.tflite").
     */
    public static void start(Context context, final String modelAsset) {
        final Context app;
        synchronized (lock) {
            if (state == STARTING || state == READY) return;
            if (!isAvailable()) {
                failure = "This phone's Android version is too old for the picture model (Android 7 or newer is needed).";
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
                    if (state != STARTING) return;
                }
                ImageEmbedder made = null;
                String problem = "";
                try {
                    ImageEmbedder.ImageEmbedderOptions options = ImageEmbedder.ImageEmbedderOptions.builder()
                            .setBaseOptions(BaseOptions.builder().setModelAssetPath(modelAsset).build())
                            .setRunningMode(RunningMode.IMAGE)
                            .setL2Normalize(true)
                            .setQuantize(false)
                            .build();
                    made = ImageEmbedder.createFromOptions(app, options);
                } catch (Throwable t) {
                    problem = String.valueOf(t.getMessage() != null ? t.getMessage() : t);
                }
                boolean keep = false;
                synchronized (lock) {
                    if (state == STARTING) {
                        if (made != null) {
                            embedder = made;
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
                    try { made.close(); } catch (Throwable ignored) { }
                }
            }
        });
    }

    /**
     * Looks at an upright picture. pixels = the picture row by row from the top, each pixel as one ARGB number.
     * middle = the share of the picture's width and height that is looked at, round its centre (0.8 = the middle
     * four fifths: the edges are mostly the hand and the background). Returns false when it is not ready or still
     * busy with the picture before (then nothing happens and the callback is not called).
     */
    public static boolean embed(final int[] pixels, final int width, final int height, final float middle, final Callback callback) {
        synchronized (lock) {
            if (state != READY || embedder == null || busy || callback == null || worker == null) return false;
            if (pixels == null || width < 32 || height < 32 || pixels.length < width * height) return false;
            busy = true;
        }
        worker.execute(new Runnable() {
            @Override
            public void run() {
                Bitmap whole = null, part = null;
                try {
                    ImageEmbedder use;
                    synchronized (lock) { use = embedder; }
                    if (use == null) { callback.onError("The picture model was stopped."); return; }
                    whole = Bitmap.createBitmap(pixels, width, height, Bitmap.Config.ARGB_8888);
                    float share = Math.max(0.3f, Math.min(1f, middle));
                    int w = Math.max(32, Math.round(width * share)), h = Math.max(32, Math.round(height * share));
                    part = share < 0.999f ? Bitmap.createBitmap(whole, (width - w) / 2, (height - h) / 2, w, h) : whole;
                    MPImage image = new BitmapImageBuilder(part).build();
                    ImageEmbedderResult result = use.embed(image);
                    float[] numbers = result.embeddingResult().embeddings().get(0).floatEmbedding();
                    if (numbers == null || numbers.length == 0) { callback.onError("The picture model gave no answer."); return; }
                    // one byte per number: only the direction of the numbers matters, so each is written as a share of the largest
                    float largest = 0f;
                    for (float n : numbers) largest = Math.max(largest, Math.abs(n));
                    if (largest <= 0f) { callback.onError("The picture model gave an empty answer."); return; }
                    byte[] bytes = new byte[numbers.length];
                    for (int i = 0; i < numbers.length; i++) bytes[i] = (byte) Math.round(numbers[i] / largest * 127f);
                    callback.onResult(Base64.encodeToString(bytes, Base64.NO_WRAP));
                } catch (Throwable t) {
                    try { callback.onError(String.valueOf(t.getMessage() != null ? t.getMessage() : t)); } catch (Throwable ignored) { }
                } finally {
                    if (part != null && part != whole) part.recycle();
                    if (whole != null) whole.recycle();
                    busy = false;
                }
            }
        });
        return true;
    }

    /** Lets the model go (the scan screen was left). It can be started again. */
    public static void stop() {
        final ImageEmbedder old;
        synchronized (lock) {
            old = embedder;
            embedder = null;
            state = NOT_STARTED;
            failure = "";
        }
        if (old == null || worker == null) return;
        worker.execute(new Runnable() {
            @Override
            public void run() {
                try { old.close(); } catch (Throwable ignored) { }
                busy = false;
            }
        });
    }
}
