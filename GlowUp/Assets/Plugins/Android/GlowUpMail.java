package com.glowup.mail;

import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.net.Uri;

/**
 * "Open Gmail" on the "Confirm your email" pop-up of sign-up Step 1. Only ever runs when the user taps that button: nothing here looks
 * at the user's mail, waits for a message or opens anything by itself.
 *
 * In this order, the first that works is used:
 *   1. the Gmail app itself (its start screen, the inbox; never one message): an explicit launcher intent for com.google.android.gm.
 *      Starting another app's activity is not limited by Android 11's package visibility, so no <queries> entry is needed: when Gmail is
 *      not installed, startActivity throws ActivityNotFoundException and the next step is tried.
 *   2. Gmail's page in the Play Store (market://), or in the browser when there is no Play Store app.
 *   3. the phone's default email app (Intent.makeMainSelectorActivity with CATEGORY_APP_EMAIL).
 * Answers "gmail", "store", "mail" or "none". Used by Assets/_Project/Scripts/Common/MailApp.cs.
 */
public final class GlowUpMail {

    private static final String GMAIL = "com.google.android.gm";

    private GlowUpMail() { }

    public static String open(final Activity activity) {
        if (activity == null) return "none";
        Intent gmail = new Intent(Intent.ACTION_MAIN);
        gmail.addCategory(Intent.CATEGORY_LAUNCHER);
        gmail.setPackage(GMAIL);
        if (start(activity, gmail)) return "gmail";
        if (start(activity, new Intent(Intent.ACTION_VIEW, Uri.parse("market://details?id=" + GMAIL)))) return "store";
        if (start(activity, new Intent(Intent.ACTION_VIEW, Uri.parse("https://play.google.com/store/apps/details?id=" + GMAIL)))) return "store";
        if (start(activity, Intent.makeMainSelectorActivity(Intent.ACTION_MAIN, Intent.CATEGORY_APP_EMAIL))) return "mail";
        return "none";
    }

    private static boolean start(Activity activity, Intent intent) {
        try {
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            activity.startActivity(intent);
            return true;
        } catch (ActivityNotFoundException | SecurityException e) {
            return false;
        }
    }
}
