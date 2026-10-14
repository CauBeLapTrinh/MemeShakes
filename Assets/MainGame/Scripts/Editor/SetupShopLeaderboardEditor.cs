#if UNITY_EDITOR
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static Catshakes.EditorTools.SetupMenuUIEditor;

namespace Catshakes.EditorTools
{
    /// <summary>
    /// Adds the shop (button + overlay + "Watch ads" popup) and the Speedrun leaderboard to the menu scene that is already
    /// there, so hand-made changes to the scene are kept. Run it from  Catshakes/Add Shop And Leaderboard UI.  Running it
    /// again replaces only the objects it made. It also fills Assets/MainGame/Resources/SkinCatalog.asset (shelf order,
    /// names, ads needed) from the folders in Sprites/Assetgame/Skin.
    ///
    /// Positions inside the shelf and the leaderboard frame are in pixels of those pictures (centre = 0, y up), so they
    /// follow the artwork when the pictures are scaled. The layout follows the demo screens (landscape, 1920 x 1080).
    /// </summary>
    public static class SetupShopLeaderboardEditor
    {
        private const string MenuScenePath = "Assets/MainGame/Scenes/MainMenuScene.unity";
        private const string ArtFolder = "Assets/MainGame/Sprites/Assetgame";
        private const string ShopFolder = ArtFolder + "/Shop";
        private const string LeaderboardFolder = ArtFolder + "/LeaderBoard";
        private const string SkinFolder = ArtFolder + "/Skin";
        private const string CatalogPath = "Assets/MainGame/Resources/SkinCatalog.asset";

        private const string ShopButtonName = "ShopButton";
        private const string ShopOverlayName = "Shop_Overlay";
        private const string LeaderboardOverlayName = "Leaderboard_Overlay";

        /// <summary>Shelf order, names and ads needed (demo). The first one is the cat the game already uses, owned from the start.</summary>
        private static readonly string[] SkinOrder = { "MemeCat3", "MemeCat2", "MemeCat4", "MemeCat1", "MemeCat", "MemeHorse" };
        private static readonly string[] SkinNames = { "Meme Cat", "Tabby Cat", "Patch Cat", "Banana Cat", "Scream Cat", "House Aura" };
        private static readonly int[] SkinAdsRequired = { 1, 2, 5, 10, 15, 20 };

        private static readonly Color TextBrown = new Color32(62, 44, 30, 255);
        private static readonly Color TextDark = new Color32(34, 34, 34, 255);
        private static readonly Color TextGrey = new Color32(120, 110, 96, 255);

        // Shelf picture (1714 x 850): slot centres, the yellow plates under them, and the shop decoration around it.
        private static readonly float[] ShelfColumnsX = { -576f, -227f, 112f };
        private static readonly float[] ShelfRowsY = { 174f, -65f };
        private static readonly float[] ShelfPlatesY = { 62f, -173f };
        private static readonly Vector2 ShelfSlotSize = new Vector2(307f, 175f);
        // The shelf is placed so that shelf, Mr. Da and the bubble together sit in the middle of the screen.
        private static readonly Vector2 ShelfPosition = new Vector2(-24f, -21f);
        private static readonly Vector2 MrDaPosition = new Vector2(468f, 35f);
        private static readonly Vector2 RegisterPosition = new Vector2(641f, -78f);
        private static readonly Vector2 BubblePosition = new Vector2(640f, 335f);
        // Inside the bubble picture (518 x 152): the text area above the tail.
        private static readonly Vector2 BubbleTextOffset = new Vector2(0f, 16f);
        private static readonly Vector2 BubbleTextSize = new Vector2(490f, 104f);
        private const string MrDaLine = "I’m over seventy years old, yet I’ve never come across a situation like this. If it were me, I’d land a few punches!";
        /// <summary>Size of that group in shelf pixels plus a margin; ShopOverlay scales it to fill the screen.</summary>
        private static readonly Vector2 ShopStageSize = new Vector2(1842f, 952f);

        // Leaderboard frame (772 x 771): podium cards (1st, 2nd, 3rd) and the list under them.
        private static readonly float[] PodiumX = { 0f, -232f, 232f };
        private const float PodiumIconY = 115f;
        private const float PodiumMedalY = 60f;
        private const float PodiumNameY = 37f;
        private const float PodiumTimeY = 8f;
        private const float ListTopY = -48f;
        private const float BoardScale = 1.07f;
        private static readonly Vector2 BoardPosition = new Vector2(0f, 50f);

        private sealed class Art
        {
            public Texture2D ShopBackground;
            public Texture2D LeaderboardBackground;
            public Sprite ShopIcon;
            public Sprite Shelf;
            public Sprite MrDa;
            public Sprite MrDaFace;
            public Sprite Bubble;
            public Sprite Register;
            public Sprite SlotHighlight;
            public Sprite AdIcon;
            public Sprite PopupBox;
            public Sprite WatchAds;
            public Sprite Board;
            public Sprite OtherRow;
            public Sprite MyRow;
            public Sprite Gold;
            public Sprite Silver;
            public Sprite Bronze;
            public Sprite Back;
            public Sprite Close;
            public Sprite LetsShake;
        }

        [MenuItem("Catshakes/Add Shop And Leaderboard UI")]
        public static void AddToMenuScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("SetupShopLeaderboardEditor: leave Play Mode first.");
                return;
            }

            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetActiveScene();
            if (scene.path != MenuScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    return;
                }

                scene = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);
            }

            MenuUIManager manager = Object.FindFirstObjectByType<MenuUIManager>();
            if (manager == null)
            {
                Debug.LogError("SetupShopLeaderboardEditor: no MenuUIManager in " + MenuScenePath + ". Run Catshakes/Auto Setup Menu UI first.");
                return;
            }

            if (Build(manager))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("SetupShopLeaderboardEditor: shop and leaderboard added to " + MenuScenePath + ".");
            }
        }

        [MenuItem("Catshakes/Debug/Reset Skins")]
        public static void ResetSkins()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SkinCatalog>(CatalogPath);
            if (catalog != null)
            {
                catalog.ResetSaved();
                Debug.Log("Catshakes: skin unlocks, ad progress and choice cleared (PlayerPrefs).");
            }
        }

        /// <summary>Builds both overlays under the canvas of <paramref name="manager"/> and wires them. False when art is missing.</summary>
        internal static bool Build(MenuUIManager manager)
        {
            if (!TryLoadArt(out Art art))
            {
                return false;
            }

            SkinCatalog catalog = EnsureCatalog();
            var canvas = (RectTransform)manager.transform;
            var mainMenuPanel = new SerializedObject(manager).FindProperty("mainMenuPanel").objectReferenceValue as CanvasGroup;
            if (mainMenuPanel == null)
            {
                Debug.LogError("SetupShopLeaderboardEditor: MenuUIManager.mainMenuPanel is not set.");
                return false;
            }

            DestroyChild(mainMenuPanel.transform, ShopButtonName);
            DestroyChild(canvas, ShopOverlayName);
            DestroyChild(canvas, LeaderboardOverlayName);

            Button shopButton = NewButton(ShopButtonName, mainMenuPanel.transform, art.ShopIcon, 1.1f, Vector2.zero);
            SetAnchored(shopButton.image.rectTransform, TopRight, TopRight, new Vector2(-40f, -35f), shopButton.image.rectTransform.sizeDelta);
            UnityEventTools.AddPersistentListener(shopButton.onClick, manager.OnShopClicked);

            ShopOverlay shop = BuildShop(canvas, art, catalog);
            LeaderboardOverlay leaderboard = BuildLeaderboard(canvas, art, catalog);

            SetRef(manager, "shopOverlay", shop);
            SetRef(manager, "leaderboardOverlay", leaderboard);
            return true;
        }

        // ---------------------------------------------------------------- shop

        private static ShopOverlay BuildShop(RectTransform canvas, Art art, SkinCatalog catalog)
        {
            // The component stays on an always-active holder; the CanvasGroup child is what gets shown and hidden.
            RectTransform holder = NewRect(ShopOverlayName, canvas);
            Stretch(holder);
            var shop = holder.gameObject.AddComponent<ShopOverlay>();

            RectTransform shopRect = NewRect("Shop", holder);
            Stretch(shopRect);
            var shopGroup = shopRect.gameObject.AddComponent<CanvasGroup>();

            // Same moving background as Level Select; it also swallows taps meant for the menu below.
            CanvasGroup background = NewScrollingBackground("BG_Shop", shopRect, art.ShopBackground);
            background.GetComponent<RawImage>().raycastTarget = true;

            RectTransform content = NewRect("Content", shopRect);
            Stretch(content);

            // Stage: shelf space scaled to fit the screen width (ShopOverlay.fitRoot).
            RectTransform stage = NewRect("Stage", content);
            SetAnchored(stage, Center, Center, Vector2.zero, Vector2.zero);

            RectTransform shelf = NewRect("Shelf", stage);
            SetAnchored(shelf, Center, Center, ShelfPosition, art.Shelf.rect.size);

            // Mr. Da stands behind the counter, so he is drawn before the shelf picture.
            Place(NewImage("MrDa", shelf, art.MrDa, 1f), MrDaPosition);
            Image shelfImage = NewImage("ShelfImage", shelf, art.Shelf, 1f);
            Stretch(shelfImage.rectTransform);
            Place(NewImage("CashRegister", shelf, art.Register, 1f), RegisterPosition);
            Image bubble = NewImage("SpeechBubble", shelf, art.Bubble, 1f);
            Place(bubble, BubblePosition);
            TMP_Text line = NewText("Line", bubble.rectTransform, MrDaLine, 22f, TextDark, TextAlignmentOptions.Center, Center, Center, BubbleTextOffset, BubbleTextSize);
            line.fontStyle = FontStyles.Normal;
            line.gameObject.AddComponent<TypewriterText>();

            var shopSerialized = new SerializedObject(shop);
            SerializedProperty slots = shopSerialized.FindProperty("slots");
            slots.arraySize = ShelfColumnsX.Length * ShelfRowsY.Length;

            for (int i = 0; i < slots.arraySize; i++)
            {
                float x = ShelfColumnsX[i % ShelfColumnsX.Length];
                int row = i / ShelfColumnsX.Length;

                RectTransform slotRect = NewRect("Slot" + (i + 1), shelf);
                SetAnchored(slotRect, Center, Center, new Vector2(x, ShelfRowsY[row]), ShelfSlotSize);
                var hitArea = slotRect.gameObject.AddComponent<Image>();
                hitArea.color = new Color(1f, 1f, 1f, 0f);
                var button = slotRect.gameObject.AddComponent<Button>();
                button.targetGraphic = hitArea;
                button.transition = Selectable.Transition.None;
                slotRect.gameObject.AddComponent<UIButtonPress>();

                Image highlight = NewImage("Highlight", slotRect, art.SlotHighlight, 1f);
                Stretch(highlight.rectTransform);
                highlight.preserveAspect = false;

                Image icon = NewImage("Icon", slotRect, art.ShopIcon, 1f);
                SetAnchored(icon.rectTransform, Center, Center, new Vector2(0f, -4f), new Vector2(250f, 150f));
                AddPulse(icon.gameObject, i * 0.17f);

                RectTransform plate = NewRect("Plate" + (i + 1), shelf);
                SetAnchored(plate, Center, Center, new Vector2(x + 4f, ShelfPlatesY[row]), new Vector2(150f, 44f));
                Image adBadge = NewImage("AdIcon", plate, art.AdIcon, 1.1f);
                SetAnchored(adBadge.rectTransform, Center, Center, new Vector2(-42f, 0f), adBadge.rectTransform.sizeDelta);
                TMP_Text label = NewText("Label", plate, "Owner", 26f, TextBrown, TextAlignmentOptions.Center, Center, Center, Vector2.zero, new Vector2(120f, 44f));
                label.textWrappingMode = TextWrappingModes.NoWrap;

                SerializedProperty slot = slots.GetArrayElementAtIndex(i);
                slot.FindPropertyRelative("button").objectReferenceValue = button;
                slot.FindPropertyRelative("icon").objectReferenceValue = icon;
                slot.FindPropertyRelative("adBadge").objectReferenceValue = adBadge.gameObject;
                slot.FindPropertyRelative("label").objectReferenceValue = label;
                slot.FindPropertyRelative("highlight").objectReferenceValue = highlight.gameObject;
            }

            shopSerialized.ApplyModifiedPropertiesWithoutUndo();

            Button back = NewButton("BackButton", content, art.Back, 1f, Vector2.zero);
            SetAnchored(back.image.rectTransform, TopLeft, TopLeft, new Vector2(40f, -35f), back.image.rectTransform.sizeDelta);

            // ---- "Watch ads to unlock" popup
            RectTransform popupRect = NewRect("WatchAdsPopup", shopRect);
            Stretch(popupRect);
            var popupGroup = popupRect.gameObject.AddComponent<CanvasGroup>();
            Button popupDim = NewDim("Dim", popupRect, 0.45f);

            Image box = NewImage("PopupBox", popupRect, art.PopupBox, 1f);
            box.raycastTarget = true; // only the dim area closes the popup
            Place(box, new Vector2(0f, 20f));

            TMP_Text popupName = NewText("SkinName", box.rectTransform, "Skin", 40f, TextDark, TextAlignmentOptions.Center, Center, Center, new Vector2(0f, 234f), new Vector2(480f, 56f));
            Image popupIcon = NewImage("SkinIcon", box.rectTransform, art.ShopIcon, 1f);
            SetAnchored(popupIcon.rectTransform, Center, Center, new Vector2(0f, 45f), new Vector2(300f, 270f));

            Button watchAds = NewButton("WatchAdsButton", box.rectTransform, art.WatchAds, 1f, new Vector2(0f, -211f));
            Button popupClose = NewButton("CloseButton", box.rectTransform, art.Close, 1f, Vector2.zero);
            SetAnchored(popupClose.image.rectTransform, TopRight, Center, new Vector2(-19f, -21f), popupClose.image.rectTransform.sizeDelta);

            SetRef(shop, "catalog", catalog);
            SetRef(shop, "shopGroup", shopGroup);
            SetRef(shop, "shopContent", content);
            SetRef(shop, "fitRoot", stage);
            SetFloat(shop, "fitWidth", ShopStageSize.x);
            SetFloat(shop, "fitHeight", ShopStageSize.y);
            SetRef(shop, "backButton", back);
            SetRef(shop, "popupGroup", popupGroup);
            SetRef(shop, "popupBox", box.rectTransform);
            SetRef(shop, "popupIcon", popupIcon);
            SetRef(shop, "popupName", popupName);
            SetRef(shop, "watchAdsButton", watchAds);
            SetRef(shop, "popupCloseButton", popupClose);
            SetRef(shop, "popupDimButton", popupDim);

            // Saved hidden; ShopOverlay shows them.
            popupRect.gameObject.SetActive(false);
            shopRect.gameObject.SetActive(false);
            return shop;
        }

        // ---------------------------------------------------------------- leaderboard

        private static LeaderboardOverlay BuildLeaderboard(RectTransform canvas, Art art, SkinCatalog catalog)
        {
            RectTransform holder = NewRect(LeaderboardOverlayName, canvas);
            Stretch(holder);
            var leaderboard = holder.gameObject.AddComponent<LeaderboardOverlay>();

            RectTransform groupRect = NewRect("Leaderboard", holder);
            Stretch(groupRect);
            var group = groupRect.gameObject.AddComponent<CanvasGroup>();

            CanvasGroup background = NewScrollingBackground("BG_Leaderboard", groupRect, art.LeaderboardBackground);
            background.GetComponent<RawImage>().raycastTarget = true;

            RectTransform panel = NewRect("SlidingPanel", groupRect);
            Stretch(panel);

            // Board space: the frame picture's own pixels, scaled to the screen.
            Image board = NewImage("Board", panel, art.Board, 1f);
            Place(board, BoardPosition);
            board.rectTransform.localScale = new Vector3(BoardScale, BoardScale, 1f);
            RectTransform boardRect = board.rectTransform;
            float boardBottom = BoardPosition.y - art.Board.rect.height * 0.5f * BoardScale;

            Button back = NewButton("BackButton", panel, art.Back, 1f, Vector2.zero);
            SetAnchored(back.image.rectTransform, TopLeft, TopLeft, new Vector2(40f, -35f), back.image.rectTransform.sizeDelta);

            Button start = NewButton("LetsShakeButton", panel, art.LetsShake, 1f, Vector2.zero);
            float startHalfHeight = start.image.rectTransform.sizeDelta.y * 0.5f;
            Place(start.image, new Vector2(0f, Mathf.Max(boardBottom - startHalfHeight - 6f, -540f + startHalfHeight + 20f)));

            var serialized = new SerializedObject(leaderboard);
            SerializedProperty podium = serialized.FindProperty("podium");
            podium.arraySize = PodiumX.Length;
            string[] places = { "First", "Second", "Third" };
            Sprite[] medals = { art.Gold, art.Silver, art.Bronze };
            for (int i = 0; i < PodiumX.Length; i++)
            {
                float x = PodiumX[i];
                Image icon = NewImage("Podium" + places[i] + "Icon", boardRect, art.ShopIcon, 1f);
                SetAnchored(icon.rectTransform, Center, Center, new Vector2(x, PodiumIconY), new Vector2(120f, 92f));
                Image medal = NewImage("Podium" + places[i] + "Medal", boardRect, medals[i], 1.4f);
                SetAnchored(medal.rectTransform, Center, Center, new Vector2(x, PodiumMedalY), medal.rectTransform.sizeDelta);
                TMP_Text name = NewText("Podium" + places[i] + "Name", boardRect, "Name", 20f, TextDark, TextAlignmentOptions.Center, Center, Center, new Vector2(x, PodiumNameY), new Vector2(160f, 26f));
                name.enableAutoSizing = true;
                name.fontSizeMin = 12f;
                name.fontSizeMax = 20f;
                TMP_Text time = NewText("Podium" + places[i] + "Time", boardRect, "00:00", 22f, TextDark, TextAlignmentOptions.Center, Center, Center, new Vector2(x, PodiumTimeY), new Vector2(136f, 30f));

                SerializedProperty slot = podium.GetArrayElementAtIndex(i);
                slot.FindPropertyRelative("icon").objectReferenceValue = icon;
                slot.FindPropertyRelative("medal").objectReferenceValue = medal;
                slot.FindPropertyRelative("name").objectReferenceValue = name;
                slot.FindPropertyRelative("time").objectReferenceValue = time;
            }

            Vector2 rowSize = art.OtherRow.rect.size;
            RectTransform list = NewRect("List", boardRect);
            SetAnchored(list, Center, new Vector2(0.5f, 1f), new Vector2(0f, ListTopY), new Vector2(rowSize.x, 320f));
            var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 12f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            SerializedProperty rows = serialized.FindProperty("rows");
            rows.arraySize = 3;
            for (int i = 0; i < rows.arraySize; i++)
            {
                BuildRow(rows.GetArrayElementAtIndex(i), list, art, rowSize, i);
            }

            TMP_Text dots = NewText("Ellipsis", list, "...", 44f, TextDark, TextAlignmentOptions.Center, Center, Center, Vector2.zero, new Vector2(rowSize.x, 30f));
            dots.characterSpacing = 20f;

            // Mr. Da plays with his own face instead of a skin.
            serialized.FindProperty("mrDaIcon").objectReferenceValue = art.MrDaFace;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            SetRef(leaderboard, "catalog", catalog);
            SetRef(leaderboard, "group", group);
            SetRef(leaderboard, "slidingPanel", panel);
            SetRef(leaderboard, "backButton", back);
            SetRef(leaderboard, "startButton", start);
            SetRef(leaderboard, "ellipsis", dots.gameObject);
            SetRef(leaderboard, "otherRowSprite", art.OtherRow);
            SetRef(leaderboard, "myRowSprite", art.MyRow);

            groupRect.gameObject.SetActive(false);
            return leaderboard;
        }

        /// <summary>One list row at the frame picture's own size: rank, icon, name (+ subtitle), time.</summary>
        private static void BuildRow(SerializedProperty row, RectTransform list, Art art, Vector2 rowSize, int index)
        {
            Image frame = NewImage("Row" + (index + 4), list, art.OtherRow, 1f);
            frame.preserveAspect = false;
            frame.rectTransform.sizeDelta = rowSize;
            RectTransform rect = frame.rectTransform;
            float left = -rowSize.x * 0.5f;

            TMP_Text rank = NewText("Rank", rect, "4", 30f, TextDark, TextAlignmentOptions.Center, Center, Center, new Vector2(left + 30f, 0f), new Vector2(48f, 44f));
            rank.enableAutoSizing = true;
            rank.fontSizeMin = 14f;
            rank.fontSizeMax = 30f;

            Image icon = NewImage("Icon", rect, art.ShopIcon, 1f);
            SetAnchored(icon.rectTransform, Center, Center, new Vector2(left + 82f, 0f), new Vector2(50f, 46f));

            TMP_Text name = NewText("Name", rect, "Name", 22f, TextDark, TextAlignmentOptions.MidlineLeft, Center, new Vector2(0f, 0.5f), new Vector2(left + 122f, 0f), new Vector2(290f, 30f));
            TMP_Text subtitle = NewText("Subtitle", rect, "No runs yet", 17f, TextGrey, TextAlignmentOptions.MidlineLeft, Center, new Vector2(0f, 0.5f), new Vector2(left + 122f, -13f), new Vector2(290f, 22f));
            subtitle.gameObject.SetActive(false);
            TMP_Text time = NewText("Time", rect, "00:00", 26f, TextDark, TextAlignmentOptions.MidlineRight, Center, new Vector2(1f, 0.5f), new Vector2(-left - 30f, 0f), new Vector2(150f, 40f));

            row.FindPropertyRelative("root").objectReferenceValue = frame.gameObject;
            row.FindPropertyRelative("frame").objectReferenceValue = frame;
            row.FindPropertyRelative("rank").objectReferenceValue = rank;
            row.FindPropertyRelative("icon").objectReferenceValue = icon;
            row.FindPropertyRelative("name").objectReferenceValue = name;
            row.FindPropertyRelative("subtitle").objectReferenceValue = subtitle;
            row.FindPropertyRelative("time").objectReferenceValue = time;
        }

        // ---------------------------------------------------------------- skins

        /// <summary>Creates the catalog if needed and (re)writes its skins: shelf order, names, icons, ads needed.</summary>
        private static SkinCatalog EnsureCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SkinCatalog>(CatalogPath);
            bool created = catalog == null;
            if (created)
            {
                catalog = ScriptableObject.CreateInstance<SkinCatalog>();
            }

            var serialized = new SerializedObject(catalog);
            SerializedProperty skins = serialized.FindProperty("skins");
            skins.arraySize = SkinOrder.Length;
            for (int i = 0; i < SkinOrder.Length; i++)
            {
                string id = SkinOrder[i];
                SerializedProperty skin = skins.GetArrayElementAtIndex(i);
                skin.FindPropertyRelative("id").stringValue = id;
                skin.FindPropertyRelative("displayName").stringValue = SkinNames[i];
                skin.FindPropertyRelative("icon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(SkinFolder + "/" + id + "/Icon.png");
                skin.FindPropertyRelative("unlockedByDefault").boolValue = i == 0;
                skin.FindPropertyRelative("adsRequired").intValue = SkinAdsRequired[i];
                AssignSkinArtwork(skin, SkinFolder + "/" + id);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (created)
            {
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            else
            {
                EditorUtility.SetDirty(catalog);
            }

            AssetDatabase.SaveAssets();
            return catalog;
        }

        /// <summary>
        /// Fills the in-game pictures of a skin from its folder by file name (body / horse-body, body-noarm, open-hand,
        /// straight-arm, elbow-arm, handshake) and gives them the import settings of the default cat: point filter, no
        /// compression, and Repeat on the straight arm (it tiles along the arm).
        /// </summary>
        private static void AssignSkinArtwork(SerializedProperty skin, string folder)
        {
            string[] fields = { "body", "bodyNoArm", "hand", "straightArm", "elbowArm", "handshake" };
            foreach (string field in fields)
            {
                skin.FindPropertyRelative(field).objectReferenceValue = null;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                string field = name.Contains("noarm") ? "bodyNoArm"
                    : name.Contains("body") ? "body"
                    : name.StartsWith("open-hand") ? "hand"
                    : name.StartsWith("straight-arm") ? "straightArm"
                    : name.StartsWith("elbow-arm") ? "elbowArm"
                    : name.StartsWith("handshake") ? "handshake"
                    : null;
                if (field == null)
                {
                    continue;
                }

                PrepareSkinTexture(path, field == "straightArm");
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is Sprite sprite)
                    {
                        skin.FindPropertyRelative(field).objectReferenceValue = sprite;
                        break;
                    }
                }
            }
        }

        private static void PrepareSkinTexture(string path, bool repeat)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            TextureWrapMode wrap = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            if (importer.filterMode == FilterMode.Point && importer.textureCompression == TextureImporterCompression.Uncompressed && importer.wrapMode == wrap)
            {
                return;
            }

            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = wrap;
            importer.SaveAndReimport();
        }

        // ---------------------------------------------------------------- helpers

        private static bool TryLoadArt(out Art art)
        {
            var missing = new StringBuilder();
            art = new Art
            {
                ShopBackground = LoadTexture(ArtFolder, "BG 2", missing),
                LeaderboardBackground = LoadTexture(ArtFolder, "BG 3", missing),
                ShopIcon = Load(ShopFolder, "IconShop", missing),
                Shelf = Load(ShopFolder, "Kệ", missing),
                MrDa = Load(ShopFolder, "Mr Da", missing),
                MrDaFace = Load(ArtFolder + "/Bác Đa", "image 385", missing),
                Bubble = Load(ShopFolder, "mess", missing),
                Register = Load(ShopFolder, "Decor", missing),
                SlotHighlight = Load(ShopFolder, "skin đang trỏ vào", missing),
                AdIcon = Load(ShopFolder, "icon ads", missing),
                PopupBox = Load(ShopFolder + "/Popup", "Khung Popup", missing),
                WatchAds = Load(ShopFolder + "/Popup", "Button watch Ads", missing),
                Board = Load(LeaderboardFolder, "Khung", missing),
                OtherRow = Load(LeaderboardFolder, "Các vị trí khác", missing),
                MyRow = Load(LeaderboardFolder, "Vị trí của tôi", missing),
                Gold = Load(LeaderboardFolder, "Vàng", missing),
                Silver = Load(LeaderboardFolder, "Bạc", missing),
                Bronze = Load(LeaderboardFolder, "Đồng", missing),
                Back = Load(ArtFolder, "Back", missing),
                Close = Load(ArtFolder, "Exit", missing),
                LetsShake = Load(ArtFolder, "Let_s Shake!", missing)
            };

            if (missing.Length > 0)
            {
                Debug.LogError("SetupShopLeaderboardEditor: missing art, nothing was built:\n" + missing);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Path of the picture named <paramref name="file"/> in <paramref name="folder"/>. Names are compared in Unicode
        /// normal form C, because Vietnamese file and folder names may be stored decomposed on disk.
        /// </summary>
        private static string FindPicture(string folder, string file)
        {
            string wantedFolder = folder.Normalize(NormalizationForm.FormC);
            string wanted = file.Normalize(NormalizationForm.FormC);
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string directory = Path.GetDirectoryName(path).Replace('\\', '/').Normalize(NormalizationForm.FormC);
                if (directory == wantedFolder && Path.GetFileNameWithoutExtension(path).Normalize(NormalizationForm.FormC) == wanted)
                {
                    return path;
                }
            }

            return null;
        }

        /// <summary>First sprite of the picture <paramref name="file"/> in <paramref name="folder"/>.</summary>
        private static Sprite Load(string folder, string file, StringBuilder missing)
        {
            string path = FindPicture(folder, file);
            if (path != null)
            {
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is Sprite sprite)
                    {
                        return sprite;
                    }
                }
            }

            missing.AppendLine(folder + "/" + file + ".png");
            return null;
        }

        private static Texture2D LoadTexture(string folder, string file, StringBuilder missing)
        {
            string path = FindPicture(folder, file);
            Texture2D texture = path != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(path) : null;
            if (texture == null)
            {
                missing.AppendLine(folder + "/" + file + ".png");
            }

            return texture;
        }

        /// <summary>Gentle scale "breathing" (no fade); the phase keeps neighbours from moving in lockstep.</summary>
        private static void AddPulse(GameObject target, float phase)
        {
            var juice = target.AddComponent<UIJuiceAnimation>();
            var serialized = new SerializedObject(juice);
            serialized.FindProperty("mode").enumValueIndex = (int)JuiceMode.Pulse;
            serialized.FindProperty("phaseOffset").floatValue = phase;
            serialized.FindProperty("pulseMinScale").floatValue = 0.93f;
            serialized.FindProperty("pulseMaxScale").floatValue = 1.07f;
            serialized.FindProperty("pulseFrequency").floatValue = 0.9f;
            serialized.FindProperty("pulseAlpha").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Place(Image image, Vector2 position)
        {
            SetAnchored(image.rectTransform, Center, Center, position, image.rectTransform.sizeDelta);
        }

        /// <summary>Full-screen dark area; it swallows taps, and its button can close what lies on top of it.</summary>
        private static Button NewDim(string name, RectTransform parent, float alpha)
        {
            RectTransform rect = NewRect(name, parent);
            Stretch(rect);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, alpha);
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            return button;
        }

        private static void DestroyChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            while (child != null)
            {
                Object.DestroyImmediate(child.gameObject);
                child = parent.Find(name);
            }
        }
    }
}
#endif
