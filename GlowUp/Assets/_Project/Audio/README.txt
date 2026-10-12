THE SOUNDS OF THE APP (all of them are in this folder)

UiTap_1_SoftTap.wav, UiTap_2_Pop.wav, UiTap_3_Click.wav, UiTap_4_Bubble.wav, UiTap_5_Chime.wav
    The five tap sounds the user can choose from in Settings > Sounds & Motion.
    Where they come from: made for this app by a small program (simple tones, no recording of anything).
    Licence: the project's own, free to use.
    Played by: Scripts/Common/UiSoundPlayer.cs (on the UiServices object, in every scene).

Delete_FreezingCold.mp3
    The freezing sound when a scan is removed in Recent Scans. The file is 6.7 seconds long, starts with 0.4 seconds of
    silence and is a quiet recording: the app skips the silence, plays the next 1.1 seconds, makes that piece louder
    and fades it out (all of this can be changed on DeleteSoundPlayer in the Inspector).
    Where it comes from: given by the project owner on 12 October 2026 (it was "Freezing Cold - Sound Effect.mp3"
    on their Desktop). The owner says it was taken from YOUTUBE. Which video (its address and who uploaded it) is not
    written down yet; the file itself carries no information about its author or licence.
    Licence: NOT CONFIRMED. "From YouTube" is where it was found, not a permission: a sound in a YouTube video belongs
    to whoever made it, unless the video or its description says it may be reused (for example "Creative Commons",
    "free to use", or a sound from YouTube's own Audio Library). Before the app is published or handed in, write here:
        the video's address: ...
        who uploaded it: ...
        what the video or its description says about reusing the sound: ...
    If it does not say that reuse is allowed, replace the sound with one whose licence is known (YouTube's Audio
    Library, freesound.org with a CC0 licence, or a sound the team records itself).
    Played by: Scripts/Common/DeleteSoundPlayer.cs (the DeleteSoundPlayer object in RecentScanActivitiesScene).
    The user's switch: Settings > Sounds & Motion > Delete sound.

HOW TO REPLACE A SOUND
    Put the new file in this folder. For a tap sound: keep the old file name, or drag the new clip into the list on
    UiServices > UiSoundPlayer. For the delete sound: drag the new clip into DeleteSoundPlayer > Clip in
    RecentScanActivitiesScene (a clip you set there yourself is kept when the scene is built again), and set how much
    of it is heard with Start At Seconds / Play Seconds / Fade Out Seconds. In the clip's import settings switch
    "Preload Audio Data" off, so it is only loaded while the user's switch is on.
