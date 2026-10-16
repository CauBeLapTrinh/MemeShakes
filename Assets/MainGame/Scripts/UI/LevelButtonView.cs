using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Catshakes
{
    public enum LevelButtonState
    {
        Unlocked = 0,
        Selected = 1,
        Locked = 2
    }

    /// <summary>
    /// One cell of the level grid. Shows the level number on the sprite that matches its state:
    /// cream block (unlocked), highlighted yellow block (selected) or grey padlock block (locked).
    /// The root is the grid cell; <see cref="face"/> is a centered child that gets the sprite's own size,
    /// so the larger "selected" sprite grows past the cell like in the design.
    /// </summary>
    public sealed class LevelButtonView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Button button;
        [SerializeField] private Image face;
        [SerializeField] private TMP_Text label;

        [Header("Sprites")]
        [SerializeField] private Sprite unlockedSprite;
        [SerializeField] private Sprite selectedSprite;
        [SerializeField] private Sprite lockedSprite;

        [Tooltip("Multiplies the sprite's own size. 1 = native pixels.")]
        [SerializeField, Min(0.1f)] private float faceScale = 1f;

        [Header("Label")]
        [SerializeField] private Color normalTextColor = new Color32(139, 111, 71, 255);
        [SerializeField] private Color lockedTextColor = new Color32(118, 108, 84, 255);
        [Tooltip("The padlock sits low on the locked sprite, so the number moves up by this much.")]
        [SerializeField] private float lockedLabelOffsetY = 20f;

        private Action<int> clicked;
        private Vector2 labelRestPosition;
        private bool hasLabelRestPosition;

        public int LevelIndex { get; private set; }

        public void Bind(int levelIndex, Action<int> onClicked)
        {
            LevelIndex = levelIndex;
            clicked = onClicked;

            if (label != null)
            {
                label.text = (levelIndex + 1).ToString();
                labelRestPosition = label.rectTransform.anchoredPosition;
                hasLabelRestPosition = true;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleClick);
        }

        public void SetState(LevelButtonState state)
        {
            Sprite sprite;
            switch (state)
            {
                case LevelButtonState.Selected: sprite = selectedSprite; break;
                case LevelButtonState.Locked: sprite = lockedSprite; break;
                default: sprite = unlockedSprite; break;
            }

            face.sprite = sprite;
            face.SetNativeSize();
            face.rectTransform.sizeDelta *= faceScale;
            button.interactable = state != LevelButtonState.Locked;

            if (label != null)
            {
                bool locked = state == LevelButtonState.Locked;
                label.color = locked ? lockedTextColor : normalTextColor;

                if (hasLabelRestPosition)
                {
                    label.rectTransform.anchoredPosition = labelRestPosition + (locked ? new Vector2(0f, lockedLabelOffsetY) : Vector2.zero);
                }
            }
        }

        /// <summary>A locked (non-interactable) button gets no onClick; it still reacts so the tap is not lost.</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (button != null && !button.interactable)
            {
                SoundManager.Play(SoundId.Locked);
                UIFx.Punch(face != null ? face.transform : transform, -0.12f, 0.3f);
            }
        }

        private void HandleClick()
        {
            if (clicked != null)
            {
                clicked(LevelIndex);
            }
        }
    }
}
