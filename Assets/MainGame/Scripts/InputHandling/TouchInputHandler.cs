using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Catshakes
{
    /// <summary>
    /// Turns touch (or mouse, for editor testing) gestures into hand moves. Every finger is tracked on its own, so two
    /// fingers can steer both cats at the same time (one finger per cat).
    ///
    /// Hybrid scheme (default), per finger:
    ///  - A touch that starts on a cat (its hand, arm or body) takes that cat and DRAGS its hand: the hand follows the
    ///    finger one cell at a time, and dragging back over the arm retracts it.
    ///  - A touch that starts anywhere else SWIPES: each swipe of at least the threshold moves a hand one cell; keep
    ///    dragging to chain more moves. It steers the selected cat, or the other cat while a finger already holds the
    ///    selected one.
    ///  - A short tap on a cell next to the selected hand moves the hand there (tapping the cell behind it retracts).
    /// A cat is steered by one finger at a time; a second finger on a cat that is already held is ignored.
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

        /// <summary>One finger (or the mouse) from press to release.</summary>
        private sealed class Gesture
        {
            public CatId Cat;
            public bool FollowFinger;
            public bool Moved;
            public bool SelectedNewCat;
            public Vector2 PressPosition;
            public Vector2 SwipeAnchor;
            public float NextDragTime;
            public bool HasSuppressedCell;
            public Vector2Int SuppressedCell;
            public bool SeenThisFrame;
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
        private const int MousePointerId = -1;

        private readonly Dictionary<int, Gesture> gestures = new Dictionary<int, Gesture>();
        private readonly List<PointerSample> samples = new List<PointerSample>(4);
        private readonly List<int> finished = new List<int>(4);

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

#if !ENABLE_INPUT_SYSTEM
            Input.multiTouchEnabled = true;
#endif
        }

        private void OnDisable()
        {
            gestures.Clear();
        }

        private void Update()
        {
            if (gridManager == null || gameCamera == null || !gridManager.IsLevelLoaded || PersistentPopups.IsAnyOpen)
            {
                gestures.Clear();
                return;
            }

            ReadPointers(samples);
            foreach (Gesture gesture in gestures.Values)
            {
                gesture.SeenThisFrame = false;
            }

            foreach (PointerSample pointer in samples)
            {
                if (pointer.PressedThisFrame)
                {
                    BeginGesture(pointer);
                }

                if (!gestures.TryGetValue(pointer.PointerId, out Gesture gesture))
                {
                    continue;
                }

                gesture.SeenThisFrame = true;
                if (pointer.IsPressed)
                {
                    ContinueGesture(gesture, pointer.Position);
                }

                if (pointer.ReleasedThisFrame)
                {
                    EndGesture(gesture, pointer.Position);
                    finished.Add(pointer.PointerId);
                }
                else if (!pointer.IsPressed)
                {
                    finished.Add(pointer.PointerId);
                }
            }

            // Fingers that vanished without a release (focus lost, too many touches) end without a tap.
            foreach (KeyValuePair<int, Gesture> pair in gestures)
            {
                if (!pair.Value.SeenThisFrame && !finished.Contains(pair.Key))
                {
                    finished.Add(pair.Key);
                }
            }

            foreach (int id in finished)
            {
                gestures.Remove(id);
            }

            finished.Clear();
        }

        // ---------------------------------------------------------------- gesture phases

        private void BeginGesture(PointerSample pointer)
        {
            gestures.Remove(pointer.PointerId);
            if (IsPointerOverUi(pointer.PointerId))
            {
                return;
            }

            Vector2Int cell = ScreenToCell(pointer.Position);
            bool startedOnCat = gridManager.TryGetCatAtCell(cell, out CatArmController touchedCat);
            if (startedOnCat && IsHeld(touchedCat.Id))
            {
                // Another finger already steers this cat.
                return;
            }

            CatId cat;
            if (startedOnCat)
            {
                cat = touchedCat.Id;
            }
            else
            {
                // Swipe elsewhere: the selected cat, or the other one while the selected cat is held by a finger.
                cat = gridManager.ActiveCat;
                if (IsHeld(cat))
                {
                    cat = cat.Other();
                    if (IsHeld(cat))
                    {
                        return;
                    }
                }
            }

            var gesture = new Gesture
            {
                Cat = cat,
                PressPosition = pointer.Position,
                SwipeAnchor = pointer.Position
            };

            if (startedOnCat && cat != gridManager.ActiveCat)
            {
                gridManager.SelectCat(cat);
                gesture.SelectedNewCat = true;
            }

            switch (controlScheme)
            {
                case ControlScheme.SwipeOnly:
                    gesture.FollowFinger = false;
                    break;
                case ControlScheme.DragOnly:
                    gesture.FollowFinger = true;
                    break;
                default:
                    gesture.FollowFinger = startedOnCat;
                    break;
            }

            gestures[pointer.PointerId] = gesture;
        }

        private void ContinueGesture(Gesture gesture, Vector2 screenPosition)
        {
            if (gridManager.IsWon)
            {
                return;
            }

            if (gesture.FollowFinger)
            {
                FollowFinger(gesture, screenPosition);
            }
            else
            {
                DetectSwipe(gesture, screenPosition);
            }
        }

        private void EndGesture(Gesture gesture, Vector2 screenPosition)
        {
            if (gesture.Moved || gesture.SelectedNewCat || !tapAdjacentCellToMove || gridManager.IsWon)
            {
                return;
            }

            if ((screenPosition - gesture.PressPosition).magnitude > TapMaxTravelPixels)
            {
                return;
            }

            CatArmController cat = gridManager.GetCat(gesture.Cat);
            Vector2Int cell = ScreenToCell(screenPosition);

            // Only taps on a neighbouring cell are moves; other taps are ignored silently.
            if (DirectionExtensions.TryFromVector(cell - cat.HandPosition, out _))
            {
                gridManager.TryMoveHandToCell(cat.Id, cell);
            }
        }

        // ---------------------------------------------------------------- gesture handlers

        private void FollowFinger(Gesture gesture, Vector2 screenPosition)
        {
            Vector2Int fingerCell = ScreenToCell(screenPosition);

            if (gesture.HasSuppressedCell)
            {
                if (fingerCell == gesture.SuppressedCell)
                {
                    return;
                }

                gesture.HasSuppressedCell = false;
            }

            if (Time.unscaledTime < gesture.NextDragTime)
            {
                return;
            }

            CatArmController cat = gridManager.GetCat(gesture.Cat);
            Vector2Int delta = fingerCell - cat.HandPosition;
            if (!DirectionExtensions.TryFromDominantAxis(delta, out Direction direction))
            {
                return;
            }

            MoveOutcome outcome = gridManager.TryMoveHand(cat.Id, direction);
            gesture.NextDragTime = Time.unscaledTime + dragStepInterval;

            if (outcome.IsSuccess())
            {
                gesture.Moved = true;
            }
            else
            {
                // Do not retry (and spam feedback) until the finger enters a different cell.
                gesture.SuppressedCell = fingerCell;
                gesture.HasSuppressedCell = true;
            }
        }

        private void DetectSwipe(Gesture gesture, Vector2 screenPosition)
        {
            Vector2 delta = screenPosition - gesture.SwipeAnchor;
            if (delta.magnitude < SwipeThresholdPixels)
            {
                return;
            }

            if (DirectionExtensions.TryFromDominantAxis(delta, out Direction direction))
            {
                gridManager.TryMoveHand(gesture.Cat, direction);
                gesture.Moved = true;
            }

            gesture.SwipeAnchor = screenPosition;
        }

        // ---------------------------------------------------------------- helpers

        private bool IsHeld(CatId cat)
        {
            foreach (Gesture gesture in gestures.Values)
            {
                if (gesture.Cat == cat)
                {
                    return true;
                }
            }

            return false;
        }

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

        /// <summary>Every active finger (touch id as PointerId), or the mouse as a single pointer when there is no touch.</summary>
        private static void ReadPointers(List<PointerSample> result)
        {
            result.Clear();
#if ENABLE_INPUT_SYSTEM
            UnityEngine.InputSystem.Touchscreen touchscreen = UnityEngine.InputSystem.Touchscreen.current;
            if (touchscreen != null)
            {
                foreach (UnityEngine.InputSystem.Controls.TouchControl touch in touchscreen.touches)
                {
                    bool pressed = touch.press.isPressed;
                    bool down = touch.press.wasPressedThisFrame;
                    bool up = touch.press.wasReleasedThisFrame;
                    if (pressed || down || up)
                    {
                        result.Add(new PointerSample(touch.position.ReadValue(), pressed, down, up, touch.touchId.ReadValue()));
                    }
                }

                if (result.Count > 0)
                {
                    return;
                }
            }

            UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                result.Add(new PointerSample(
                    mouse.position.ReadValue(),
                    mouse.leftButton.isPressed,
                    mouse.leftButton.wasPressedThisFrame,
                    mouse.leftButton.wasReleasedThisFrame,
                    MousePointerId));
            }
#else
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                bool began = touch.phase == TouchPhase.Began;
                bool ended = touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled;
                result.Add(new PointerSample(touch.position, !ended, began, ended, touch.fingerId));
            }

            if (result.Count > 0)
            {
                return;
            }

            result.Add(new PointerSample(
                Input.mousePosition,
                Input.GetMouseButton(0),
                Input.GetMouseButtonDown(0),
                Input.GetMouseButtonUp(0),
                MousePointerId));
#endif
        }
    }
}
