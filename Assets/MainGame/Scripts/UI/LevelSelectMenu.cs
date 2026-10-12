using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Catshakes
{
    /// <summary>
    /// Builds one button per level from the catalog in <see cref="GameSceneManager"/>.
    /// Levels the player has not unlocked yet are shown but not clickable.
    /// </summary>
    public sealed class LevelSelectMenu : MonoBehaviour
    {
        [Tooltip("Parent for the level buttons. Give it a Grid Layout Group.")]
        [SerializeField] private RectTransform contentRoot;
        [Tooltip("Button prefab with a TMP_Text child used as the label.")]
        [SerializeField] private Button levelButtonPrefab;
        [SerializeField] private Button backButton;
        [SerializeField] private TMP_Text bestSpeedrunText;
        [SerializeField] private string bestSpeedrunFormat = "Best speedrun: {0}";

        private void Start()
        {
            if (backButton != null)
            {
                backButton.onClick.AddListener(HandleBackClicked);
            }

            Build();
        }

        private void Build()
        {
            GameSceneManager manager = GameSceneManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning("LevelSelectMenu: no GameSceneManager found. Start the game from the Title scene.", this);
                return;
            }

            if (contentRoot == null || levelButtonPrefab == null)
            {
                Debug.LogError("LevelSelectMenu needs contentRoot and levelButtonPrefab assigned.", this);
                return;
            }

            int unlockedCount = manager.UnlockedLevelCount;

            for (int i = 0; i < manager.LevelCount; i++)
            {
                int levelIndex = i;
                Button button = Instantiate(levelButtonPrefab, contentRoot);

                TMP_Text label = button.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    label.text = (levelIndex + 1).ToString();
                }

                button.interactable = levelIndex < unlockedCount;
                button.onClick.AddListener(() => manager.StartLevel(levelIndex));
            }

            if (bestSpeedrunText != null)
            {
                bestSpeedrunText.gameObject.SetActive(manager.HasBestSpeedrun);
                if (manager.HasBestSpeedrun)
                {
                    bestSpeedrunText.text = string.Format(bestSpeedrunFormat, GameSceneManager.FormatTime(manager.BestSpeedrunSeconds));
                }
            }
        }

        private void HandleBackClicked()
        {
            GameSceneManager manager = GameSceneManager.Instance;
            if (manager != null)
            {
                manager.ReturnToMainMenu();
            }
        }
    }
}
