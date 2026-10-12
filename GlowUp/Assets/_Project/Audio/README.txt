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
    on their Desktop). The file was converted from a video file and carries NO information about its author or licence.
    Licence: NOT CONFIRMED. Before the app is published or handed in, the team must find out where the sound is from
    and that its licence allows use in an app, and write the answer here (name of the author or site, the licence,
    the link). If that cannot be confirmed, replace it with a sound whose licence is known.
    Played by: Scripts/Common/DeleteSoundPlayer.cs (the DeleteSoundPlayer object in RecentScanActivitiesScene).
    The user's switch: Settings > Sounds & Motion > Delete sound.

HOW TO REPLACE A SOUND
    Put the new file in this folder. For a tap sound: keep the old file name, or drag the new clip into the list on
    UiServices > UiSoundPlayer. For the delete sound: drag the new clip into DeleteSoundPlayer > Clip in
    RecentScanActivitiesScene (a clip you set there yourself is kept when the scene is built again), and set how much
    of it is heard with Start At Seconds / Play Seconds / Fade Out Seconds. In the clip's import settings switch
    "Preload Audio Data" off, so it is only loaded while the user's switch is on.
