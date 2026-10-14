using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        Switch,

        // Menus and feedback
        /// <summary>Pointer enters a button (mouse only).</summary>
        Hover,
        PopupOpen,
        PopupClose,
        /// <summary>A panel sliding in or out.</summary>
        Whoosh,
        /// <summary>Tap on something still locked.</summary>
        Locked,
        /// <summary>One rewarded ad watched.</summary>
        AdReward,
        /// <summary>A skin unlocked.</summary>
        Unlock,
        /// <summary>A skin put on.</summary>
        Equip,
        /// <summary>Better leaderboard rank or a new best Speedrun.</summary>
        RankUp,
        /// <summary>One "syllable" of a character talking (typewriter text).</summary>
        Talk,
        Undo,
        Reset
    }

    /// <summary>
    /// Plays the background music and the sound effects (move, push, win, cut, switch cat, door, switch, buttons, and the
    /// menu feedback sounds: hover, popups, unlocks, rank up, talking...).
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

        [Serializable]
        private sealed class ExtraClip
        {
            public SoundId id;
            public AudioClip clip;
        }

        [Tooltip("Clips for the other sounds (hover, popups, unlock, rank up...). A missing one uses a synthesized placeholder.")]
        [SerializeField] private List<ExtraClip> extraClips = new List<ExtraClip>();

        [Header("Music")]
        [SerializeField] private AudioSource musicSource;
        [Tooltip("Menu music (the chill loop), looped in every scene except the gameplay scene.")]
        [SerializeField] private AudioClip musicClip;
        [Tooltip("Music of the gameplay scene. When empty the menu music keeps playing there.")]
        [SerializeField] private AudioClip gameplayMusicClip;
        [Tooltip("Scene that plays the gameplay music.")]
        [SerializeField] private string gameplaySceneName = "GameplayScene";
        [Tooltip("Seconds for the old music to fade to silence before the new one starts.")]
        [SerializeField, Min(0f)] private float musicFadeOutDuration = 1.2f;
        [Tooltip("Seconds for the new music to fade in.")]
        [SerializeField, Min(0f)] private float musicFadeInDuration = 1.5f;
        [Tooltip("Music volume relative to the Music slider, so the loop stays in the background.")]
        [SerializeField, Range(0f, 1f)] private float musicVolumeScale = 0.6f;

        private static readonly HashSet<GridManager> BoundGrids = new HashSet<GridManager>();

        private bool isAutoCreated;
        private int lastPriorityFrame = -1;
        private int lastDoorFrame = -1;
        private int lastSwitchFrame = -1;
        private AudioSource pitchedSource;
        private float musicFade = 1f;
        private Coroutine musicRoutine;
        private float lastHoverTime = -1f;
        private readonly Dictionary<SoundId, AudioClip> placeholders = new Dictionary<SoundId, AudioClip>();

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

        /// <summary>Plays a sound at another pitch (1 = normal), e.g. to vary repeated blips.</summary>
        public static void Play(SoundId id, float pitch)
        {
            EnsureInstance().PlayPitched(id, pitch);
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

            pitchedSource = gameObject.AddComponent<AudioSource>();
            pitchedSource.playOnAwake = false;
            pitchedSource.loop = false;

            if (musicSource == null || musicSource == sfxSource)
            {
                musicSource = gameObject.AddComponent<AudioSource>();
            }

            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicFade = 1f;
            musicSource.volume = AudioManager.MusicVolume * musicVolumeScale;
            AudioClip startClip = MusicFor(SceneManager.GetActiveScene());
            if (startClip != null)
            {
                musicSource.clip = startClip;
                musicSource.Play();
            }

            SceneManager.sceneLoaded += HandleSceneLoaded;

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
                musicSource.volume = AudioManager.MusicVolume * musicVolumeScale * musicFade;
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (Instance == this)
            {
                UnsubscribeAll();
                Instance = null;
            }
        }

        // ---------------------------------------------------------------- music

        /// <summary>The menu loop everywhere, the gameplay loop in the gameplay scene.</summary>
        private AudioClip MusicFor(Scene scene)
        {
            return scene.name == gameplaySceneName && gameplayMusicClip != null ? gameplayMusicClip : musicClip;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single)
            {
                ChangeMusic(MusicFor(scene));
            }
        }

        /// <summary>
        /// Fades the current music to silence, then starts <paramref name="clip"/> and fades it in. Nothing happens when
        /// that clip is already playing (e.g. the next Speedrun level), so the loop is not restarted.
        /// </summary>
        public void ChangeMusic(AudioClip clip)
        {
            if (musicSource == null || clip == null || (musicSource.clip == clip && musicSource.isPlaying))
            {
                return;
            }

            if (musicRoutine != null)
            {
                StopCoroutine(musicRoutine);
            }

            musicRoutine = StartCoroutine(ChangeMusicRoutine(clip));
        }

        private IEnumerator ChangeMusicRoutine(AudioClip clip)
        {
            if (musicSource.isPlaying)
            {
                float start = musicFade;
                for (float elapsed = 0f; elapsed < musicFadeOutDuration; elapsed += Time.unscaledDeltaTime)
                {
                    musicFade = Mathf.Lerp(start, 0f, elapsed / musicFadeOutDuration);
                    yield return null;
                }
            }

            musicFade = 0f;
            musicSource.Stop();
            musicSource.clip = clip;
            musicSource.Play();

            for (float elapsed = 0f; elapsed < musicFadeInDuration; elapsed += Time.unscaledDeltaTime)
            {
                // Ease in: quiet for a moment, then up to full volume.
                float t = elapsed / musicFadeInDuration;
                musicFade = t * t;
                yield return null;
            }

            musicFade = 1f;
            musicRoutine = null;
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
            // Sweeping the mouse over a row of buttons should not rattle.
            if (id == SoundId.Hover)
            {
                if (Time.unscaledTime - lastHoverTime < 0.06f)
                {
                    return;
                }

                lastHoverTime = Time.unscaledTime;
            }

            AudioClip clip = GetClip(id);
            if (clip != null && sfxSource != null)
            {
                sfxSource.PlayOneShot(clip, AudioManager.SfxVolume);
            }
        }

        private void PlayPitched(SoundId id, float pitch)
        {
            AudioClip clip = GetClip(id);
            if (clip != null && pitchedSource != null)
            {
                pitchedSource.pitch = pitch;
                pitchedSource.PlayOneShot(clip, AudioManager.SfxVolume);
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
            }

            foreach (ExtraClip extra in extraClips)
            {
                if (extra != null && extra.id == id && extra.clip != null)
                {
                    return extra.clip;
                }
            }

            if (!placeholders.TryGetValue(id, out AudioClip placeholder))
            {
                placeholder = CreatePlaceholderClip(id);
                placeholders[id] = placeholder;
            }

            return placeholder;
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

                case SoundId.Hover:
                    // Tiny, quiet tick.
                    return CreateClip("Hover", 0.04f, t =>
                    {
                        float envelope = Mathf.Min(t / 0.002f, 1f) * Mathf.Exp(-t * 110f);
                        return 0.14f * envelope * Tone(t, 1700f, 2100f, 0.04f);
                    });

                case SoundId.PopupOpen:
                    // Soft rising "bloop".
                    return CreateClip("PopupOpen", 0.16f, t =>
                    {
                        float envelope = Mathf.Min(t / 0.006f, 1f) * Mathf.Exp(-t * 16f);
                        float wave = Tone(t, 380f, 920f, 0.16f);
                        return 0.32f * envelope * (wave + 0.25f * Mathf.Sign(wave));
                    });

                case SoundId.PopupClose:
                    // The same bloop going down.
                    return CreateClip("PopupClose", 0.14f, t =>
                    {
                        float envelope = Mathf.Min(t / 0.006f, 1f) * Mathf.Exp(-t * 18f);
                        float wave = Tone(t, 820f, 330f, 0.14f);
                        return 0.28f * envelope * (wave + 0.25f * Mathf.Sign(wave));
                    });

                case SoundId.Whoosh:
                {
                    // Noise through a low-pass filter whose cutoff sweeps up and down.
                    const float duration = 0.38f;
                    float filtered = 0f;
                    return CreateClip("Whoosh", duration, t =>
                    {
                        float shape = Mathf.Sin(Mathf.PI * t / duration);
                        float cutoff = 0.02f + 0.25f * shape;
                        filtered += cutoff * ((UnityEngine.Random.value * 2f - 1f) - filtered);
                        return 0.9f * shape * shape * filtered;
                    });
                }

                case SoundId.Locked:
                    // Two dull descending "bonk"s.
                    return CreateClip("Locked", 0.22f, t =>
                    {
                        bool second = t >= 0.09f;
                        float local = second ? t - 0.09f : t;
                        float frequency = second ? 150f : 210f;
                        float envelope = Mathf.Min(local / 0.003f, 1f) * Mathf.Exp(-local * 26f);
                        return 0.22f * envelope * Mathf.Sign(Mathf.Sin(2f * Mathf.PI * frequency * t));
                    });

                case SoundId.AdReward:
                    // Coin: B5 then a ringing E6.
                    return CreateClip("AdReward", 0.32f, t =>
                    {
                        bool second = t >= 0.07f;
                        float local = second ? t - 0.07f : t;
                        float frequency = second ? 1318.5f : 987.8f;
                        float envelope = Mathf.Min(local / 0.002f, 1f) * Mathf.Exp(-local * (second ? 9f : 30f));
                        float phase = 2f * Mathf.PI * frequency * t;
                        return envelope * (0.18f * Mathf.Sin(phase) + 0.08f * Mathf.Sign(Mathf.Sin(phase)));
                    });

                case SoundId.Unlock:
                {
                    // Fast sparkling arpeggio G5 C6 E6 G6 C7, the last note ringing, with a shimmer on top.
                    float[] notes = { 783.99f, 1046.5f, 1318.5f, 1568f, 2093f };
                    const float noteLength = 0.065f;
                    return CreateClip("Unlock", 1f, t =>
                    {
                        int index = Mathf.Min(Mathf.FloorToInt(t / noteLength), notes.Length - 1);
                        float local = t - index * noteLength;
                        float decay = index == notes.Length - 1 ? 3.5f : 14f;
                        float envelope = Mathf.Min(local / 0.003f, 1f) * Mathf.Exp(-local * decay);
                        float tone = Mathf.Sin(2f * Mathf.PI * notes[index] * t) + 0.35f * Mathf.Sin(4f * Mathf.PI * notes[index] * t);
                        float shimmer = t < 0.3f ? 0f : Mathf.Sin(2f * Mathf.PI * 3136f * t) * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 18f * t)) * Mathf.Exp(-(t - 0.3f) * 5f);
                        return 0.22f * envelope * tone + 0.05f * shimmer;
                    });
                }

                case SoundId.Equip:
                    // Quick upward "pop" with a click.
                    return CreateClip("Equip", 0.13f, t =>
                    {
                        float envelope = Mathf.Min(t / 0.003f, 1f) * Mathf.Exp(-t * 24f);
                        float click = (UnityEngine.Random.value * 2f - 1f) * Mathf.Exp(-t * 300f);
                        return 0.32f * envelope * Tone(t, 300f, 1300f, 0.13f) + 0.15f * click;
                    });

                case SoundId.RankUp:
                {
                    // Chiptune "level up": C5 E5 G5 C6 E6 and a held G6 with vibrato.
                    float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f, 1568f };
                    const float noteLength = 0.075f;
                    return CreateClip("RankUp", 0.95f, t =>
                    {
                        int index = Mathf.Min(Mathf.FloorToInt(t / noteLength), notes.Length - 1);
                        float local = t - index * noteLength;
                        bool last = index == notes.Length - 1;
                        float envelope = Mathf.Min(local / 0.003f, 1f) * (last ? Mathf.Exp(-local * 2.5f) : Mathf.Exp(-local * 8f));
                        float vibrato = last ? 1f + 0.012f * Mathf.Sin(2f * Mathf.PI * 7f * local) : 1f;
                        float wave = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * notes[index] * vibrato * t));
                        return 0.15f * envelope * wave;
                    });
                }

                case SoundId.Talk:
                    // One square-wave syllable; played at random pitches it sounds like mumbling.
                    return CreateClip("Talk", 0.05f, t =>
                    {
                        float envelope = Mathf.Min(t / 0.004f, 1f) * Mathf.Exp(-t * 45f);
                        return 0.1f * envelope * Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 520f * t));
                    });

                case SoundId.Undo:
                    // Short falling blip, the reverse of a move.
                    return CreateClip("Undo", 0.11f, t =>
                    {
                        float envelope = Mathf.Min(t / 0.004f, 1f) * Mathf.Exp(-t * 26f);
                        return 0.3f * envelope * Tone(t, 900f, 480f, 0.11f);
                    });

                case SoundId.Reset:
                    // "Rewind": a fast falling square sweep with a flutter.
                    return CreateClip("Reset", 0.32f, t =>
                    {
                        float envelope = Mathf.Min(t / 0.005f, 1f) * Mathf.Exp(-t * 6f);
                        float flutter = 0.6f + 0.4f * Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 28f * t));
                        return 0.13f * envelope * flutter * Mathf.Sign(Tone(t, 1300f, 180f, 0.32f));
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
