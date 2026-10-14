using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Catshakes
{
    /// <summary>
    /// Speedrun leaderboard, shown when the player picks Speed Run; the board slides down from the top. The podium cards
    /// show ranks 1-3 (icon, numbered medal, name, time), the list below shows ranks 4-6. When the player is not in the
    /// top six (or has no run yet), the list shows ranks 4-5, then "...", then the player's own highlighted row.
    /// There is no server: the other players come from <see cref="FakeLeaderboard"/> (made up, but the same on every
    /// device and moving on by itself day by day) and the player's entry is the saved best Speedrun time. Times read as
    /// hours:minutes. "Let's Shake!" starts the Speedrun.
    /// </summary>
    public sealed class LeaderboardOverlay : MonoBehaviour
    {
        [Serializable]
        private sealed class PodiumSlot
        {
            public Image icon;
            public Image medal;
            public TMP_Text name;
            public TMP_Text time;
        }

        [Serializable]
        private sealed class Row
        {
            public GameObject root;
            public Image frame;
            public TMP_Text rank;
            public Image icon;
            public TMP_Text name;
            [Tooltip("Second line under the name (\"No runs yet\").")]
            public TMP_Text subtitle;
            public TMP_Text time;
        }

        private sealed class Entry
        {
            public string Name;
            public Sprite Icon;
            public float Seconds;
            public bool IsPlayer;
            public int Rank;
        }

        private const int PodiumCount = 3;
        private const string SeenRankKey = "Catshakes.Leaderboard.SeenRank";
        private const string SeenBestKey = "Catshakes.Leaderboard.SeenBest";

        [SerializeField] private SkinCatalog catalog;

        [Header("Panel")]
        [SerializeField] private CanvasGroup group;
        [Tooltip("Everything that slides down from the top (board, buttons).")]
        [SerializeField] private RectTransform slidingPanel;
        [SerializeField] private Button backButton;
        [SerializeField] private Button startButton;

        [Header("Board")]
        [Tooltip("1st, 2nd, 3rd.")]
        [SerializeField] private PodiumSlot[] podium = new PodiumSlot[PodiumCount];
        [Tooltip("Ranks 4, 5, 6 (the last one becomes the player's row when the player is further down).")]
        [SerializeField] private Row[] rows = new Row[3];
        [Tooltip("The \"...\" under the list, or between the list and the player's own row.")]
        [SerializeField] private GameObject ellipsis;
        [SerializeField] private Sprite otherRowSprite;
        [SerializeField] private Sprite myRowSprite;

        [Header("Data")]
        [SerializeField] private string playerName = "Me";
        [SerializeField] private string noRunText = "No runs yet";
        [Tooltip("{0} = old rank, {1} = new rank.")]
        [SerializeField] private string rankUpFormat = "RANK UP! #{0} > #{1}";
        [SerializeField] private string newRankFormat = "NEW RANK! #{0}";
        [Tooltip("Mr. Da's face; he plays without a skin.")]
        [SerializeField] private Sprite mrDaIcon;

        [Header("Timing")]
        [SerializeField, Min(0f)] private float slideDuration = 0.4f;

        private bool isAnimating;
        private RectTransform playerTarget;
        private int shownPlayerRank;
        private float shownPlayerBest;

        public bool IsOpen { get; private set; }
        public bool IsBusy => IsOpen || isAnimating;

        private void Awake()
        {
            if (backButton != null)
            {
                backButton.onClick.AddListener(OnBackClicked);
            }

            if (startButton != null)
            {
                startButton.onClick.AddListener(OnStartClicked);
            }

            if (group != null)
            {
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
                group.gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (IsOpen && !isAnimating && WasBackKeyPressed())
            {
                OnBackClicked();
            }
        }

        public void Open()
        {
            if (IsOpen || isAnimating || group == null)
            {
                return;
            }

            Refresh();
            SoundManager.Play(SoundId.Whoosh);
            StartCoroutine(SlideRoutine(true));
        }

        public void Close()
        {
            if (IsOpen && !isAnimating)
            {
                SoundManager.Play(SoundId.Whoosh);
                StartCoroutine(SlideRoutine(false));
            }
        }

        private void OnBackClicked()
        {
            if (IsOpen && !isAnimating)
            {
                Click();
                Close();
            }
        }

        private void OnStartClicked()
        {
            if (!IsOpen || isAnimating)
            {
                return;
            }

            Click();
            GameSceneManager manager = GameSceneManager.Instance;
            if (manager == null)
            {
                Debug.LogError("LeaderboardOverlay: add a GameSceneManager to the scene to start the Speedrun.", this);
                return;
            }

            manager.StartSpeedrun();
        }

        // ---------------------------------------------------------------- content

        /// <summary>Rebuilds the podium and the rows from the rivals and the player's best time.</summary>
        public void Refresh()
        {
            List<Entry> ranking = BuildRanking(out Entry player);

            playerTarget = null;
            for (int i = 0; i < podium.Length; i++)
            {
                Entry entry = i < ranking.Count ? ranking[i] : null;
                FillPodium(podium[i], entry);
                if (entry != null && entry.IsPlayer && podium[i] != null && podium[i].icon != null)
                {
                    playerTarget = podium[i].icon.rectTransform;
                }
            }

            // The list continues after the podium. The player is shown in their own place when it fits in the rows,
            // otherwise the last row is the player's, after the "...".
            int lastShownRank = PodiumCount + rows.Length;
            bool playerBelow = player.Rank <= 0 || player.Rank > lastShownRank;

            for (int i = 0; i < rows.Length; i++)
            {
                Entry entry;
                if (playerBelow && i == rows.Length - 1)
                {
                    entry = player;
                }
                else
                {
                    int index = PodiumCount + i;
                    entry = index < ranking.Count ? ranking[index] : null;
                }

                FillRow(rows[i], entry);
                if (entry != null && entry.IsPlayer && rows[i] != null && rows[i].root != null)
                {
                    playerTarget = (RectTransform)rows[i].root.transform;
                }
            }

            PlaceEllipsis(playerBelow);
            shownPlayerRank = player.Rank;
            shownPlayerBest = player.Seconds;
        }

        /// <summary>
        /// After a new best Speedrun (since the board was last seen) the player's row celebrates: sound, bounce, pixel
        /// burst and "RANK UP" with the old and new rank. Rank changes that only come from other players moving do not.
        /// </summary>
        private void CelebrateRankUp()
        {
            int seenRank = PlayerPrefs.GetInt(SeenRankKey, 0);
            float seenBest = PlayerPrefs.GetFloat(SeenBestKey, -1f);
            PlayerPrefs.SetInt(SeenRankKey, shownPlayerRank);
            PlayerPrefs.SetFloat(SeenBestKey, shownPlayerBest);
            PlayerPrefs.Save();

            bool improved = shownPlayerBest > 0f && (seenBest <= 0f || shownPlayerBest < seenBest - 0.01f);
            if (!improved || shownPlayerRank <= 0)
            {
                return;
            }

            SoundManager.Play(SoundId.RankUp);
            if (playerTarget == null)
            {
                return;
            }

            UIFx.Punch(playerTarget, 0.12f, 0.5f);
            UIFx.Burst(playerTarget, UIFx.GoldColors, 24, 600f);
            string text = seenRank > shownPlayerRank
                ? string.Format(rankUpFormat, seenRank, shownPlayerRank)
                : string.Format(newRankFormat, shownPlayerRank);
            UIFx.FloatingText(playerTarget, text, UIFx.TextColor, 38f);
        }

        /// <summary>
        /// The fastest players, ranked 1..n: the made-up board for now, with the player put in their place when they
        /// make it into the shown places. <paramref name="player"/> gets their real rank (0 without a run).
        /// </summary>
        private List<Entry> BuildRanking(out Entry player)
        {
            DateTime now = DateTime.UtcNow;
            int shown = PodiumCount + rows.Length;
            var ranking = new List<Entry>(shown + 1);
            foreach (FakeLeaderboard.Rival rival in FakeLeaderboard.Top(now, shown))
            {
                ranking.Add(new Entry { Name = rival.Name, Icon = rival.IsMrDa ? mrDaIcon : SkinIcon(rival.SkinId), Seconds = rival.Seconds });
            }

            GameSceneManager manager = GameSceneManager.Instance;
            float best = manager != null ? manager.BestSpeedrunSeconds : -1f;
            SkinCatalog.Skin skin = catalog != null ? catalog.Selected : null;
            player = new Entry
            {
                Name = playerName,
                Icon = skin != null ? skin.icon : null,
                Seconds = best,
                IsPlayer = true
            };

            if (best > 0f)
            {
                player.Rank = FakeLeaderboard.CountFaster(now, best) + 1;
                if (player.Rank <= shown)
                {
                    ranking.Insert(Mathf.Min(player.Rank - 1, ranking.Count), player);
                }
            }

            for (int i = 0; i < ranking.Count; i++)
            {
                ranking[i].Rank = i + 1;
            }

            return ranking;
        }

        private Sprite SkinIcon(string skinId)
        {
            SkinCatalog.Skin skin = catalog != null ? catalog.Find(skinId) : null;
            return skin != null ? skin.icon : null;
        }

        private static void FillPodium(PodiumSlot slot, Entry entry)
        {
            if (slot == null)
            {
                return;
            }

            SetIcon(slot.icon, entry != null ? entry.Icon : null);
            if (slot.medal != null)
            {
                slot.medal.enabled = entry != null;
            }

            SetText(slot.name, entry != null ? entry.Name : string.Empty);
            SetText(slot.time, entry != null ? FormatTime(entry.Seconds) : string.Empty);
        }

        private void FillRow(Row row, Entry entry)
        {
            if (row == null || row.root == null)
            {
                return;
            }

            row.root.SetActive(entry != null);
            if (entry == null)
            {
                return;
            }

            bool hasRun = entry.Seconds > 0f;
            if (row.frame != null)
            {
                Sprite frame = entry.IsPlayer ? myRowSprite : otherRowSprite;
                if (frame != null)
                {
                    row.frame.sprite = frame;
                }
            }

            SetText(row.rank, entry.Rank > 0 ? entry.Rank.ToString() : string.Empty);
            if (row.rank != null)
            {
                // Long ranks ("1284") get smaller so they stay in front of the icon.
                int digits = entry.Rank.ToString().Length;
                row.rank.enableAutoSizing = false;
                row.rank.fontSize = digits <= 2 ? 30f : digits == 3 ? 21f : digits == 4 ? 16f : 13f;
            }
            SetIcon(row.icon, entry.Icon);
            SetText(row.name, entry.Name);
            SetText(row.time, FormatTime(hasRun ? entry.Seconds : 0f));

            // "No runs yet" sits under the name; the name moves up to make room.
            bool showSubtitle = entry.IsPlayer && !hasRun;
            if (row.subtitle != null)
            {
                row.subtitle.gameObject.SetActive(showSubtitle);
                row.subtitle.text = noRunText;
            }

            if (row.name != null)
            {
                row.name.rectTransform.anchoredPosition = new Vector2(row.name.rectTransform.anchoredPosition.x, showSubtitle ? 9f : 0f);
            }
        }

        /// <summary>The "..." goes under the list, or before the last row when that row is the player's.</summary>
        private void PlaceEllipsis(bool beforeLastRow)
        {
            if (ellipsis == null || rows.Length == 0)
            {
                return;
            }

            ellipsis.SetActive(true);
            int sibling = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                if (beforeLastRow && i == rows.Length - 1)
                {
                    ellipsis.transform.SetSiblingIndex(sibling++);
                }

                if (rows[i] != null && rows[i].root != null)
                {
                    rows[i].root.transform.SetSiblingIndex(sibling++);
                }
            }

            if (!beforeLastRow)
            {
                ellipsis.transform.SetSiblingIndex(sibling);
            }
        }

        /// <summary>Hours and minutes, like 00:35 for 35 minutes.</summary>
        private static string FormatTime(float seconds)
        {
            int minutes = Mathf.Max(0, Mathf.FloorToInt(seconds / 60f));
            return (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
        }

        private static void SetIcon(Image image, Sprite sprite)
        {
            if (image != null)
            {
                image.sprite = sprite;
                image.enabled = sprite != null;
            }
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
            {
                text.text = value;
            }
        }

        // ---------------------------------------------------------------- animation

        private IEnumerator SlideRoutine(bool open)
        {
            isAnimating = true;
            group.gameObject.SetActive(true);
            group.interactable = false;
            group.blocksRaycasts = true;

            // Off-screen above the canvas: one full panel height up.
            float hidden = slidingPanel != null ? slidingPanel.rect.height : 0f;
            float from = open ? hidden : 0f;
            float to = open ? 0f : hidden;
            float startAlpha = open ? 0f : group.alpha;
            float endAlpha = open ? 1f : 0f;
            float elapsed = 0f;

            while (elapsed < slideDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / slideDuration);
                float eased = open ? EaseOutBack(t) : t * t;
                SetPanelOffset(Mathf.LerpUnclamped(from, to, eased));
                group.alpha = Mathf.Lerp(startAlpha, endAlpha, open ? Mathf.Clamp01(t * 3f) : t);
                yield return null;
            }

            SetPanelOffset(to);
            group.alpha = endAlpha;

            if (open)
            {
                group.interactable = true;
            }
            else
            {
                group.blocksRaycasts = false;
                group.gameObject.SetActive(false);
                SetPanelOffset(0f);
            }

            IsOpen = open;
            isAnimating = false;

            if (open)
            {
                CelebrateRankUp();
            }
        }

        private void SetPanelOffset(float y)
        {
            if (slidingPanel != null)
            {
                slidingPanel.anchoredPosition = new Vector2(slidingPanel.anchoredPosition.x, y);
            }
        }

        private static float EaseOutBack(float t)
        {
            const float overshoot = 1.2f;
            float u = t - 1f;
            return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
        }

        private static void Click()
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayClick();
            }
        }

        private static bool WasBackKeyPressed()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}
