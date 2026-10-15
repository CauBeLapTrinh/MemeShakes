using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Catshakes
{
    /// <summary>
    /// A stand-alone "watch an ad to unlock this skin" button (the shop has its own popup and does not need it).
    /// Put it on a uGUI Button and pick the skin by its id from the <see cref="SkinCatalog"/>. A tap shows a rewarded ad
    /// through <see cref="SkinAdRewardManager"/>; each earned reward counts as one watched ad for that skin, and the skin
    /// unlocks (and is put on) once it has enough. The label shows the progress, then "Owned".
    /// Progress is stored by the catalog (PlayerPrefs), so it is shared with the shop.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class SkinUnlockButton : MonoBehaviour
    {
        [Tooltip("Skin id from the SkinCatalog (its folder name, e.g. MemeCat2).")]
        [SerializeField] private string skinId = "MemeCat2";
        [Tooltip("Optional. Shows the ad progress or the owned state.")]
        [SerializeField] private TMP_Text label;
        [SerializeField] private string progressFormat = "Watch ad {0} / {1}";
        [SerializeField] private string ownedText = "Owned";
        [SerializeField] private string loadingText = "Loading ad...";

        private Button button;
        private SkinCatalog catalog;
        private bool waitingForReward;

        private string RewardKey => "skin_button_" + skinId;

        private void Awake()
        {
            button = GetComponent<Button>();
            catalog = SkinCatalog.Load();
            button.onClick.AddListener(HandleClicked);
        }

        private void OnEnable()
        {
            SkinAdRewardManager.OnSkinRewardUnlocked += HandleRewardUnlocked;
            SkinAdRewardManager.OnAdAvailable += Refresh;
            SkinCatalog.Changed += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            SkinAdRewardManager.OnSkinRewardUnlocked -= HandleRewardUnlocked;
            SkinAdRewardManager.OnAdAvailable -= Refresh;
            SkinCatalog.Changed -= Refresh;
        }

        private void OnDestroy()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(HandleClicked);
            }
        }

        private void HandleClicked()
        {
            SkinCatalog.Skin skin = catalog != null ? catalog.Find(skinId) : null;
            if (skin == null || catalog.IsOwned(skin) || waitingForReward)
            {
                return;
            }

            waitingForReward = true;
            RewardedAds.Show(RewardKey, rewarded =>
            {
                waitingForReward = false;
                Refresh();
            });
        }

        /// <summary>The reward event: only rewards this button asked for count, so nothing is counted twice.</summary>
        private void HandleRewardUnlocked(string rewardKey)
        {
            if (rewardKey != RewardKey)
            {
                return;
            }

            SkinCatalog.Skin skin = catalog != null ? catalog.Find(skinId) : null;
            if (skin != null && catalog.AddWatchedAd(skin))
            {
                catalog.Select(skin);
            }

            Refresh();
        }

        private void Refresh()
        {
            SkinCatalog.Skin skin = catalog != null ? catalog.Find(skinId) : null;
            bool owned = skin != null && catalog.IsOwned(skin);
            bool ready = RewardedAds.IsReady;

            button.interactable = skin != null && !owned && !waitingForReward;
            if (label == null || skin == null)
            {
                return;
            }

            if (owned)
            {
                label.text = ownedText;
            }
            else if (!ready)
            {
                label.text = loadingText;
            }
            else
            {
                label.text = string.Format(progressFormat, catalog.AdsWatched(skin), skin.adsRequired);
            }
        }
    }
}
