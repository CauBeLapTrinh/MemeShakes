using System.Collections;
using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// Entry point of the Gameplay scene. Loads the level chosen in <see cref="GameSceneManager"/>,
    /// fits the camera to the board and reports the handshake back to the scene flow.
    /// </summary>
    public sealed class GameplayController : MonoBehaviour
    {
        [SerializeField] private GridManager gridManager;
        [SerializeField] private CameraFitter cameraFitter;

        [Tooltip("Played when this scene is started directly in the Editor, where no GameSceneManager exists.")]
        [SerializeField] private LevelDataSO fallbackLevel;

        [Tooltip("Pause between a Speedrun level being solved and the next one loading.")]
        [SerializeField, Min(0f)] private float speedrunAdvanceDelay = 0.6f;

        private void OnEnable()
        {
            if (gridManager != null)
            {
                gridManager.Won += HandleWon;
            }
        }

        private void OnDisable()
        {
            if (gridManager != null)
            {
                gridManager.Won -= HandleWon;
            }
        }

        private void Start()
        {
            if (gridManager == null)
            {
                Debug.LogError("GameplayController needs a GridManager reference.", this);
                return;
            }

            GameSceneManager manager = GameSceneManager.Instance;
            LevelDataSO level = manager != null && manager.CurrentLevel != null ? manager.CurrentLevel : fallbackLevel;

            if (level == null)
            {
                Debug.LogError("No level to play: start from the Title scene or assign a fallback level.", this);
                return;
            }

            if (!gridManager.LoadLevel(level))
            {
                return;
            }

            if (cameraFitter != null)
            {
                cameraFitter.Fit(gridManager.Coords);
            }
        }

        private void HandleWon()
        {
            GameSceneManager manager = GameSceneManager.Instance;
            if (manager == null)
            {
                return;
            }

            manager.ReportLevelCompleted();

            // Still true only while more Speedrun levels remain.
            if (manager.IsSpeedrunActive)
            {
                StartCoroutine(AdvanceAfterDelay(manager));
            }
        }

        private IEnumerator AdvanceAfterDelay(GameSceneManager manager)
        {
            yield return new WaitForSecondsRealtime(speedrunAdvanceDelay);
            manager.GoToNextLevel();
        }
    }
}
