using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// Door linked to switches by <see cref="LinkId"/>. <see cref="GridManager"/> decides when it is
    /// open (a linked switch is active, or something is standing in the doorway) and calls <see cref="SetOpen"/>.
    /// </summary>
    public sealed class DoorEntity : GridEntity
    {
        [Header("Visual")]
        [SerializeField] private SpriteRenderer spriteRenderer;
        [Tooltip("Optional. When both sprites are set they are swapped; otherwise the renderer is tinted.")]
        [SerializeField] private Sprite closedSprite;
        [SerializeField] private Sprite openSprite;
        [SerializeField] private Color closedColor = new Color(0.55f, 0.32f, 0.18f, 1f);
        [SerializeField] private Color openColor = new Color(0.55f, 0.32f, 0.18f, 0.25f);

        [Tooltip("Optional child object shown only while the door is closed (for example the bars of a gate). When set, sprites and tints above are ignored.")]
        [SerializeField] private GameObject closedVisualRoot;

        public int LinkId { get; private set; }
        public bool IsOpen { get; private set; }

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
            LinkId = data.linkId;
            SetOpen(false);
        }

        public void SetOpen(bool open)
        {
            IsOpen = open;
            ApplyVisual();
        }

        /// <summary>Rotates the door 90 degrees so it spans a corridor that runs up and down.</summary>
        public void SetVertical(bool vertical)
        {
            transform.rotation = Quaternion.Euler(0f, 0f, vertical ? 90f : 0f);
        }

        private void ApplyVisual()
        {
            if (closedVisualRoot != null)
            {
                closedVisualRoot.SetActive(!IsOpen);
                return;
            }

            if (spriteRenderer == null)
            {
                return;
            }

            if (closedSprite != null && openSprite != null)
            {
                spriteRenderer.sprite = IsOpen ? openSprite : closedSprite;
                spriteRenderer.color = Color.white;
            }
            else
            {
                spriteRenderer.color = IsOpen ? openColor : closedColor;
            }
        }
    }
}
