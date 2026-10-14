using System;
using System.Collections.Generic;
using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// A believable Speedrun leaderboard without a server. Everything is computed from a fixed seed and the current date,
    /// so every device shows the same board, and the board moves on by itself as days pass:
    ///
    ///  - Elites: a fixed roster of named players. Each one shows up on their own day, starts with a slow time and gets
    ///    faster day after day toward a personal limit (exponential learning curve), so ranks swap now and then and new
    ///    names climb the board. Nobody's limit is under <see cref="RecordSeconds"/> (35:00), and the first elite is at
    ///    that pace from the start, so the top time always reads 00:35.
    ///  - Crowd: thousands of nameless players whose number grows every day. Their times follow a long-tailed curve
    ///    behind the elites. They only matter for the player's own rank ("#1284").
    ///
    /// Times are in seconds; the board shows them as hours:minutes.
    /// </summary>
    public static class FakeLeaderboard
    {
        public readonly struct Rival
        {
            public readonly string Name;
            /// <summary>Skin id from the <see cref="SkinCatalog"/>, or null for Mr. Da (who uses his own face).</summary>
            public readonly string SkinId;
            public readonly float Seconds;

            public Rival(string name, string skinId, float seconds)
            {
                Name = name;
                SkinId = skinId;
                Seconds = seconds;
            }

            public bool IsMrDa => SkinId == null;
        }

        /// <summary>The fastest any fake player can ever be: 35 minutes.</summary>
        public const float RecordSeconds = 35f * 60f;

        private const int Seed = 20261014;
        private const int EliteCount = 60;

        // Day 0 of the simulation (UTC). Earlier days count as negative.
        private static readonly DateTime Epoch = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        // Crowd: size on day 0, daily growth, and the time range of the curve (fastest to slowest).
        private const float CrowdOnDayZero = 1800f;
        private const float CrowdPerDay = 63f;
        private const float CrowdFastestSeconds = 48f * 60f;
        private const float CrowdSlowestSeconds = 5f * 3600f;
        private const float CrowdCurve = 1.7f;

        private static readonly string[] SkinIds = { "MemeCat3", "MemeCat2", "MemeCat4", "MemeCat1", "MemeCat", "MemeHorse" };
        // Cheaper skins are worn more often.
        private static readonly float[] SkinWeights = { 5f, 4f, 3f, 2f, 1.5f, 1f };

        // Names from the demo, given to fixed places of the roster.
        private static readonly Dictionary<int, string> FixedNames = new Dictionary<int, string>
        {
            { 0, "Mr. Da" }, { 1, "Thai Doi" }, { 2, "WhoUrDad?" }, { 4, "Poor Guy..." }, { 6, "Phuc Du Bich" }, { 9, "Boba" }
        };

        private static readonly string[] TagStarts =
        {
            "Shake", "Meow", "Paw", "Catto", "Neko", "Whisker", "Banana", "Mochi", "Tofu", "Pho", "Lag", "Speedy", "Turbo",
            "Sleepy", "Grumpy", "Chill", "Noob", "Lucky", "Ninja", "Ghost", "Salty", "Crispy", "Tiny", "Mega", "Sigma"
        };

        private static readonly string[] TagEnds =
        {
            "Master", "Lord", "King", "Queen", "Boi", "Gang", "Hands", "Runner", "Shaker", "Cat", "Paws", "God", "Bro", "Chan",
            "Kun", "Enjoyer", "Main", "Andy"
        };

        private static readonly string[] VietFirst =
        {
            "tuan", "minh", "huy", "hoang", "khanh", "linh", "thu", "ngoc", "bao", "quang", "trang", "duc", "phuong", "nam",
            "vy", "long", "thao", "kien", "my", "an"
        };

        private static readonly string[] VietLast = { "anh", "hoa", "dung", "tam", "vu", "le", "tran", "pham", "nguyen", "do" };

        private static readonly string[] OddNames =
        {
            "ban tay vang", "Hand Enjoyer", "touch grass pls", "cat dad", "me ko cho choi", "1 more try", "AFK", "no hands",
            "sus cat", "slow but sure", "di hoc ve", "Bao Gio Moi Xong", "help", "i hate doors", "barrel lover"
        };

        private sealed class Elite
        {
            public string Name;
            public string SkinId;
            public float JoinDay;
            public float StartSeconds;
            public float LimitSeconds;
            public float Rate;
        }

        private static Elite[] elites;

        /// <summary>Days since the epoch, with the time of day as the fraction.</summary>
        public static float DayOf(DateTime utcNow)
        {
            return (float)(utcNow.ToUniversalTime() - Epoch).TotalDays;
        }

        /// <summary>The fastest <paramref name="count"/> players on <paramref name="utcNow"/>, fastest first.</summary>
        public static List<Rival> Top(DateTime utcNow, int count)
        {
            float day = DayOf(utcNow);
            var board = new List<Rival>(EliteCount);
            foreach (Elite elite in GetElites())
            {
                if (day >= elite.JoinDay)
                {
                    board.Add(new Rival(elite.Name, elite.SkinId, TimeOf(elite, day)));
                }
            }

            board.Sort((a, b) => a.Seconds.CompareTo(b.Seconds));
            if (board.Count > count)
            {
                board.RemoveRange(count, board.Count - count);
            }

            return board;
        }

        /// <summary>How many fake players are faster than <paramref name="seconds"/> on <paramref name="utcNow"/>.</summary>
        public static int CountFaster(DateTime utcNow, float seconds)
        {
            float day = DayOf(utcNow);
            int faster = 0;
            foreach (Elite elite in GetElites())
            {
                if (day >= elite.JoinDay && TimeOf(elite, day) < seconds)
                {
                    faster++;
                }
            }

            return faster + Mathf.FloorToInt(CrowdSize(day) * CrowdShareFaster(seconds));
        }

        /// <summary>Everyone on the board (elites who have shown up and the crowd).</summary>
        public static int PlayerCount(DateTime utcNow)
        {
            float day = DayOf(utcNow);
            int elitesIn = 0;
            foreach (Elite elite in GetElites())
            {
                if (day >= elite.JoinDay)
                {
                    elitesIn++;
                }
            }

            return elitesIn + Mathf.FloorToInt(CrowdSize(day));
        }

        // ---------------------------------------------------------------- model

        private static float TimeOf(Elite elite, float day)
        {
            float practice = Mathf.Max(0f, day - elite.JoinDay);
            return elite.LimitSeconds + (elite.StartSeconds - elite.LimitSeconds) * Mathf.Exp(-elite.Rate * practice);
        }

        private static float CrowdSize(float day)
        {
            // Grows every day, a little faster at the weekend; never below the launch crowd.
            float weekendBoost = 0.15f * Mathf.Max(0f, Mathf.Sin(day * Mathf.PI * 2f / 7f));
            return CrowdOnDayZero + Mathf.Max(0f, day) * CrowdPerDay * (1f + weekendBoost);
        }

        /// <summary>Share of the crowd faster than <paramref name="seconds"/> (0..1), from the long-tailed time curve.</summary>
        private static float CrowdShareFaster(float seconds)
        {
            if (seconds <= CrowdFastestSeconds)
            {
                return 0f;
            }

            if (seconds >= CrowdSlowestSeconds)
            {
                return 1f;
            }

            // Inverse of t(q) = fastest * (slowest / fastest)^(q^curve).
            float position = Mathf.Log(seconds / CrowdFastestSeconds) / Mathf.Log(CrowdSlowestSeconds / CrowdFastestSeconds);
            return Mathf.Pow(position, 1f / CrowdCurve);
        }

        private static Elite[] GetElites()
        {
            if (elites != null)
            {
                return elites;
            }

            var random = new System.Random(Seed);
            var used = new HashSet<string>(FixedNames.Values);
            elites = new Elite[EliteCount];

            for (int i = 0; i < EliteCount; i++)
            {
                float share = i / (float)(EliteCount - 1);
                var elite = new Elite();

                // Personal limit: the first one is at the record pace; the others are about 45 s apart (plus a long tail and
                // some noise, so neighbours can still pass each other), which keeps the top times from all reading 00:35.
                elite.LimitSeconds = RecordSeconds + 4f + (i == 0 ? 0f : i * 45f + 900f * Mathf.Pow(share, 1.5f) + Range(random, 0f, 60f));

                // The early names were there at launch; the rest show up over the first months.
                elite.JoinDay = i < 8 ? -30f - i : Range(random, -10f, 150f);
                elite.StartSeconds = elite.LimitSeconds + (i == 0 ? 40f : Range(random, 5f, 30f) * 60f);
                elite.Rate = i == 0 ? 0.3f : Range(random, 0.02f, 0.12f);

                if (FixedNames.TryGetValue(i, out string fixedName))
                {
                    elite.Name = fixedName;
                }
                else
                {
                    elite.Name = MakeName(random, used);
                }

                elite.SkinId = elite.Name == "Mr. Da" ? null : PickSkin(random);
                elites[i] = elite;
            }

            return elites;
        }

        private static string MakeName(System.Random random, HashSet<string> used)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                string name;
                switch (random.Next(5))
                {
                    case 0:
                        name = Pick(random, TagStarts) + Pick(random, TagEnds);
                        break;
                    case 1:
                        name = Pick(random, TagStarts) + Pick(random, TagEnds) + random.Next(1, 100);
                        break;
                    case 2:
                        name = Pick(random, VietFirst) + (random.Next(2) == 0 ? "_" : ".") + Pick(random, VietLast) + (random.Next(2) == 0 ? "2k" + random.Next(5, 10) : random.Next(10, 100).ToString());
                        break;
                    case 3:
                        name = Capitalize(Pick(random, VietFirst)) + " " + Capitalize(Pick(random, VietLast));
                        break;
                    default:
                        name = Pick(random, OddNames);
                        break;
                }

                if (used.Add(name))
                {
                    return name;
                }
            }

            return "Player" + random.Next(1000, 9999);
        }

        private static string PickSkin(System.Random random)
        {
            float total = 0f;
            foreach (float weight in SkinWeights)
            {
                total += weight;
            }

            float roll = (float)random.NextDouble() * total;
            for (int i = 0; i < SkinIds.Length; i++)
            {
                roll -= SkinWeights[i];
                if (roll <= 0f)
                {
                    return SkinIds[i];
                }
            }

            return SkinIds[0];
        }

        private static string Pick(System.Random random, string[] options)
        {
            return options[random.Next(options.Length)];
        }

        private static string Capitalize(string word)
        {
            return word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word.Substring(1);
        }

        private static float Range(System.Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }
    }
}
