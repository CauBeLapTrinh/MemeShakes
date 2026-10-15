using System;
using System.Collections.Generic;
using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// Every skin the shop sells, in shelf order, plus the player's saved ownership and choice (PlayerPrefs).
    /// The asset lives at Assets/MainGame/Resources/SkinCatalog.asset; the shop and the leaderboard reference it.
    /// </summary>
    [CreateAssetMenu(menuName = "Catshakes/Skin Catalog", fileName = "SkinCatalog")]
    public sealed class SkinCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Skin
        {
            [Tooltip("Stable key used in the save data; the skin's folder name.")]
            public string id;
            public string displayName;
            public Sprite icon;
            [Tooltip("Owned from the start (no ad needed).")]
            public bool unlockedByDefault;
            [Tooltip("Rewarded ads the player must watch to unlock it.")]
            [Min(1)] public int adsRequired = 1;

            [Header("In-game artwork (same layout as the default cat; empty keeps the cat's own picture)")]
            [Tooltip("Body facing right with the start of the arm on its right edge.")]
            public Sprite body;
            [Tooltip("Optional body without the arm.")]
            public Sprite bodyNoArm;
            [Tooltip("Open hand, facing right.")]
            public Sprite hand;
            [Tooltip("Straight arm, one cell wide; repeated along the arm.")]
            public Sprite straightArm;
            [Tooltip("Elbow (arm coming from the left and going down).")]
            public Sprite elbowArm;
            [Tooltip("Two clasped hands shown when the hands meet.")]
            public Sprite handshake;
        }

        private const string ResourcePath = "SkinCatalog";

        private const string OwnedKeyPrefix = "Catshakes.Skin.Owned.";
        private const string AdsWatchedKeyPrefix = "Catshakes.Skin.AdsWatched.";
        private const string SelectedKey = "Catshakes.Skin.Selected";

        [SerializeField] private Skin[] skins = Array.Empty<Skin>();

        public IReadOnlyList<Skin> Skins => skins;

        private static SkinCatalog loaded;

        /// <summary>The catalog in Resources (Assets/MainGame/Resources/SkinCatalog.asset); null when it is missing.</summary>
        public static SkinCatalog Load()
        {
            if (loaded == null)
            {
                loaded = Resources.Load<SkinCatalog>(ResourcePath);
            }

            return loaded;
        }

        /// <summary>Raised after a skin was unlocked or selected.</summary>
        public static event Action Changed;

        public Skin Find(string id)
        {
            foreach (Skin skin in skins)
            {
                if (skin != null && skin.id == id)
                {
                    return skin;
                }
            }

            return null;
        }

        public bool IsOwned(Skin skin)
        {
            return skin != null && (skin.unlockedByDefault || PlayerPrefs.GetInt(OwnedKeyPrefix + skin.id, 0) == 1);
        }

        /// <summary>Ads already watched toward unlocking <paramref name="skin"/>.</summary>
        public int AdsWatched(Skin skin)
        {
            return skin != null ? PlayerPrefs.GetInt(AdsWatchedKeyPrefix + skin.id, 0) : 0;
        }

        /// <summary>Counts one watched ad; the skin is unlocked once enough ads were watched. True when it is now owned.</summary>
        public bool AddWatchedAd(Skin skin)
        {
            if (skin == null || IsOwned(skin))
            {
                return IsOwned(skin);
            }

            int watched = AdsWatched(skin) + 1;
            PlayerPrefs.SetInt(AdsWatchedKeyPrefix + skin.id, watched);
            GameAnalytics.SkinAdProgress(skin.id, watched, skin.adsRequired);
            if (watched >= skin.adsRequired)
            {
                Unlock(skin);
                return true;
            }

            PlayerPrefs.Save();
            Changed?.Invoke();
            return false;
        }

        public void Unlock(Skin skin)
        {
            if (skin == null || IsOwned(skin))
            {
                return;
            }

            PlayerPrefs.SetInt(OwnedKeyPrefix + skin.id, 1);
            PlayerPrefs.Save();
            GameAnalytics.SkinUnlocked(skin.id);
            Changed?.Invoke();
        }

        /// <summary>The skin the player uses: the saved choice when it is still owned, otherwise the first owned skin.</summary>
        public Skin Selected
        {
            get
            {
                Skin saved = Find(PlayerPrefs.GetString(SelectedKey, string.Empty));
                if (IsOwned(saved))
                {
                    return saved;
                }

                foreach (Skin skin in skins)
                {
                    if (IsOwned(skin))
                    {
                        return skin;
                    }
                }

                return skins.Length > 0 ? skins[0] : null;
            }
        }

        public bool IsSelected(Skin skin)
        {
            return skin != null && Selected == skin;
        }

        public void Select(Skin skin)
        {
            if (!IsOwned(skin))
            {
                return;
            }

            PlayerPrefs.SetString(SelectedKey, skin.id);
            PlayerPrefs.Save();
            GameAnalytics.SkinSelected(skin.id);
            Changed?.Invoke();
        }

        /// <summary>Forgets every unlock and the choice (debug menu).</summary>
        public void ResetSaved()
        {
            foreach (Skin skin in skins)
            {
                if (skin != null)
                {
                    PlayerPrefs.DeleteKey(OwnedKeyPrefix + skin.id);
                    PlayerPrefs.DeleteKey(AdsWatchedKeyPrefix + skin.id);
                }
            }

            PlayerPrefs.DeleteKey(SelectedKey);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
