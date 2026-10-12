using System.Collections;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Catshakes
{
    public enum MenuState
    {
        Title = 0,
        MainMenu = 1,
        LevelSelect = 2
    }

    /// <summary>
    /// State machine for the menu scene: Title -> MainMenu -> LevelSelect, plus the Settings popup on top.
    /// Every panel is a CanvasGroup that is cross-faded; the green and yellow backgrounds and the shared
    /// logo/mascot group fade with it. Button handlers (public On... methods) are wired in the inspector or
    /// by the setup tool. Level progress and scene loading are delegated to <see cref="GameSceneManager"/>.
    /// </summary>
    public sealed class MenuUIManager : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private CanvasGroup titlePanel;
        [SerializeField] private CanvasGroup mainMenuPanel;
        [SerializeField] private CanvasGroup levelSelectPanel;
        [Tooltip("Logo and mascot: visible on the Title and Main Menu panels, hidden on Level Select.")]
        [SerializeField] private CanvasGroup brandingGroup;

        [Header("Backgrounds")]
        [SerializeField] private CanvasGroup greenBackground;
        [SerializeField] private CanvasGroup yellowBackground;

        [Header("Settings popup")]
        [SerializeField] private CanvasGroup settingsGroup;
        [SerializeField] private RectTransform settingsBox;

        [Header("Main menu")]
        [SerializeField] private Image speedRunImage;
        [SerializeField] private Sprite speedRunLockedSprite;
        [SerializeField] private Sprite speedRunUnlockedSprite;

        [Header("Level select")]
        [SerializeField] private LevelSelectGrid levelGrid;

        [Header("Timing")]
        [SerializeField, Min(0f)] private float fadeDuration = 0.25f;
        [SerializeField, Min(0f)] private float settingsDuration = 0.2f;

        private const int GroupCount = 6;
        private const float PanelPopScale = 0.94f;

        private CanvasGroup[] groups;
        private bool isTransitioning;
        private bool isSettingsAnimating;
        private Coroutine shakeRoutine;

        public MenuState State { get; private set; } = MenuState.Title;
        public bool IsSettingsOpen { get; private set; }

        private void Awake()
        {
            groups = new[] { titlePanel, mainMenuPanel, levelSelectPanel, brandingGroup, greenBackground, yellowBackground };
        }

        private void Start()
        {
            if (GameSceneManager.Instance == null)
            {
                Debug.LogWarning("MenuUIManager: no GameSceneManager in the scene, levels cannot be started.", this);
            }

            if (settingsGroup != null)
            {
                settingsGroup.alpha = 0f;
                settingsGroup.gameObject.SetActive(false);
            }

            MenuState start = MenuState.Title;
            GameSceneManager manager = GameSceneManager.Instance;
            if (manager != null)
            {
                // Coming back from a level: reopen the screen the player asked for.
                if (manager.LastRequestedScene == SceneId.LevelSelect)
                {
                    start = MenuState.LevelSelect;
                }
                else if (manager.LastRequestedScene == SceneId.MainMenu)
                {
                    start = MenuState.MainMenu;
                }
            }

            RefreshSpeedRunButton();
            if (levelGrid != null)
            {
                levelGrid.Refresh();
            }

            SnapTo(start);
        }

        private void Update()
        {
            if (WasBackKeyPressed())
            {
                HandleBackKey();
            }
        }

        // ---------------------------------------------------------------- button handlers

        /// <summary>Title: full-screen tap.</summary>
        public void OnTitleTapped()
        {
            if (CanNavigate() && State == MenuState.Title)
            {
                Click();
                GoTo(MenuState.MainMenu);
            }
        }

        /// <summary>Main menu: Play.</summary>
        public void OnPlayClicked()
        {
            if (CanNavigate() && State == MenuState.MainMenu)
            {
                Click();
                if (levelGrid != null)
                {
                    levelGrid.Refresh();
                }

                GoTo(MenuState.LevelSelect);
            }
        }

        /// <summary>Main menu: Speed Run. Locked until every level has been cleared once.</summary>
        public void OnSpeedRunClicked()
        {
            if (!CanNavigate() || State != MenuState.MainMenu)
            {
                return;
            }

            if (!GameSceneManager.IsSpeedrunUnlocked)
            {
                if (shakeRoutine != null)
                {
                    StopCoroutine(shakeRoutine);
                }

                shakeRoutine = StartCoroutine(ShakeRoutine((RectTransform)speedRunImage.transform));
                return;
            }

            Click();
            GameSceneManager manager = GameSceneManager.Instance;
            if (manager != null)
            {
                manager.StartSpeedrun();
            }
        }

        /// <summary>Main menu: Settings.</summary>
        public void OnSettingsClicked()
        {
            if (!CanNavigate() || State != MenuState.MainMenu || settingsGroup == null)
            {
                return;
            }

            Click();
            StartCoroutine(SettingsRoutine(true));
        }

        /// <summary>Settings popup: the [X] button or a tap on the dimmed area.</summary>
        public void OnSettingsCloseClicked()
        {
            if (!IsSettingsOpen || isSettingsAnimating)
            {
                return;
            }

            Click();
            StartCoroutine(SettingsRoutine(false));
        }

        /// <summary>Level select: the back arrow.</summary>
        public void OnBackClicked()
        {
            if (CanNavigate() && State == MenuState.LevelSelect)
            {
                Click();
                GoTo(MenuState.MainMenu);
            }
        }

        /// <summary>Level select: "LET'S SHAKE!" loads the selected level.</summary>
        public void OnLetsShakeClicked()
        {
            if (!CanNavigate() || State != MenuState.LevelSelect || levelGrid == null)
            {
                return;
            }

            if (!levelGrid.TryGetSelectedLevel(out int levelIndex))
            {
                return;
            }

            GameSceneManager manager = GameSceneManager.Instance;
            if (manager == null)
            {
                Debug.LogError("MenuUIManager: add a GameSceneManager to the scene to start levels.", this);
                return;
            }

            Click();
            manager.StartLevel(levelIndex);
        }

        // ---------------------------------------------------------------- navigation

        public void GoTo(MenuState next)
        {
            if (isTransitioning || next == State)
            {
                return;
            }

            StartCoroutine(TransitionRoutine(next));
        }

        private bool CanNavigate()
        {
            return !isTransitioning && !IsSettingsOpen && !isSettingsAnimating;
        }

        private void HandleBackKey()
        {
            if (IsSettingsOpen)
            {
                OnSettingsCloseClicked();
            }
            else if (State == MenuState.LevelSelect)
            {
                OnBackClicked();
            }
        }

        private static bool WasBackKeyPressed()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        private static void Click()
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayClick();
            }
        }

        private void RefreshSpeedRunButton()
        {
            if (speedRunImage == null)
            {
                return;
            }

            Sprite sprite = GameSceneManager.IsSpeedrunUnlocked ? speedRunUnlockedSprite : speedRunLockedSprite;
            if (sprite != null)
            {
                speedRunImage.sprite = sprite;
            }
        }

        // ---------------------------------------------------------------- panel fading

        private static float TargetAlpha(int groupIndex, MenuState state)
        {
            switch (groupIndex)
            {
                case 0: return state == MenuState.Title ? 1f : 0f;
                case 1: return state == MenuState.MainMenu ? 1f : 0f;
                case 2: return state == MenuState.LevelSelect ? 1f : 0f;
                case 3: return state == MenuState.LevelSelect ? 0f : 1f; // logo + mascot
                case 4: return state == MenuState.LevelSelect ? 0f : 1f; // green background
                default: return state == MenuState.LevelSelect ? 1f : 0f; // yellow background
            }
        }

        private void SnapTo(MenuState state)
        {
            State = state;
            for (int i = 0; i < groups.Length; i++)
            {
                CanvasGroup group = groups[i];
                if (group == null)
                {
                    continue;
                }

                float target = TargetAlpha(i, state);
                group.gameObject.SetActive(target > 0f);
                group.alpha = target;
                group.interactable = target > 0.5f;
                group.blocksRaycasts = target > 0.5f;
                group.transform.localScale = Vector3.one;
            }
        }

        private IEnumerator TransitionRoutine(MenuState next)
        {
            isTransitioning = true;

            float[] from = new float[GroupCount];
            float[] to = new float[GroupCount];

            for (int i = 0; i < groups.Length; i++)
            {
                CanvasGroup group = groups[i];
                if (group == null)
                {
                    continue;
                }

                from[i] = group.alpha;
                to[i] = TargetAlpha(i, next);
                group.interactable = false;
                group.blocksRaycasts = false;

                if (to[i] > 0f)
                {
                    group.gameObject.SetActive(true);
                }
            }

            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / fadeDuration));
                ApplyBlend(from, to, t);
                yield return null;
            }

            ApplyBlend(from, to, 1f);

            for (int i = 0; i < groups.Length; i++)
            {
                CanvasGroup group = groups[i];
                if (group == null)
                {
                    continue;
                }

                bool visible = to[i] > 0.5f;
                group.interactable = visible;
                group.blocksRaycasts = visible;
                group.transform.localScale = Vector3.one;

                if (to[i] <= 0f)
                {
                    group.gameObject.SetActive(false);
                }
            }

            State = next;
            isTransitioning = false;
        }

        private void ApplyBlend(float[] from, float[] to, float t)
        {
            for (int i = 0; i < groups.Length; i++)
            {
                CanvasGroup group = groups[i];
                if (group == null)
                {
                    continue;
                }

                group.alpha = Mathf.Lerp(from[i], to[i], t);

                // Only the three panels pop in; the backgrounds and branding must always cover the screen.
                if (i < 3)
                {
                    float pop = to[i] > from[i] ? Mathf.Lerp(PanelPopScale, 1f, t) : 1f;
                    group.transform.localScale = new Vector3(pop, pop, 1f);
                }
            }
        }

        // ---------------------------------------------------------------- settings popup

        private IEnumerator SettingsRoutine(bool open)
        {
            isSettingsAnimating = true;

            settingsGroup.gameObject.SetActive(true);
            settingsGroup.interactable = false;
            settingsGroup.blocksRaycasts = true;

            float startAlpha = open ? 0f : settingsGroup.alpha;
            float endAlpha = open ? 1f : 0f;

            float elapsed = 0f;
            while (elapsed < settingsDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / settingsDuration);
                settingsGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, t);

                if (settingsBox != null)
                {
                    float scale = open ? Mathf.LerpUnclamped(0.8f, 1f, EaseOutBack(t)) : Mathf.Lerp(1f, 0.9f, t);
                    settingsBox.localScale = new Vector3(scale, scale, 1f);
                }

                yield return null;
            }

            settingsGroup.alpha = endAlpha;
            if (settingsBox != null)
            {
                settingsBox.localScale = Vector3.one;
            }

            if (open)
            {
                settingsGroup.interactable = true;
            }
            else
            {
                settingsGroup.blocksRaycasts = false;
                settingsGroup.gameObject.SetActive(false);
            }

            IsSettingsOpen = open;
            isSettingsAnimating = false;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        // ---------------------------------------------------------------- feedback

        private IEnumerator ShakeRoutine(RectTransform target)
        {
            Vector2 rest = target.anchoredPosition;
            const float duration = 0.35f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float falloff = 1f - Mathf.Clamp01(elapsed / duration);
                float dx = Mathf.Sin(elapsed * 70f) * 18f * falloff;
                target.anchoredPosition = rest + new Vector2(dx, 0f);
                yield return null;
            }

            target.anchoredPosition = rest;
            shakeRoutine = null;
        }
    }
}
