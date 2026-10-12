using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// Floor switch. Kind SwitchMomentary (Switch_1) is active only while pressed;
    /// kind SwitchToggle (Switch_2) flips each time something steps onto it.
    /// <see cref="GridManager"/> drives <see cref="SetActive"/>; this component only shows the state.
    /// </summary>
    public sealed class SwitchEntity : GridEntity
    {
        [Header("Visual")]
        [SerializeField] private SpriteRenderer spriteRenderer;
        [Tooltip("Optional. When both sprites are set they are swapped; otherwise the renderer is tinted.")]
        [SerializeField] private Sprite inactiveSprite;
        [SerializeField] private Sprite activeSprite;
        [SerializeField] private Color inactiveColor = new Color(0.65f, 0.65f, 0.65f, 1f);
        [SerializeField] private Color activeColor = new Color(1f, 0.85f, 0.2f, 1f);

        public TileType Kind { get; private set; } = TileType.SwitchMomentary;
        public int LinkId { get; private set; }
        public bool IsActive { get; private set; }
        public bool IsMomentary => Kind == TileType.SwitchMomentary;
        public bool IsToggle => Kind == TileType.SwitchToggle;

        private void Reset()
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        private void Awake()
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }
        }

        public void Configure(GridSpace coords, ObstacleData data)
        {
            Initialize(coords, data.position);
            Kind = data.tileType == TileType.SwitchToggle ? TileType.SwitchToggle : TileType.SwitchMomentary;
            LinkId = data.linkId;
            SetActive(IsToggle && data.initiallyOn);
        }

        public void SetActive(bool active)
        {
            IsActive = active;
            ApplyVisual();
        }

        /// <summary>Flips the state and returns the new value.</summary>
        public bool Flip()
        {
            SetActive(!IsActive);
            return IsActive;
        }

        private void ApplyVisual()
        {
            if (spriteRenderer == null)
            {
                return;
            }

            if (inactiveSprite != null && activeSprite != null)
            {
                spriteRenderer.sprite = IsActive ? activeSprite : inactiveSprite;
                spriteRenderer.color = Color.white;
            }
            else
            {
                spriteRenderer.color = IsActive ? activeColor : inactiveColor;
            }
        }
    }
}
