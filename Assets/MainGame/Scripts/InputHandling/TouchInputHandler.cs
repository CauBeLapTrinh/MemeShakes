using UnityEngine;
using UnityEngine.EventSystems;

namespace Catshakes
{
    /// <summary>
    /// Turns touch (or mouse, for editor testing) gestures into hand moves for the selected cat.
    ///
    /// Hybrid scheme (default):
    ///  - Touch that starts on a cat selects it and DRAGS its hand: the hand follows the finger one cell at a time,
    ///    and dragging back over the arm retracts it.
    ///  - Touch that starts anywhere else SWIPES: each swipe of at least the threshold moves the selected cat's
    ///    hand one cell; keep dragging to chain more moves.
    ///  - A short tap on a cell next to the selected hand moves the hand there (tapping the cell behind it retracts).
    /// Uses the Input System package when it is enabled, and the legacy Input class otherwise.
    /// </summary>
    public sealed class TouchInputHandler : MonoBehaviour
    {
        public enum ControlScheme
        {
            Hybrid,
            SwipeOnly,
            DragOnly
        }

        private readonly struct PointerSample
        {
            public readonly Vector2 Position;
            public readonly bool IsPressed;
            public readonly bool PressedThisFrame;
            public readonly bool ReleasedThisFrame;
            public readonly int PointerId;

            public PointerSample(Vector2 position, bool isPressed, bool pressedThisFrame, bool releasedThisFrame, int pointerId)
            {
                Position = position;
                IsPressed = isPressed;
                PressedThisFrame = pressedThisFrame;
                ReleasedThisFrame = releasedThisFrame;
                PointerId = pointerId;
            }
        }

        [Header("References")]
        [SerializeField] private GridManager gridManager;
        [Tooltip("Camera that renders the board. Defaults to Camera.main.")]
        [SerializeField] private Camera gameCamera;

        [Header("Controls")]
        [SerializeField] private ControlScheme controlScheme = ControlScheme.Hybrid;
        [Tooltip("Minimum finger travel for one swipe step, in inches (converted with the screen DPI).")]
        [SerializeField, Min(0.02f)] private float swipeThresholdInches = 0.18f;
        [Tooltip("Minimum time between two moves while dragging the hand, in seconds.")]
        [SerializeField, Min(0.01f)] private float dragStepInterval = 0.09f;
        [SerializeField] private bool tapAdjacentCellToMove = true;
        [Tooltip("A touch that travels less than this many inches counts as a tap.")]
        [SerializeField, Min(0.02f)] private float tapMaxTravelInches = 0.12f;

        private const float FallbackDpi = 160f;

        private bool isTracking;
        private bool followFinger;
        private bool gestureMoved;
        private bool pressSelectedNewCat;
        private Vector2 pressPosition;
        private Vector2 swipeAnchor;
        private float nextDragTime;
        private bool hasSuppressedCell;
        private Vector2Int suppressedCell;

        private float Dpi => Screen.dpi > 0f ? Screen.dpi : FallbackDpi;
        private float SwipeThresholdPixels => Mathf.Max(24f, swipeThresholdInches * Dpi);
        private float TapMaxTravelPixels => Mathf.Max(12f, tapMaxTravelInches * Dpi);

        private void Awake()
        {
            if (gridManager == null)
            {
                gridManager = FindFirstObjectByType<GridManager>();
            }

            if (gameCamera == null)
            {
                gameCamera = Camera.main;
            }
        }

        private void OnDisable()
        {
            isTracking = false;
        }

        private void Update()
        {
            if (gridManager == null || gameCamera == null || !gridManager.IsLevelLoaded)
            {
                isTracking = false;
                return;
            }

            if (!TryReadPointer(out PointerSample pointer))
            {
                return;
            }

            if (pointer.PressedThisFrame)
            {
                BeginGesture(pointer);
            }

            if (!isTracking)
            {
                return;
            }

            if (pointer.IsPressed)
            {
                ContinueGesture(pointer.Position);
            }

            if (pointer.ReleasedThisFrame)
            {
                EndGesture(pointer.Position);
            }
            else if (!pointer.IsPressed)
            {
                isTracking = false;
            }
        }

        // ---------------------------------------------------------------- gesture phases

        private void BeginGesture(PointerSample pointer)
        {
            isTracking = false;
            if (IsPointerOverUi(pointer.PointerId))
            {
                return;
            }

            isTracking = true;
            gestureMoved = false;
            pressSelectedNewCat = false;
            hasSuppressedCell = false;
            pressPosition = pointer.Position;
            swipeAnchor = pointer.Position;
            nextDragTime = 0f;

            Vector2Int cell = ScreenToCell(pointer.Position);
            bool startedOnCat = gridManager.TryGetCatAtCell(cell, out CatArmController cat);

            if (startedOnCat && cat.Id != gridManager.ActiveCat)
            {
                gridManager.SelectCat(cat.Id);
                pressSelectedNewCat = true;
            }

            switch (controlScheme)
            {
                case ControlScheme.SwipeOnly:
                    followFinger = false;
                    break;
                case ControlScheme.DragOnly:
                    followFinger = true;
                    break;
                default:
                    followFinger = startedOnCat;
                    break;
            }
        }

        private void ContinueGesture(Vector2 screenPosition)
        {
            if (gridManager.IsWon)
            {
                return;
            }

            if (followFinger)
            {
                FollowFinger(screenPosition);
            }
            else
            {
                DetectSwipe(screenPosition);
            }
        }

        private void EndGesture(Vector2 screenPosition)
        {
            isTracking = false;

            if (gestureMoved || pressSelectedNewCat || !tapAdjacentCellToMove || gridManager.IsWon)
            {
                return;
            }

            if ((screenPosition - pressPosition).magnitude > TapMaxTravelPixels)
            {
                return;
            }

            CatArmController cat = gridManager.GetCat(gridManager.ActiveCat);
            Vector2Int cell = ScreenToCell(screenPosition);

            // Only taps on a neighbouring cell are moves; other taps are ignored silently.
            if (DirectionExtensions.TryFromVector(cell - cat.HandPosition, out _))
            {
                gridManager.TryMoveHandToCell(cat.Id, cell);
            }
        }

        // ---------------------------------------------------------------- gesture handlers

        private void FollowFinger(Vector2 screenPosition)
        {
            Vector2Int fingerCell = ScreenToCell(screenPosition);

            if (hasSuppressedCell)
            {
                if (fingerCell == suppressedCell)
                {
                    return;
                }

                hasSuppressedCell = false;
            }

            if (Time.unscaledTime < nextDragTime)
            {
                return;
            }

            CatArmController cat = gridManager.GetCat(gridManager.ActiveCat);
            Vector2Int delta = fingerCell - cat.HandPosition;
            if (!DirectionExtensions.TryFromDominantAxis(delta, out Direction direction))
            {
                return;
            }

            MoveOutcome outcome = gridManager.TryMoveHand(cat.Id, direction);
            nextDragTime = Time.unscaledTime + dragStepInterval;

            if (outcome.IsSuccess())
            {
                gestureMoved = true;
            }
            else
            {
                // Do not retry (and spam feedback) until the finger enters a different cell.
                suppressedCell = fingerCell;
                hasSuppressedCell = true;
            }
        }

        private void DetectSwipe(Vector2 screenPosition)
        {
            Vector2 delta = screenPosition - swipeAnchor;
            if (delta.magnitude < SwipeThresholdPixels)
            {
                return;
            }

            if (DirectionExtensions.TryFromDominantAxis(delta, out Direction direction))
            {
                gridManager.TryMoveHand(gridManager.ActiveCat, direction);
                gestureMoved = true;
            }

            swipeAnchor = screenPosition;
        }

        // ---------------------------------------------------------------- helpers

        private Vector2Int ScreenToCell(Vector2 screenPosition)
        {
            Vector3 world = gameCamera.ScreenToWorldPoint(
                new Vector3(screenPosition.x, screenPosition.y, Mathf.Abs(gameCamera.transform.position.z)));
            return gridManager.Coords.WorldToGrid(world);
        }

        private static bool IsPointerOverUi(int pointerId)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            return pointerId >= 0
                ? eventSystem.IsPointerOverGameObject(pointerId)
                : eventSystem.IsPointerOverGameObject();
        }

        private static bool TryReadPointer(out PointerSample sample)
        {
#if ENABLE_INPUT_SYSTEM
            UnityEngine.InputSystem.Pointer pointer = UnityEngine.InputSystem.Pointer.current;
            if (pointer == null)
            {
                sample = default;
                return false;
            }

            sample = new PointerSample(
                pointer.position.ReadValue(),
                pointer.press.isPressed,
                pointer.press.wasPressedThisFrame,
                pointer.press.wasReleasedThisFrame,
                -1);
            return true;
#else
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                bool began = touch.phase == TouchPhase.Began;
                bool ended = touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled;
                sample = new PointerSample(touch.position, !ended, began, ended, touch.fingerId);
                return true;
            }

            sample = new PointerSample(
                Input.mousePosition,
                Input.GetMouseButton(0),
                Input.GetMouseButtonDown(0),
                Input.GetMouseButtonUp(0),
                -1);
            return true;
#endif
        }
    }
}
