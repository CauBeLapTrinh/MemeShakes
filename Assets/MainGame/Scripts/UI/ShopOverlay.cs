using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Catshakes
{
    /// <summary>
    /// Mr. Da's shop, opened from the shop button of the main menu. The shelf shows one skin per slot: a tap on an owned
    /// skin puts it on, a tap on a locked skin opens the "Watch ads to unlock" popup. A locked skin needs several ads; its
    /// plate shows the progress ("1 / 2"). Ads come from <see cref="RewardedAds"/> (LevelPlay via SkinAdRewardManager).
    /// Built and wired by Catshakes/Add Shop And Leaderboard UI.
    /// </summary>
    public sealed class ShopOverlay : MonoBehaviour
    {
        [Serializable]
        private sealed class Slot
        {
            public Button button;
            public Image icon;
            [Tooltip("Ad icon on the plate under a locked skin.")]
            public GameObject adBadge;
            [Tooltip("\"Owner\" under an owned skin, the watched / required ads under a locked one.")]
            public TMP_Text label;
            [Tooltip("Yellow frame around the skin in use.")]
            public GameObject highlight;
        }

        [SerializeField] private SkinCatalog catalog;

        [Header("Shop")]
        [SerializeField] private CanvasGroup shopGroup;
        [SerializeField] private RectTransform shopContent;
        [SerializeField] private Button backButton;
        [Tooltip("Shelf, Mr. Da and the bubble; scaled so a fitWidth x fitHeight area fills the screen (up to maxScale).")]
        [SerializeField] private RectTransform fitRoot;
        [SerializeField, Min(1f)] private float fitWidth = 1842f;
        [SerializeField, Min(1f)] private float fitHeight = 952f;
        [SerializeField, Min(0.1f)] private float maxScale = 1.2f;
        [SerializeField] private Slot[] slots = Array.Empty<Slot>();
        [SerializeField] private string ownedLabel = "Owner";
        [Tooltip("Floats up from a skin when it gets unlocked.")]
        [SerializeField] private string unlockedText = "UNLOCKED!";
        [SerializeField] private string progressLabelFormat = "{0} / {1}";
        [Tooltip("Floats up from the Watch ads button when no ad is loaded yet.")]
        [SerializeField] private string adNotReadyText = "No ad yet, try again!";
        [SerializeField] private string noInternetText = "No internet connection!";
        [Tooltip("Space between the ad icon and the progress text; the text is centered in what is left of the plate.")]
        [SerializeField] private float progressLabelGap = 2f;

        [Header("Watch ads popup")]
        [SerializeField] private CanvasGroup popupGroup;
        [SerializeField] private RectTransform popupBox;
        [SerializeField] private Image popupIcon;
        [SerializeField] private TMP_Text popupName;
        [SerializeField] private Button watchAdsButton;
        [SerializeField] private Button popupCloseButton;
        [SerializeField] private Button popupDimButton;

        [Header("Timing")]
        [SerializeField, Min(0f)] private float duration = 0.22f;

        private SkinCatalog.Skin pendingSkin;
        private bool isAnimating;

        public bool IsOpen { get; private set; }
        public bool IsPopupOpen { get; private set; }

        /// <summary>True while the shop is open or animating; the menu must not react to its own buttons.</summary>
        public bool IsBusy => IsOpen || isAnimating;

        private void Awake()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                int index = i;
                if (slots[i] != null && slots[i].button != null)
                {
                    slots[i].button.onClick.AddListener(() => OnSlotClicked(index));
                }
            }

            AddListener(backButton, Close);
            AddListener(watchAdsButton, OnWatchAdsClicked);
            AddListener(popupCloseButton, ClosePopup);
            AddListener(popupDimButton, ClosePopup);

            HideImmediately(shopGroup);
            HideImmediately(popupGroup);
        }

        private void OnEnable()
        {
            SkinCatalog.Changed += Refresh;
        }

        private void OnDisable()
        {
            SkinCatalog.Changed -= Refresh;
        }

        private void Update()
        {
            if (!IsOpen || isAnimating || !WasBackKeyPressed())
            {
                return;
            }

            if (IsPopupOpen)
            {
                ClosePopup();
            }
            else
            {
                Close();
            }
        }

        public void Open()
        {
            if (IsOpen || isAnimating || shopGroup == null)
            {
                return;
            }

            Refresh();
            FitToScreen();
            SoundManager.Play(SoundId.Whoosh);
            StartCoroutine(FadeRoutine(shopGroup, shopContent, true, () => IsOpen = true));
        }

        public void Close()
        {
            if (!IsOpen || isAnimating)
            {
                return;
            }

            Click();
            SoundManager.Play(SoundId.Whoosh);
            IsPopupOpen = false;
            HideImmediately(popupGroup);
            StartCoroutine(FadeRoutine(shopGroup, shopContent, false, () => IsOpen = false));
        }

        /// <summary>Redraws every slot from the catalog and the saved ownership.</summary>
        public void Refresh()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                Slot slot = slots[i];
                if (slot == null)
                {
                    continue;
                }

                SkinCatalog.Skin skin = SkinAt(i);
                bool hasSkin = skin != null && skin.icon != null;
                bool owned = hasSkin && catalog.IsOwned(skin);
                bool selected = owned && catalog.IsSelected(skin);

                if (slot.icon != null)
                {
                    slot.icon.gameObject.SetActive(hasSkin);
                    slot.icon.sprite = hasSkin ? skin.icon : null;
                }

                if (slot.adBadge != null)
                {
                    slot.adBadge.SetActive(hasSkin && !owned);
                }

                if (slot.label != null)
                {
                    slot.label.gameObject.SetActive(hasSkin);
                    if (hasSkin)
                    {
                        slot.label.text = owned ? ownedLabel : string.Format(progressLabelFormat, catalog.AdsWatched(skin), skin.adsRequired);
                    }

                    LayoutLabel(slot, hasSkin && !owned);
                }

                if (slot.highlight != null)
                {
                    slot.highlight.SetActive(selected);
                }

                if (slot.button != null)
                {
                    slot.button.interactable = hasSkin;
                }
            }
        }

        /// <summary>
        /// Stretches the label over the plate: the whole plate (centered) for "Owner", or only the part right of
        /// the ad icon for the progress, so the icon and the text never overlap.
        /// </summary>
        private void LayoutLabel(Slot slot, bool showsAdIcon)
        {
            RectTransform label = slot.label.rectTransform;
            RectTransform plate = label.parent as RectTransform;
            float left = 0f;
            if (showsAdIcon && plate != null && slot.adBadge != null && slot.adBadge.transform is RectTransform ad && ad.parent == plate)
            {
                left = Mathf.Max(0f, ad.localPosition.x + ad.rect.xMax - plate.rect.xMin + progressLabelGap);
            }

            label.anchorMin = new Vector2(0f, label.anchorMin.y);
            label.anchorMax = new Vector2(1f, label.anchorMax.y);
            label.offsetMin = new Vector2(left, label.offsetMin.y);
            label.offsetMax = new Vector2(0f, label.offsetMax.y);
        }

        private void OnSlotClicked(int index)
        {
            SkinCatalog.Skin skin = SkinAt(index);
            if (!IsOpen || isAnimating || IsPopupOpen || skin == null)
            {
                return;
            }

            Click();
            if (catalog.IsOwned(skin))
            {
                if (!catalog.IsSelected(skin))
                {
                    catalog.Select(skin);
                    CelebrateSlot(index, SoundId.Equip, UIFx.MintColors, 10, null);
                }

                return;
            }

            pendingSkin = skin;
            if (popupIcon != null)
            {
                popupIcon.sprite = skin.icon;
            }

            if (popupName != null)
            {
                popupName.text = skin.displayName;
            }

            SoundManager.Play(SoundId.PopupOpen);
            StartCoroutine(FadeRoutine(popupGroup, popupBox, true, () => IsPopupOpen = true));
        }

        private void OnWatchAdsClicked()
        {
            if (!IsPopupOpen || isAnimating || pendingSkin == null)
            {
                return;
            }

            Click();
            SkinCatalog.Skin skin = pendingSkin;
            if (!RewardedAds.IsReady && watchAdsButton != null)
            {
                // Show() starts loading one; the player can tap again in a moment.
                UIFx.FloatingText((RectTransform)watchAdsButton.transform, RewardedAds.HasInternet ? adNotReadyText : noInternetText, UIFx.TextColor);
            }

            RewardedAds.Show("shop_skin_" + skin.id, rewarded =>
            {
                if (!rewarded)
                {
                    return;
                }

                int index = IndexOf(skin);
                if (!catalog.AddWatchedAd(skin))
                {
                    // One more ad counted: the popup closes and the skin's plate (ad icon + "1 / 2") bounces with the
                    // new progress floating up from the slot.
                    string progress = string.Format(progressLabelFormat, catalog.AdsWatched(skin), skin.adsRequired);
                    HidePopup(false, () => CelebrateSlot(index, SoundId.AdReward, UIFx.GoldColors, 12, progress));
                    return;
                }

                catalog.Select(skin);
                HidePopup(false, () => CelebrateSlot(index, SoundId.Unlock, UIFx.GoldColors, 26, unlockedText));
            });
        }

        private void ClosePopup()
        {
            if (IsPopupOpen && !isAnimating)
            {
                Click();
                HidePopup(true);
            }
        }

        /// <summary>Fades the popup out; <paramref name="onHidden"/> runs once it is gone (right away if it cannot close now).</summary>
        private void HidePopup(bool playSound, Action onHidden = null)
        {
            if (!IsPopupOpen || isAnimating)
            {
                onHidden?.Invoke();
                return;
            }

            if (playSound)
            {
                SoundManager.Play(SoundId.PopupClose);
            }

            pendingSkin = null;
            StartCoroutine(FadeRoutine(popupGroup, popupBox, false, () =>
            {
                IsPopupOpen = false;
                onHidden?.Invoke();
            }));
        }

        /// <summary>Sound, pixel burst and a bounce of the slot's plate and frame (plus an optional floating label).</summary>
        private void CelebrateSlot(int index, SoundId sound, Color[] colors, int pieces, string text)
        {
            SoundManager.Play(sound);
            if (index < 0 || index >= slots.Length || slots[index] == null || slots[index].button == null)
            {
                return;
            }

            Slot slot = slots[index];
            var slotRect = (RectTransform)slot.button.transform;
            UIFx.Burst(slotRect, colors, pieces);
            if (slot.label != null)
            {
                UIFx.Punch(slot.label.transform.parent, 0.3f, 0.45f);
            }

            if (slot.highlight != null)
            {
                UIFx.Punch(slot.highlight.transform, 0.12f, 0.4f);
            }

            if (!string.IsNullOrEmpty(text))
            {
                UIFx.FloatingText(slotRect, text, UIFx.TextColor, 51f);
            }
        }

        private int IndexOf(SkinCatalog.Skin skin)
        {
            for (int i = 0; catalog != null && i < catalog.Skins.Count; i++)
            {
                if (catalog.Skins[i] == skin)
                {
                    return i;
                }
            }

            return -1;
        }

        private void FitToScreen()
        {
            var area = fitRoot != null ? fitRoot.parent as RectTransform : null;
            if (area != null)
            {
                float scale = Mathf.Min(maxScale, Mathf.Min(area.rect.width / fitWidth, area.rect.height / fitHeight));
                fitRoot.localScale = new Vector3(scale, scale, 1f);
            }
        }

        private SkinCatalog.Skin SkinAt(int index)
        {
            return catalog != null && index >= 0 && index < catalog.Skins.Count ? catalog.Skins[index] : null;
        }

        // ---------------------------------------------------------------- animation

        private IEnumerator FadeRoutine(CanvasGroup group, RectTransform box, bool open, Action done)
        {
            isAnimating = true;
            group.gameObject.SetActive(true);
            group.interactable = false;
            group.blocksRaycasts = true;

            float startAlpha = open ? 0f : group.alpha;
            float endAlpha = open ? 1f : 0f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                group.alpha = Mathf.Lerp(startAlpha, endAlpha, t);
                if (box != null)
                {
                    float scale = open ? Mathf.LerpUnclamped(0.85f, 1f, EaseOutBack(t)) : Mathf.Lerp(1f, 0.9f, t);
                    box.localScale = new Vector3(scale, scale, 1f);
                }

                yield return null;
            }

            group.alpha = endAlpha;
            if (box != null)
            {
                box.localScale = Vector3.one;
            }

            if (open)
            {
                group.interactable = true;
            }
            else
            {
                HideImmediately(group);
            }

            isAnimating = false;
            done?.Invoke();
        }

        private static void HideImmediately(CanvasGroup group)
        {
            if (group == null)
            {
                return;
            }

            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            group.gameObject.SetActive(false);
        }

        private static void AddListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.AddListener(action);
            }
        }

        private static float EaseOutBack(float t)
        {
            const float overshoot = 1.70158f;
            float u = t - 1f;
            return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
        }

        private static void Click()
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayClick();
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
    }
}
