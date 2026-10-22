using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Catshakes
{
    /// <summary>
    /// Persistent singleton that owns the scene flow (Title, MainMenu, LevelSelect, Gameplay), the level catalog,
    /// saved progress and the Speedrun timer.
    ///
    /// Place it (on a root GameObject) in the Title scene, which must be the first scene in Build Settings.
    /// It survives scene loads through DontDestroyOnLoad.
    ///
    /// Speedrun: starts at level 1, runs one continuous timer across all levels and only counts time spent
    /// playing (scene loads, fades and the short delay after a win are excluded). The best time is saved.
    /// </summary>
    public sealed class GameSceneManager : MonoBehaviour
    {
        private const string UnlockedLevelsKey = "Catshakes.UnlockedLevels";
        private const string BestSpeedrunKey = "Catshakes.BestSpeedrunSeconds";

        /// <summary>PlayerPrefs flag (1 = set) written when the last level of the catalog is solved. Unlocks Speed Run.</summary>
        public const string AllLevelsClearedKey = "AllLevelsCleared";
        private const float MaxTimerStepSeconds = 0.25f;

        [Header("Scene names (must match Build Settings)")]
        [SerializeField] private string titleSceneName = "Title";
        [SerializeField] private string mainMenuSceneName = "MainMenu";
        [SerializeField] private string levelSelectSceneName = "LevelSelect";
        [SerializeField] private string gameplaySceneName = "Gameplay";

        [Header("Levels (in play order)")]
        [SerializeField] private LevelDataSO[] levels = new LevelDataSO[0];

        [Header("Testing")]
        [Tooltip("Opens every level (and Speed Run) without playing through the game. Turn it on before building a test build for a phone, and off for release.")]
        [SerializeField] private bool unlockAllLevels;

        [Header("Transition (optional)")]
        [Tooltip("Full-screen black Image with a CanvasGroup, kept under this object. Leave empty for instant loads.")]
        [SerializeField] private CanvasGroup fadeGroup;
        [SerializeField, Min(0f)] private float fadeDuration = 0.25f;
        [Tooltip("Fade out / fade in time (each) when a solved level hands over to the next one. Slower than a menu change so it feels like a scene cut.")]
        [SerializeField, Min(0f)] private float levelTransitionFadeDuration = 0.4f;
        [Tooltip("Black screen is held at least this long between the two fades, so a fast load does not flash.")]
        [SerializeField, Min(0f)] private float minBlackHoldSeconds = 0.08f;

        private bool levelCompletedPending;
        private bool isTimerPausedByGame;
        private bool isApplicationPaused;

        public static GameSceneManager Instance { get; private set; }

        public IReadOnlyList<LevelDataSO> Levels => levels;
        public int LevelCount => levels != null ? levels.Length : 0;
        public SceneId CurrentScene { get; private set; } = SceneId.Title;

        /// <summary>The scene most recently asked for with <see cref="LoadScene"/>. A shared menu scene uses it to open the right panel.</summary>
        public SceneId LastRequestedScene { get; private set; } = SceneId.Title;
        public int CurrentLevelIndex { get; private set; }
        public bool IsTransitioning { get; private set; }
        public bool IsSpeedrunActive { get; private set; }
        public float SpeedrunElapsedSeconds { get; private set; }

        public LevelDataSO CurrentLevel
        {
            get { return IsValidLevelIndex(CurrentLevelIndex) ? levels[CurrentLevelIndex] : null; }
        }

        public bool HasNextLevel => CurrentLevelIndex + 1 < LevelCount;
        public float BestSpeedrunSeconds => PlayerPrefs.GetFloat(BestSpeedrunKey, -1f);
        public bool HasBestSpeedrun => BestSpeedrunSeconds > 0f;

        /// <summary>True once the player has cleared every level; the Speed Run button of the menu depends on it.</summary>
        public static bool IsSpeedrunUnlocked => (Instance != null && Instance.unlockAllLevels) || PlayerPrefs.GetInt(AllLevelsClearedKey, 0) == 1;

        /// <summary>True while the "Unlock All Levels" testing toggle is on.</summary>
        public bool AreAllLevelsUnlocked => unlockAllLevels;

        /// <summary>How many levels (counted from the first) the player may open.</summary>
        public int UnlockedLevelCount
        {
            get
            {
                if (unlockAllLevels)
                {
                    return Mathf.Max(1, LevelCount);
                }

                return Mathf.Clamp(PlayerPrefs.GetInt(UnlockedLevelsKey, 1), 1, Mathf.Max(1, LevelCount));
            }
        }

        /// <summary>True while the Speedrun clock is advancing.</summary>
        public bool IsTimerRunning
        {
            get
            {
                return IsSpeedrunActive
                       && CurrentScene == SceneId.Gameplay
                       && !levelCompletedPending
                       && !isTimerPausedByGame
                       && !isApplicationPaused
                       && !IsTransitioning;
            }
        }

        /// <summary>Raised after a scene has finished loading.</summary>
        public event Action<SceneId> SceneLoaded;

        /// <summary>Raised when the handshake of the current level was reported (after progress and speedrun state were updated).</summary>
        public event Action LevelCompleted;

        /// <summary>Raised when the last speedrun level is done: total seconds and whether it is a new personal best.</summary>
        public event Action<float, bool> SpeedrunFinished;

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

            if (unlockAllLevels)
            {
                Debug.LogWarning("GameSceneManager: 'Unlock All Levels' is ON (testing build). Turn it off before release.", this);
            }

            // DontDestroyOnLoad only works on root objects.
            if (transform.parent != null)
            {
                transform.SetParent(null, false);
            }

            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;

            // Phones run at 30 fps unless asked; the animations and the scrolling backgrounds want 60.
            Application.targetFrameRate = 60;

            if (TryResolveSceneId(SceneManager.GetActiveScene().name, out SceneId activeScene))
            {
                CurrentScene = activeScene;
            }

            if (fadeGroup != null)
            {
                fadeGroup.alpha = 0f;
                fadeGroup.blocksRaycasts = false;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                Instance = null;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            isApplicationPaused = paused;
        }

        private void Update()
        {
            if (IsTimerRunning)
            {
                SpeedrunElapsedSeconds += Mathf.Min(Time.unscaledDeltaTime, MaxTimerStepSeconds);
            }
        }

        // ---------------------------------------------------------------- navigation

        public void LoadScene(SceneId scene)
        {
            LoadScene(scene, fadeDuration);
        }

        private void LoadScene(SceneId scene, float fadeSeconds)
        {
            if (IsTransitioning)
            {
                return;
            }

            LastRequestedScene = scene;
            StartCoroutine(LoadSceneRoutine(scene, fadeSeconds));
        }

        /// <summary>Starts a level in normal mode. Locked levels are refused.</summary>
        public void StartLevel(int levelIndex)
        {
            if (!IsValidLevelIndex(levelIndex))
            {
                Debug.LogError("GameSceneManager.StartLevel: invalid level index " + levelIndex + ".", this);
                return;
            }

            if (levelIndex >= UnlockedLevelCount)
            {
                Debug.LogWarning("GameSceneManager.StartLevel: level " + levelIndex + " is still locked.", this);
                return;
            }

            IsSpeedrunActive = false;
            BeginLevel(levelIndex);
        }

        /// <summary>Starts (or restarts) a Speedrun from the first level with the timer at zero.</summary>
        public void StartSpeedrun()
        {
            if (LevelCount == 0)
            {
                Debug.LogError("GameSceneManager.StartSpeedrun: no levels are assigned.", this);
                return;
            }

            IsSpeedrunActive = true;
            SpeedrunElapsedSeconds = 0f;
            GameAnalytics.SpeedrunStart();
            BeginLevel(0);
        }

        /// <summary>Called by the gameplay scene when the two hands meet.</summary>
        public void ReportLevelCompleted()
        {
            // The board locks after a win, so this runs once per solve. Replaying a level in normal mode may report again.
            levelCompletedPending = true;
            UnlockLevelsUpTo(CurrentLevelIndex + 1);

            if (!HasNextLevel)
            {
                PlayerPrefs.SetInt(AllLevelsClearedKey, 1);
                PlayerPrefs.Save();
            }

            if (IsSpeedrunActive && !HasNextLevel)
            {
                FinishSpeedrun();
            }

            LevelCompleted?.Invoke();
        }

        /// <summary>Loads the next level, or the level select scene after the last one, through a slow fade to black.</summary>
        public void GoToNextLevel()
        {
            if (IsTransitioning)
            {
                return;
            }

            if (!HasNextLevel)
            {
                IsSpeedrunActive = false;
                LoadScene(SceneId.LevelSelect, levelTransitionFadeDuration);
                return;
            }

            BeginLevel(CurrentLevelIndex + 1, levelTransitionFadeDuration);
        }

        /// <summary>Loads the current level again through the fade (the player lost it). Progress and the Speedrun timer carry on.</summary>
        public void ReloadLevel()
        {
            if (IsTransitioning)
            {
                return;
            }

            BeginLevel(CurrentLevelIndex, levelTransitionFadeDuration);
        }

        public void ReturnToLevelSelect()
        {
            IsSpeedrunActive = false;
            LoadScene(SceneId.LevelSelect);
        }

        public void ReturnToMainMenu()
        {
            IsSpeedrunActive = false;
            LoadScene(SceneId.MainMenu);
        }

        /// <summary>Lets a pause menu stop and resume the Speedrun clock.</summary>
        public void SetTimerPaused(bool paused)
        {
            isTimerPausedByGame = paused;
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Debug menu: turns the "Unlock All Levels" testing toggle on or off at runtime.</summary>
        public void SetUnlockAllLevels(bool value)
        {
            unlockAllLevels = value;
        }

        /// <summary>Debug menu: opens the first <paramref name="count"/> levels and saves it as progress.</summary>
        public void SetUnlockedLevelCount(int count)
        {
            PlayerPrefs.SetInt(UnlockedLevelsKey, Mathf.Clamp(count, 1, Mathf.Max(1, LevelCount)));
            PlayerPrefs.Save();
        }

        /// <summary>Debug menu: jumps to a level whatever its lock state, keeping progress untouched.</summary>
        public void DebugPlayLevel(int levelIndex)
        {
            if (!IsValidLevelIndex(levelIndex))
            {
                Debug.LogError("GameSceneManager.DebugPlayLevel: invalid level index " + levelIndex + ".", this);
                return;
            }

            IsSpeedrunActive = false;
            BeginLevel(levelIndex);
        }

        /// <summary>Deletes saved progress and the best speedrun time.</summary>
        public void ResetSavedProgress()
        {
            PlayerPrefs.DeleteKey(UnlockedLevelsKey);
            PlayerPrefs.DeleteKey(BestSpeedrunKey);
            PlayerPrefs.DeleteKey(AllLevelsClearedKey);
            PlayerPrefs.Save();
        }

        /// <summary>Formats seconds as mm:ss.ff using a culture-independent decimal point.</summary>
        public static string FormatTime(float seconds)
        {
            float clamped = Mathf.Max(0f, seconds);
            int minutes = Mathf.FloorToInt(clamped / 60f);
            float remainder = clamped - minutes * 60f;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00.00}", minutes, remainder);
        }

        // ---------------------------------------------------------------- internals

        private void BeginLevel(int levelIndex)
        {
            BeginLevel(levelIndex, fadeDuration);
        }

        private void BeginLevel(int levelIndex, float fadeSeconds)
        {
            CurrentLevelIndex = levelIndex;
            levelCompletedPending = false;
            GameAnalytics.LevelStart(levelIndex, IsSpeedrunActive);
            LoadScene(SceneId.Gameplay, fadeSeconds);
        }

        private void FinishSpeedrun()
        {
            float total = SpeedrunElapsedSeconds;
            float best = BestSpeedrunSeconds;
            bool isNewBest = best <= 0f || total < best;

            if (isNewBest)
            {
                PlayerPrefs.SetFloat(BestSpeedrunKey, total);
                PlayerPrefs.Save();
            }

            IsSpeedrunActive = false;
            GameAnalytics.SpeedrunComplete(total, isNewBest);
            SpeedrunFinished?.Invoke(total, isNewBest);
        }

        private void UnlockLevelsUpTo(int completedLevelIndex)
        {
            int target = Mathf.Min(LevelCount, completedLevelIndex + 1);
            if (target > PlayerPrefs.GetInt(UnlockedLevelsKey, 1))
            {
                PlayerPrefs.SetInt(UnlockedLevelsKey, target);
                PlayerPrefs.Save();
            }
        }

        private bool IsValidLevelIndex(int index)
        {
            return levels != null && index >= 0 && index < levels.Length && levels[index] != null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (TryResolveSceneId(scene.name, out SceneId loaded))
            {
                CurrentScene = loaded;
                SceneLoaded?.Invoke(loaded);
            }
        }

        private IEnumerator LoadSceneRoutine(SceneId scene, float fadeSeconds)
        {
            IsTransitioning = true;
            yield return Fade(1f, fadeSeconds);

            AsyncOperation operation = SceneManager.LoadSceneAsync(GetSceneName(scene));
            if (operation == null)
            {
                Debug.LogError("Could not load scene '" + GetSceneName(scene) + "'. Is it added to Build Settings?", this);
                yield return Fade(0f, fadeSeconds);
                IsTransitioning = false;
                yield break;
            }

            // The screen is fully black here. Wait for the load, and keep it black for a minimum time so a fast load does not flash.
            float heldFor = 0f;
            while (!operation.isDone || heldFor < minBlackHoldSeconds)
            {
                heldFor += Time.unscaledDeltaTime;
                yield return null;
            }

            // One frame so the new scene has drawn its first frame behind the black before it is revealed.
            yield return null;
            yield return Fade(0f, fadeSeconds);
            IsTransitioning = false;
        }

        private IEnumerator Fade(float targetAlpha, float duration)
        {
            if (fadeGroup == null || duration <= 0f)
            {
                yield break;
            }

            fadeGroup.blocksRaycasts = true;
            float startAlpha = fadeGroup.alpha;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                fadeGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
                yield return null;
            }

            fadeGroup.alpha = targetAlpha;
            fadeGroup.blocksRaycasts = targetAlpha > 0.01f;
        }

        private string GetSceneName(SceneId scene)
        {
            switch (scene)
            {
                case SceneId.Title: return titleSceneName;
                case SceneId.MainMenu: return mainMenuSceneName;
                case SceneId.LevelSelect: return levelSelectSceneName;
                default: return gameplaySceneName;
            }
        }

        private bool TryResolveSceneId(string sceneName, out SceneId scene)
        {
            if (sceneName == titleSceneName) { scene = SceneId.Title; return true; }
            if (sceneName == mainMenuSceneName) { scene = SceneId.MainMenu; return true; }
            if (sceneName == levelSelectSceneName) { scene = SceneId.LevelSelect; return true; }
            if (sceneName == gameplaySceneName) { scene = SceneId.Gameplay; return true; }

            scene = SceneId.Title;
            return false;
        }
    }
}
