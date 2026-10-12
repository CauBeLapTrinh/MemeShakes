using System.Collections;
using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// A pushable wooden barrel. <see cref="GridManager"/> decides whether a push is legal;
    /// this component stores the cell and slides the sprite to it.
    /// </summary>
    public sealed class Barrel : GridEntity
    {
        [SerializeField, Min(0f)] private float moveDuration = 0.12f;

        private Coroutine moveRoutine;

        /// <summary>Moves the barrel to <paramref name="cell"/> (logical position changes immediately).</summary>
        public void SetGridPosition(Vector2Int cell, bool animate)
        {
            GridPosition = cell;
            Vector3 target = Coords.GridToWorld(cell);

            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
                moveRoutine = null;
            }

            if (!animate || moveDuration <= 0f || !isActiveAndEnabled)
            {
                transform.position = target;
                return;
            }

            moveRoutine = StartCoroutine(MoveRoutine(target));
        }

        private IEnumerator MoveRoutine(Vector3 target)
        {
            Vector3 start = transform.position;
            float elapsed = 0f;

            while (elapsed < moveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / moveDuration));
                transform.position = Vector3.Lerp(start, target, t);
                yield return null;
            }

            transform.position = target;
            moveRoutine = null;
        }
    }
}
