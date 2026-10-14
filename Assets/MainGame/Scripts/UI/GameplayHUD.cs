using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Catshakes
{
    /// <summary>
    /// In-game overlay: level name, move counter, Speedrun timer, remaining steps, undo / reset / switch-cat buttons
    /// and the win panel. Every reference is optional so the HUD can be built up step by step.
    /// </summary>
    public sealed class GameplayHUD : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GridManager gridManager;

        [Header("Texts")]
        [SerializeField] private TMP_Text levelNameText;
        [SerializeField] private TMP_Text moveCountText;
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private TMP_Text activeCatText;
        [SerializeField] private TMP_Text catAStepsText;
        [SerializeField] private TMP_Text catBStepsText;

        [Header("Buttons")]
        [SerializeField] private Button undoButton;
        [SerializeField] private Button resetButton;
        [SerializeField] private Button switchCatButton;
        [SerializeField] private Button menuButton;

        [Header("Win panel")]
        [SerializeField] private GameObject winPanel;
        [SerializeField] private TMP_Text winTitleText;
        [SerializeField] private TMP_Text winSummaryText;
        [SerializeField] private Button nextLevelButton;
        [SerializeField] private Button levelSelectButton;
        [SerializeField] private Button replayButton;

        [Header("Text formats")]
        [SerializeField] private string movesFormat = "Moves: {0}";
        [SerializeField] private string stepsFormat = "{0}/{1}";
        [SerializeField] private string activeCatFormat = "Controlling: {0}";
        [SerializeField] private string speedrunLevelFormat = "{0}  ({1}/{2})";
        [SerializeField] private string catAName = "Cat A";
        [SerializeField] private string catBName = "Cat B";
        [SerializeField] private string winTitle = "Handshake!";
        [SerializeField] private string speedrunWinTitle = "Speedrun complete!";
        [SerializeField] private string newBestFormat = "Time {0}  -  NEW BEST!";
        [SerializeField] private string timeAndBestFormat = "Time {0}  -  Best {1}";

        private string speedrunSummary;
        private bool showFinalTimer;
        private int lastDisplayedCentiseconds = -1;

        private void Awake()
        {
            Bind(undoButton, HandleUndoClicked);
            Bind(resetButton, HandleResetClicked);
            Bind(switchCatButton, HandleSwitchCatClicked);
            Bind(menuButton, HandleMenuClicked);
            Bind(nextLevelButton, HandleNextLevelClicked);
            Bind(levelSelectButton, HandleMenuClicked);
            Bind(replayButton, HandleReplayClicked);

            HideWinPanel();
        }

        private void OnEnable()
        {
            if (gridManager != null)
            {
                gridManager.StateChanged += Refresh;
                gridManager.LevelLoaded += HandleLevelLoaded;
                gridManager.WinPresentationFinished += HandleGridWon;
                gridManager.ActiveCatChanged += HandleActiveCatChanged;
            }

            GameSceneManager manager = GameSceneManager.Instance;
            if (manager != null)
            {
                manager.LevelCompleted += HandleManagerLevelCompleted;
                manager.SpeedrunFinished += HandleSpeedrunFinished;
            }
        }

        private void OnDisable()
        {
            if (gridManager != null)
            {
                gridManager.StateChanged -= Refresh;
                gridManager.LevelLoaded -= HandleLevelLoaded;
                gridManager.WinPresentationFinished -= HandleGridWon;
                gridManager.ActiveCatChanged -= HandleActiveCatChanged;
            }

            GameSceneManager manager = GameSceneManager.Instance;
            if (manager != null)
            {
                manager.LevelCompleted -= HandleManagerLevelCompleted;
                manager.SpeedrunFinished -= HandleSpeedrunFinished;
            }
        }

        private void Start()
        {
            Refresh();
            RefreshActiveCatText();
        }

        private void Update()
        {
            if (timerText == null)
            {
                return;
            }

            GameSceneManager manager = GameSceneManager.Instance;
            bool showTimer = manager != null && (manager.IsSpeedrunActive || showFinalTimer);

            if (timerText.gameObject.activeSelf != showTimer)
            {
                timerText.gameObject.SetActive(showTimer);
            }

            if (!showTimer)
            {
                return;
            }

            int centiseconds = Mathf.FloorToInt(manager.SpeedrunElapsedSeconds * 100f);
            if (centiseconds != lastDisplayedCentiseconds)
            {
                lastDisplayedCentiseconds = centiseconds;
                timerText.text = GameSceneManager.FormatTime(manager.SpeedrunElapsedSeconds);
            }
        }

        // ---------------------------------------------------------------- refresh

        private void Refresh()
        {
            if (gridManager == null)
            {
                return;
            }

            if (moveCountText != null)
            {
                moveCountText.text = string.Format(movesFormat, gridManager.MoveCount);
            }

            if (undoButton != null)
            {
                undoButton.interactable = gridManager.CanUndo;
            }

            RefreshLevelName();
            RefreshStepTexts();
        }

        private void RefreshLevelName()
        {
            if (levelNameText == null || gridManager.CurrentLevel == null)
            {
                return;
            }

            string levelName = gridManager.CurrentLevel.DisplayName;
            GameSceneManager manager = GameSceneManager.Instance;

            levelNameText.text = manager != null && manager.IsSpeedrunActive
                ? string.Format(speedrunLevelFormat, levelName, manager.CurrentLevelIndex + 1, manager.LevelCount)
                : levelName;
        }

        private void RefreshStepTexts()
        {
            CatArmController catA = gridManager.GetCat(CatId.CatA);
            CatArmController catB = gridManager.GetCat(CatId.CatB);

            if (catAStepsText != null && catA != null)
            {
                catAStepsText.text = string.Format(stepsFormat, catA.StepsRemaining, catA.MaxSteps);
            }

            if (catBStepsText != null && catB != null)
            {
                catBStepsText.text = string.Format(stepsFormat, catB.StepsRemaining, catB.MaxSteps);
            }
        }

        private void RefreshActiveCatText()
        {
            if (activeCatText == null || gridManager == null)
            {
                return;
            }

            string catName = gridManager.ActiveCat == CatId.CatA ? catAName : catBName;
            activeCatText.text = string.Format(activeCatFormat, catName);
        }

        // ---------------------------------------------------------------- game events

        private void HandleLevelLoaded()
        {
            HideWinPanel();
            speedrunSummary = null;
            Refresh();
            RefreshActiveCatText();
        }

        private void HandleActiveCatChanged(CatId id)
        {
            RefreshActiveCatText();
        }

        // Without a GameSceneManager (scene played directly in the Editor) the grid event drives the panel,
        // once the victory effects are over.
        private void HandleGridWon()
        {
            if (GameSceneManager.Instance == null)
            {
                ShowWinPanel(winTitle, string.Format(movesFormat, gridManager.MoveCount), false);
            }
        }

        private void HandleSpeedrunFinished(float totalSeconds, bool isNewBest)
        {
            GameSceneManager manager = GameSceneManager.Instance;
            string time = GameSceneManager.FormatTime(totalSeconds);

            speedrunSummary = isNewBest
                ? string.Format(newBestFormat, time)
                : string.Format(timeAndBestFormat, time, GameSceneManager.FormatTime(manager != null ? manager.BestSpeedrunSeconds : totalSeconds));
            showFinalTimer = true;
        }

        private void HandleManagerLevelCompleted()
        {
            GameSceneManager manager = GameSceneManager.Instance;
            if (manager == null || manager.IsSpeedrunActive)
            {
                // A Speedrun with levels left advances automatically; no panel needed.
                return;
            }

            if (speedrunSummary != null)
            {
                ShowWinPanel(speedrunWinTitle, speedrunSummary, false);
                speedrunSummary = null;
                return;
            }

            ShowWinPanel(winTitle, string.Format(movesFormat, gridManager.MoveCount), manager.HasNextLevel);
        }

        // ---------------------------------------------------------------- win panel

        private void ShowWinPanel(string title, string summary, bool canGoToNextLevel)
        {
            if (winPanel == null)
            {
                return;
            }

            if (winTitleText != null)
            {
                winTitleText.text = title;
            }

            if (winSummaryText != null)
            {
                winSummaryText.text = summary;
            }

            if (nextLevelButton != null)
            {
                nextLevelButton.gameObject.SetActive(canGoToNextLevel);
            }

            winPanel.SetActive(true);
        }

        private void HideWinPanel()
        {
            if (winPanel != null)
            {
                winPanel.SetActive(false);
            }
        }

        // ---------------------------------------------------------------- buttons

        private static void Bind(Button button, UnityAction action)
        {
            if (button != null)
            {
                button.onClick.AddListener(action);
            }
        }

        private void HandleUndoClicked()
        {
            gridManager.Undo();
        }

        private void HandleResetClicked()
        {
            GameSceneManager manager = GameSceneManager.Instance;
            if (gridManager.IsWon && manager != null && manager.IsSpeedrunActive)
            {
                // The level is already solved and the next one is about to load.
                return;
            }

            HideWinPanel();
            gridManager.ResetLevel();
        }

        private void HandleSwitchCatClicked()
        {
            gridManager.ToggleActiveCat();
        }

        private void HandleMenuClicked()
        {
            GameSceneManager manager = GameSceneManager.Instance;
            if (manager != null)
            {
                manager.ReturnToLevelSelect();
            }
        }

        private void HandleNextLevelClicked()
        {
            GameSceneManager manager = GameSceneManager.Instance;
            if (manager != null)
            {
                manager.GoToNextLevel();
            }
        }

        private void HandleReplayClicked()
        {
            HideWinPanel();
            gridManager.ResetLevel();
        }
    }
}
