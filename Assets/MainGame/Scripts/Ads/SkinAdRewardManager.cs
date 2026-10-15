using System;
using System.Collections;
using Unity.Services.LevelPlay;
using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// Rewarded video ads through Unity LevelPlay (Ads Mediation 9.5+), used to unlock skins.
    ///
    ///  - One instance lives for the whole session (DontDestroyOnLoad); a second copy destroys itself.
    ///  - Start() initializes the SDK with <see cref="androidAppKey"/>. After OnInitSuccess the rewarded ad object
    ///    is created, its events are hooked up and an ad is loaded right away, so one is ready when the player asks.
    ///  - <see cref="ShowRewardedAd()"/> (for a UI Button) or <see cref="ShowRewardedAd(string, Action{bool})"/> (from
    ///    code) shows the ad when it is ready; otherwise it reports "not ready" and starts loading one.
    ///  - When LevelPlay grants the reward, <see cref="OnSkinRewardUnlocked"/> is raised and the caller's callback
    ///    gets true. A new ad is loaded after every show, and after a failed load with a growing delay.
    ///
    /// The shop goes through <see cref="RewardedAds.Show"/>, which forwards here. Without an app key, or on a
    /// platform LevelPlay does not support (the Editor with a Standalone target), the Editor grants the reward at
    /// once so the game can still be tested; a device build never grants a reward without a real ad.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkinAdRewardManager : MonoBehaviour
    {
        [Header("LevelPlay (values from the LevelPlay dashboard)")]
        [Tooltip("Apps > your Android app: the App Key under the app name.")]
        [SerializeField] private string androidAppKey = "";
        [Tooltip("Ad units > your Android app: the ID of the Rewarded ad unit.")]
        [SerializeField] private string adUnitId = "";
        [Tooltip("Optional placement name from the dashboard (for capping and reports). Empty = default placement.")]
        [SerializeField] private string placementName = "";

        [Tooltip("Must match the dashboard's COPPA setting (App settings > Privacy). Off = \"Not directed\" to children.")]
        [SerializeField] private bool childDirected;

        [Header("Loading")]
        [Tooltip("First retry delay after a failed load, in seconds; doubles after each failure up to the maximum.")]
        [SerializeField, Min(1f)] private float retryDelay = 5f;
        [SerializeField, Min(1f)] private float maxRetryDelay = 60f;

        [Header("Diagnostics (Development Builds only, ignored in release builds)")]
        [Tooltip("Opens the LevelPlay Test Suite after init: shows which ad networks are set up and lets you load/show test ads per network.")]
        [SerializeField] private bool launchTestSuite;
        [Tooltip("Verbose ad network (adapter) logs in Logcat and an integration check (ValidateIntegration) after init.")]
        [SerializeField] private bool verboseAdLogs = true;

        [Header("Editor")]
        [Tooltip("In the Editor, grant the reward without an ad when LevelPlay cannot run (no app key or a Standalone build target).")]
        [SerializeField] private bool simulateInEditor = true;

        // OnAdRewarded may arrive a moment after OnAdClosed; wait this long before treating a close as "no reward".
        private const float RewardGraceSeconds = 1f;

        /// <summary>Raised once per earned reward with the reward key the ad was shown for (see ShowRewardedAd).</summary>
        public static event Action<string> OnSkinRewardUnlocked;

        /// <summary>Raised when the player asked for an ad but none was loaded yet (a load has been started).</summary>
        public static event Action OnAdNotReady;

        /// <summary>Raised when an ad is loaded and can be shown (for example to enable buttons).</summary>
        public static event Action OnAdAvailable;

        public static SkinAdRewardManager Instance { get; private set; }

        private LevelPlayRewardedAd rewardedAd;
        private bool isInitialized;
        private bool isInitializing;
        private bool isLoading;
        private float initRetryDelay;
        private float currentRetryDelay;

        // The ad being shown: who asked, for what reward, and whether the reward came already.
        private bool isShowing;
        private string pendingRewardKey;
        private Action<bool> pendingCallback;
        private bool rewardGranted;

        /// <summary>True when the SDK is up and an ad can be shown right now (or the Editor would simulate one).</summary>
        public bool IsAdReady => (rewardedAd != null && rewardedAd.IsAdReady()) || CanSimulate;

        /// <summary>True while an ad is on screen.</summary>
        public bool IsShowing => isShowing;

        /// <summary>
        /// True in a "no ads" build (scripting define CATSHAKES_NO_ADS, switched by Catshakes > Ads in the Editor menu):
        /// LevelPlay is never started and every "Watch ads" is granted at once, for testing without ads.
        /// </summary>
        public static bool AdsDisabled
        {
            get
            {
#if CATSHAKES_NO_ADS
                return true;
#else
                return false;
#endif
            }
        }

        private bool CanSimulate => AdsDisabled || (Application.isEditor && simulateInEditor && !isInitialized);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
            OnSkinRewardUnlocked = null;
            OnAdNotReady = null;
            OnAdAvailable = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (transform.parent != null)
            {
                transform.SetParent(null, false);
            }

            DontDestroyOnLoad(gameObject);
            currentRetryDelay = retryDelay;
            initRetryDelay = retryDelay;
        }

        private void Start()
        {
            if (Instance != this)
            {
                return;
            }

            if (AdsDisabled)
            {
                Debug.Log("SkinAdRewardManager: NO ADS build (CATSHAKES_NO_ADS). LevelPlay is not started; rewards are granted at once.");
                return;
            }

            if (string.IsNullOrWhiteSpace(androidAppKey) || string.IsNullOrWhiteSpace(adUnitId))
            {
                Debug.LogWarning("SkinAdRewardManager: set the Android App Key and the Rewarded Ad Unit ID in the Inspector. Ads are off"
                                 + (Application.isEditor && simulateInEditor ? " (the Editor simulates rewards)." : "."));
                return;
            }

            // Callbacks first, then Init (asynchronous: the result arrives in OnInitSuccess / OnInitFailed).
            LevelPlay.OnInitSuccess += HandleInitSuccess;
            LevelPlay.OnInitFailed += HandleInitFailed;

            // Privacy flags go before Init. Same value as the dashboard (CatShakes: COPPA "Not directed").
            LevelPlayPrivacySettings.SetCOPPA(childDirected);

            // Both must be set before Init. Debug.isDebugBuild keeps them out of release builds.
            if (Debug.isDebugBuild && !Application.isEditor)
            {
                if (launchTestSuite)
                {
                    LevelPlay.SetMetaData("is_test_suite", "enable");
                }

                if (verboseAdLogs)
                {
                    LevelPlay.SetAdaptersDebug(true);
                }
            }

            Initialize();
        }

        private void Initialize()
        {
            if (isInitialized || isInitializing)
            {
                return;
            }

            isInitializing = true;
            LevelPlay.Init(androidAppKey.Trim());
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            LevelPlay.OnInitSuccess -= HandleInitSuccess;
            LevelPlay.OnInitFailed -= HandleInitFailed;

            if (rewardedAd != null)
            {
                rewardedAd.OnAdLoaded -= HandleAdLoaded;
                rewardedAd.OnAdLoadFailed -= HandleAdLoadFailed;
                rewardedAd.OnAdDisplayed -= HandleAdDisplayed;
                rewardedAd.OnAdDisplayFailed -= HandleAdDisplayFailed;
                rewardedAd.OnAdRewarded -= HandleAdRewarded;
                rewardedAd.OnAdClosed -= HandleAdClosed;
                rewardedAd.OnAdImpressionDataReady -= HandleImpressionData;
                rewardedAd.DestroyAd();
                rewardedAd = null;
            }

            Instance = null;
        }

        // ---------------------------------------------------------------- public API

        /// <summary>For a UI Button's OnClick: shows an ad for the default reward key ("skin").</summary>
        public void ShowRewardedAd()
        {
            ShowRewardedAd("skin", null);
        }

        /// <summary>
        /// Shows a rewarded ad. <paramref name="rewardKey"/> is passed back in <see cref="OnSkinRewardUnlocked"/> so a
        /// listener knows what the reward was for (for example "shop_skin_MemeCat2"). <paramref name="onFinished"/> gets
        /// true when the reward was earned and false otherwise (not ready, failed, or closed early); it is always called
        /// exactly once.
        /// </summary>
        public void ShowRewardedAd(string rewardKey, Action<bool> onFinished)
        {
            GameAnalytics.AdRequested(rewardKey, IsAdReady, RewardedAds.HasInternet);
            if (isShowing)
            {
                onFinished?.Invoke(false);
                return;
            }

            if (CanSimulate)
            {
                Debug.Log("SkinAdRewardManager: " + (AdsDisabled ? "NO ADS build" : "LevelPlay is not running in the Editor")
                          + ", granting the reward for '" + rewardKey + "' without an ad.");
                OnSkinRewardUnlocked?.Invoke(rewardKey);
                onFinished?.Invoke(true);
                return;
            }

            if (rewardedAd == null || !rewardedAd.IsAdReady())
            {
                Debug.Log("SkinAdRewardManager: no rewarded ad is ready yet, loading one.");
                OnAdNotReady?.Invoke();
                if (!isInitialized && !string.IsNullOrWhiteSpace(androidAppKey) && !string.IsNullOrWhiteSpace(adUnitId))
                {
                    Initialize();
                }

                LoadAd();
                onFinished?.Invoke(false);
                return;
            }

            string placement = string.IsNullOrWhiteSpace(placementName) ? null : placementName.Trim();
            if (placement != null && LevelPlayRewardedAd.IsPlacementCapped(placement))
            {
                Debug.Log("SkinAdRewardManager: placement '" + placement + "' is capped for now.");
                OnAdNotReady?.Invoke();
                onFinished?.Invoke(false);
                return;
            }

            isShowing = true;
            rewardGranted = false;
            pendingRewardKey = rewardKey;
            pendingCallback = onFinished;
            rewardedAd.ShowAd(placement);
        }

        /// <summary>Loads a rewarded ad unless one is loading or already loaded.</summary>
        public void LoadAd()
        {
            if (rewardedAd == null || isLoading || rewardedAd.IsAdReady())
            {
                return;
            }

            isLoading = true;
            rewardedAd.LoadAd();
        }

        // ---------------------------------------------------------------- SDK callbacks

        private void HandleInitSuccess(LevelPlayConfiguration configuration)
        {
            Debug.Log("LevelPlay SDK initialized successfully");
            isInitialized = true;
            isInitializing = false;
            if (rewardedAd != null)
            {
                return;
            }

            if (Debug.isDebugBuild && !Application.isEditor)
            {
                if (verboseAdLogs)
                {
                    // Logcat: one line per installed network adapter (VERIFIED / MISSING ...).
                    LevelPlay.ValidateIntegration();
                }

                if (launchTestSuite)
                {
                    LevelPlay.LaunchTestSuite();
                }
            }

            // Ad objects may only be created after a successful init.
            rewardedAd = new LevelPlayRewardedAd(adUnitId.Trim());
            rewardedAd.OnAdLoaded += HandleAdLoaded;
            rewardedAd.OnAdLoadFailed += HandleAdLoadFailed;
            rewardedAd.OnAdDisplayed += HandleAdDisplayed;
            rewardedAd.OnAdDisplayFailed += HandleAdDisplayFailed;
            rewardedAd.OnAdRewarded += HandleAdRewarded;
            rewardedAd.OnAdClosed += HandleAdClosed;
            rewardedAd.OnAdImpressionDataReady += HandleImpressionData;

            // Preload so an ad is ready the moment the player opens a skin.
            LoadAd();
        }

        private void HandleInitFailed(LevelPlayInitError error)
        {
            // Usually no internet when the game started: try again later (and when the player asks for an ad).
            isInitializing = false;
            Debug.LogWarning("SkinAdRewardManager: LevelPlay init failed (" + error.ErrorCode + "): " + error.ErrorMessage
                             + ". Retrying in " + initRetryDelay + " s.");
            StartCoroutine(RetryInitAfter(initRetryDelay));
            initRetryDelay = Mathf.Min(initRetryDelay * 2f, maxRetryDelay);
        }

        private void HandleAdLoaded(LevelPlayAdInfo adInfo)
        {
            isLoading = false;
            currentRetryDelay = retryDelay;
            Debug.Log("SkinAdRewardManager: rewarded ad loaded from " + adInfo.AdNetwork + " (" + adInfo.InstanceName + ").");
            OnAdAvailable?.Invoke();
        }

        private void HandleAdLoadFailed(LevelPlayAdError error)
        {
            isLoading = false;
            // Only the first failure of a streak, so a long offline period does not flood the analytics.
            if (Mathf.Approximately(currentRetryDelay, retryDelay))
            {
                GameAnalytics.AdLoadFailed(error.ErrorCode, error.ErrorMessage);
            }
            Debug.LogWarning("SkinAdRewardManager: rewarded ad failed to load (" + error.ErrorCode + "): " + error.ErrorMessage
                             + ". Retrying in " + currentRetryDelay + " s."
                             + (error.ErrorCode == 509 ? " (509 = no network returned an ad: check the ad unit's networks/instances in the LevelPlay dashboard.)" : ""));
            StartCoroutine(RetryLoadAfter(currentRetryDelay));
            currentRetryDelay = Mathf.Min(currentRetryDelay * 2f, maxRetryDelay);
        }

        private void HandleImpressionData(LevelPlayImpressionData data)
        {
            // Revenue of one shown ad (USD, may be null); goes to Firebase as "ad_impression" once Firebase is in.
            GameAnalytics.AdImpression(data.AdNetwork, data.AdFormat, data.InstanceName, data.Revenue);
        }

        private void HandleAdDisplayed(LevelPlayAdInfo adInfo)
        {
            // The ad covers the game; LevelPlay pauses the app on Android, nothing else to do here.
        }

        private void HandleAdDisplayFailed(LevelPlayAdInfo adInfo, LevelPlayAdError error)
        {
            Debug.LogWarning("SkinAdRewardManager: rewarded ad failed to show (" + error.ErrorCode + "): " + error.ErrorMessage);
            GameAnalytics.AdShowFailed(error.ErrorCode + " " + error.ErrorMessage);
            FinishShow(false);
            LoadAd();
        }

        private void HandleAdRewarded(LevelPlayAdInfo adInfo, LevelPlayReward reward)
        {
            if (rewardGranted)
            {
                return;
            }

            // Grant here, not in OnAdClosed: the player may close the ad right after earning the reward.
            rewardGranted = true;
            string key = string.IsNullOrEmpty(pendingRewardKey) ? reward.Name : pendingRewardKey;
            GameAnalytics.AdRewarded(key);
            OnSkinRewardUnlocked?.Invoke(key);
            FinishShow(true);
        }

        private void HandleAdClosed(LevelPlayAdInfo adInfo)
        {
            if (isShowing && !rewardGranted)
            {
                StartCoroutine(FinishWithoutRewardAfterGrace());
            }

            LoadAd();
        }

        // ---------------------------------------------------------------- helpers

        private IEnumerator RetryInitAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Initialize();
        }

        private IEnumerator RetryLoadAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            LoadAd();
        }

        private IEnumerator FinishWithoutRewardAfterGrace()
        {
            yield return new WaitForSecondsRealtime(RewardGraceSeconds);
            if (isShowing && !rewardGranted)
            {
                FinishShow(false);
            }
        }

        private void FinishShow(bool rewarded)
        {
            if (!isShowing)
            {
                return;
            }

            isShowing = false;
            Action<bool> callback = pendingCallback;
            pendingCallback = null;
            pendingRewardKey = null;
            callback?.Invoke(rewarded);
        }
    }
}
