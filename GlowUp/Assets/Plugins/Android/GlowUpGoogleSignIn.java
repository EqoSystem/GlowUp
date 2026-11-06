package com.glowup.signin;

import android.app.Activity;
import android.os.CancellationSignal;

import androidx.credentials.Credential;
import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.CustomCredential;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.GetCredentialCancellationException;
import androidx.credentials.exceptions.GetCredentialException;
import androidx.credentials.exceptions.NoCredentialException;

import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;

import java.util.concurrent.Executor;
import java.util.concurrent.Executors;

/**
 * Google sign-in INSIDE the app (7 November 2026), with Android's Credential Manager: the phone shows its own Google account
 * sheet and hands back a Google ID token. Firebase then signs in with that token (GoogleAuthProvider credential, see
 * Assets/_Project/Scripts/Auth/NativeGoogleSignIn.cs).
 *
 * Why: the old way opened Google's page in the phone's BROWSER and came back through glowup-admin-portal.firebaseapp.com. A
 * browser that partitions or blocks storage for that site (Brave with Shields, strict privacy modes) loses the sign-in's state
 * there and shows "Unable to process request due to missing initial state". No browser is involved any more.
 *
 * Needs, in mainTemplate.gradle: androidx.credentials:credentials, androidx.credentials:credentials-play-services-auth and
 * com.google.android.libraries.identity.googleid:googleid. The web client id is read from the Firebase resources
 * ("default_web_client_id", from google-services.json) when the caller passes none.
 */
public final class GlowUpGoogleSignIn {

    /** Told how it ended. Called on a background thread: the Unity side only stores the answer. */
    public interface Callback {
        void onToken(String idToken, String email, String displayName);
        void onCancelled();
        /** code: "no-account" (no Google account on the phone / none chosen), "unavailable", or the exception's type. */
        void onError(String code, String message);
    }

    private static final Executor WORKER = Executors.newSingleThreadExecutor();

    private GlowUpGoogleSignIn() { }

    /** True when the libraries are in the build (an older build without them falls back to the browser way). */
    public static boolean isAvailable() {
        try {
            Class.forName("androidx.credentials.CredentialManager");
            Class.forName("com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption");
            return true;
        } catch (Throwable t) {
            return false;
        }
    }

    /** The OAuth web client id from the Firebase resources, or "". */
    public static String webClientId(Activity activity) {
        if (activity == null) return "";
        try {
            int id = activity.getResources().getIdentifier("default_web_client_id", "string", activity.getPackageName());
            return id != 0 ? activity.getString(id) : "";
        } catch (Throwable t) {
            return "";
        }
    }

    public static void signIn(final Activity activity, final String clientId, final Callback callback) {
        if (activity == null || callback == null) return;
        final String serverClientId = clientId != null && clientId.length() > 0 ? clientId : webClientId(activity);
        if (serverClientId.length() == 0) { callback.onError("unavailable", "no web client id"); return; }
        activity.runOnUiThread(new Runnable() {
            @Override public void run() {
                try {
                    GetSignInWithGoogleOption option = new GetSignInWithGoogleOption.Builder(serverClientId).build();
                    GetCredentialRequest request = new GetCredentialRequest.Builder().addCredentialOption(option).build();
                    CredentialManager manager = CredentialManager.create(activity);
                    manager.getCredentialAsync(activity, request, new CancellationSignal(), WORKER,
                        new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                            @Override public void onResult(GetCredentialResponse response) {
                                try {
                                    Credential credential = response.getCredential();
                                    if (credential instanceof CustomCredential
                                            && GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(credential.getType())) {
                                        GoogleIdTokenCredential google = GoogleIdTokenCredential.createFrom(((CustomCredential) credential).getData());
                                        callback.onToken(google.getIdToken(), google.getId(), google.getDisplayName() != null ? google.getDisplayName() : "");
                                    } else {
                                        callback.onError("unavailable", "unexpected credential type");
                                    }
                                } catch (Throwable t) {
                                    callback.onError("unavailable", String.valueOf(t.getMessage()));
                                }
                            }

                            @Override public void onError(GetCredentialException e) {
                                if (e instanceof GetCredentialCancellationException) callback.onCancelled();
                                else if (e instanceof NoCredentialException) callback.onError("no-account", String.valueOf(e.getMessage()));
                                else callback.onError(String.valueOf(e.getType()), String.valueOf(e.getMessage()));
                            }
                        });
                } catch (Throwable t) {
                    callback.onError("unavailable", String.valueOf(t.getMessage()));
                }
            }
        });
    }
}
