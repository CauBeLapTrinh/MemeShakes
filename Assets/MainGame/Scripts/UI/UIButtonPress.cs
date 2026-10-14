using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Catshakes
{
    /// <summary>
    /// Squishes a button a little while it is held down and springs it back on release, so pixel-art buttons
    /// (which have no pressed sprite) still give feedback, and plays the click sound. Add it next to the Button.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIButtonPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [SerializeField, Range(0.7f, 1f)] private float pressedScale = 0.93f;
        [Tooltip("Scale change per second; 1.5 turns a 7% squish into about 50 ms.")]
        [SerializeField, Min(0.1f)] private float speed = 1.5f;
        [Tooltip("Plays the shared button click sound (SoundManager) when the button is clicked.")]
        [SerializeField] private bool playClickSound = true;

        [Header("Hover (mouse only; off on phones and for touches)")]
        [Tooltip("Scale while the mouse is over the button.")]
        [SerializeField, Range(1f, 1.3f)] private float hoverScale = 1.07f;
        [SerializeField] private bool playHoverSound = true;

        private Selectable selectable;
        private Vector3 restScale;
        private float targetFactor = 1f;
        private float currentFactor = 1f;
        private bool isHovered;
        private bool isPressed;

        private void Awake()
        {
            selectable = GetComponent<Selectable>();
        }

        private void OnEnable()
        {
            restScale = transform.localScale;
            targetFactor = 1f;
            currentFactor = 1f;
            isHovered = false;
            isPressed = false;
        }

        private void OnDisable()
        {
            transform.localScale = restScale;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (IsInteractable)
            {
                isPressed = true;
                UpdateTarget();
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            isPressed = false;
            UpdateTarget();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!IsMouse(eventData) || !IsInteractable)
            {
                return;
            }

            isHovered = true;
            UpdateTarget();
            if (playHoverSound)
            {
                SoundManager.Play(SoundId.Hover);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isHovered = false;
            isPressed = false;
            UpdateTarget();
        }

        private bool IsInteractable => selectable == null || selectable.IsInteractable();

        private void UpdateTarget()
        {
            targetFactor = isPressed ? pressedScale : isHovered && IsInteractable ? hoverScale : 1f;
        }

        /// <summary>Hover only makes sense for a real mouse: never on phones, never for touches on a touch screen.</summary>
        private static bool IsMouse(PointerEventData eventData)
        {
            if (Application.isMobilePlatform)
            {
                return false;
            }

#if ENABLE_INPUT_SYSTEM
            if (eventData is ExtendedPointerEventData extended)
            {
                return extended.pointerType == UIPointerType.MouseOrPen;
            }
#endif
            return eventData.pointerId < 0;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (playClickSound && (selectable == null || selectable.IsInteractable()))
            {
                SoundManager.Play(SoundId.Button);
            }
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
