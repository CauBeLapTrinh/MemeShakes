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
        private Coroutine appearRoutine;
        private Coroutine crushRoutine;
        private Vector3 restScale = Vector3.one;
        private bool restScaleCaptured;

        /// <summary>True once a closing door has crushed this crate. A crushed crate is hidden and not part of the board.</summary>
        public bool IsCrushed { get; private set; }

        private void Awake()
        {
            CaptureRestScale();
        }

        private void CaptureRestScale()
        {
            if (!restScaleCaptured)
            {
                restScaleCaptured = true;
                restScale = transform.localScale;
            }
        }

        /// <summary>Squashes the crate flat and hides it (a door closed on it). A zero duration hides it at once.</summary>
        public void Crush(float duration)
        {
            if (IsCrushed)
            {
                return;
            }

            CaptureRestScale();
            IsCrushed = true;
            StopAllAnimations();

            if (duration <= 0f || !isActiveAndEnabled)
            {
                gameObject.SetActive(false);
                return;
            }

            crushRoutine = StartCoroutine(CrushRoutine(duration));
        }

        /// <summary>Brings a crushed crate back (Undo / Reset).</summary>
        public void Restore()
        {
            if (!IsCrushed)
            {
                return;
            }

            IsCrushed = false;
            StopAllAnimations();
            transform.localScale = restScale;
            gameObject.SetActive(true);
        }

        private void StopAllAnimations()
        {
            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
                moveRoutine = null;
            }

            if (appearRoutine != null)
            {
                StopCoroutine(appearRoutine);
                appearRoutine = null;
            }

            if (crushRoutine != null)
            {
                StopCoroutine(crushRoutine);
                crushRoutine = null;
            }
        }

        private IEnumerator CrushRoutine(float duration)
        {
            Vector3 full = restScale;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                transform.localScale = new Vector3(full.x * (1f + 0.3f * t), full.y * (1f - t), full.z);
                yield return null;
            }

            crushRoutine = null;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Replaces the look of this crate (used for severed hands, which behave exactly like a crate).
        /// A null sprite keeps the current one.
        /// </summary>
        public void SetVisual(Sprite sprite, float angleDegrees, Vector3 spriteScale)
        {
            transform.rotation = Quaternion.Euler(0f, 0f, angleDegrees);

            SpriteRenderer spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            if (spriteRenderer == null || sprite == null)
            {
                return;
            }

            spriteRenderer.sprite = sprite;
            spriteRenderer.transform.localScale = spriteScale;
        }

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

        /// <summary>Grows the crate from nothing with a small overshoot (used when a severed hand drops).</summary>
        public void PlayAppear(float duration)
        {
            if (duration <= 0f || !isActiveAndEnabled)
            {
                return;
            }

            if (appearRoutine != null)
            {
                StopCoroutine(appearRoutine);
            }

            appearRoutine = StartCoroutine(AppearRoutine(duration));
        }

        private IEnumerator AppearRoutine(float duration)
        {
            Vector3 fullScale = transform.localScale;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float overshoot = 1.70158f;
                float u = t - 1f;
                float eased = 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
                transform.localScale = fullScale * eased;
                yield return null;
            }

            transform.localScale = fullScale;
            appearRoutine = null;
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
