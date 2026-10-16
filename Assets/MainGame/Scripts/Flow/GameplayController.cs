using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

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

        [Tooltip("Victory effect prefab (Assets/MainGame/Effects/WinEffect). A generated effect is used when empty.")]
        [SerializeField] private WinEffect winEffectPrefab;

        [Tooltip("Sprite shown when the two hands meet (two hands clasped, Handshakes.png). It replaces both hand sprites. When empty, the hands just meet in the middle of the cell.")]
        [SerializeField] private Sprite handshakeSprite;

        [Tooltip("How long the victory effects play before the win popup (or the next Speedrun level) appears.")]
        [SerializeField, Min(0f)] private float winEffectDuration = 1.4f;

        [Tooltip("Extra pause after the victory effects, before the screen fades out and the next level loads.")]
        [FormerlySerializedAs("speedrunAdvanceDelay")]
        [SerializeField, Min(0f)] private float nextLevelDelay = 0.3f;

        [Header("Pixel effects")]
        [Tooltip("Small pixel particle bursts for moves, pushes, cuts, switches, doors and cat switching.")]
        [SerializeField] private bool playPixelEffects = true;
        [SerializeField] private Color switchSparkleColor = new Color(0.45f, 1f, 0.5f);
        [SerializeField] private Color catASparkleColor = new Color(1f, 0.7f, 0.25f);
        [SerializeField] private Color catBSparkleColor = new Color(0.4f, 0.75f, 1f);

        private Coroutine winRoutine;
        private GameObject handshakeObject;

        private void OnEnable()
        {
            if (gridManager != null)
            {
                gridManager.Won += HandleWon;
                gridManager.StateChanged += HandleStateChanged;
                gridManager.MoveResolved += HandleMoveResolved;
                gridManager.ObjectPushed += HandleObjectPushed;
                gridManager.ArmCutAtCell += HandleArmCut;
                gridManager.ObjectCrushed += HandleObjectCrushed;
                gridManager.SwitchChanged += HandleSwitchChanged;
                gridManager.DoorChanged += HandleDoorChanged;
                gridManager.CatSwitched += HandleCatSwitched;
            }
        }

        private void OnDisable()
        {
            if (gridManager != null)
            {
                gridManager.Won -= HandleWon;
                gridManager.StateChanged -= HandleStateChanged;
                gridManager.MoveResolved -= HandleMoveResolved;
                gridManager.ObjectPushed -= HandleObjectPushed;
                gridManager.ArmCutAtCell -= HandleArmCut;
                gridManager.ObjectCrushed -= HandleObjectCrushed;
                gridManager.SwitchChanged -= HandleSwitchChanged;
                gridManager.DoorChanged -= HandleDoorChanged;
                gridManager.CatSwitched -= HandleCatSwitched;
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

            ApplySelectedSkin();

            if (!gridManager.LoadLevel(level))
            {
                return;
            }

            if (cameraFitter != null)
            {
                cameraFitter.Fit(gridManager.Coords);
            }
        }

        /// <summary>Dresses both cats (and the handshake picture) in the skin chosen in the shop.</summary>
        private void ApplySelectedSkin()
        {
            SkinCatalog catalog = SkinCatalog.Load();
            SkinCatalog.Skin skin = catalog != null ? catalog.Selected : null;
            if (skin == null)
            {
                return;
            }

            foreach (CatId id in new[] { CatId.CatA, CatId.CatB })
            {
                CatArmController cat = gridManager.GetCat(id);
                if (cat != null)
                {
                    cat.ApplySkin(skin);
                }
            }
            if (skin.handshake != null)
            {
                handshakeSprite = skin.handshake;
            }
        }

        // ---------------------------------------------------------------- pixel effects

        private float CellSize => gridManager.Coords.CellSize;

        private Vector3 CellWorld(Vector2Int cell)
        {
            return gridManager.Coords.GridToWorld(cell);
        }

        private void HandleMoveResolved(CatId id, MoveOutcome outcome)
        {
            if (!playPixelEffects || (outcome != MoveOutcome.Extended && outcome != MoveOutcome.Retracted))
            {
                return;
            }

            CatArmController cat = gridManager.GetCat(id);
            Vector2Int direction = cat.HandDirection;
            if (outcome == MoveOutcome.Retracted)
            {
                // The hand moved back: the dust stays where it came from.
                direction = -direction;
            }

            PixelFx.Dust(CellWorld(cat.HandPosition), CellSize, direction);
        }

        private void HandleObjectPushed(Vector2Int cell, Vector2Int direction)
        {
            if (playPixelEffects)
            {
                PixelFx.Debris(CellWorld(cell), CellSize, direction);
            }
        }

        private void HandleArmCut(CatId id, Vector2Int doorCell)
        {
            if (playPixelEffects)
            {
                PixelFx.Sparks(CellWorld(doorCell), CellSize);
            }
        }

        private void HandleObjectCrushed(Vector2Int cell)
        {
            if (playPixelEffects)
            {
                PixelFx.Smash(CellWorld(cell), CellSize);
            }
        }

        private void HandleSwitchChanged(Vector2Int cell, bool active)
        {
            if (playPixelEffects && active)
            {
                PixelFx.Sparkle(CellWorld(cell), CellSize, switchSparkleColor);
            }
        }

        private void HandleDoorChanged(Vector2Int cell, bool open)
        {
            if (playPixelEffects)
            {
                PixelFx.DoorDust(CellWorld(cell), CellSize);
            }
        }

        private void HandleCatSwitched(CatId id)
        {
            if (playPixelEffects)
            {
                Color color = id == CatId.CatA ? catASparkleColor : catBSparkleColor;
                PixelFx.Sparkle(CellWorld(gridManager.GetCat(id).RootPosition), CellSize, color);
            }
        }

        // ---------------------------------------------------------------- win

        private void HandleWon()
        {
            if (winRoutine != null)
            {
                StopCoroutine(winRoutine);
            }

            winRoutine = StartCoroutine(WinSequence());
        }

        /// <summary>
        /// Replaces both hands by one handshake sprite (two clasped hands) on the cell where they met. The sprite is turned
        /// along the line the hands met on and pops in with a small overshoot.
        /// </summary>
        private void ShowHandshake(Vector3 position)
        {
            CatArmController catA = gridManager.GetCat(CatId.CatA);
            CatArmController catB = gridManager.GetCat(CatId.CatB);

            DestroyHandshake();
            handshakeObject = new GameObject("Handshake");
            handshakeObject.transform.position = position;

            // Hands only meet head-on, so the picture (two arms and the clasp) lies along the line cat A arrived on.
            Vector2Int arrival = catA.HandDirection;
            float angle = arrival.x != 0 ? 0f : 90f;

            handshakeObject.transform.rotation = Quaternion.Euler(0f, 0f, angle);

            var spriteRenderer = handshakeObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = handshakeSprite;
            spriteRenderer.sortingOrder = Mathf.Max(catA.HandSortingOrder, catB.HandSortingOrder) + 1;

            catA.SetHandVisible(false);
            catB.SetHandVisible(false);

            // The hand sprites and the handshake sprite share the same pixel size, so the scale of a hand fits.
            StartCoroutine(PopRoutine(handshakeObject.transform, catA.HandScale, 0.25f));
        }

        private static IEnumerator PopRoutine(Transform target, Vector3 fullScale, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration && target != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float overshoot = 1.70158f;
                float u = t - 1f;
                target.localScale = fullScale * (1f + (overshoot + 1f) * u * u * u + overshoot * u * u);
                yield return null;
            }

            if (target != null)
            {
                target.localScale = fullScale;
            }
        }

        private void DestroyHandshake()
        {
            if (handshakeObject != null)
            {
                Destroy(handshakeObject);
                handshakeObject = null;
            }
        }

        // Reset or a new level: bring the hands back and remove the handshake sprite.
        private void HandleStateChanged()
        {
            if (handshakeObject == null || gridManager.IsWon)
            {
                return;
            }

            DestroyHandshake();
            gridManager.GetCat(CatId.CatA).SetHandVisible(true);
            gridManager.GetCat(CatId.CatB).SetHandVisible(true);
        }

        /// <summary>
        /// Victory effects first (confetti, ring, pulsing cats), then a fade to black and the next level (no popup).
        /// Only a finished Speedrun stays, to show its time. Resetting the level during the effects cancels the result.
        /// </summary>
        private IEnumerator WinSequence()
        {
            Vector3 handshakePosition = gridManager.Coords.GridToWorld(gridManager.GetCat(CatId.CatA).HandPosition);
            WinEffect.Spawn(winEffectPrefab, handshakePosition, gridManager.Coords.CellSize, winEffectDuration);
            if (handshakeSprite != null)
            {
                ShowHandshake(handshakePosition);
            }
            else
            {
                gridManager.GetCat(CatId.CatA).PlayHandshakePose(0.2f);
                gridManager.GetCat(CatId.CatB).PlayHandshakePose(0.2f);
            }

            gridManager.GetCat(CatId.CatA).PlayWinPulse(winEffectDuration);
            gridManager.GetCat(CatId.CatB).PlayWinPulse(winEffectDuration);

            yield return new WaitForSecondsRealtime(winEffectDuration);
            winRoutine = null;

            if (!gridManager.IsWon)
            {
                yield break;
            }

            gridManager.FinishWinPresentation();

            GameSceneManager manager = GameSceneManager.Instance;
            if (manager == null)
            {
                yield break;
            }

            bool wasSpeedrun = manager.IsSpeedrunActive;
            GameAnalytics.LevelComplete(manager.CurrentLevelIndex, wasSpeedrun, gridManager.MoveCount, Time.timeSinceLevelLoad);
            manager.ReportLevelCompleted();

            // A finished Speedrun stays on screen: the HUD shows its time. Every other win moves on by itself.
            bool speedrunJustFinished = wasSpeedrun && !manager.IsSpeedrunActive;
            if (speedrunJustFinished)
            {
                yield break;
            }

            yield return new WaitForSecondsRealtime(nextLevelDelay);

            // The player may have left through the menu during the pause.
            if (!manager.IsTransitioning && manager.CurrentScene == SceneId.Gameplay)
            {
                manager.GoToNextLevel();
            }
        }
    }
}
