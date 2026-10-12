using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// The sounds of removing a scan in Recent Scans (the project owner's note of 12 October 2026):
    ///   - the "freezing" sound KEEPS PLAYING while the rows shake (softer; SetShaking) and while a row or a gathered
    ///     group of rows is carried by the finger (full loudness; BeginCarry / EndCarry);
    ///   - a second, short sound is played once when it is dropped into the remove area (PlayDrop).
    /// Play() is the earlier way (the freezing sound once, at the drop); it is kept for "Keep Playing While Carried" off.
    ///
    ///   - One player for the screen. A new request while the sound is still playing is ignored, so it never
    ///     repeats, overlaps or stacks when the user removes quickly.
    ///   - Only a short piece of the clip is used (Play Seconds, with a fade-out), so the sound ends with the short
    ///     pop of the tile and not seconds later. The piece is cut out once, when the screen opens: the silence at
    ///     the start of the file is skipped and a quiet recording is brought up to a normal loudness. The rest of the
    ///     file is let go of at once.
    ///   - The user's switch is Settings > Sounds and Motion > Delete sound (UiPreferences.DeleteSound). While it
    ///     is off nothing is played AND nothing is loaded; everything is released when the screen is left.
    ///   - It goes through the app's ordinary sound output, like the tap sounds: it follows the phone's media
    ///     volume and does not stop the user's music. With "Quiet When Phone Is Silent" it also stays quiet while
    ///     the phone is on silent or vibrate.
    ///
    /// There are two recordings to choose from (see the README in Assets/_Project/Audio for where each came from):
    ///   Clip      Delete_FreezingCold.mp3, the one the project owner chose (taken from a YouTube video; credited in the app).
    ///             Measured on 12 October 2026: 6.7 seconds, the first 0.4 seconds silent, and quiet throughout.
    ///   Own Clip  Delete_Freeze_Made.wav, made for this app by a small program; tick "Use Own Sound" to play it instead.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class DeleteSoundPlayer : MonoBehaviour
    {
        [Tooltip("The sound. Import settings: Load Type 'Decompress On Load' and 'Preload Audio Data' off, so it is only loaded while the switch in Settings is on.")]
        [SerializeField] private AudioClip clip;
        [Tooltip("The app's OWN freezing sound, made by a small program (Audio/Source~/make_freeze_sound.py): it belongs to the project, so there is no question about its licence.")]
        [SerializeField] private AudioClip ownClip;
        [Tooltip("Tick this to play the app's own sound in place of the one above. Listen to both first: click each file in Assets/_Project/Audio.")]
        [SerializeField] private bool useOwnSound = false;

        [Tooltip("The short sound of the DROP: played once when the row (or the group) is let go inside the remove area. Made by Audio/Source~/make_drop_sound.py.")]
        [SerializeField] private AudioClip dropClip;
        [Range(0f, 1f)] [SerializeField] private float dropVolume = 1f;

        [Header("While a row is carried")]
        [Tooltip("On = the freezing sound plays again and again for as long as a row is carried by the finger, and stops when it is let go")]
        [SerializeField] private bool keepPlayingWhileCarried = true;
        [Tooltip("On = the freezing sound also plays, softer, for as long as the rows SHAKE (delete mode), before one is picked up")]
        [SerializeField] private bool playWhileShaking = true;
        [Tooltip("How loud it is while the rows only shake, as a share of the carried loudness")]
        [Range(0f, 1f)] [SerializeField] private float loudnessWhileShaking = 0.45f;
        [Tooltip("The carried sound fades out in this many seconds when the row is let go")]
        [SerializeField] private float carryFadeSeconds = 0.12f;

        [Header("Which piece of the clip is heard")]
        [Tooltip("On = the silence at the start of the file is skipped")]
        [SerializeField] private bool skipSilenceAtStart = true;
        [Tooltip("Seconds to skip after that (0 = start where the sound starts)")]
        [SerializeField] private float startAtSeconds = 0f;
        [Tooltip("How long it is heard (seconds), the fade-out included. The pop of a removed tile takes about a quarter of a second.")]
        [SerializeField] private float playSeconds = 1.1f;
        [Tooltip("The last part of that time fades out (seconds)")]
        [SerializeField] private float fadeOutSeconds = 0.5f;

        [Header("Loudness")]
        [Tooltip("The piece is made this loud at its loudest point (1 = as loud as a sound can be). 0 = leave the recording as it is.")]
        [Range(0f, 1f)] [SerializeField] private float loudestPoint = 0.85f;
        [Tooltip("A recording is never made more than this many times louder (a very quiet one would only become noise)")]
        [SerializeField] private float mostGain = 6f;
        [Range(0f, 1f)] [SerializeField] private float volume = 0.9f;
        [Tooltip("On = the Volume slider of Settings > Sounds & Motion also sets how loud this sound is")]
        [SerializeField] private bool followSoundVolumeSetting = true;

        [Header("Behaviour")]
        [Tooltip("No sound while the phone is on silent or vibrate (Android)")]
        [SerializeField] private bool quietWhenPhoneIsSilent = true;

        /// <summary>The recording the piece is cut from: the app's own sound when that is chosen (and there), otherwise the other one.</summary>
        AudioClip Chosen => useOwnSound && ownClip != null ? ownClip : clip;

        AudioSource source;
        AudioSource dropSource;         // a second voice, so the drop is heard over the last of the freezing sound
        bool carrying, shaking, looping;
        float carryFade = -1f;
        AudioClip piece;                // the short, ready-made piece that is played
        float endsAt = -1f;

        /// <summary>True from the start of the sound until it has ended.</summary>
        public bool IsPlaying => endsAt > 0f && Time.unscaledTime < endsAt;
        /// <summary>True when the piece is ready to be played at once.</summary>
        public bool Ready => piece != null;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false; source.loop = false; source.spatialBlend = 0f;
            dropSource = gameObject.AddComponent<AudioSource>();
            dropSource.playOnAwake = false; dropSource.loop = false; dropSource.spatialBlend = 0f;
        }

        float Loudness => Mathf.Clamp01(volume * (followSoundVolumeSetting ? UiPreferences.TapSoundVolume : 1f));

        /// <summary>
        /// The rows started or stopped SHAKING (delete mode). While they shake the freezing sound plays round and round,
        /// softer than while a row is carried ("Loudness While Shaking"); it stops when the shaking stops.
        /// </summary>
        public void SetShaking(bool on)
        {
            if (shaking == on) return;
            shaking = on;
            if (on) StartLoop(); else if (!carrying) StopLoop();
        }

        /// <summary>
        /// A row was picked up to be removed: the freezing sound plays at full loudness until EndCarry (it is played
        /// round and round; its own fade-out makes each round end softly). Calling it again while it plays does nothing.
        /// </summary>
        public bool BeginCarry()
        {
            if (!keepPlayingWhileCarried) return false;
            carrying = true;
            return StartLoop();
        }

        /// <summary>The row was let go (anywhere): back to the softer shaking sound, or silence when nothing shakes.</summary>
        public void EndCarry()
        {
            if (!carrying) return;
            carrying = false;
            if (!shaking || !playWhileShaking) StopLoop();
        }

        bool StartLoop()
        {
            if (!carrying && !(shaking && playWhileShaking)) return false;
            if (looping) { carryFade = -1f; return true; }
            if (!UiPreferences.DeleteSound || source == null || !isActiveAndEnabled) return false;
            if (quietWhenPhoneIsSilent && PhoneIsSilent()) return false;
            Prepare();
            if (piece == null) return false;
            source.clip = piece; source.loop = true; source.volume = LoopLoudness; source.time = 0f;
            source.Play();
            looping = true; carryFade = -1f; endsAt = -1f;
            return true;
        }

        void StopLoop()
        {
            if (!looping) return;
            looping = false;
            carryFade = Mathf.Max(0.01f, carryFadeSeconds);
        }

        /// <summary>How loud the looping sound is right now: full while a row is carried, softer while the rows only shake.</summary>
        float LoopLoudness => Loudness * (carrying ? 1f : Mathf.Clamp01(loudnessWhileShaking));

        /// <summary>True while the freezing sound is kept playing for a carried row.</summary>
        public bool IsCarrying => carrying;

        /// <summary>The row was DROPPED into the remove area: one short sound of its own. Returns true when it started.</summary>
        public bool PlayDrop()
        {
            if (!UiPreferences.DeleteSound || dropSource == null || dropClip == null || !isActiveAndEnabled) return false;
            if (quietWhenPhoneIsSilent && PhoneIsSilent()) return false;
            dropSource.PlayOneShot(dropClip, Mathf.Clamp01(dropVolume * Loudness));
            return true;
        }

        void OnEnable() { UiPreferences.Changed += Prepare; Prepare(); }
        void OnDisable() { UiPreferences.Changed -= Prepare; Release(); }

        /// <summary>Makes the piece ready while the user's switch is on, and lets it go while the switch is off.</summary>
        void Prepare()
        {
            if (!UiPreferences.DeleteSound) { Release(); return; }
            AudioClip clip = Chosen;
            if (piece != null || clip == null) return;
            try
            {
                bool wasLoaded = clip.loadState == AudioDataLoadState.Loaded;
                if (!wasLoaded && !clip.LoadAudioData()) { Debug.LogWarning("Delete sound: the clip could not be loaded."); return; }
                var all = new float[clip.samples * clip.channels];
                bool read = clip.GetData(all, 0);
                if (!wasLoaded) clip.UnloadAudioData();                 // the whole file is not needed any more
                if (!read) { Debug.LogWarning("Delete sound: set the clip's Load Type to 'Decompress On Load' so a piece can be cut out of it."); return; }
                float[] cut = Cut(all, clip.channels, clip.frequency, skipSilenceAtStart, startAtSeconds, playSeconds, fadeOutSeconds, loudestPoint, mostGain);
                if (cut.Length == 0) return;
                piece = AudioClip.Create("DeleteSoundPiece", cut.Length / clip.channels, clip.channels, clip.frequency, false);
                piece.SetData(cut, 0);
            }
            catch (System.Exception e) { Debug.LogWarning("Delete sound: not ready: " + e.Message); piece = null; }
        }

        void Release()
        {
            if (source != null && source.isPlaying) source.Stop();
            if (source != null) source.loop = false;
            carrying = false; looping = false; carryFade = -1f;
            endsAt = -1f;
            if (piece != null) Destroy(piece);
            piece = null;
        }

        /// <summary>
        /// Call once when a removal starts (one tile or a whole group). Returns true when the sound started; false when
        /// the switch is off, the phone is silent, or the sound is still playing from the removal before.
        /// </summary>
        public bool Play()
        {
            if (!UiPreferences.DeleteSound || source == null || !isActiveAndEnabled || IsPlaying || looping) return false;
            if (quietWhenPhoneIsSilent && PhoneIsSilent()) return false;
            Prepare();
            if (piece == null) return false;                // not ready: better no sound than a late one
            source.clip = piece; source.loop = false;
            source.volume = Loudness;
            source.time = 0f; carryFade = -1f;
            source.Play();
            endsAt = Time.unscaledTime + piece.length;
            return true;
        }

        /// <summary>
        /// The piece that is played, cut out of the whole recording: from where the sound starts (the silence before it
        /// skipped), `playSeconds` long, faded out at its end, and brought up to `loudestPoint`. Plain arithmetic on the
        /// samples, so it is covered by a test.
        /// </summary>
        public static float[] Cut(float[] samples, int channels, int frequency, bool skipSilence, float startSeconds, float playSeconds, float fadeSeconds,
            float loudestPoint, float mostGain)
        {
            if (samples == null || channels <= 0 || frequency <= 0) return new float[0];
            int frames = samples.Length / channels;
            float peakOfAll = 0f;
            for (int i = 0; i < samples.Length; i++) peakOfAll = Mathf.Max(peakOfAll, Mathf.Abs(samples[i]));
            if (peakOfAll <= 0.0001f) return new float[0];             // a silent file

            int first = 0;
            if (skipSilence)
            {
                float heard = peakOfAll * 0.05f;                        // "the sound has started" = a twentieth of its loudest point
                while (first < frames && Loudest(samples, first, channels) < heard) first++;
                first = Mathf.Max(0, first - frequency / 100);          // a hundredth of a second before it, so the start is not clipped
            }
            first = Mathf.Clamp(first + Mathf.RoundToInt(Mathf.Max(0f, startSeconds) * frequency), 0, Mathf.Max(0, frames - 1));
            int count = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(0.05f, playSeconds) * frequency), 1, frames - first);
            int fade = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(0f, fadeSeconds) * frequency), 0, count);
            int rise = Mathf.Min(count, frequency / 200);               // five thousandths of a second in, so it does not start with a click

            var cut = new float[count * channels];
            float peak = 0f;
            for (int f = 0; f < count; f++)
            {
                float shape = 1f;
                if (f < rise) shape = f / (float)rise;
                if (fade > 0 && f >= count - fade) shape *= (count - f) / (float)fade;
                for (int c = 0; c < channels; c++)
                {
                    float v = samples[(first + f) * channels + c] * shape;
                    cut[f * channels + c] = v;
                    peak = Mathf.Max(peak, Mathf.Abs(v));
                }
            }
            if (loudestPoint > 0f && peak > 0.0001f)
            {
                float gain = Mathf.Min(loudestPoint / peak, Mathf.Max(1f, mostGain));
                for (int i = 0; i < cut.Length; i++) cut[i] = Mathf.Clamp(cut[i] * gain, -1f, 1f);
            }
            return cut;
        }

        static float Loudest(float[] samples, int frame, int channels)
        {
            float most = 0f;
            for (int c = 0; c < channels; c++) most = Mathf.Max(most, Mathf.Abs(samples[frame * channels + c]));
            return most;
        }

        void Update()
        {
            if (endsAt > 0f && Time.unscaledTime >= endsAt) endsAt = -1f;
            // the loop glides between its two loudnesses (shaking, softer; carried, full)
            if (looping && source != null) source.volume = Mathf.MoveTowards(source.volume, LoopLoudness, 3f * Time.unscaledDeltaTime);
            if (carryFade > 0f && source != null)
            {
                // the carried sound dies away quickly; then the voice is free again
                source.volume = Mathf.MoveTowards(source.volume, 0f, Loudness * Time.unscaledDeltaTime / carryFade);
                if (source.volume <= 0.001f) { source.Stop(); source.loop = false; carryFade = -1f; }
            }
        }

        /// <summary>Android: the ringer is on silent or vibrate. Elsewhere, and when the phone will not say: false.</summary>
        static bool PhoneIsSilent()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var audio = activity.Call<AndroidJavaObject>("getSystemService", "audio"))
                    return audio != null && audio.Call<int>("getRingerMode") != 2;          // 2 = the ringer is on
            }
            catch (System.Exception) { return false; }
#else
            return false;
#endif
        }
    }
}
