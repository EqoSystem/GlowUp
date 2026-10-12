THE SOUNDS OF THE APP (all of them are in this folder)

UiTap_1_SoftTap.wav, UiTap_2_Pop.wav, UiTap_3_Click.wav, UiTap_4_Bubble.wav, UiTap_5_Chime.wav
    The five tap sounds the user can choose from in Settings > Sounds & Motion.
    Where they come from: made for this app by the small program Source~/make_tap_sounds.py (simple tones, no
    recording of anything).
    Licence: the project's own, free to use.
    Played by: Scripts/Common/UiSoundPlayer.cs (on the UiServices object, in every scene).

Delete_FreezingCold.mp3
    The freezing sound when a scan is removed in Recent Scans. The file is 6.7 seconds long, starts with 0.4 seconds of
    silence and is a quiet recording: the app skips the silence, plays the next 1.1 seconds, makes that piece louder
    and fades it out (all of this can be changed on DeleteSoundPlayer in the Inspector).
    Where it comes from: given by the project owner on 12 October 2026 (it was "Freezing Cold - Sound Effect.mp3"
    on their Desktop), taken from this YouTube video, as the owner showed on the same day:
        the video's address:  https://www.youtube.com/watch?v=enz7eXF5IUo
        its title:            "Teeth Chattering - Sound Effect for editing" (uploaded 17 August 2020)
        who uploaded it:      the channel "Sound Library" (https://www.youtube.com/@SoundLibrary1)
        (the file's name and the video's title are not the same: the owner showed this video as its source)
    What the video's description says about reusing the sound (copied on 12 October 2026):
        "Feel free to use the uploaded sounds. That's what they are here for after all!
         Just don't forget to credit the original creator, if there is one. Of course if you want, you can also credit me :)"
        "DISCLAIMER: This sound is most likely not produced by myself, but rather taken from somewhere else. [...] If
         there is no recognizable creator in the video, it's either Royalty free and/or falls under the Creative Commons
         license and/or has an unknown creator."
    Credit: sound "Teeth Chattering" as shared by the YouTube channel Sound Library; original creator unknown.
    Licence: PERMISSION FROM THE UPLOADER, BUT NOT A CONFIRMED LICENCE. The channel says the sound may be used, and asks
    for a credit. It also says it did not make the sound and does not know who did. So nobody who owns the sound has
    given permission in writing. For a school project that is shown and graded, the credit above is the fair thing to do.
    Before the app is PUBLISHED (a store, a public download), replace it with a sound whose licence is known: YouTube's
    own Audio Library, freesound.org with a CC0 licence, a sound the team records itself, or one made by a program
    like the tap sounds.
    Credited in the app: Privacy Policy and Terms, section 17 "Credits" (Assets/_Project/Legal/PrivacyPolicyAndTerms.txt).
    Played by: Scripts/Common/DeleteSoundPlayer.cs (the DeleteSoundPlayer object in RecentScanActivitiesScene), as "Clip".
    The user's switch: Settings > Sounds & Motion > Delete sound.

Delete_Freeze_Made.wav
    The app's OWN freezing sound for the same moment: 1.2 seconds of bright ticks (ice forming), a glassy shimmer, a
    soft hiss, a tone that climbs, and one small glass bell near the end.
    Where it comes from: made for this app on 12 October 2026 by the small program Source~/make_freeze_sound.py.
    Nothing is recorded or copied; the sound is worked out from numbers.
    Licence: the project's own, free to use. No credit is needed.
    NOT LISTENED TO YET: the program can measure the sound but not hear it. Click the file in Unity's Project window
    to play it and decide whether it fits. To change it, change the numbers in the program and run it again:
        python Source~/make_freeze_sound.py .          (in this folder; only Python is needed)
    To use it: in RecentScanActivitiesScene select DeleteSoundPlayer and tick "Use Own Sound" (the tick is kept when
    the scene is built again). Then take the first line out of section 17 "Credits" of the Privacy Policy and Terms,
    because the YouTube sound is no longer in use.

THE FOLDER Source~
    The small programs that made the app's own sounds. Unity leaves folders whose name ends in ~ alone, so they are
    not part of the app; they are kept so that anybody can see how the sounds were made and make them again.

HOW TO REPLACE A SOUND
    Put the new file in this folder. For a tap sound: keep the old file name, or drag the new clip into the list on
    UiServices > UiSoundPlayer. For the delete sound: drag the new clip into DeleteSoundPlayer > Clip in
    RecentScanActivitiesScene (a clip you set there yourself is kept when the scene is built again), and set how much
    of it is heard with Start At Seconds / Play Seconds / Fade Out Seconds. In the clip's import settings switch
    "Preload Audio Data" off, so it is only loaded while the user's switch is on.

Delete_Drop_Made.wav
    The short sound of the DROP: played once when a scan (or a gathered group) is let go inside the remove area.
    0.4 seconds: a low knock, a burst of bright splinters, a glassy ring.
    Where it comes from: made for this app on 12 October 2026 by the small program Source~/make_drop_sound.py.
    Licence: the project's own, free to use. NOT LISTENED TO YET (see Delete_Freeze_Made.wav).
    Since the same day the freezing sound KEEPS PLAYING while a row is carried by the finger and stops when it is let
    go ("Keep Playing While Carried" on DeleteSoundPlayer; untick it for the earlier behaviour).
