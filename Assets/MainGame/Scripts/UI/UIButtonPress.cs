using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Catshakes
{
    /// <summary>
    /// Squishes a button a little while it is held down and springs it back on release, so pixel-art buttons
    /// (which have no pressed sprite) still give feedback. Add it next to the Button.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIButtonPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField, Range(0.7f, 1f)] private float pressedScale = 0.93f;
        [Tooltip("Scale change per second; 1.5 turns a 7% squish into about 50 ms.")]
        [SerializeField, Min(0.1f)] private float speed = 1.5f;

        private Selectable selectable;
        private Vector3 restScale;
        private float targetFactor = 1f;
        private float currentFactor = 1f;

        private void Awake()
        {
            selectable = GetComponent<Selectable>();
        }

        private void OnEnable()
        {
            restScale = transform.localScale;
            targetFactor = 1f;
            currentFactor = 1f;
        }

        private void OnDisable()
        {
            transform.localScale = restScale;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (selectable == null || selectable.IsInteractable())
            {
                targetFactor = pressedScale;
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            targetFactor = 1f;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            targetFactor = 1f;
        }

        private void Update()
        {
            if (Mathf.Approximately(currentFactor, targetFactor))
            {
                return;
            }

            currentFactor = Mathf.MoveTowards(currentFactor, targetFactor, speed * Time.unscaledDeltaTime);
            transform.localScale = restScale * currentFactor;
        }
    }
}
