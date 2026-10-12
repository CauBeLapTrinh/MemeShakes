using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// One cat and its snake-like arm. The arm is a list of grid cells (<see cref="ArmPath"/>) that starts next to
    /// the cat's root cell. The last cell is the hand. Extending appends one cell, retracting removes the last one.
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
        [Tooltip("Optional object enabled only while this cat is the controlled one.")]
        [SerializeField] private GameObject selectionIndicator;
        [Tooltip("Optional label showing the remaining steps.")]
        [SerializeField] private TMP_Text stepsLabel;
        [Tooltip("{0} = steps remaining, {1} = max steps.")]
        [SerializeField] private string stepsLabelFormat = "{0}";

        [Header("Animation")]
        [SerializeField, Min(0f)] private float moveDuration = 0.12f;

        private readonly List<Vector2Int> armPath = new List<Vector2Int>();
        private GridSpace coords;
        private Vector2Int rootPosition;
        private int maxSteps = 1;
        private Coroutine animationRoutine;

        /// <summary>Raised whenever the arm length (and therefore the remaining steps) changes.</summary>
        public event Action<CatArmController> StepsChanged;

        public CatId Id => catId;
        public Vector2Int RootPosition => rootPosition;
        public int MaxSteps => maxSteps;
        public IReadOnlyList<Vector2Int> ArmPath => armPath;
        public int StepsRemaining => maxSteps - armPath.Count;
        public bool CanExtend => StepsRemaining > 0;

        /// <summary>True once at least one arm segment exists, i.e. the hand has left the cat's body.</summary>
        public bool HasExtendedArm => armPath.Count > 0;
        public bool IsSelected { get; private set; }
        public bool IsAnimating => animationRoutine != null;

        /// <summary>The tip of the arm; equals the root cell while the arm is not extended.</summary>
        public Vector2Int HandPosition => armPath.Count > 0 ? armPath[armPath.Count - 1] : rootPosition;

        /// <summary>The cell directly behind the hand (the one the hand would return to when retracting).</summary>
        public Vector2Int CellBehindHand => armPath.Count > 1 ? armPath[armPath.Count - 2] : rootPosition;

        /// <summary>True when <paramref name="cell"/> is the cell the hand would retract into.</summary>
        public bool IsRetractTarget(Vector2Int cell)
        {
            return armPath.Count > 0 && cell == CellBehindHand;
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

        public void Initialize(GridSpace gridSpace, Vector2Int root, int steps)
        {
            StopAnimationRoutine();
            coords = gridSpace;
            rootPosition = root;
            maxSteps = Mathf.Max(1, steps);
            armPath.Clear();

            if (armLine != null)
            {
                armLine.useWorldSpace = true;
            }

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

        /// <summary>Removes the last arm cell. Fails if the arm is not extended.</summary>
        public bool RetractLast()
        {
            if (armPath.Count == 0)
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

        private void StopAnimationRoutine()
        {
            if (animationRoutine != null)
            {
                StopCoroutine(animationRoutine);
                animationRoutine = null;
            }
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

            if (armLine != null)
            {
                armLine.positionCount = totalPointCount;
                for (int i = 0; i < logicalPointCount; i++)
                {
                    armLine.SetPosition(i, PointWorld(i));
                }
            }

            float elapsed = 0f;
            while (elapsed < moveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / moveDuration));
                Vector3 tip = Vector3.Lerp(from, to, t);

                if (armLine != null)
                {
                    armLine.SetPosition(tipIndex, tip);
                }

                PlaceHand(tip);
                yield return null;
            }

            animationRoutine = null;
            SnapView();
        }

        private void SnapView()
        {
            if (coords == null)
            {
                return;
            }

            if (bodyVisual != null)
            {
                bodyVisual.position = coords.GridToWorld(rootPosition);
            }

            if (armLine != null)
            {
                int pointCount = armPath.Count + 1;
                armLine.positionCount = pointCount;
                for (int i = 0; i < pointCount; i++)
                {
                    armLine.SetPosition(i, PointWorld(i));
                }
            }

            PlaceHand(coords.GridToWorld(HandPosition));
            UpdateLabel();
        }

        /// <summary>Index 0 is the root cell; index n is armPath[n - 1].</summary>
        private Vector3 PointWorld(int index)
        {
            return coords.GridToWorld(index == 0 ? rootPosition : armPath[index - 1]);
        }

        private void PlaceHand(Vector3 worldPosition)
        {
            if (handVisual == null)
            {
                return;
            }

            handVisual.position = worldPosition;
            handVisual.rotation = Quaternion.Euler(0f, 0f, FacingAngle());
        }

        private float FacingAngle()
        {
            if (armPath.Count > 0 && DirectionExtensions.TryFromVector(HandPosition - CellBehindHand, out Direction facing))
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
