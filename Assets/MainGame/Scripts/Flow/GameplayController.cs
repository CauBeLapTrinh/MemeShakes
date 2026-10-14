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

        [Tooltip("Victory effect prefab (Assets/MainGame/Effects/WinEffect). A generated effect is used when empty.")]
        [SerializeField] private WinEffect winEffectPrefab;

        [Tooltip("Sprite shown when the two hands meet (two hands clasped, Handshakes.png). It replaces both hand sprites. When empty, the hands just meet in the middle of the cell.")]
        [SerializeField] private Sprite handshakeSprite;

        [Tooltip("When the hands meet at an angle, this many pixels are cut off each side of the handshake picture so only the clasp remains.")]
        [SerializeField, Min(0f)] private float claspTrimPixels = 38f;

        [Tooltip("How long the victory effects play before the win popup (or the next Speedrun level) appears.")]
        [SerializeField, Min(0f)] private float winEffectDuration = 1.4f;

        [Tooltip("Extra pause after the victory effects, before the next Speedrun level loads.")]
        [SerializeField, Min(0f)] private float speedrunAdvanceDelay = 0.6f;

        private Coroutine winRoutine;
        private GameObject handshakeObject;
        private Sprite claspSprite;

        private void OnEnable()
        {
            if (gridManager != null)
            {
                gridManager.Won += HandleWon;
                gridManager.StateChanged += HandleStateChanged;
            }
        }

        private void OnDisable()
        {
            if (gridManager != null)
            {
                gridManager.Won -= HandleWon;
                gridManager.StateChanged -= HandleStateChanged;
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
            if (winRoutine != null)
            {
                StopCoroutine(winRoutine);
            }

            winRoutine = StartCoroutine(WinSequence());
        }

        /// <summary>
        /// Replaces both hands by one handshake sprite (two clasped hands) on the cell where they met. The sprite is turned
        /// along the direction cat A arrived from and pops in with a small overshoot.
        /// </summary>
        private void ShowHandshake(Vector3 position)
        {
            CatArmController catA = gridManager.GetCat(CatId.CatA);
            CatArmController catB = gridManager.GetCat(CatId.CatB);

            DestroyHandshake();
            handshakeObject = new GameObject("Handshake");
            handshakeObject.transform.position = position;

            // Direction each hand arrived in. Head-on hands use the full picture (arms included); hands that meet at an
            // angle use only the clasp, turned along the line between the two arrivals, and the arms join it from the sides.
            Vector2 arrivalA = AngleToVector(catA.HandAngle);
            Vector2 arrivalB = AngleToVector(catB.HandAngle);
            bool headOn = Vector2.Dot(arrivalA, arrivalB) < -0.99f;

            Vector2 axis = headOn ? arrivalA : arrivalA - arrivalB;
            float angle = Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg;
            if (angle > 90f)
            {
                angle -= 180f;
            }
            else if (angle <= -90f)
            {
                angle += 180f;
            }

            handshakeObject.transform.rotation = Quaternion.Euler(0f, 0f, angle);

            var spriteRenderer = handshakeObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = headOn ? handshakeSprite : GetClaspSprite();
            spriteRenderer.sortingOrder = Mathf.Max(catA.HandSortingOrder, catB.HandSortingOrder) + 1;

            catA.SetHandVisible(false);
            catB.SetHandVisible(false);

            // The hand sprites and the handshake sprite share the same pixel size, so the scale of a hand fits.
            StartCoroutine(PopRoutine(handshakeObject.transform, catA.HandScale, 0.25f));
        }

        private static Vector2 AngleToVector(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Round(Mathf.Cos(radians)), Mathf.Round(Mathf.Sin(radians)));
        }

        /// <summary>The handshake picture without its arms: only the two clasped hands in the middle.</summary>
        private Sprite GetClaspSprite()
        {
            if (claspSprite == null)
            {
                Rect source = handshakeSprite.textureRect;
                float trim = Mathf.Clamp(claspTrimPixels, 0f, source.width * 0.4f);
                var rect = new Rect(source.x + trim, source.y, source.width - trim * 2f, source.height);
                claspSprite = Sprite.Create(handshakeSprite.texture, rect, new Vector2(0.5f, 0.5f), handshakeSprite.pixelsPerUnit);
            }

            return claspSprite;
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
        /// Victory effects first (confetti, ring, pulsing cats), then the result: the win popup or the next Speedrun level.
        /// Resetting the level during the effects cancels the result.
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

            manager.ReportLevelCompleted();

            // Still true only while more Speedrun levels remain.
            if (manager.IsSpeedrunActive)
            {
                yield return new WaitForSecondsRealtime(speedrunAdvanceDelay);
                manager.GoToNextLevel();
            }
        }
    }
}
