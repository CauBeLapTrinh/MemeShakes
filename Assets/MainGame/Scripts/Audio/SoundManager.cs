using System;
using System.Collections.Generic;
using UnityEngine;

namespace Catshakes
{
    /// <summary>The gameplay sounds managed by <see cref="SoundManager"/>.</summary>
    public enum SoundId
    {
        Move,
        Push,
        Win,
        Lose,
        SwitchCat,
        Cut,
        Button,
        Door,
        Switch
    }

    /// <summary>
    /// Plays the background music and the sound effects (move, push, win, cut, switch cat, door, switch, buttons).
    /// It listens to every <see cref="GridManager"/> by itself, so no wiring is needed: it is created automatically when
    /// the game starts and survives scene loads. Music volume follows the Music slider of <see cref="AudioManager"/>,
    /// effects follow the SFX slider.
    ///
    /// The clips live in Assets/MainGame/Sounds. They are assigned on the prefab
    /// Assets/MainGame/Resources/SoundManager.prefab, which is loaded automatically: replace the wav files (or the
    /// clips on that prefab) to change the sounds. If the prefab or an effect clip is missing, a short synthesized
    /// placeholder is used. "Cut" is played when a closing door cuts an arm; "Push" for a crate or the other cat.
    /// </summary>
    public sealed class SoundManager : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private const string ResourcePath = "SoundManager";

        [SerializeField] private AudioSource sfxSource;
        [SerializeField] private AudioClip moveClip;
        [SerializeField] private AudioClip pushClip;
        [SerializeField] private AudioClip winClip;
        [SerializeField] private AudioClip loseClip;
        [SerializeField] private AudioClip switchCatClip;
        [SerializeField] private AudioClip cutClip;
        [SerializeField] private AudioClip buttonClip;
        [SerializeField] private AudioClip doorClip;
        [SerializeField] private AudioClip switchClip;

        [Header("Music")]
        [SerializeField] private AudioSource musicSource;
        [Tooltip("Background music, looped in every scene.")]
        [SerializeField] private AudioClip musicClip;
        [Tooltip("Music volume relative to the Music slider, so the loop stays in the background.")]
        [SerializeField, Range(0f, 1f)] private float musicVolumeScale = 0.6f;

        private static readonly HashSet<GridManager> BoundGrids = new HashSet<GridManager>();

        private bool isAutoCreated;
        private int lastPriorityFrame = -1;
        private int lastDoorFrame = -1;
        private int lastSwitchFrame = -1;

        public static SoundManager Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
            BoundGrids.Clear();
        }

        // Created as soon as the first scene is loaded, so the music plays from the start.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateOnStart()
        {
            EnsureInstance();
        }

        /// <summary>Plays a sound, creating the manager when none exists yet.</summary>
        public static void Play(SoundId id)
        {
            EnsureInstance().PlayInternal(id);
        }

        /// <summary>Makes the manager react to the events of <paramref name="grid"/>. Called by the GridManager itself.</summary>
        public static void BindGrid(GridManager grid)
        {
            if (grid == null)
            {
                return;
            }

            BoundGrids.Add(grid);
            EnsureInstance().Subscribe(grid);
        }

        public static void UnbindGrid(GridManager grid)
        {
            if (grid == null)
            {
                return;
            }

            BoundGrids.Remove(grid);
            if (Instance != null)
            {
                Instance.Unsubscribe(grid);
            }
        }

        private static SoundManager EnsureInstance()
        {
            if (Instance == null)
            {
                SoundManager prefab = Resources.Load<SoundManager>(ResourcePath);
                SoundManager manager = prefab != null
                    ? Instantiate(prefab)
                    : new GameObject("SoundManager").AddComponent<SoundManager>();
                manager.gameObject.name = "SoundManager";
                manager.isAutoCreated = true;
            }

            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                if (!Instance.isAutoCreated)
                {
                    Destroy(gameObject);
                    return;
                }

                // A manager placed in a scene (with real clips) replaces the automatically created one.
                SoundManager automatic = Instance;
                automatic.UnsubscribeAll();
                Instance = null;
                Destroy(automatic.gameObject);
            }

            Instance = this;

            if (transform.parent != null)
            {
                transform.SetParent(null, false);
            }

            DontDestroyOnLoad(gameObject);

            if (sfxSource == null)
            {
                sfxSource = gameObject.AddComponent<AudioSource>();
            }

            sfxSource.playOnAwake = false;
            sfxSource.loop = false;

            if (musicSource == null || musicSource == sfxSource)
            {
                musicSource = gameObject.AddComponent<AudioSource>();
            }

            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.volume = AudioManager.MusicVolume * musicVolumeScale;
            if (musicClip != null)
            {
                musicSource.clip = musicClip;
                musicSource.Play();
            }

            BuildPlaceholderClips();

            foreach (GridManager grid in BoundGrids)
            {
                if (grid != null)
                {
                    Subscribe(grid);
                }
            }
        }

        private void Update()
        {
            if (musicSource != null)
            {
                musicSource.volume = AudioManager.MusicVolume * musicVolumeScale;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                UnsubscribeAll();
                Instance = null;
            }
        }

        // ---------------------------------------------------------------- events

        private void Subscribe(GridManager grid)
        {
            Unsubscribe(grid);
            grid.MoveResolved += HandleMoveResolved;
            grid.ArmCut += HandleArmCut;
            grid.Won += HandleWon;
            grid.CatSwitched += HandleCatSwitched;
            grid.DoorChanged += HandleDoorChanged;
            grid.SwitchChanged += HandleSwitchChanged;
        }

        private void Unsubscribe(GridManager grid)
        {
            grid.MoveResolved -= HandleMoveResolved;
            grid.ArmCut -= HandleArmCut;
            grid.Won -= HandleWon;
            grid.CatSwitched -= HandleCatSwitched;
            grid.DoorChanged -= HandleDoorChanged;
            grid.SwitchChanged -= HandleSwitchChanged;
        }

        private void UnsubscribeAll()
        {
            foreach (GridManager grid in BoundGrids)
            {
                if (grid != null)
                {
                    Unsubscribe(grid);
                }
            }
        }

        private void HandleMoveResolved(CatId cat, MoveOutcome outcome)
        {
            // The move that wins or cuts an arm already played its own sound in this frame.
            if (lastPriorityFrame == Time.frameCount)
            {
                return;
            }

            switch (outcome)
            {
                case MoveOutcome.Extended:
                case MoveOutcome.Retracted:
                    PlayInternal(SoundId.Move);
                    break;
                case MoveOutcome.ExtendedAndPushedBarrel:
                case MoveOutcome.ExtendedAndPushedCat:
                    PlayInternal(SoundId.Push);
                    break;
            }
        }

        private void HandleArmCut(CatId cat)
        {
            lastPriorityFrame = Time.frameCount;
            PlayInternal(SoundId.Cut);
        }

        private void HandleCatSwitched(CatId cat)
        {
            PlayInternal(SoundId.SwitchCat);
        }

        // Several doors can move in one step; one sound per frame is enough.
        private void HandleDoorChanged(Vector2Int cell, bool open)
        {
            if (lastDoorFrame != Time.frameCount)
            {
                lastDoorFrame = Time.frameCount;
                PlayInternal(SoundId.Door);
            }
        }

        private void HandleSwitchChanged(Vector2Int cell, bool active)
        {
            if (lastSwitchFrame != Time.frameCount)
            {
                lastSwitchFrame = Time.frameCount;
                PlayInternal(SoundId.Switch);
            }
        }

        private void HandleWon()
        {
            lastPriorityFrame = Time.frameCount;
            PlayInternal(SoundId.Win);
        }

        // ---------------------------------------------------------------- playback

        private void PlayInternal(SoundId id)
        {
            AudioClip clip = GetClip(id);
            if (clip != null && sfxSource != null)
            {
                sfxSource.PlayOneShot(clip, AudioManager.SfxVolume);
            }
        }

        private AudioClip GetClip(SoundId id)
        {
            switch (id)
            {
                case SoundId.Move: return moveClip;
                case SoundId.Push: return pushClip;
                case SoundId.Win: return winClip;
                case SoundId.Lose: return loseClip;
                case SoundId.SwitchCat: return switchCatClip;
                case SoundId.Cut: return cutClip;
                case SoundId.Button: return buttonClip;
                case SoundId.Door: return doorClip;
                case SoundId.Switch: return switchClip;
                default: return null;
            }
        }

        // ---------------------------------------------------------------- placeholder sounds

        private void BuildPlaceholderClips()
        {
            if (moveClip == null) moveClip = CreatePlaceholderClip(SoundId.Move);
            if (pushClip == null) pushClip = CreatePlaceholderClip(SoundId.Push);
            if (winClip == null) winClip = CreatePlaceholderClip(SoundId.Win);
            if (loseClip == null) loseClip = CreatePlaceholderClip(SoundId.Lose);
            if (switchCatClip == null) switchCatClip = CreatePlaceholderClip(SoundId.SwitchCat);
            if (cutClip == null) cutClip = CreatePlaceholderClip(SoundId.Cut);
            if (buttonClip == null) buttonClip = CreatePlaceholderClip(SoundId.Button);
            if (doorClip == null) doorClip = CreatePlaceholderClip(SoundId.Door);
            if (switchClip == null) switchClip = CreatePlaceholderClip(SoundId.Switch);
        }

        /// <summary>
        /// Synthesizes the placeholder sound for <paramref name="id"/>. The editor menu
        /// "Catshakes / Setup Sound And Effect Assets" saves these as wav files in Assets/MainGame/Sounds.
        /// </summary>
        public static AudioClip CreatePlaceholderClip(SoundId id)
        {
            switch (id)
            {
                case SoundId.Move:
                    // Short rising blip.
                    return CreateClip("Move", 0.09f, t =>
                    {
                        float envelope = Mathf.Min(t / 0.005f, 1f) * Mathf.Exp(-t * 28f);
                        return 0.35f * envelope * Tone(t, 440f, 700f, 0.09f);
                    });

                case SoundId.Push:
                    // Low wooden thud with a burst of noise at the start.
                    return CreateClip("Push", 0.2f, t =>
                    {
                        float thud = Tone(t, 95f, 45f, 0.2f) * Mathf.Exp(-t * 16f);
                        float knock = (UnityEngine.Random.value * 2f - 1f) * Mathf.Exp(-t * 90f);
                        return 0.6f * thud + 0.25f * knock;
                    });

                case SoundId.Win:
                {
                    // Rising arpeggio C - E - G - C.
                    float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
                    const float noteLength = 0.13f;
                    return CreateClip("Win", noteLength * 3f + 0.5f, t =>
                    {
                        int index = Mathf.Min(Mathf.FloorToInt(t / noteLength), notes.Length - 1);
                        float local = t - index * noteLength;
                        float decay = index == notes.Length - 1 ? 4f : 10f;
                        float envelope = Mathf.Min(local / 0.004f, 1f) * Mathf.Exp(-local * decay);
                        float frequency = notes[index];
                        float tone = Mathf.Sin(2f * Mathf.PI * frequency * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * frequency * t);
                        return 0.3f * envelope * tone;
                    });
                }

                case SoundId.SwitchCat:
                    // Two quick square-wave notes going up, like a chiptune "swap".
                    return CreateClip("SwitchCat", 0.16f, t =>
                    {
                        float frequency = t < 0.07f ? 660f : 990f;
                        float local = t < 0.07f ? t : t - 0.07f;
                        float envelope = Mathf.Min(local / 0.003f, 1f) * Mathf.Exp(-local * 22f);
                        return 0.18f * envelope * Mathf.Sign(Mathf.Sin(2f * Mathf.PI * frequency * t));
                    });

                case SoundId.Cut:
                    // Metallic "shing" (falling noisy sweep) followed by a dull chop.
                    return CreateClip("Cut", 0.32f, t =>
                    {
                        float swish = (UnityEngine.Random.value * 2f - 1f) * Mathf.Exp(-t * 18f) * Mathf.Min(t / 0.01f, 1f);
                        float ring = Tone(t, 2400f, 1500f, 0.32f) * Mathf.Exp(-t * 14f);
                        float chopTime = Mathf.Max(0f, t - 0.06f);
                        float chop = t < 0.06f ? 0f : Tone(chopTime, 160f, 60f, 0.26f) * Mathf.Exp(-chopTime * 20f);
                        return 0.22f * swish + 0.18f * ring + 0.5f * chop;
                    });

                case SoundId.Button:
                    // Soft click.
                    return CreateClip("Button", 0.06f, t =>
                    {
                        float envelope = Mathf.Min(t / 0.002f, 1f) * Mathf.Exp(-t * 60f);
                        return 0.3f * envelope * Tone(t, 1200f, 800f, 0.06f);
                    });

                case SoundId.Door:
                    // Low rumble of a heavy door sliding.
                    return CreateClip("Door", 0.28f, t =>
                    {
                        float envelope = Mathf.Min(t / 0.02f, 1f) * Mathf.Exp(-t * 9f);
                        float noise = UnityEngine.Random.value * 2f - 1f;
                        float rumble = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 70f * t));
                        return envelope * (0.15f * rumble + 0.12f * noise);
                    });

                case SoundId.Switch:
                    // Short mechanical click with a tiny tone.
                    return CreateClip("Switch", 0.1f, t =>
                    {
                        float envelope = Mathf.Min(t / 0.002f, 1f) * Mathf.Exp(-t * 45f);
                        float noise = UnityEngine.Random.value * 2f - 1f;
                        return envelope * (0.2f * noise + 0.2f * Mathf.Sin(2f * Mathf.PI * 1500f * t));
                    });

                default:
                    // Falling buzzy tone.
                    return CreateClip("Lose", 0.45f, t =>
                    {
                        float envelope = Mathf.Min(t / 0.005f, 1f) * Mathf.Exp(-t * 5f);
                        float wave = Tone(t, 330f, 110f, 0.45f);
                        return 0.35f * envelope * (wave + 0.4f * Mathf.Sign(wave));
                    });
            }
        }

        /// <summary>Sine whose frequency glides linearly from <paramref name="startHz"/> to <paramref name="endHz"/> over <paramref name="duration"/>.</summary>
        private static float Tone(float t, float startHz, float endHz, float duration)
        {
            float phase = startHz * t + (endHz - startHz) * t * t / (2f * duration);
            return Mathf.Sin(2f * Mathf.PI * phase);
        }

        private static AudioClip CreateClip(string clipName, float duration, Func<float, float> generator)
        {
            int sampleCount = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                data[i] = Mathf.Clamp(generator(i / (float)SampleRate), -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(clipName, sampleCount, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
