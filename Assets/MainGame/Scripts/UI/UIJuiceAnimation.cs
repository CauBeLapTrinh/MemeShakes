using UnityEngine;

namespace Catshakes
{
    public enum JuiceMode
    {
        /// <summary>Floats up and down, breathes (scale) and tilts, all with sine waves. For the logo and the mascot.</summary>
        Idle = 0,

        /// <summary>Only pulses scale and, optionally, alpha. For "Tap to Start Game".</summary>
        Pulse = 1
    }

    /// <summary>
    /// Cheap, code-only life for static UI: no Animator or clips. Everything is relative to the values the
    /// RectTransform had when the object was enabled, and is put back when it is disabled.
    /// Uses unscaled time so it keeps playing while the game is paused.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class UIJuiceAnimation : MonoBehaviour
    {
        [SerializeField] private JuiceMode mode = JuiceMode.Idle;

        [Tooltip("Shifts the waves in time so several objects do not move in lockstep.")]
        [SerializeField] private float phaseOffset;

        [Header("Idle: float")]
        [Tooltip("Vertical travel in canvas units, each way.")]
        [SerializeField] private float floatAmplitude = 14f;
        [Tooltip("Cycles per second.")]
        [SerializeField] private float floatFrequency = 0.35f;

        [Header("Idle: breathing")]
        [SerializeField] private float breatheMinScale = 0.98f;
        [SerializeField] private float breatheMaxScale = 1.04f;
        [SerializeField] private float breatheFrequency = 0.3f;

        [Header("Idle: tilt")]
        [Tooltip("Rotation around Z in degrees, each way.")]
        [SerializeField] private float tiltAngle = 3f;
        [SerializeField] private float tiltFrequency = 0.22f;

        [Header("Pulse")]
        [SerializeField] private float pulseMinScale = 0.94f;
        [SerializeField] private float pulseMaxScale = 1.06f;
        [SerializeField] private float pulseFrequency = 0.8f;
        [SerializeField] private bool pulseAlpha = true;
        [SerializeField, Range(0f, 1f)] private float pulseMinAlpha = 0.35f;

        private RectTransform rectTransform;
        private CanvasGroup canvasGroup;
        private Vector2 baseAnchoredPosition;
        private Vector3 baseScale;
        private Quaternion baseRotation;
        private float baseAlpha;
        private bool hasBase;

        private void Awake()
        {
            rectTransform = (RectTransform)transform;
        }

        private void OnEnable()
        {
            if (rectTransform == null)
            {
                rectTransform = (RectTransform)transform;
            }

            baseAnchoredPosition = rectTransform.anchoredPosition;
            baseScale = rectTransform.localScale;
            baseRotation = rectTransform.localRotation;

            if (mode == JuiceMode.Pulse && pulseAlpha)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }

                baseAlpha = canvasGroup.alpha;
            }

            hasBase = true;
        }

        private void OnDisable()
        {
            if (!hasBase)
            {
                return;
            }

            rectTransform.anchoredPosition = baseAnchoredPosition;
            rectTransform.localScale = baseScale;
            rectTransform.localRotation = baseRotation;

            if (canvasGroup != null)
            {
                canvasGroup.alpha = baseAlpha;
            }

            hasBase = false;
        }

        private void Update()
        {
            float time = Time.unscaledTime + phaseOffset;

            if (mode == JuiceMode.Idle)
            {
                float lift = Mathf.Sin(time * floatFrequency * Mathf.PI * 2f) * floatAmplitude;
                float breathe01 = Mathf.Sin(time * breatheFrequency * Mathf.PI * 2f) * 0.5f + 0.5f;
                float tilt = Mathf.Sin(time * tiltFrequency * Mathf.PI * 2f) * tiltAngle;

                rectTransform.anchoredPosition = baseAnchoredPosition + new Vector2(0f, lift);
                rectTransform.localScale = baseScale * Mathf.Lerp(breatheMinScale, breatheMaxScale, breathe01);
                rectTransform.localRotation = baseRotation * Quaternion.Euler(0f, 0f, tilt);
            }
            else
            {
                float pulse01 = Mathf.Sin(time * pulseFrequency * Mathf.PI * 2f) * 0.5f + 0.5f;
                rectTransform.localScale = baseScale * Mathf.Lerp(pulseMinScale, pulseMaxScale, pulse01);

                if (canvasGroup != null)
                {
                    canvasGroup.alpha = baseAlpha * Mathf.Lerp(pulseMinAlpha, 1f, pulse01);
                }
            }
        }
    }
}
