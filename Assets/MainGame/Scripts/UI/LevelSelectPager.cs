using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Catshakes
{
    /// <summary>
    /// Horizontal swipe paging for the level grid. Lives on the viewport (the masked rect); it moves
    /// <see cref="pagesRoot"/> sideways and snaps to the nearest page when the finger is released.
    /// </summary>
    public sealed class LevelSelectPager : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const float SnapSpeed = 14f;
        private const float EdgeResistance = 0.35f;
        private const float FlickSpeed = 600f;
        private const float SwipeFraction = 0.15f;

        private RectTransform viewport;
        private RectTransform pagesRoot;
        private float pageStride;
        private int pageCount = 1;
        private int currentPage;
        private float targetX;
        private bool dragging;
        private float dragStartX;
        private float dragStartLocalX;
        private float lastLocalX;
        private float velocity;

        /// <summary>Raised when the visible page changes (by swipe or <see cref="GoToPage"/>).</summary>
        public event Action<int> PageChanged;

        public int CurrentPage => currentPage;
        public int PageCount => pageCount;

        /// <summary>Where the strip is right now in pages (0 = first page, 1.5 = halfway between the second and third). Follows swipes and the snap animation.</summary>
        public float PagePosition => pagesRoot != null ? -pagesRoot.anchoredPosition.x / pageStride : currentPage;

        public void Setup(RectTransform pagesContainer, float stride, int pages)
        {
            viewport = (RectTransform)transform;
            pagesRoot = pagesContainer;
            pageStride = Mathf.Max(1f, stride);
            pageCount = Mathf.Max(1, pages);
            currentPage = Mathf.Clamp(currentPage, 0, pageCount - 1);
            targetX = PageX(currentPage);
            SetX(targetX);
        }

        public void GoToPage(int page, bool instant)
        {
            if (pagesRoot == null)
            {
                return;
            }

            int clamped = Mathf.Clamp(page, 0, pageCount - 1);
            bool changed = clamped != currentPage;
            currentPage = clamped;
            targetX = PageX(currentPage);

            if (instant)
            {
                SetX(targetX);
            }

            // A jump (opening the panel) is not a swipe, so it raises no event and plays no sound.
            if (changed && !instant)
            {
                PageChanged?.Invoke(currentPage);
            }
        }

        private void Update()
        {
            if (pagesRoot == null || dragging)
            {
                return;
            }

            float x = pagesRoot.anchoredPosition.x;
            if (Mathf.Abs(x - targetX) < 0.5f)
            {
                if (x != targetX)
                {
                    SetX(targetX);
                }

                return;
            }

            SetX(Mathf.Lerp(x, targetX, 1f - Mathf.Exp(-SnapSpeed * Time.unscaledDeltaTime)));
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (pagesRoot == null || pageCount <= 1)
            {
                return;
            }

            dragging = true;
            dragStartX = pagesRoot.anchoredPosition.x;
            dragStartLocalX = ToLocalX(eventData);
            lastLocalX = dragStartLocalX;
            velocity = 0f;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging)
            {
                return;
            }

            float localX = ToLocalX(eventData);
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            velocity = Mathf.Lerp(velocity, (localX - lastLocalX) / dt, 0.5f);
            lastLocalX = localX;

            float x = dragStartX + (localX - dragStartLocalX);
            float min = PageX(pageCount - 1);
            float max = PageX(0);
            if (x > max)
            {
                x = max + (x - max) * EdgeResistance;
            }
            else if (x < min)
            {
                x = min + (x - min) * EdgeResistance;
            }

            SetX(x);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!dragging)
            {
                return;
            }

            dragging = false;

            float moved = pagesRoot.anchoredPosition.x - PageX(currentPage);
            int page = currentPage;

            if (velocity < -FlickSpeed || moved < -pageStride * SwipeFraction)
            {
                page++;
            }
            else if (velocity > FlickSpeed || moved > pageStride * SwipeFraction)
            {
                page--;
            }

            GoToPage(page, false);
        }

        private float PageX(int page)
        {
            return -page * pageStride;
        }

        private void SetX(float x)
        {
            Vector2 position = pagesRoot.anchoredPosition;
            position.x = x;
            pagesRoot.anchoredPosition = position;
        }

        private float ToLocalX(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, eventData.position, eventData.pressEventCamera, out Vector2 local);
            return local.x;
        }
    }
}
