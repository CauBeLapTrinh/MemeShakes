using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Catshakes
{
    /// <summary>
    /// In-game overlay: level number, Speedrun timer, restart and settings buttons (the shared Settings popup with a
    /// "Give up" button back to level select) and the win panel. Every reference is optional, so the move counter,
    /// remaining steps and undo / switch-cat buttons can be left out (the current layout does).
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
        [Tooltip("Opens the shared Settings popup (music and sound volume).")]
        [SerializeField] private Button settingsButton;

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
        [Tooltip("Next to the \"Level\" picture. {0} = level number (the same as on the level select buttons).")]
        [SerializeField] private string levelNumberFormat = "{0}";
        [SerializeField] private string catAName = "Cat A";
        [SerializeField] private string catBName = "Cat B";
        [SerializeField] private string winTitle = "Handshake!";
        [SerializeField] private string speedrunWinTitle = "Speedrun complete!";
        [SerializeField] private string newBestFormat = "Time {0}  -  NEW BEST!";
        [SerializeField] private string timeAndBestFormat = "Time {0}  -  Best {1}";

        private string speedrunSummary;
        private bool speedrunWasNewBest;
        private bool showFinalTimer;
        private int lastDisplayedCentiseconds = -1;

        private void Awake()
        {
            Bind(undoButton, HandleUndoClicked);
            Bind(resetButton, HandleResetClicked);
            Bind(switchCatButton, HandleSwitchCatClicked);
            Bind(menuButton, HandleMenuClicked);
            Bind(settingsButton, HandleSettingsClicked);
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

            // The level number as on the level select buttons; the level's own name when the scene runs on its own.
            GameSceneManager manager = GameSceneManager.Instance;
            levelNameText.text = manager != null
                ? string.Format(levelNumberFormat, manager.CurrentLevelIndex)
                : gridManager.CurrentLevel.DisplayName;
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

            speedrunWasNewBest = isNewBest;
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
                return;
            }

            // Solved levels hand over to the next one by themselves (see GameplayController); only a finished Speedrun
            // keeps a panel, because it shows the final time.
            if (speedrunSummary != null)
            {
                ShowWinPanel(speedrunWinTitle, speedrunSummary, false);
                speedrunSummary = null;
                if (speedrunWasNewBest)
                {
                    speedrunWasNewBest = false;
                    SoundManager.Play(SoundId.RankUp);
                    if (winPanel != null)
                    {
                        UIFx.Burst((RectTransform)winPanel.transform, UIFx.GoldColors, 28, 700f);
                    }
                }

                return;
            }
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

                // UIButtonPress gives the squish, the mouse hover and the click sound.
                if (button.GetComponent<UIButtonPress>() == null)
                {
                    button.gameObject.AddComponent<UIButtonPress>();
                }
            }
        }

        private void HandleSettingsClicked()
        {
            PersistentPopups popups = PersistentPopups.Instance;
            if (popups != null)
            {
                popups.OpenSettings(HandleGiveUpClicked);
            }
        }

        private void HandleUndoClicked()
        {
            SoundManager.Play(SoundId.Undo);
            gridManager.Undo();
        }

        private void HandleResetClicked()
        {
            GameSceneManager manager = GameSceneManager.Instance;
            if (gridManager.IsWon && manager != null && (winPanel == null || !winPanel.activeSelf))
            {
                // The level is already solved and the next one is about to load.
                return;
            }

            HideWinPanel();
            SoundManager.Play(SoundId.Reset);
            GameAnalytics.LevelRestart(manager != null ? manager.CurrentLevelIndex : -1, gridManager.MoveCount);
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

        private void HandleGiveUpClicked()
        {
            GameSceneManager manager = GameSceneManager.Instance;
            GameAnalytics.GiveUp(manager != null ? manager.CurrentLevelIndex : -1, manager != null && manager.IsSpeedrunActive);
            HandleMenuClicked();
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
