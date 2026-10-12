using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// Persistent audio hub: one looping music source and one one-shot SFX source, each with a volume that is
    /// stored in PlayerPrefs. The Settings popup reads and writes the volumes through the static properties,
    /// so they also work (and are saved) when no AudioManager exists in the scene.
    ///
    /// Assign clips to <see cref="menuMusic"/> and <see cref="clickClip"/> when audio assets are available.
    /// Duplicates created by reloading the menu scene destroy themselves.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        public const string MusicVolumeKey = "Catshakes.MusicVolume";
        public const string SfxVolumeKey = "Catshakes.SfxVolume";

        private const float DefaultMusicVolume = 0.7f;
        private const float DefaultSfxVolume = 0.8f;

        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioSource sfxSource;
        [Tooltip("Looped while the menu is shown. Optional.")]
        [SerializeField] private AudioClip menuMusic;
        [Tooltip("Played by every menu button. Optional.")]
        [SerializeField] private AudioClip clickClip;

        public static AudioManager Instance { get; private set; }

        /// <summary>Music volume from 0 to 1. Saved to PlayerPrefs (call <see cref="SaveSettings"/> to flush to disk).</summary>
        public static float MusicVolume
        {
            get { return Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume)); }
            set
            {
                PlayerPrefs.SetFloat(MusicVolumeKey, Mathf.Clamp01(value));
                if (Instance != null)
                {
                    Instance.ApplyVolumes();
                }
            }
        }

        /// <summary>Sound-effect volume from 0 to 1. Saved to PlayerPrefs (call <see cref="SaveSettings"/> to flush to disk).</summary>
        public static float SfxVolume
        {
            get { return Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumeKey, DefaultSfxVolume)); }
            set
            {
                PlayerPrefs.SetFloat(SfxVolumeKey, Mathf.Clamp01(value));
                if (Instance != null)
                {
                    Instance.ApplyVolumes();
                }
            }
        }

        public static void SaveSettings()
        {
            PlayerPrefs.Save();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // DontDestroyOnLoad only works on root objects.
            if (transform.parent != null)
            {
                transform.SetParent(null, false);
            }

            DontDestroyOnLoad(gameObject);

            if (musicSource == null)
            {
                musicSource = gameObject.AddComponent<AudioSource>();
            }

            if (sfxSource == null)
            {
                sfxSource = gameObject.AddComponent<AudioSource>();
            }

            musicSource.playOnAwake = false;
            musicSource.loop = true;
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;

            ApplyVolumes();

            if (menuMusic != null)
            {
                PlayMusic(menuMusic);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void PlayMusic(AudioClip clip)
        {
            if (clip == null || (musicSource.clip == clip && musicSource.isPlaying))
            {
                return;
            }

            musicSource.clip = clip;
            musicSource.Play();
        }

        public void PlaySfx(AudioClip clip)
        {
            if (clip != null)
            {
                sfxSource.PlayOneShot(clip);
            }
        }

        public void PlayClick()
        {
            PlaySfx(clickClip);
        }

        private void ApplyVolumes()
        {
            if (musicSource != null)
            {
                musicSource.volume = MusicVolume;
            }

            if (sfxSource != null)
            {
                sfxSource.volume = SfxVolume;
            }
        }
    }
}
