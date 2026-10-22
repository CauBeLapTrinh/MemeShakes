using System.ComponentModel;
using Catshakes;
using UnityEngine;

public partial class SROptions
{
    private static GameSceneManager Manager
    {
        get
        {
            GameSceneManager manager = GameSceneManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning("SROptions: no GameSceneManager (start from the Title scene).");
            }

            return manager;
        }
    }

    private static GridManager Grid
    {
        get
        {
            GridManager grid = Object.FindFirstObjectByType<GridManager>();
            if (grid == null)
            {
                Debug.LogWarning("SROptions: no GridManager in this scene (open a level first).");
            }

            return grid;
        }
    }

    // ---------------------------------------------------------------- Level

    /// <summary>Index in the GameSceneManager level list, counted from 0 (Level 1 = 0).</summary>
    [Category("Level")]
    public int lvlIndex { get; set; }

    [Category("Level")]
    public void PlayLevel()
    {
        Manager?.DebugPlayLevel(lvlIndex);
    }

    [Category("Level")]
    public void NextLevel()
    {
        GameSceneManager manager = Manager;
        if (manager != null)
        {
            manager.DebugPlayLevel(Mathf.Min(manager.CurrentLevelIndex + 1, manager.LevelCount - 1));
        }
    }

    [Category("Level")]
    public void PreviousLevel()
    {
        GameSceneManager manager = Manager;
        if (manager != null)
        {
            manager.DebugPlayLevel(Mathf.Max(manager.CurrentLevelIndex - 1, 0));
        }
    }

    [Category("Level")]
    public void RestartLevel()
    {
        Manager?.ReloadLevel();
    }

    [Category("Level")]
    public void UndoMove()
    {
        Grid?.Undo();
    }

    [Category("Level")]
    public void ResetBoard()
    {
        Grid?.ResetLevel();
    }

    [Category("Level")]
    public string CurrentLevelInfo
    {
        get
        {
            GameSceneManager manager = GameSceneManager.Instance;
            if (manager == null)
            {
                return "-";
            }

            return manager.CurrentLevelIndex + " / " + manager.LevelCount
                   + " (unlocked " + manager.UnlockedLevelCount + ")";
        }
    }

    // ---------------------------------------------------------------- Game

    [Category("Game")]
    public void WinGame()
    {
        Grid?.DebugForceWin();
    }

    [Category("Game")]
    public void LoseGame()
    {
        Grid?.DebugForceLose();
    }

    // ---------------------------------------------------------------- Progress

    [Category("Progress")]
    public bool UnlockAllLevels
    {
        get
        {
            GameSceneManager manager = GameSceneManager.Instance;
            return manager != null && manager.AreAllLevelsUnlocked;
        }
        set { Manager?.SetUnlockAllLevels(value); }
    }

    /// <summary>Saves the first N levels as opened (level select shows them as playable).</summary>
    [Category("Progress")]
    public int UnlockedCount { get; set; } = 1;

    [Category("Progress")]
    public void ApplyUnlockedCount()
    {
        Manager?.SetUnlockedLevelCount(UnlockedCount);
    }

    [Category("Progress")]
    public void UnlockAllAndSave()
    {
        GameSceneManager manager = Manager;
        if (manager != null)
        {
            manager.SetUnlockedLevelCount(manager.LevelCount);
            PlayerPrefs.SetInt(GameSceneManager.AllLevelsClearedKey, 1);
            PlayerPrefs.Save();
        }
    }

    [Category("Progress")]
    public void ResetProgress()
    {
        Manager?.ResetSavedProgress();
    }

    [Category("Progress")]
    public void StartSpeedrun()
    {
        Manager?.StartSpeedrun();
    }

    // ---------------------------------------------------------------- Navigation

    [Category("Navigation")]
    public void GoToLevelSelect()
    {
        Manager?.ReturnToLevelSelect();
    }

    [Category("Navigation")]
    public void GoToMainMenu()
    {
        Manager?.ReturnToMainMenu();
    }

    // ---------------------------------------------------------------- Time Scale

    [Category("Time Scale")]
    public float TimeScale { get; set; } = 1f;

    [Category("Time Scale")]
    public void SetTimeScale()
    {
        Time.timeScale = Mathf.Max(0f, TimeScale);
    }

    [Category("Time Scale")]
    public void ResetTimeScale()
    {
        TimeScale = 1f;
        Time.timeScale = 1f;
    }
}
