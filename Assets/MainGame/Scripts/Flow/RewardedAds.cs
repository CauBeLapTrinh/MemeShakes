using System;
using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// Placeholder for rewarded video ads. No ad SDK is integrated yet, so every request succeeds at once.
    /// Replace the body of <see cref="Show"/> with the real ad call (invoke the callback with true only when the reward
    /// was earned); the shop does not need to change.
    /// </summary>
    public static class RewardedAds
    {
        public static void Show(string placement, Action<bool> onFinished)
        {
            Debug.Log("RewardedAds: no ad SDK yet, granting the reward for '" + placement + "'.");
            onFinished?.Invoke(true);
        }
    }
}
