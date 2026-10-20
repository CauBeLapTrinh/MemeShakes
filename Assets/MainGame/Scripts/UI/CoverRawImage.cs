using UnityEngine;
using UnityEngine.UI;

namespace Catshakes
{
    /// <summary>
    /// Fills this rect with a still picture like CSS "background-size: cover": the picture keeps its aspect ratio and the
    /// part that does not fit is cropped (centered) by setting the <see cref="RawImage.uvRect"/>. Re-fits when the rect changes.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class CoverRawImage : MonoBehaviour
    {
        private RawImage image;
        private RectTransform rectTransform;

        private void OnEnable()
        {
            Fit();
        }

        private void OnRectTransformDimensionsChange()
        {
            Fit();
        }

        private void Fit()
        {
            if (image == null)
            {
                image = GetComponent<RawImage>();
                rectTransform = (RectTransform)transform;
            }

            Texture texture = image.texture;
            Rect rect = rectTransform.rect;
            if (texture == null || texture.width <= 0 || texture.height <= 0 || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            float textureAspect = (float)texture.width / texture.height;
            float rectAspect = rect.width / rect.height;

            Rect uv = new Rect(0f, 0f, 1f, 1f);
            if (rectAspect > textureAspect)
            {
                // Rect is wider: show the full width, crop top and bottom.
                uv.height = textureAspect / rectAspect;
                uv.y = (1f - uv.height) * 0.5f;
            }
            else
            {
                uv.width = rectAspect / textureAspect;
                uv.x = (1f - uv.width) * 0.5f;
            }

            image.uvRect = uv;
        }
    }
}
