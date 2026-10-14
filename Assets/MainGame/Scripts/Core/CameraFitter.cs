using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// Sizes and positions an orthographic camera so the whole board is visible, centered in the area
    /// left free by the HUD. Works for any aspect ratio (9:16 portrait is the design target) and refits
    /// automatically when the screen size changes.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFitter : MonoBehaviour
    {
        [Header("Margins")]
        [Tooltip("Empty space kept around the board, measured in cells.")]
        [SerializeField, Min(0f)] private float boardPaddingCells = 0.3f;
        [Tooltip("Fraction of the screen height reserved at the top for the HUD (timer, level name).")]
        [SerializeField, Range(0f, 0.4f)] private float topUiFraction = 0.12f;
        [Tooltip("Fraction of the screen height reserved at the bottom for the HUD (undo, reset buttons).")]
        [SerializeField, Range(0f, 0.4f)] private float bottomUiFraction = 0.12f;
        [Tooltip("Fraction of the screen width kept free on the left and on the right (each side).")]
        [SerializeField, Range(0f, 0.3f)] private float sideUiFraction = 0.03f;
        [Tooltip("Small boards are not zoomed in beyond this many visible cells (vertically), so cells never get huge.")]
        [SerializeField, Min(1f)] private float minVisibleHeightCells = 3.5f;

        [Header("Camera")]
        [SerializeField] private float cameraZ = -10f;

        private Camera targetCamera;
        private GridSpace fittedSpace;
        private int lastScreenWidth;
        private int lastScreenHeight;

        private void Awake()
        {
            targetCamera = GetComponent<Camera>();
            targetCamera.orthographic = true;
        }

        /// <summary>Fits the camera to the given board and remembers it so later screen changes refit automatically.</summary>
        public void Fit(GridSpace space)
        {
            if (space == null)
            {
                return;
            }

            fittedSpace = space;
            ApplyFit();
        }

        private void Update()
        {
            if (fittedSpace == null)
            {
                return;
            }

            if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
            {
                ApplyFit();
            }
        }

        private void ApplyFit()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
            }

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;

            float aspect = lastScreenHeight > 0 ? (float)lastScreenWidth / lastScreenHeight : 9f / 16f;
            float padding = boardPaddingCells * fittedSpace.CellSize;
            float boardWidth = fittedSpace.Width * fittedSpace.CellSize + 2f * padding;
            float boardHeight = fittedSpace.Height * fittedSpace.CellSize + 2f * padding;

            float usableFraction = Mathf.Max(0.1f, 1f - topUiFraction - bottomUiFraction);
            float usableWidthFraction = Mathf.Max(0.1f, 1f - 2f * sideUiFraction);
            float visibleHeightForHeight = boardHeight / usableFraction;
            float visibleHeightForWidth = boardWidth / (aspect * usableWidthFraction);
            float visibleHeight = Mathf.Max(visibleHeightForHeight, visibleHeightForWidth);
            visibleHeight = Mathf.Max(visibleHeight, minVisibleHeightCells * fittedSpace.CellSize / usableFraction);

            targetCamera.orthographic = true;
            targetCamera.orthographicSize = visibleHeight * 0.5f;

            // Shift the camera so the board sits in the middle of the area between the top and bottom HUD bands.
            Vector3 center = fittedSpace.Center;
            float verticalShift = (bottomUiFraction - topUiFraction) * visibleHeight * 0.5f;
            targetCamera.transform.position = new Vector3(center.x, center.y - verticalShift, cameraZ);
        }
    }
}
