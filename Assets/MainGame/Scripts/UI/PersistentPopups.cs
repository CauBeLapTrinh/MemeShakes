using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Catshakes
{
    /// <summary>
    /// Popups shared by every scene (for now the Settings popup with the Music and SFX sliders). The prefab
    /// Assets/MainGame/Resources/PersistentPopups.prefab holds its own overlay canvas; it is loaded on first use and kept
    /// alive with DontDestroyOnLoad, so the menu and the gameplay scene open the very same popup.
    /// </summary>
    public sealed class PersistentPopups : MonoBehaviour
    {
        private const string ResourcePath = "PersistentPopups";

        [Header("Settings popup")]
        [SerializeField] private CanvasGroup settingsGroup;
        [SerializeField] private RectTransform settingsBox;
        [SerializeField] private Button settingsCloseButton;
        [Tooltip("Full-screen dimmed area behind the popup; a tap on it closes the popup.")]
        [SerializeField] private Button settingsDimButton;

        [Header("Timing")]
        [SerializeField, Min(0f)] private float settingsDuration = 0.2f;

        private static PersistentPopups instance;
        private bool isAnimating;

        /// <summary>Raised after the Settings popup has finished closing.</summary>
        public event Action SettingsClosed;

        /// <summary>The shared popups, loaded from Resources on first use. Null when the prefab is missing.</summary>
        public static PersistentPopups Instance
        {
            get
            {
                if (instance == null)
                {
                    Create();
                }

                return instance;
            }
        }

        /// <summary>True while any shared popup is open or animating; gameplay input should wait.</summary>
        public static bool IsAnyOpen => instance != null && (instance.IsSettingsOpen || instance.isAnimating);

        public bool IsSettingsOpen { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            instance = null;
        }

        private static void Create()
        {
            PersistentPopups prefab = Resources.Load<PersistentPopups>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogWarning("PersistentPopups: Resources/" + ResourcePath + ".prefab is missing.");
                return;
            }

            PersistentPopups created = Instantiate(prefab);
            created.name = ResourcePath;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            if (transform.parent != null)
            {
                transform.SetParent(null, false);
            }

            DontDestroyOnLoad(gameObject);

            if (settingsCloseButton != null)
            {
                settingsCloseButton.onClick.AddListener(CloseSettings);
            }

            if (settingsDimButton != null)
            {
                settingsDimButton.onClick.AddListener(CloseSettings);
            }

            if (settingsGroup != null)
            {
                settingsGroup.alpha = 0f;
                settingsGroup.gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private void Update()
        {
            if (IsSettingsOpen && !isAnimating && WasBackKeyPressed())
            {
                CloseSettings();
            }
        }

        public void OpenSettings()
        {
            if (IsSettingsOpen || isAnimating || settingsGroup == null)
            {
                return;
            }

            SoundManager.Play(SoundId.PopupOpen);
            StartCoroutine(SettingsRoutine(true));
        }

        public void CloseSettings()
        {
            if (!IsSettingsOpen || isAnimating)
            {
                return;
            }

            SoundManager.Play(SoundId.PopupClose);
            StartCoroutine(SettingsRoutine(false));
        }

        private IEnumerator SettingsRoutine(bool open)
        {
            isAnimating = true;
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
                    float scale = open ? Mathf.LerpUnclamped(0.85f, 1f, EaseOutBack(t)) : Mathf.Lerp(1f, 0.9f, t);
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
            isAnimating = false;

            if (!open)
            {
                SettingsClosed?.Invoke();
            }
        }

        private static float EaseOutBack(float t)
        {
            const float overshoot = 1.70158f;
            float u = t - 1f;
            return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
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
    }
}
