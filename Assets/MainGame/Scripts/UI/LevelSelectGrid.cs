using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Catshakes
{
    /// <summary>
    /// The level buttons (0..SlotCount-1), split into swipeable pages. A slot is unlocked when the player has
    /// reached it and a level asset exists for it in <see cref="GameSceneManager"/>; every other slot is shown locked.
    /// Exactly one unlocked slot is selected at a time; <see cref="SelectedLevelIndex"/> holds it.
    /// <para>
    /// <see cref="contentRoot"/> is the viewport. Its Grid Layout Group only describes how one page looks (columns,
    /// cell size, spacing); at runtime it is replaced by one page per screenful of buttons. The row count per page
    /// comes from the viewport height. <see cref="slotCount"/> is the minimum number of slots, so the grid grows
    /// automatically when the game has more levels than that.
    /// </para>
    /// </summary>
    public sealed class LevelSelectGrid : MonoBehaviour
    {
        private static readonly Color DotActiveColor = new Color32(139, 111, 71, 255);
        private static readonly Color DotIdleColor = new Color32(139, 111, 71, 90);

        [Tooltip("The viewport: parent of the pages. Give it a Grid Layout Group describing one page.")]
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private LevelButtonView levelButtonPrefab;
        [Tooltip("Minimum number of slots. More levels than this add pages automatically.")]
        [SerializeField, Min(1)] private int slotCount = 30;

        [Header("Paging")]
        [Tooltip("Empty space between two pages, so the neighbouring page never peeks in at the edge.")]
        [SerializeField, Min(0f)] private float pageGap = 160f;
        [Tooltip("How far past the viewport a button may draw (the selected button is larger than its cell).")]
        [SerializeField, Min(0f)] private float clipPadding = 50f;
        [SerializeField] private float dotSize = 22f;
        [SerializeField] private float dotSpacing = 18f;
        [Tooltip("Scale of the dot for the page being shown. Dots in between blend smoothly while swiping.")]
        [SerializeField, Min(1f)] private float dotActiveScale = 1.5f;
        [Tooltip("Distance from the bottom of the viewport to the page dots.")]
        [SerializeField] private float dotsOffsetY = 45f;

        private readonly List<LevelButtonView> views = new List<LevelButtonView>();
        private readonly List<Image> dots = new List<Image>();
        private int selectedIndex = -1;
        private int builtSlots;
        private int slotsPerPage = 1;
        private LevelSelectPager pager;
        private RectTransform dotsRoot;

        private static Sprite dotSprite;

        /// <summary>Raised when the player picks another level.</summary>
        public event Action<int> SelectionChanged;

        public int SelectedLevelIndex => selectedIndex;
        public int SlotCount => builtSlots > 0 ? builtSlots : slotCount;
        public int PageCount => pager != null ? pager.PageCount : 1;

        private void Awake()
        {
            EnsureBuilt();
        }

        /// <summary>Re-reads the saved progress and redraws every button. Call when the panel is shown.</summary>
        public void Refresh()
        {
            EnsureBuilt();

            if (!IsUnlocked(selectedIndex))
            {
                selectedIndex = LastUnlockedIndex();
            }

            ApplyStates();

            if (pager != null)
            {
                // Open on the page that holds the selected level.
                pager.GoToPage(Mathf.Max(0, selectedIndex) / slotsPerPage, true);
                RefreshDots();
            }
        }

        public bool TryGetSelectedLevel(out int levelIndex)
        {
            levelIndex = selectedIndex;
            return IsUnlocked(selectedIndex);
        }

        private int RequiredSlots()
        {
            GameSceneManager manager = GameSceneManager.Instance;
            int levels = manager != null ? manager.LevelCount : 0;
            return Mathf.Max(slotCount, levels);
        }

        private void EnsureBuilt()
        {
            int required = RequiredSlots();
            if (views.Count > 0 && required == builtSlots)
            {
                return;
            }

            if (contentRoot == null || levelButtonPrefab == null)
            {
                Debug.LogError("LevelSelectGrid needs contentRoot and levelButtonPrefab assigned.", this);
                return;
            }

            Rebuild(required);
        }

        private void Rebuild(int slots)
        {
            GridLayoutGroup template = contentRoot.GetComponent<GridLayoutGroup>();
            Vector2 cell = template != null ? template.cellSize : new Vector2(138f, 128f);
            Vector2 spacing = template != null ? template.spacing : new Vector2(24f, 60f);
            RectOffset padding = template != null ? new RectOffset(template.padding.left, template.padding.right, template.padding.top, template.padding.bottom) : new RectOffset();
            TextAnchor alignment = template != null ? template.childAlignment : TextAnchor.UpperCenter;

            if (template != null)
            {
                // The viewport no longer lays out buttons itself; each page does.
                template.enabled = false;
                Destroy(template);
            }

            Rect area = contentRoot.rect;
            int columns = template != null && template.constraint == GridLayoutGroup.Constraint.FixedColumnCount
                ? Mathf.Max(1, template.constraintCount)
                : Mathf.Max(1, Mathf.FloorToInt((area.width + spacing.x) / (cell.x + spacing.x)));
            int rows = Mathf.Max(1, Mathf.FloorToInt((area.height + spacing.y) / (cell.y + spacing.y)));
            slotsPerPage = columns * rows;

            // Drop anything left over from editing so the grid never shows duplicates.
            for (int i = contentRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(contentRoot.GetChild(i).gameObject);
            }

            views.Clear();
            builtSlots = slots;

            int pageCount = Mathf.Max(1, Mathf.CeilToInt(slots / (float)slotsPerPage));
            float stride = area.width + pageGap;

            // Clip to the viewport, and catch swipes that start between two buttons.
            RectMask2D mask = contentRoot.GetComponent<RectMask2D>();
            if (mask == null)
            {
                mask = contentRoot.gameObject.AddComponent<RectMask2D>();
            }

            mask.padding = new Vector4(-clipPadding, -clipPadding, -clipPadding, -clipPadding);

            if (contentRoot.GetComponent<Graphic>() == null)
            {
                Image catcher = contentRoot.gameObject.AddComponent<Image>();
                catcher.color = new Color(1f, 1f, 1f, 0f);
                catcher.raycastTarget = true;
            }

            RectTransform pagesRoot = NewStretchedRect("Pages", contentRoot);

            for (int p = 0; p < pageCount; p++)
            {
                RectTransform page = NewStretchedRect("Page_" + p, pagesRoot);
                page.anchoredPosition = new Vector2(p * stride, 0f);

                GridLayoutGroup layout = page.gameObject.AddComponent<GridLayoutGroup>();
                layout.cellSize = cell;
                layout.spacing = spacing;
                layout.padding = padding;
                layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
                layout.startAxis = GridLayoutGroup.Axis.Horizontal;
                layout.childAlignment = alignment;
                layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                layout.constraintCount = columns;

                int first = p * slotsPerPage;
                int last = Mathf.Min(slots, first + slotsPerPage);
                for (int i = first; i < last; i++)
                {
                    LevelButtonView view = Instantiate(levelButtonPrefab, page);
                    view.name = "Level_" + i;
                    view.Bind(i, HandleLevelClicked);
                    views.Add(view);
                }
            }

            pager = contentRoot.GetComponent<LevelSelectPager>();
            if (pager == null)
            {
                pager = contentRoot.gameObject.AddComponent<LevelSelectPager>();
                pager.PageChanged += HandlePageChanged;
            }

            pager.Setup(pagesRoot, stride, pageCount);
            BuildDots(pageCount);
        }

        private static RectTransform NewStretchedRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private void BuildDots(int pageCount)
        {
            if (dotsRoot != null)
            {
                Destroy(dotsRoot.gameObject);
            }

            dots.Clear();

            if (contentRoot.parent == null)
            {
                return;
            }

            // Sibling of the viewport, so the viewport's mask does not clip the dots.
            dotsRoot = NewStretchedRect("PageDots", contentRoot.parent);
            dotsRoot.anchorMin = contentRoot.anchorMin;
            dotsRoot.anchorMax = contentRoot.anchorMax;
            dotsRoot.pivot = contentRoot.pivot;
            dotsRoot.offsetMin = contentRoot.offsetMin;
            dotsRoot.offsetMax = contentRoot.offsetMax;
            dotsRoot.SetSiblingIndex(contentRoot.GetSiblingIndex() + 1);

            if (pageCount <= 1)
            {
                dotsRoot.gameObject.SetActive(false);
                return;
            }

            float total = pageCount * dotSize + (pageCount - 1) * dotSpacing;
            float y = -contentRoot.rect.height * contentRoot.pivot.y - dotsOffsetY;

            for (int i = 0; i < pageCount; i++)
            {
                var go = new GameObject("Dot_" + i, typeof(RectTransform), typeof(Image));
                var rect = (RectTransform)go.transform;
                rect.SetParent(dotsRoot, false);
                rect.anchorMin = new Vector2(0.5f, contentRoot.pivot.y);
                rect.anchorMax = rect.anchorMin;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(dotSize, dotSize);
                rect.anchoredPosition = new Vector2(-total * 0.5f + dotSize * 0.5f + i * (dotSize + dotSpacing), y);

                Image image = go.GetComponent<Image>();
                image.sprite = GetDotSprite();
                image.raycastTarget = false;
                dots.Add(image);
            }

            RefreshDots();
        }

        private void HandlePageChanged(int page)
        {
            RefreshDots();
            SoundManager.Play(SoundId.Whoosh);
        }

        private void LateUpdate()
        {
            if (dots.Count > 1 && pager != null)
            {
                RefreshDots();
            }
        }

        /// <summary>Each dot grows and darkens as the strip approaches its page, so the highlight glides along with the swipe.</summary>
        private void RefreshDots()
        {
            float position = pager != null ? pager.PagePosition : 0f;
            for (int i = 0; i < dots.Count; i++)
            {
                float weight = 1f - Mathf.Clamp01(Mathf.Abs(position - i));
                weight = weight * weight * (3f - 2f * weight);
                dots[i].color = Color.Lerp(DotIdleColor, DotActiveColor, weight);
                dots[i].rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, dotActiveScale, weight);
            }
        }

        private static Sprite GetDotSprite()
        {
            if (dotSprite != null)
            {
                return dotSprite;
            }

            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PageDot",
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            float radius = size * 0.5f - 1f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f));
                    byte alpha = (byte)(Mathf.Clamp01(radius - distance + 0.5f) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            dotSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            dotSprite.hideFlags = HideFlags.HideAndDontSave;
            return dotSprite;
        }

        private void HandleLevelClicked(int levelIndex)
        {
            if (!IsUnlocked(levelIndex) || levelIndex == selectedIndex)
            {
                return;
            }

            selectedIndex = levelIndex;
            ApplyStates();

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayClick();
            }

            SelectionChanged?.Invoke(selectedIndex);
        }

        private void ApplyStates()
        {
            for (int i = 0; i < views.Count; i++)
            {
                LevelButtonState state;
                if (!IsUnlocked(i))
                {
                    state = LevelButtonState.Locked;
                }
                else
                {
                    state = i == selectedIndex ? LevelButtonState.Selected : LevelButtonState.Unlocked;
                }

                views[i].SetState(state);
            }
        }

        private int PlayableSlots()
        {
            GameSceneManager manager = GameSceneManager.Instance;
            if (manager == null)
            {
                return 0;
            }

            return Mathf.Min(SlotCount, Mathf.Min(manager.UnlockedLevelCount, manager.LevelCount));
        }

        private bool IsUnlocked(int levelIndex)
        {
            return levelIndex >= 0 && levelIndex < PlayableSlots();
        }

        private int LastUnlockedIndex()
        {
            return PlayableSlots() - 1;
        }
    }
}
