using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GlowUp.Common
{
    /// <summary>
    /// Plays a short sound whenever a button is tapped, in every scene. The five sounds are in the list
    /// below (replace a clip to change a sound); which one is used, whether sounds are on and how loud
    /// they are is the user's choice on the Settings page.
    ///
    /// One of these lives on the UiServices object and stays alive across scenes. It finds the buttons
    /// by itself, including ones a screen creates later (list rows, tiles), so no button needs a script.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class UiSoundPlayer : MonoBehaviour
    {
        public static UiSoundPlayer Instance { get; private set; }

        [Tooltip("The tap sounds the user can choose from (shown on the Settings page in this order)")]
        [SerializeField] private AudioClip[] tapSounds = new AudioClip[0];
        [Tooltip("The names shown for them on the Settings page")]
        [SerializeField] private string[] soundNames = { "Soft tap", "Pop", "Click", "Bubble", "Chime" };
        [Tooltip("How often the scene is checked for new buttons (seconds)")]
        [SerializeField] private float lookForButtonsEvery = 0.6f;
        [Tooltip("Buttons with one of these names stay silent (the dim layer behind a pop-up, for example)")]
        [SerializeField] private string[] silentButtonNames = { "Backdrop" };

        AudioSource source;
        readonly HashSet<int> wired = new HashSet<int>();
        float timer;

        public int SoundCount => tapSounds != null ? tapSounds.Length : 0;
        public string NameOf(int index) => soundNames != null && index >= 0 && index < soundNames.Length ? soundNames[index] : "Sound " + (index + 1);

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            source = GetComponent<AudioSource>();
            source.playOnAwake = false; source.loop = false; source.spatialBlend = 0f;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Instance = null;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) { wired.Clear(); timer = lookForButtonsEvery; }

        void Update()
        {
            if (Instance != this) return;
            timer += Time.unscaledDeltaTime;
            if (timer < lookForButtonsEvery) return;
            timer = 0f;
            foreach (Button button in FindObjectsOfType<Button>(true))
            {
                if (!wired.Add(button.GetInstanceID()) || IsSilent(button.name)) continue;
                button.onClick.AddListener(PlayTap);
            }
        }

        bool IsSilent(string buttonName)
        {
            if (silentButtonNames == null) return false;
            foreach (string name in silentButtonNames) if (name == buttonName) return true;
            return false;
        }

        /// <summary>The user's chosen tap sound (nothing when sounds are switched off).</summary>
        public void PlayTap()
        {
            if (!UiPreferences.TapSounds) return;
            Play(UiPreferences.TapSoundIndex);
        }

        /// <summary>Plays one of the sounds once, even when tap sounds are off (the Settings page uses it to let the user listen).</summary>
        public void Play(int index)
        {
            if (source == null || tapSounds == null || tapSounds.Length == 0) return;
            AudioClip clip = tapSounds[Mathf.Clamp(index, 0, tapSounds.Length - 1)];
            if (clip != null) source.PlayOneShot(clip, UiPreferences.TapSoundVolume);
        }
    }
}
