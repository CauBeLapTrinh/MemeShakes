using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Catshakes.EditorTools
{
    /// <summary>
    /// Catshakes > Ads > With Ads / Without Ads: picks whether the next Android build shows real rewarded ads.
    /// "Without Ads" adds the scripting define CATSHAKES_NO_ADS: LevelPlay is never started and every "Watch ads" is
    /// granted at once (for testing skins and the shop without ads). The choice is stored in the Player Settings, so it
    /// stays until switched back, and the menu shows a check mark next to the current mode.
    /// </summary>
    public static class AdsBuildMode
    {
        private const string Define = "CATSHAKES_NO_ADS";
        private const string WithAdsMenu = "Catshakes/Ads/With Ads (LevelPlay)";
        private const string WithoutAdsMenu = "Catshakes/Ads/Without Ads (rewards granted at once, for testing)";

        private static readonly NamedBuildTarget[] Targets = { NamedBuildTarget.Android, NamedBuildTarget.Standalone };

        public static bool AdsDisabled => HasDefine(NamedBuildTarget.Android);

        [MenuItem(WithAdsMenu, false, 0)]
        private static void UseAds()
        {
            SetAdsDisabled(false);
        }

        [MenuItem(WithAdsMenu, true)]
        private static bool UseAdsValidate()
        {
            Menu.SetChecked(WithAdsMenu, !AdsDisabled);
            return true;
        }

        [MenuItem(WithoutAdsMenu, false, 1)]
        private static void UseNoAds()
        {
            SetAdsDisabled(true);
        }

        [MenuItem(WithoutAdsMenu, true)]
        private static bool UseNoAdsValidate()
        {
            Menu.SetChecked(WithoutAdsMenu, AdsDisabled);
            return true;
        }

        public static void SetAdsDisabled(bool disabled)
        {
            if (disabled == AdsDisabled)
            {
                Debug.Log("Ads build mode is already: " + Describe(disabled));
                return;
            }

            foreach (NamedBuildTarget target in Targets)
            {
                List<string> defines = PlayerSettings.GetScriptingDefineSymbols(target)
                    .Split(';').Select(d => d.Trim()).Where(d => d.Length > 0 && d != Define).ToList();
                if (disabled)
                {
                    defines.Add(Define);
                }

                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Ads build mode: " + Describe(disabled) + ". Scripts recompile now; the next build uses this mode.");
        }

        private static bool HasDefine(NamedBuildTarget target)
        {
            return PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Any(d => d.Trim() == Define);
        }

        private static string Describe(bool disabled)
        {
            return disabled ? "WITHOUT ads (CATSHAKES_NO_ADS, rewards granted at once)" : "WITH ads (LevelPlay)";
        }
    }
}
