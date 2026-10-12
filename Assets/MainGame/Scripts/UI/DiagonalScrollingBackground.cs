using UnityEngine;
using UnityEngine.UI;

namespace Catshakes
{
    /// <summary>
    /// Scrolls a tiling texture endlessly along a diagonal by moving the <see cref="RawImage.uvRect"/>.
    /// The texture keeps its aspect ratio on any screen: <see cref="tilesAcrossWidth"/> says how many copies fit
    /// across the width of this rect, the number of rows follows from the rect height.
    ///
    /// The texture needs Wrap Mode = Repeat (the menu setup tool sets this on the background textures).
    /// Runs on unscaled time so it keeps moving while the game is paused.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    [DisallowMultipleComponent]
    public sealed class DiagonalScrollingBackground : MonoBehaviour
    {
        [Tooltip("Direction the picture travels on screen. (-1,-1) = toward the bottom-left corner. Normalized at runtime.")]
        [SerializeField] private Vector2 direction = new Vector2(-1f, -1f);

        [Tooltip("Scroll speed in canvas units per second.")]
        [SerializeField, Min(0f)] private float speed = 45f;

        [Tooltip("How many copies of the texture fit across the width of this rect.")]
        [SerializeField, Min(0.1f)] private float tilesAcrossWidth = 1f;

        private RawImage image;
        private RectTransform rectTransform;
        private Vector2 offset;

        private void Awake()
        {
            image = GetComponent<RawImage>();
            rectTransform = (RectTransform)transform;

            if (image.texture != null)
            {
                // Safety net: without Repeat the picture would smear at the edges instead of tiling.
                image.texture.wrapMode = TextureWrapMode.Repeat;
            }
        }

        private void LateUpdate()
        {
            Texture texture = image.texture;
            if (texture == null || texture.width <= 0 || texture.height <= 0)
            {
                return;
            }

            Rect rect = rectTransform.rect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            float tileWidth = rect.width / tilesAcrossWidth;
            float tileHeight = tileWidth * texture.height / texture.width;

            Vector2 travel = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.zero;
            float distance = speed * Time.unscaledDeltaTime;

            // The picture moves along 'travel', so the texture coordinate under a fixed screen point moves the opposite way.
            offset.x = Mathf.Repeat(offset.x - travel.x * distance / tileWidth, 1f);
            offset.y = Mathf.Repeat(offset.y - travel.y * distance / tileHeight, 1f);

            image.uvRect = new Rect(offset.x, offset.y, rect.width / tileWidth, rect.height / tileHeight);
        }
    }
}
