using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// One cat and its snake-like arm. The arm is a list of grid cells (<see cref="ArmPath"/>). The first cell is the
    /// hand's start cell, one cell in front of the cat's face (never on the body); the last cell is the hand.
    /// Extending appends one cell, retracting removes the last one (the start cell is never removed).
    /// This class owns the arm data and its visuals; whether a move is legal is decided by <see cref="GridManager"/>.
    /// </summary>
    public sealed class CatArmController : MonoBehaviour
    {
        private enum ViewChange
        {
            Extended,
            Retracted
        }

        [Header("Identity")]
        [SerializeField] private CatId catId = CatId.CatA;
        [Tooltip("Direction the hand sprite faces while the arm is fully retracted (artwork must face right by default).")]
        [SerializeField] private Direction restFacing = Direction.Right;

        [Header("View")]
        [Tooltip("Cat body sprite placed on the root cell.")]
        [SerializeField] private Transform bodyVisual;
        [Tooltip("Hand sprite placed on the tip of the arm. Artwork should face right.")]
        [SerializeField] private Transform handVisual;
        [Tooltip("Draws the arm through every cell of the path. Use World Space; Corner/Cap vertices give round joints.")]
        [SerializeField] private LineRenderer armLine;
        [Header("Arm artwork (optional; the arm line is drawn with the straight-arm texture)")]
        [Tooltip("Body sprite whose arm starts on the right edge (artwork faces right; it is mirrored when the cat faces left).")]
        [SerializeField] private Sprite bodySprite;
        [Tooltip("Body sprite without the arm, used when the cat faces up or down; the arm line starts behind the body.")]
        [SerializeField] private Sprite bodyNoArmSprite;
        [Tooltip("Hand sprite (open hand with a piece of arm, artwork faces right).")]
        [SerializeField] private Sprite handSprite;
        [Tooltip("Straight arm picture, one cell wide. It is the texture of the arm line and repeats once per cell.")]
        [SerializeField] private Sprite straightArmSprite;
        [Tooltip("Elbow picture (arm coming from the left and going down), placed on every bend of the arm.")]
        [SerializeField] private Sprite elbowArmSprite;

        [Tooltip("Optional object enabled only while this cat is the controlled one.")]
        [SerializeField] private GameObject selectionIndicator;
        [Tooltip("Optional label showing the remaining steps.")]
        [SerializeField] private TMP_Text stepsLabel;
        [Tooltip("{0} = steps remaining, {1} = max steps.")]
        [SerializeField] private string stepsLabelFormat = "{0}";
        [Tooltip("Local position of the steps label on the body when the body sprite is set (cat facing right; mirrored for left).")]
        [SerializeField] private Vector2 stepsLabelOffset = new Vector2(0.62f, 0.6f);

        [Header("Animation")]
        [SerializeField, Min(0f)] private float moveDuration = 0.12f;
        [Tooltip("Pause after a cut, before the stump starts to retract.")]
        [SerializeField, Min(0f)] private float cutPauseDuration = 0.15f;
        [Tooltip("Time the stump needs to retract by one cell after a cut.")]
        [SerializeField, Min(0f)] private float cutRetractStepDuration = 0.08f;
        [Tooltip("Time the new hand needs to grow (scale up) after the stump has retracted.")]
        [SerializeField, Min(0f)] private float cutRegrowDuration = 0.3f;

        private readonly List<Vector2Int> armPath = new List<Vector2Int>();
        private GridSpace coords;
        private Vector2Int rootPosition;
        private int maxSteps = 1;
        private Coroutine animationRoutine;
        private Coroutine cutRoutine;
        private Coroutine pulseRoutine;
        private Coroutine handshakeRoutine;
        private Vector3 handBaseScale = Vector3.one;
        private Vector3 bodyBaseScale = Vector3.one;
        private bool scalesCaptured;
        private Sprite alignedHandSprite;
        private Material armTextureMaterial;
        private readonly List<LineRenderer> armRuns = new List<LineRenderer>();
        private readonly List<SpriteRenderer> armElbows = new List<SpriteRenderer>();
        private readonly List<Vector3> armPoints = new List<Vector3>();
        private int armRunCount;
        private int armElbowCount;
        private int armSortingLayerId;
        private int armRunSortingOrder;

        /// <summary>Raised whenever the arm length (and therefore the remaining steps) changes.</summary>
        public event Action<CatArmController> StepsChanged;

        public CatId Id => catId;
        public Vector2Int RootPosition => rootPosition;
        public int MaxSteps => maxSteps;
        public IReadOnlyList<Vector2Int> ArmPath => armPath;
        /// <summary>Number of cells the hand has moved beyond its start cell.</summary>
        public int ExtendedLength => Mathf.Max(0, armPath.Count - 1);
        public int StepsRemaining => maxSteps - ExtendedLength;
        public bool CanExtend => StepsRemaining > 0;

        /// <summary>True once the hand has moved beyond its start cell.</summary>
        public bool HasExtendedArm => armPath.Count > 1;
        public bool IsSelected { get; private set; }
        public bool IsAnimating => animationRoutine != null || cutRoutine != null;

        /// <summary>True from the moment the arm is cut until the new hand has grown back. Input must wait.</summary>
        public bool IsPlayingCut => cutRoutine != null;

        /// <summary>The tip of the arm; equals the hand start cell (in front of the cat) while the arm is not extended.</summary>
        public Vector2Int HandPosition => armPath.Count > 0 ? armPath[armPath.Count - 1] : rootPosition;

        /// <summary>The cell directly behind the hand (the one the hand would return to when retracting).</summary>
        public Vector2Int CellBehindHand => armPath.Count > 1 ? armPath[armPath.Count - 2] : rootPosition;

        /// <summary>Direction the hand points to: the direction of the last arm step, or the cat's facing.</summary>
        public Vector2Int HandDirection => armPath.Count > 1 ? HandPosition - CellBehindHand : restFacing.ToVector();

        /// <summary>The sprite of the hand, used for the severed hand left behind by a closing door.</summary>
        public Sprite HandSprite
        {
            get
            {
                if (handVisual == null)
                {
                    return null;
                }

                SpriteRenderer handRenderer = handVisual.GetComponentInChildren<SpriteRenderer>();
                return handRenderer != null ? handRenderer.sprite : null;
            }
        }

        /// <summary>World scale of the hand sprite, so a severed hand has the same size.</summary>
        public Vector3 HandScale
        {
            get
            {
                if (handVisual == null)
                {
                    return Vector3.one;
                }

                SpriteRenderer handRenderer = handVisual.GetComponentInChildren<SpriteRenderer>();
                return handRenderer != null ? handRenderer.transform.lossyScale : Vector3.one;
            }
        }

        /// <summary>Sorting order of the hand sprite, so the handshake sprite can be drawn right above it.</summary>
        public int HandSortingOrder
        {
            get
            {
                if (handVisual == null)
                {
                    return 0;
                }

                SpriteRenderer handRenderer = handVisual.GetComponentInChildren<SpriteRenderer>();
                return handRenderer != null ? handRenderer.sortingOrder : 0;
            }
        }

        /// <summary>Shows or hides the hand sprite (hidden while the handshake sprite replaces both hands).</summary>
        public void SetHandVisible(bool visible)
        {
            if (handVisual != null)
            {
                handVisual.gameObject.SetActive(visible);
            }

        }

        /// <summary>Z rotation in degrees of the hand sprite for the current arm.</summary>
        public float HandAngle => FacingAngle();

        /// <summary>
        /// Finds the arm index of <paramref name="cell"/> when a closing door there would cut the arm: any arm cell except the
        /// start cell, which includes the tip. (The start cell never lies on a door; levels and pushes keep it off doors.)
        /// </summary>
        public bool TryFindPassThroughIndex(Vector2Int cell, out int index)
        {
            index = armPath.IndexOf(cell);
            return index >= 1;
        }

        /// <summary>
        /// Cuts the arm at the door on arm index <paramref name="index"/> (the tip counts too). Logically the arm is back at
        /// its start cell at once (all steps refunded); visually the stump retracts cell by cell and a new hand grows at the
        /// start cell. <see cref="IsPlayingCut"/> stays true until that is finished.
        /// </summary>
        public void CutAt(int index)
        {
            if (index < 1 || index >= armPath.Count)
            {
                return;
            }

            StopAnimationRoutine();

            // The cells the stump still covers: from the start cell up to the cell in front of the door.
            var stumpPath = new List<Vector2Int>(index);
            for (int i = 0; i < index; i++)
            {
                stumpPath.Add(armPath[i]);
            }

            Vector2Int startCell = armPath[0];
            armPath.Clear();
            armPath.Add(startCell);
            UpdateLabel();
            StepsChanged?.Invoke(this);

            if (coords == null || !isActiveAndEnabled)
            {
                SnapView();
                return;
            }

            cutRoutine = StartCoroutine(CutSequence(stumpPath));
        }

        /// <summary>
        /// Moves the hand sprite back along its own direction so that, when both hands stand on the same cell,
        /// they meet in the middle of it (palm to palm) instead of lying on top of each other.
        /// </summary>
        public void PlayHandshakePose(float duration)
        {
            if (!isActiveAndEnabled || handVisual == null || coords == null)
            {
                return;
            }

            if (handshakeRoutine != null)
            {
                StopCoroutine(handshakeRoutine);
            }

            handshakeRoutine = StartCoroutine(HandshakeRoutine(duration));
        }

        /// <summary>Pulses the hand and body for <paramref name="duration"/> seconds (victory feedback).</summary>
        public void PlayWinPulse(float duration)
        {
            if (!isActiveAndEnabled || duration <= 0f)
            {
                return;
            }

            if (pulseRoutine != null)
            {
                StopCoroutine(pulseRoutine);
            }

            pulseRoutine = StartCoroutine(PulseRoutine(duration));
        }

        /// <summary>
        /// Squashes the cat flat (a door closed on its body or on the hand next to it) and keeps it flat until the next
        /// restore. <paramref name="duration"/> is the time the squash takes.
        /// </summary>
        public void PlayCrushed(float duration)
        {
            if (!isActiveAndEnabled || duration <= 0f)
            {
                return;
            }

            if (pulseRoutine != null)
            {
                StopCoroutine(pulseRoutine);
            }

            pulseRoutine = StartCoroutine(CrushedRoutine(duration));
        }

        private IEnumerator CrushedRoutine(float duration)
        {
            EnsureBaseScales();
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float squash = 1f - 0.9f * t * t;
                Vector3 bodyScale = new Vector3(bodyBaseScale.x * (1f + 0.35f * t), bodyBaseScale.y * squash, bodyBaseScale.z);
                Vector3 handScale = new Vector3(handBaseScale.x * (1f + 0.35f * t), handBaseScale.y * squash, handBaseScale.z);

                if (handVisual != null)
                {
                    handVisual.localScale = handScale;
                }

                if (bodyVisual != null)
                {
                    bodyVisual.localScale = bodyScale;
                }

                yield return null;
            }

            pulseRoutine = null;
        }

        /// <summary>True when <paramref name="cell"/> is the cell the hand would retract into.</summary>
        public bool IsRetractTarget(Vector2Int cell)
        {
            return armPath.Count > 1 && cell == CellBehindHand;
        }

        /// <summary>True for the root cell or any arm cell.</summary>
        public bool OccupiesCell(Vector2Int cell)
        {
            return cell == rootPosition || armPath.Contains(cell);
        }

        /// <summary>True for arm cells only (the root is not part of the arm).</summary>
        public bool ArmContains(Vector2Int cell)
        {
            return armPath.Contains(cell);
        }

        /// <summary>
        /// Swaps in the artwork of <paramref name="skin"/> (body, hand, arm, elbow). Pictures the skin does not have keep
        /// the cat's own ones. Call it before <see cref="Initialize"/>, which puts the pictures in place.
        /// </summary>
        public void ApplySkin(SkinCatalog.Skin skin)
        {
            if (skin == null)
            {
                return;
            }

            if (skin.body != null)
            {
                bodySprite = skin.body;
                bodyNoArmSprite = skin.bodyNoArm;
            }

            if (skin.hand != null && skin.hand != handSprite)
            {
                handSprite = skin.hand;
                alignedHandSprite = null;
            }

            if (skin.straightArm != null)
            {
                straightArmSprite = skin.straightArm;
            }

            if (skin.elbowArm != null)
            {
                elbowArmSprite = skin.elbowArm;
            }
        }

        public void Initialize(GridSpace gridSpace, Vector2Int root, Vector2Int handStart, Direction facing, int steps)
        {
            StopAnimationRoutine();
            coords = gridSpace;
            rootPosition = root;
            restFacing = facing;
            maxSteps = Mathf.Max(1, steps);
            armPath.Clear();
            armPath.Add(handStart);

            if (armLine != null)
            {
                armLine.useWorldSpace = true;
                armLine.enabled = true;
                ConfigureArmTexture();
            }

            ApplyArtwork();
            SetSelected(false);
            SnapView();
            StepsChanged?.Invoke(this);
        }

        /// <summary>Appends <paramref name="cell"/> to the arm. Fails if out of steps or not adjacent to the hand.</summary>
        public bool Extend(Vector2Int cell)
        {
            if (!CanExtend)
            {
                return false;
            }

            Vector2Int delta = cell - HandPosition;
            if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
            {
                return false;
            }

            CompleteAnimationImmediately();
            Vector2Int previousHand = HandPosition;
            armPath.Add(cell);
            UpdateLabel();
            PlayView(ViewChange.Extended, previousHand);
            StepsChanged?.Invoke(this);
            return true;
        }

        /// <summary>Removes the last arm cell. Fails if the hand is on its start cell.</summary>
        public bool RetractLast()
        {
            if (armPath.Count <= 1)
            {
                return false;
            }

            CompleteAnimationImmediately();
            Vector2Int previousHand = HandPosition;
            armPath.RemoveAt(armPath.Count - 1);
            UpdateLabel();
            PlayView(ViewChange.Retracted, previousHand);
            StepsChanged?.Invoke(this);
            return true;
        }

        /// <summary>
        /// Moves the whole cat (body and arm) one cell, because the other cat pushed it. The caller has checked
        /// that every target cell is free. The visuals slide to the new cells.
        /// </summary>
        public void ShiftBy(Vector2Int delta)
        {
            if (delta == Vector2Int.zero)
            {
                return;
            }

            CompleteAnimationImmediately();
            rootPosition += delta;
            for (int i = 0; i < armPath.Count; i++)
            {
                armPath[i] += delta;
            }

            if (coords == null)
            {
                return;
            }

            if (!isActiveAndEnabled || moveDuration <= 0f)
            {
                SnapView();
                return;
            }

            animationRoutine = StartCoroutine(SlideRoutine(new Vector3(-delta.x, -delta.y, 0f) * coords.CellSize));
        }

        /// <summary>Replaces the body cell and the whole arm (used by Undo/Reset) and snaps the visuals.</summary>
        public void RestoreState(Vector2Int root, IReadOnlyList<Vector2Int> path)
        {
            rootPosition = root;
            RestorePath(path);
        }

        /// <summary>Replaces the whole arm (used by Undo/Reset) and snaps the visuals without animation.</summary>
        public void RestorePath(IReadOnlyList<Vector2Int> path)
        {
            StopAnimationRoutine();
            armPath.Clear();
            for (int i = 0; i < path.Count; i++)
            {
                armPath.Add(path[i]);
            }

            SnapView();
            StepsChanged?.Invoke(this);
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (selectionIndicator != null)
            {
                selectionIndicator.SetActive(selected);
            }
        }

        /// <summary>Finishes any running slide at once so the visuals match the logical state.</summary>
        public void CompleteAnimationImmediately()
        {
            if (animationRoutine == null)
            {
                return;
            }

            StopCoroutine(animationRoutine);
            animationRoutine = null;
            SnapView();
        }

        private void Awake()
        {
            EnsureBaseScales();
        }

        private void StopAnimationRoutine()
        {
            if (animationRoutine != null)
            {
                StopCoroutine(animationRoutine);
                animationRoutine = null;
            }

            if (cutRoutine != null)
            {
                StopCoroutine(cutRoutine);
                cutRoutine = null;
            }

            if (pulseRoutine != null)
            {
                StopCoroutine(pulseRoutine);
                pulseRoutine = null;
            }

            if (handshakeRoutine != null)
            {
                StopCoroutine(handshakeRoutine);
                handshakeRoutine = null;
            }
        }

        private void EnsureBaseScales()
        {
            if (scalesCaptured)
            {
                return;
            }

            scalesCaptured = true;
            if (handVisual != null)
            {
                handBaseScale = handVisual.localScale;
            }

            if (bodyVisual != null)
            {
                bodyBaseScale = bodyVisual.localScale;
            }
        }

        private void RestoreScales()
        {
            EnsureBaseScales();
            if (handVisual != null)
            {
                handVisual.localScale = handBaseScale;
            }

            if (bodyVisual != null)
            {
                bodyVisual.localScale = bodyBaseScale;
            }
        }

        private void SetHandScale(float factor)
        {
            EnsureBaseScales();
            if (handVisual != null)
            {
                handVisual.localScale = handBaseScale * factor;
            }
        }

        /// <summary>
        /// After a cut: the old hand stays behind, so the stump (root, start cell ... cell in front of the door)
        /// retracts cell by cell to the start cell, then a new hand grows there.
        /// </summary>
        private IEnumerator CutSequence(List<Vector2Int> stumpPath)
        {
            var points = new List<Vector3>(stumpPath.Count + 1) { coords.GridToWorld(rootPosition) };
            for (int i = 0; i < stumpPath.Count; i++)
            {
                points.Add(coords.GridToWorld(stumpPath[i]));
            }

            // The old hand is gone from the arm; the tip of the stump stays where the cut happened for a moment.
            SetHandScale(0f);
            int tipPoint = points.Count - 1;
            SetCutLine(points, tipPoint, points[tipPoint]);

            float pause = 0f;
            while (pause < cutPauseDuration)
            {
                pause += Time.deltaTime;
                yield return null;
            }

            for (int last = tipPoint; last >= 2; last--)
            {
                Vector3 from = points[last];
                Vector3 to = points[last - 1];
                float elapsed = 0f;

                while (elapsed < cutRetractStepDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / cutRetractStepDuration);
                    SetCutLine(points, last, Vector3.Lerp(from, to, t));
                    yield return null;
                }
            }

            SetCutLine(points, 1, points[1]);

            // A new hand grows on the start cell.
            if (handVisual != null)
            {
                handVisual.position = points[1];
                handVisual.rotation = Quaternion.Euler(0f, 0f, FacingAngle());
            }

            float grow = 0f;
            while (grow < cutRegrowDuration)
            {
                grow += Time.deltaTime;
                float t = Mathf.Clamp01(grow / cutRegrowDuration);
                float overshoot = 1.70158f;
                float u = t - 1f;
                SetHandScale(1f + (overshoot + 1f) * u * u * u + overshoot * u * u);
                yield return null;
            }

            cutRoutine = null;
            SnapView();
        }

        /// <summary>Draws the first <paramref name="fixedPointCount"/> points plus a moving tip.</summary>
        private void SetCutLine(List<Vector3> points, int fixedPointCount, Vector3 tip)
        {
            if (armLine == null)
            {
                return;
            }

            armPoints.Clear();
            for (int i = 0; i < fixedPointCount; i++)
            {
                armPoints.Add(points[i]);
            }

            armPoints.Add(tip);
            DrawArm(armPoints);
        }

        private IEnumerator HandshakeRoutine(float duration)
        {
            Vector3 center = coords.GridToWorld(HandPosition);
            Vector3 forward = Quaternion.Euler(0f, 0f, FacingAngle()) * Vector3.right;

            // Half the length of the hand sprite, so the two fingertips meet at the middle of the cell.
            float halfLength = coords.CellSize * 0.25f;
            SpriteRenderer handRenderer = handVisual.GetComponentInChildren<SpriteRenderer>();
            if (handRenderer != null && handRenderer.sprite != null)
            {
                halfLength = handRenderer.sprite.bounds.extents.x * handRenderer.transform.lossyScale.x;
            }

            float offset = Mathf.Min(halfLength * 0.85f, coords.CellSize * 0.45f);
            Vector3 from = handVisual.position;
            Vector3 to = center - forward * offset;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                handVisual.position = Vector3.Lerp(from, to, t);
                yield return null;
            }

            handVisual.position = to;
            handshakeRoutine = null;
        }

        private IEnumerator PulseRoutine(float duration)
        {
            EnsureBaseScales();
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float factor = 1f + 0.35f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3f)) * (1f - t);

                if (handVisual != null)
                {
                    handVisual.localScale = handBaseScale * factor;
                }

                if (bodyVisual != null)
                {
                    bodyVisual.localScale = bodyBaseScale * factor;
                }

                yield return null;
            }

            RestoreScales();
            pulseRoutine = null;
        }

        private void PlayView(ViewChange change, Vector2Int previousHandCell)
        {
            if (coords == null)
            {
                return;
            }

            if (!isActiveAndEnabled || moveDuration <= 0f)
            {
                SnapView();
                return;
            }

            animationRoutine = StartCoroutine(AnimateTip(change, previousHandCell));
        }

        private IEnumerator AnimateTip(ViewChange change, Vector2Int previousHandCell)
        {
            Vector3 from = coords.GridToWorld(previousHandCell);
            Vector3 to = coords.GridToWorld(HandPosition);

            // Points of the finished arm: root + every cell. A retraction needs one extra point for the vanishing tip.
            int logicalPointCount = armPath.Count + 1;
            int totalPointCount = change == ViewChange.Extended ? logicalPointCount : logicalPointCount + 1;
            int tipIndex = totalPointCount - 1;

            armPoints.Clear();
            for (int i = 0; i < totalPointCount; i++)
            {
                armPoints.Add(PointWorld(Mathf.Min(i, logicalPointCount - 1)));
            }

            // The hand turns smoothly from the direction it had before this step to the one it has now.
            float toAngle = FacingAngle();
            float fromAngle = AngleOfDirection(PreviousHandDirection(change, previousHandCell));
            bool fromFlip = IsLeftAngle(fromAngle);
            bool toFlip = IsLeftAngle(toAngle);

            float elapsed = 0f;
            while (elapsed < moveDuration)
            {
                elapsed += Time.deltaTime;
                float linear = Mathf.Clamp01(elapsed / moveDuration);
                float t = Mathf.SmoothStep(0f, 1f, linear);
                Vector3 tip = Vector3.Lerp(from, to, t);

                armPoints[tipIndex] = tip;
                DrawArm(armPoints);

                PlaceHand(tip, Mathf.LerpAngle(fromAngle, toAngle, t), t < 0.5f ? fromFlip : toFlip);
                yield return null;
            }

            animationRoutine = null;
            SnapView();
        }

        private IEnumerator SlideRoutine(Vector3 startOffset)
        {
            float elapsed = 0f;
            while (elapsed < moveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / moveDuration));
                ApplyViewOffset(Vector3.Lerp(startOffset, Vector3.zero, t));
                yield return null;
            }

            animationRoutine = null;
            SnapView();
        }

        /// <summary>Draws body, arm and hand on their logical cells, shifted by <paramref name="offset"/>.</summary>
        private void ApplyViewOffset(Vector3 offset)
        {
            if (bodyVisual != null)
            {
                bodyVisual.position = coords.GridToWorld(rootPosition) + offset;
            }

            DrawLogicalArm(offset);
            PlaceHand(coords.GridToWorld(HandPosition) + offset);
        }

        private void SnapView()
        {
            if (coords == null)
            {
                return;
            }

            RestoreScales();

            if (bodyVisual != null)
            {
                bodyVisual.position = coords.GridToWorld(rootPosition);
            }

            DrawLogicalArm(Vector3.zero);
            PlaceHand(coords.GridToWorld(HandPosition));
            UpdateLabel();
        }

        // ---------------------------------------------------------------- sprite arm

        private bool UsesTexturedArm => straightArmSprite != null;

        /// <summary>True when the body sprite already draws the start of the arm; the body is then turned toward the hand start.</summary>
        private bool BodyHasArm => bodySprite != null;

        /// <summary>
        /// Puts the body and hand sprites in place. The body is turned so the arm drawn on it points at the hand's start cell:
        /// mirrored when the cat faces left, a quarter turn when it faces up or down.
        /// </summary>
        private void ApplyArtwork()
        {
            SpriteRenderer bodyRenderer = bodyVisual != null ? bodyVisual.GetComponentInChildren<SpriteRenderer>() : null;
            if (bodyRenderer != null)
            {
                if (BodyHasArm)
                {
                    bodyRenderer.sprite = bodySprite;
                }
                else if (bodyNoArmSprite != null)
                {
                    bodyRenderer.sprite = bodyNoArmSprite;
                }

                bodyRenderer.flipX = BodyHasArm && restFacing == Direction.Left;

                float bodyAngle = BodyHasArm && (restFacing == Direction.Up || restFacing == Direction.Down) ? restFacing.ToAngleDegrees() : 0f;
                bodyVisual.localRotation = Quaternion.Euler(0f, 0f, bodyAngle);

                // The label sits in the empty corner beside the head, which turns together with the body; the text stays upright.
                if (bodySprite != null && stepsLabel != null)
                {
                    float x = bodyRenderer.flipX ? -stepsLabelOffset.x : stepsLabelOffset.x;
                    stepsLabel.transform.localPosition = new Vector3(x, stepsLabelOffset.y, stepsLabel.transform.localPosition.z);
                    stepsLabel.transform.rotation = Quaternion.identity;
                }
            }

            SpriteRenderer handRenderer = handVisual != null ? handVisual.GetComponentInChildren<SpriteRenderer>() : null;
            if (handRenderer != null && handSprite != null)
            {
                handRenderer.sprite = GetAlignedHandSprite();
            }

            if (UsesTexturedArm && bodyRenderer != null && selectionIndicator != null)
            {
                // The arm line is drawn right below the body; keep the selection highlight below it.
                SpriteRenderer indicatorRenderer = selectionIndicator.GetComponent<SpriteRenderer>();
                if (indicatorRenderer != null && indicatorRenderer.sortingOrder >= bodyRenderer.sortingOrder - 2)
                {
                    indicatorRenderer.sortingOrder = bodyRenderer.sortingOrder - 3;
                }
            }
        }

        /// <summary>
        /// The hand sprite may be a trimmed part of a square picture (the arm was cut off in the Sprite Editor). Its pivot is
        /// moved to the centre of the whole picture, so the hand sits on its cell exactly like the full artwork would.
        /// </summary>
        private Sprite GetAlignedHandSprite()
        {
            if (alignedHandSprite == null)
            {
                Texture2D texture = handSprite.texture;
                Rect rect = handSprite.textureRect;
                var pivot = new Vector2((texture.width * 0.5f - rect.x) / rect.width, (texture.height * 0.5f - rect.y) / rect.height);
                alignedHandSprite = Sprite.Create(texture, rect, pivot, handSprite.pixelsPerUnit);
                alignedHandSprite.name = handSprite.name;
            }

            return alignedHandSprite;
        }

        /// <summary>
        /// Gives the arm line the straight-arm texture: the line is one cell wide and the picture repeats once per cell, so the
        /// arm keeps the pixel-art look along every bend and shrinks or grows smoothly like a plain line.
        /// </summary>
        private void ConfigureArmTexture()
        {
            if (armLine == null || !UsesTexturedArm || coords == null)
            {
                return;
            }

            Texture2D texture = straightArmSprite.texture;
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Point;

            if (armTextureMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                armTextureMaterial = new Material(shader) { name = "ArmLine (straight-arm)" };
            }

            armTextureMaterial.mainTexture = texture;

            armTextureMaterial.mainTextureScale = new Vector2(1f / coords.CellSize, 1f);
            // Behind the body, so the arm comes out of the cat's side, and below the hand. The elbows lie on top of the lines.
            SpriteRenderer bodyRenderer = bodyVisual != null ? bodyVisual.GetComponentInChildren<SpriteRenderer>() : null;
            armSortingLayerId = bodyRenderer != null ? bodyRenderer.sortingLayerID : armLine.sortingLayerID;
            armRunSortingOrder = (bodyRenderer != null ? bodyRenderer.sortingOrder : HandSortingOrder) - 2;

            if (armRuns.Count == 0)
            {
                armRuns.Add(armLine);
            }

            foreach (LineRenderer run in armRuns)
            {
                ConfigureRun(run);
            }
        }

        /// <summary>One straight piece of the arm: a one-cell-wide line that repeats the arm picture along its length.</summary>
        private void ConfigureRun(LineRenderer run)
        {
            run.useWorldSpace = true;
            run.sharedMaterial = armTextureMaterial;
            run.textureMode = LineTextureMode.Tile;
            run.alignment = LineAlignment.View;
            run.widthMultiplier = coords.CellSize;
            run.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
            run.numCornerVertices = 0;
            run.numCapVertices = 0;
            run.startColor = Color.white;
            run.endColor = Color.white;
            run.colorGradient = new Gradient();
            run.sortingLayerID = armSortingLayerId;
            run.sortingOrder = armRunSortingOrder;
        }

        /// <summary>
        /// Draws the arm through <paramref name="points"/>. With the arm picture every straight stretch is its own line (so the
        /// picture never gets bent or stretched) and every bend gets an elbow picture on top; zero-length pieces are skipped.
        /// </summary>
        private void DrawArm(List<Vector3> points)
        {
            if (armLine == null)
            {
                return;
            }

            if (!UsesTexturedArm || coords == null)
            {
                armLine.positionCount = points.Count;
                for (int i = 0; i < points.Count; i++)
                {
                    armLine.SetPosition(i, points[i]);
                }

                return;
            }

            armRunCount = 0;
            armElbowCount = 0;
            bool hasRun = false;
            Vector3 runStart = default;
            Vector3 runEnd = default;
            Vector3 runDirection = default;

            for (int i = 1; i < points.Count; i++)
            {
                Vector3 delta = points[i] - points[i - 1];
                if (delta.sqrMagnitude < 1e-8f)
                {
                    continue;
                }

                Vector3 direction = delta.normalized;
                if (hasRun && Vector3.Dot(direction, runDirection) > 0.99f)
                {
                    runEnd = points[i];
                    continue;
                }

                if (hasRun)
                {
                    ShowRun(runStart, runEnd);
                    if (Vector3.Dot(direction, runDirection) > -0.99f)
                    {
                        ShowElbow(points[i - 1], -runDirection, direction);
                    }
                }

                hasRun = true;
                runStart = points[i - 1];
                runEnd = points[i];
                runDirection = direction;
            }

            if (hasRun)
            {
                ShowRun(runStart, runEnd);
            }

            for (int i = armRunCount; i < armRuns.Count; i++)
            {
                armRuns[i].enabled = false;
            }

            for (int i = armElbowCount; i < armElbows.Count; i++)
            {
                armElbows[i].gameObject.SetActive(false);
            }
        }

        /// <summary>Draws the arm on its logical cells (root and every cell of the path), shifted by <paramref name="offset"/>.</summary>
        private void DrawLogicalArm(Vector3 offset)
        {
            armPoints.Clear();
            for (int i = 0; i <= armPath.Count; i++)
            {
                armPoints.Add(PointWorld(i) + offset);
            }

            DrawArm(armPoints);
        }

        private void ShowRun(Vector3 start, Vector3 end)
        {
            if (armRunCount >= armRuns.Count)
            {
                var runObject = new GameObject("ArmRun");
                runObject.transform.SetParent(armLine.transform.parent, false);
                LineRenderer created = runObject.AddComponent<LineRenderer>();
                ConfigureRun(created);
                armRuns.Add(created);
            }

            LineRenderer run = armRuns[armRunCount++];
            run.enabled = true;
            run.positionCount = 2;
            run.SetPosition(0, start);
            run.SetPosition(1, end);
        }

        /// <summary>Puts the elbow picture on a bend; <paramref name="toPrevious"/> and <paramref name="toNext"/> are the two arm directions.</summary>
        private void ShowElbow(Vector3 corner, Vector3 toPrevious, Vector3 toNext)
        {
            if (elbowArmSprite == null)
            {
                return;
            }

            if (armElbowCount >= armElbows.Count)
            {
                var elbowObject = new GameObject("ArmElbow");
                elbowObject.transform.SetParent(transform, false);
                armElbows.Add(elbowObject.AddComponent<SpriteRenderer>());
            }

            SpriteRenderer elbow = armElbows[armElbowCount++];
            elbow.gameObject.SetActive(true);
            elbow.sprite = elbowArmSprite;
            elbow.sortingLayerID = armSortingLayerId;
            elbow.sortingOrder = armRunSortingOrder + 1;

            // The picture connects the left and the bottom edge; turn it by whole quarters so it connects the two arm directions.
            Vector3 diagonal = toPrevious + toNext;
            float angle = Mathf.Atan2(diagonal.y, diagonal.x) * Mathf.Rad2Deg + 135f;
            angle = Mathf.Round(angle / 90f) * 90f;

            float unitsWide = elbowArmSprite.rect.width / elbowArmSprite.pixelsPerUnit;
            float scale = coords.CellSize / Mathf.Max(0.0001f, unitsWide) / Mathf.Max(0.0001f, transform.lossyScale.x);
            elbow.transform.position = corner;
            elbow.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            elbow.transform.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>Index 0 is the root cell; index n is armPath[n - 1].</summary>
        private Vector3 PointWorld(int index)
        {
            return coords.GridToWorld(index == 0 ? rootPosition : armPath[index - 1]);
        }

        private void PlaceHand(Vector3 worldPosition)
        {
            float angle = FacingAngle();
            PlaceHand(worldPosition, angle, IsLeftAngle(angle));
        }

        private void PlaceHand(Vector3 worldPosition, float angle, bool mirrored)
        {
            if (handVisual == null)
            {
                return;
            }

            handVisual.position = worldPosition;
            handVisual.rotation = Quaternion.Euler(0f, 0f, angle);

            // A hand pointing left is mirrored instead of upside down, so the thumb stays on top.
            SpriteRenderer handRenderer = handVisual.GetComponentInChildren<SpriteRenderer>();
            if (handRenderer != null)
            {
                handRenderer.flipY = mirrored;
            }
        }

        private static bool IsLeftAngle(float angle)
        {
            return Mathf.Abs(Mathf.DeltaAngle(angle, 180f)) < 1f;
        }

        private static float AngleOfDirection(Vector2Int direction)
        {
            return Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        }

        /// <summary>The direction the hand pointed to before the step that just happened.</summary>
        private Vector2Int PreviousHandDirection(ViewChange change, Vector2Int previousHandCell)
        {
            if (change == ViewChange.Retracted)
            {
                // The hand was on previousHandCell, one step beyond the current hand.
                return previousHandCell - HandPosition;
            }

            // Extended: the hand was on the cell before the current one, reached by the step before the last.
            int count = armPath.Count;
            return count >= 3 ? armPath[count - 2] - armPath[count - 3] : restFacing.ToVector();
        }

        private float FacingAngle()
        {
            if (armPath.Count > 1 && DirectionExtensions.TryFromVector(HandPosition - CellBehindHand, out Direction facing))
            {
                return facing.ToAngleDegrees();
            }

            return restFacing.ToAngleDegrees();
        }

        private void UpdateLabel()
        {
            if (stepsLabel != null)
            {
                stepsLabel.text = string.Format(stepsLabelFormat, StepsRemaining, maxSteps);
            }
        }
    }
}
