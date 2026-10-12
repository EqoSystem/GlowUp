using UnityEngine;

namespace GlowUp.Common
{
    /// <summary>
    /// The "freezing" sound of Recent Scans: played ONCE each time a scan (or a group of scans) is removed by
    /// dragging it into the shadow, at the moment the tile starts to pop.
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
    /// The clip is Assets/_Project/Audio/Delete_FreezingCold.mp3 (see the README in that folder for where it came from).
    /// What was measured on it (12 October 2026): 6.7 seconds, the first 0.4 seconds silent, and quiet throughout
    /// (its loudest point is about a fifth of full loudness).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class DeleteSoundPlayer : MonoBehaviour
    {
        [Tooltip("The sound. Import settings: Load Type 'Decompress On Load' and 'Preload Audio Data' off, so it is only loaded while the switch in Settings is on.")]
        [SerializeField] private AudioClip clip;

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

        AudioSource source;
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
        }

        void OnEnable() { UiPreferences.Changed += Prepare; Prepare(); }
        void OnDisable() { UiPreferences.Changed -= Prepare; Release(); }

        /// <summary>Makes the piece ready while the user's switch is on, and lets it go while the switch is off.</summary>
        void Prepare()
        {
            if (!UiPreferences.DeleteSound) { Release(); return; }
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
            if (!UiPreferences.DeleteSound || source == null || !isActiveAndEnabled || IsPlaying) return false;
            if (quietWhenPhoneIsSilent && PhoneIsSilent()) return false;
            Prepare();
            if (piece == null) return false;                // not ready: better no sound than a late one
            source.clip = piece;
            source.volume = Mathf.Clamp01(volume * (followSoundVolumeSetting ? UiPreferences.TapSoundVolume : 1f));
            source.time = 0f;
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
