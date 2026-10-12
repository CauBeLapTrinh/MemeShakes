using TMPro;
using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// Button handlers for the Title and Main Menu scenes. Wire the public methods to Button.onClick events:
    ///  Title: full-screen "tap to start" button -> OpenMainMenu.
    ///  Main Menu: Play -> OpenLevelSelect, Speedrun -> StartSpeedrun, Quit -> QuitGame.
    /// </summary>
    public sealed class MenuNavigationUI : MonoBehaviour
    {
        [Tooltip("Optional text that shows the best speedrun time (Main Menu).")]
        [SerializeField] private TMP_Text bestSpeedrunText;
        [SerializeField] private string bestSpeedrunFormat = "Best speedrun: {0}";

        private void Start()
        {
            RefreshBestSpeedrun();
        }

        public void OpenTitle()
        {
            LoadScene(SceneId.Title);
        }

        public void OpenMainMenu()
        {
            LoadScene(SceneId.MainMenu);
        }

        public void OpenLevelSelect()
        {
            LoadScene(SceneId.LevelSelect);
        }

        public void StartSpeedrun()
        {
            GameSceneManager manager = GameSceneManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning("MenuNavigationUI: no GameSceneManager found. Start the game from the Title scene.", this);
                return;
            }

            manager.StartSpeedrun();
        }

        public void QuitGame()
        {
            GameSceneManager manager = GameSceneManager.Instance;
            if (manager != null)
            {
                manager.QuitGame();
                return;
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void LoadScene(SceneId scene)
        {
            GameSceneManager manager = GameSceneManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning("MenuNavigationUI: no GameSceneManager found. Start the game from the Title scene.", this);
                return;
            }

            manager.LoadScene(scene);
        }

        private void RefreshBestSpeedrun()
        {
            if (bestSpeedrunText == null)
            {
                return;
            }

            GameSceneManager manager = GameSceneManager.Instance;
            bool hasBest = manager != null && manager.HasBestSpeedrun;
            bestSpeedrunText.gameObject.SetActive(hasBest);

            if (hasBest)
            {
                bestSpeedrunText.text = string.Format(bestSpeedrunFormat, GameSceneManager.FormatTime(manager.BestSpeedrunSeconds));
            }
        }
    }
}
