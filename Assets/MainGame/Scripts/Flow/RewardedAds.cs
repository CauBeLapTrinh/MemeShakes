using System;
using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// Entry point for rewarded video ads used by the game (the shop's "Watch ads" button). Forwards to
    /// <see cref="SkinAdRewardManager"/> (Unity LevelPlay). Without a manager in the session (for example when a scene
    /// is played on its own in the Editor) the Editor grants the reward at once and a device build grants nothing.
    /// </summary>
    public static class RewardedAds
    {
        /// <summary>True when an ad can be shown right now.</summary>
        public static bool IsReady
        {
            get
            {
                SkinAdRewardManager manager = SkinAdRewardManager.Instance;
                return manager != null ? manager.IsAdReady : Application.isEditor || SkinAdRewardManager.AdsDisabled;
            }
        }

        /// <summary>False when the device has no network at all (ads cannot load).</summary>
        public static bool HasInternet => Application.internetReachability != NetworkReachability.NotReachable;

        /// <summary>Shows a rewarded ad; <paramref name="onFinished"/> gets true only when the reward was earned.</summary>
        public static void Show(string placement, Action<bool> onFinished)
        {
            SkinAdRewardManager manager = SkinAdRewardManager.Instance;
            if (manager != null)
            {
                manager.ShowRewardedAd(placement, onFinished);
                return;
            }

            if (Application.isEditor || SkinAdRewardManager.AdsDisabled)
            {
                Debug.Log("RewardedAds: no SkinAdRewardManager in this session, granting the reward for '" + placement + "' (Editor only).");
                onFinished?.Invoke(true);
                return;
            }

            Debug.LogWarning("RewardedAds: no SkinAdRewardManager in this session, no ad shown.");
            onFinished?.Invoke(false);
        }
    }
}
