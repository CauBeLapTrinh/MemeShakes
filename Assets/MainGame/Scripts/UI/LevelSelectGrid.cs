using System;
using System.Collections.Generic;
using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// The grid of level buttons (0..slotCount-1). A slot is unlocked when the player has reached it and a
    /// level asset exists for it in <see cref="GameSceneManager"/>; every other slot is shown locked.
    /// Exactly one unlocked slot is selected at a time; <see cref="SelectedLevelIndex"/> holds it.
    /// </summary>
    public sealed class LevelSelectGrid : MonoBehaviour
    {
        [Tooltip("Parent of the buttons. Give it a Grid Layout Group.")]
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private LevelButtonView levelButtonPrefab;
        [SerializeField, Min(1)] private int slotCount = 30;

        private readonly List<LevelButtonView> views = new List<LevelButtonView>();
        private int selectedIndex = -1;

        /// <summary>Raised when the player picks another level.</summary>
        public event Action<int> SelectionChanged;

        public int SelectedLevelIndex => selectedIndex;
        public int SlotCount => slotCount;

        private void Awake()
        {
            EnsureBuilt();
        }

        /// <summary>Re-reads the saved progress and redraws every button. Call when the panel is shown.</summary>
        public void Refresh()
        {
            EnsureBuilt();

            if (!IsUnlocked(selectedIndex))
            {
                selectedIndex = LastUnlockedIndex();
            }

            ApplyStates();
        }

        public bool TryGetSelectedLevel(out int levelIndex)
        {
            levelIndex = selectedIndex;
            return IsUnlocked(selectedIndex);
        }

        private void EnsureBuilt()
        {
            if (views.Count > 0)
            {
                return;
            }

            if (contentRoot == null || levelButtonPrefab == null)
            {
                Debug.LogError("LevelSelectGrid needs contentRoot and levelButtonPrefab assigned.", this);
                return;
            }

            // Drop anything left over from editing so the grid never shows duplicates.
            for (int i = contentRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(contentRoot.GetChild(i).gameObject);
            }

            for (int i = 0; i < slotCount; i++)
            {
                LevelButtonView view = Instantiate(levelButtonPrefab, contentRoot);
                view.name = "Level_" + i;
                view.Bind(i, HandleLevelClicked);
                views.Add(view);
            }
        }

        private void HandleLevelClicked(int levelIndex)
        {
            if (!IsUnlocked(levelIndex) || levelIndex == selectedIndex)
            {
                return;
            }

            selectedIndex = levelIndex;
            ApplyStates();

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayClick();
            }

            SelectionChanged?.Invoke(selectedIndex);
        }

        private void ApplyStates()
        {
            for (int i = 0; i < views.Count; i++)
            {
                LevelButtonState state;
                if (!IsUnlocked(i))
                {
                    state = LevelButtonState.Locked;
                }
                else
                {
                    state = i == selectedIndex ? LevelButtonState.Selected : LevelButtonState.Unlocked;
                }

                views[i].SetState(state);
            }
        }

        private int PlayableSlots()
        {
            GameSceneManager manager = GameSceneManager.Instance;
            if (manager == null)
            {
                return 0;
            }

            return Mathf.Min(slotCount, Mathf.Min(manager.UnlockedLevelCount, manager.LevelCount));
        }

        private bool IsUnlocked(int levelIndex)
        {
            return levelIndex >= 0 && levelIndex < PlayableSlots();
        }

        private int LastUnlockedIndex()
        {
            return PlayableSlots() - 1;
        }
    }
}
