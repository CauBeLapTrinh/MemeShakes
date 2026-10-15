using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
#if CATSHAKES_FIREBASE
using Firebase;
using Firebase.Analytics;
using Firebase.Crashlytics;
#endif

namespace Catshakes
{
    /// <summary>
    /// One place for every game event (levels, Speedrun, ads, skins). Each event is written to the log as
    /// "[Analytics] name {key=value, ...}" (Logcat tag Unity) and, when the Firebase SDK is installed and the scripting
    /// define CATSHAKES_FIREBASE is set (Player Settings > Other Settings > Scripting Define Symbols), also sent to
    /// Firebase Analytics, with Crashlytics catching crashes and uncaught exceptions.
    ///
    /// Without Firebase the calls only log, so the game builds and runs the same either way.
    /// Event and parameter names follow Firebase's rules: letters, digits and '_', at most 40 characters.
    /// </summary>
    public static class GameAnalytics
    {
        /// <summary>Set to false to silence the "[Analytics]" log lines (events still go to Firebase).</summary>
        public static bool LogToConsole = true;

#if CATSHAKES_FIREBASE
        private static bool firebaseReady;
        private static readonly List<KeyValuePair<string, object[]>> pending = new List<KeyValuePair<string, object[]>>();
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
#if CATSHAKES_FIREBASE
            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task =>
            {
                if (task.Result != DependencyStatus.Available)
                {
                    Debug.LogWarning("[Analytics] Firebase is not available: " + task.Result);
                    return;
                }

                // Crashlytics: uncaught C# exceptions count as crashes so they show up on the dashboard.
                Crashlytics.ReportUncaughtExceptionsAsFatal = true;
                FirebaseAnalytics.SetAnalyticsCollectionEnabled(true);
                firebaseReady = true;
                Debug.Log("[Analytics] Firebase ready.");
            });
#endif
        }

        // ---------------------------------------------------------------- game events

        public static void LevelStart(int levelIndex, bool speedrun)
        {
            Log("level_start", "level", levelIndex, "speedrun", speedrun ? 1 : 0);
        }

        public static void LevelComplete(int levelIndex, bool speedrun, int moves, float levelSeconds)
        {
            Log("level_complete", "level", levelIndex, "speedrun", speedrun ? 1 : 0, "moves", moves, "seconds", Mathf.RoundToInt(levelSeconds));
        }

        public static void LevelRestart(int levelIndex, int moves)
        {
            Log("level_restart", "level", levelIndex, "moves", moves);
        }

        public static void GiveUp(int levelIndex, bool speedrun)
        {
            Log("give_up", "level", levelIndex, "speedrun", speedrun ? 1 : 0);
        }

        public static void SpeedrunStart()
        {
            Log("speedrun_start");
        }

        public static void SpeedrunComplete(float totalSeconds, bool newBest)
        {
            Log("speedrun_complete", "seconds", Mathf.RoundToInt(totalSeconds), "new_best", newBest ? 1 : 0);
        }

        // ---------------------------------------------------------------- ads and skins

        /// <summary>The player tapped "Watch ads". <paramref name="ready"/> = an ad could be shown right away.</summary>
        public static void AdRequested(string rewardKey, bool ready, bool online)
        {
            Log("ad_reward_request", "reward", rewardKey, "ready", ready ? 1 : 0, "online", online ? 1 : 0);
        }

        public static void AdRewarded(string rewardKey)
        {
            Log("ad_rewarded", "reward", rewardKey);
        }

        public static void AdShowFailed(string reason)
        {
            Log("ad_show_failed", "reason", reason);
        }

        public static void AdLoadFailed(int errorCode, string message)
        {
            Log("ad_load_failed", "code", errorCode, "message", message);
        }

        /// <summary>Impression-level revenue from LevelPlay, in Firebase's standard "ad_impression" format.</summary>
        public static void AdImpression(string adNetwork, string adFormat, string adUnit, double? revenueUsd)
        {
            Log("ad_impression", "ad_platform", "LevelPlay", "ad_source", adNetwork, "ad_format", adFormat,
                "ad_unit_name", adUnit, "currency", "USD", "value", revenueUsd ?? 0d);
        }

        public static void SkinAdProgress(string skinId, int watched, int required)
        {
            Log("skin_ad_progress", "skin", skinId, "watched", watched, "required", required);
        }

        public static void SkinUnlocked(string skinId)
        {
            Log("skin_unlocked", "skin", skinId);
        }

        public static void SkinSelected(string skinId)
        {
            Log("skin_selected", "skin", skinId);
        }

        // ---------------------------------------------------------------- plumbing

        /// <summary>Logs a custom event. Parameters come in pairs: name, value (int, long, float, double, bool or string).</summary>
        public static void Log(string eventName, params object[] parameters)
        {
            if (LogToConsole)
            {
                Debug.Log("[Analytics] " + eventName + Describe(parameters));
            }

#if CATSHAKES_FIREBASE
            if (!firebaseReady)
            {
                // Events from the first frames, before Firebase finished starting.
                if (pending.Count < 50)
                {
                    pending.Add(new KeyValuePair<string, object[]>(eventName, parameters));
                }

                return;
            }

            FlushPending();
            Send(eventName, parameters);
#endif
        }

#if CATSHAKES_FIREBASE
        private static void FlushPending()
        {
            if (pending.Count == 0)
            {
                return;
            }

            var copy = pending.ToArray();
            pending.Clear();
            foreach (var item in copy)
            {
                Send(item.Key, item.Value);
            }
        }

        private static void Send(string eventName, object[] parameters)
        {
            var list = new List<Parameter>();
            for (int i = 0; i + 1 < parameters.Length; i += 2)
            {
                string key = parameters[i] as string;
                object value = parameters[i + 1];
                if (string.IsNullOrEmpty(key) || value == null)
                {
                    continue;
                }

                switch (value)
                {
                    case int v: list.Add(new Parameter(key, v)); break;
                    case long v: list.Add(new Parameter(key, v)); break;
                    case float v: list.Add(new Parameter(key, v)); break;
                    case double v: list.Add(new Parameter(key, v)); break;
                    case bool v: list.Add(new Parameter(key, v ? 1L : 0L)); break;
                    default: list.Add(new Parameter(key, Truncate(value.ToString(), 100))); break;
                }
            }

            FirebaseAnalytics.LogEvent(eventName, list.ToArray());
        }

        private static string Truncate(string text, int max)
        {
            return text.Length <= max ? text : text.Substring(0, max);
        }
#endif

        private static string Describe(object[] parameters)
        {
            if (parameters == null || parameters.Length < 2)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(" {");
            for (int i = 0; i + 1 < parameters.Length; i += 2)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                object value = parameters[i + 1];
                builder.Append(parameters[i]).Append('=')
                       .Append(value is System.IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value);
            }

            return builder.Append('}').ToString();
        }
    }
}
