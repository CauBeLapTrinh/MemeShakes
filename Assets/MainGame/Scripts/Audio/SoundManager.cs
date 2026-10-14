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
        Lose
    }

    /// <summary>
    /// Plays the gameplay sound effects (move, push a crate, win, lose). It listens to every <see cref="GridManager"/>
    /// by itself, so no wiring is needed: it is created automatically on first use and survives scene loads.
    /// Volume follows the SFX slider of <see cref="AudioManager"/>.
    ///
    /// The clips live in Assets/MainGame/Sounds (Move, Push, Win, Lose). They are assigned on the prefab
    /// Assets/MainGame/Resources/SoundManager.prefab, which is loaded automatically: replace the wav files (or the
    /// clips on that prefab) to change the sounds. If the prefab or a clip is missing, a short synthesized placeholder is used.
    /// "Lose" is played when a closing door cuts an arm; "Push" for a crate or the other cat.
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

        private static readonly HashSet<GridManager> BoundGrids = new HashSet<GridManager>();

        private bool isAutoCreated;
        private int lastPriorityFrame = -1;

        public static SoundManager Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
            BoundGrids.Clear();
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

            BuildPlaceholderClips();

            foreach (GridManager grid in BoundGrids)
            {
                if (grid != null)
                {
                    Subscribe(grid);
                }
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
        }

        private void Unsubscribe(GridManager grid)
        {
            grid.MoveResolved -= HandleMoveResolved;
            grid.ArmCut -= HandleArmCut;
            grid.Won -= HandleWon;
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
            PlayInternal(SoundId.Lose);
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
