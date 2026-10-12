#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Catshakes.EditorTools
{
    /// <summary>
    /// One-click builder for the menu scene (Title, Main Menu, Level Select, Settings popup).
    /// Run it from  Catshakes/Auto Setup Menu UI.  It
    ///  - sets the background textures to Wrap Mode = Repeat,
    ///  - creates the LevelButton prefab,
    ///  - makes sure sample levels exist and builds Assets/MainGame/Scenes/MainMenuScene.unity,
    ///  - wires every serialized field and Button.onClick to <see cref="MenuUIManager"/>,
    ///  - puts MainMenuScene first in Build Settings (followed by GameplayScene).
    /// Running it again rebuilds the scene and the prefab from scratch.
    ///
    /// The game is landscape (1920 x 1080, match height): the main-menu buttons sit in one row at the bottom and
    /// the level grid has 10 columns x 3 rows, like the concept art. It also sets the project to landscape-only.
    /// </summary>
    public static class SetupMenuUIEditor
    {
        private const string SpriteFolder = "Assets/MainGame/Sprites/Assetgame/";
        private const string UiPrefabFolder = "Assets/Prefabs/UI";
        private const string LevelButtonPrefabPath = UiPrefabFolder + "/LevelButtonPrefab.prefab";
        private const string MenuScenePath = "Assets/MainGame/Scenes/MainMenuScene.unity";
        private const string GameplayScenePath = "Assets/MainGame/Scenes/GameplayScene.unity";
        private const string GameplaySceneName = "GameplayScene";
        private const string MenuSceneName = "MainMenuScene";

        private const float ButtonScale = 1.1f;

        private static readonly Color GreenBackdrop = new Color32(98, 224, 108, 255);
        private static readonly Color TapTextColor = new Color32(128, 88, 52, 255);

        private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        private static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        private static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);
        private static readonly Vector2 BottomCenter = new Vector2(0.5f, 0f);
        private static readonly Vector2 TopRight = new Vector2(1f, 1f);
        private static readonly Vector2 BottomRight = new Vector2(1f, 0f);

        private sealed class Art
        {
            public Texture2D TitleLogo;
            public Texture2D SelectLevelTitle;
            public Texture2D BgGreen;
            public Texture2D BgYellow;
            public Sprite Mascot;
            public Sprite Play;
            public Sprite SpeedRunUnlocked;
            public Sprite SpeedRunLocked;
            public Sprite Settings;
            public Sprite PopupBox;
            public Sprite SliderTrack;
            public Sprite SliderFill;
            public Sprite SliderHandle;
            public Sprite Close;
            public Sprite Back;
            public Sprite LetsShake;
            public Sprite LevelUnlocked;
            public Sprite LevelSelected;
            public Sprite LevelLocked;
        }

        // ---------------------------------------------------------------- menu commands

        [MenuItem("Catshakes/Auto Setup Menu UI")]
        public static void SetupMenuUi()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("SetupMenuUIEditor: leave Play Mode first.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            ApplyLandscapeProjectSettings();
            PrepareBackgroundTextures();

            if (!TryLoadArt(out Art art))
            {
                return;
            }

            EnsureFolder("Assets/Prefabs");
            EnsureFolder(UiPrefabFolder);
            EnsureFolder("Assets/MainGame/Scenes");

            BuildLevelButtonPrefab(art);
            AssetDatabase.ImportAsset(LevelButtonPrefabPath, ImportAssetOptions.ForceSynchronousImport);
            List<string> levelPaths = CollectLevelPaths();

            BuildScene(art, levelPaths);
            UpdateBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("SetupMenuUIEditor: done. Scene: " + MenuScenePath + ", levels in catalog: " + levelPaths.Count + ", level button prefab: " + LevelButtonPrefabPath);
        }

        [MenuItem("Catshakes/Debug/Unlock All Levels And Speed Run")]
        public static void UnlockEverything()
        {
            PlayerPrefs.SetInt("Catshakes.UnlockedLevels", 9999);
            PlayerPrefs.SetInt(GameSceneManager.AllLevelsClearedKey, 1);
            PlayerPrefs.Save();
            Debug.Log("Catshakes: all levels and Speed Run unlocked (PlayerPrefs).");
        }

        [MenuItem("Catshakes/Debug/Reset Saved Progress")]
        public static void ResetProgress()
        {
            PlayerPrefs.DeleteKey("Catshakes.UnlockedLevels");
            PlayerPrefs.DeleteKey("Catshakes.BestSpeedrunSeconds");
            PlayerPrefs.DeleteKey(GameSceneManager.AllLevelsClearedKey);
            PlayerPrefs.Save();
            Debug.Log("Catshakes: saved progress cleared.");
        }

        /// <summary>Locks the player to the two landscape orientations (Android and iOS).</summary>
        public static void ApplyLandscapeProjectSettings()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
        }

        // ---------------------------------------------------------------- assets

        private static void PrepareBackgroundTextures()
        {
            string[] names = { "BG 1", "BG 2", "BG 3" };
            foreach (string name in names)
            {
                string path = SpriteFolder + name + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                if (importer.wrapMode == TextureWrapMode.Repeat && !importer.mipmapEnabled)
                {
                    continue;
                }

                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
        }

        private static bool TryLoadArt(out Art art)
        {
            var missing = new List<string>();
            art = new Art
            {
                TitleLogo = LoadTexture("Title Game", missing),
                SelectLevelTitle = LoadTexture("Title 2", missing),
                BgGreen = LoadTexture("BG 1", missing),
                BgYellow = LoadTexture("BG 2", missing),
                Mascot = LoadSprite("Mascot", "Mascot_0", missing),
                Play = LoadSprite("Play", "Play_0", missing),
                SpeedRunUnlocked = LoadSprite("Speedrun Unlock", "Speedrun Unlock_0", missing),
                SpeedRunLocked = LoadSprite("Speedrun Lock", "Speedrun Lock_0", missing),
                Settings = LoadSprite("Settings (2)", "Settings (2)_0", missing),
                PopupBox = LoadSprite("Popup Settings", "Popup Settings_0", missing),
                SliderTrack = LoadSprite("Slide 1", "Slide 1_0", missing),
                SliderFill = LoadSprite("Slide 2", "Slide 2_0", missing),
                SliderHandle = LoadSprite("Dot", "Dot_0", missing),
                Close = LoadSprite("Exit", "Exit_0", missing),
                Back = LoadSprite("Back", "Back_0", missing),
                LetsShake = LoadSprite("Let_s Shake!", "Let_s Shake!_0", missing),
                LevelUnlocked = LoadSprite("Level Unlock", "Level Unlock_0", missing),
                LevelSelected = LoadSprite("Level Select", "Level Select_0", missing),
                LevelLocked = LoadSprite("Level Lock", "Level Lock_0", missing)
            };

            if (missing.Count > 0)
            {
                Debug.LogError("SetupMenuUIEditor: missing art, nothing was built:\n" + string.Join("\n", missing));
                return false;
            }

            return true;
        }

        private static Texture2D LoadTexture(string file, List<string> missing)
        {
            string path = SpriteFolder + file + ".png";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                missing.Add(path);
            }

            return texture;
        }

        private static Sprite LoadSprite(string file, string spriteName, List<string> missing)
        {
            string path = SpriteFolder + file + ".png";
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var sprite = asset as Sprite;
                if (sprite != null && sprite.name == spriteName)
                {
                    return sprite;
                }
            }

            missing.Add(path + " (sprite '" + spriteName + "')");
            return null;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            int slash = folder.LastIndexOf('/');
            string parent = folder.Substring(0, slash);
            string leaf = folder.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static List<string> CollectLevelPaths()
        {
            if (AssetDatabase.FindAssets("t:LevelDataSO").Length < 2)
            {
                EditorApplication.ExecuteMenuItem("Catshakes/Create Sample Levels");
            }

            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:LevelDataSO"))
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            // Level0_Tutorial sorts before Level_01 ('0' < '_').
            paths.Sort(string.CompareOrdinal);
            return paths;
        }

        private static void BuildLevelButtonPrefab(Art art)
        {
            var root = new GameObject("LevelButtonPrefab", typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = new Vector2(138f, 128f);

            Image face = NewImage("Face", rootRect, art.LevelUnlocked, 1f);
            face.raycastTarget = true;
            SetAnchored(face.rectTransform, Center, Center, Vector2.zero, face.rectTransform.sizeDelta);

            TMP_Text label = NewText("Label", face.rectTransform, "0", 56f, new Color32(139, 111, 71, 255), TextAlignmentOptions.Center, Center, Center, Vector2.zero, new Vector2(126f, 76f));

            var button = root.AddComponent<Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.None;
            root.AddComponent<UIButtonPress>();

            var view = root.AddComponent<LevelButtonView>();
            SetRef(view, "button", button);
            SetRef(view, "face", face);
            SetRef(view, "label", label);
            SetRef(view, "unlockedSprite", art.LevelUnlocked);
            SetRef(view, "selectedSprite", art.LevelSelected);
            SetRef(view, "lockedSprite", art.LevelLocked);
            SetFloat(view, "faceScale", 0.85f);
            SetFloat(view, "lockedLabelOffsetY", 17f);

            PrefabUtility.SaveAsPrefabAsset(root, LevelButtonPrefabPath);
            Object.DestroyImmediate(root);
        }

        // ---------------------------------------------------------------- scene

        private static void BuildScene(Art art, List<string> levelPaths)
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Load the prefab only now: creating a scene unloads unused assets, which would invalidate a reference taken earlier.
            var levelButtonPrefab = AssetDatabase.LoadAssetAtPath<LevelButtonView>(LevelButtonPrefabPath);
            if (levelButtonPrefab == null)
            {
                Debug.LogError("SetupMenuUIEditor: could not load " + LevelButtonPrefabPath);
                return;
            }

            BuildCamera();
            BuildSystems(levelPaths);
            BuildEventSystem();

            // ---- Canvas
            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            Transform root = canvasObject.transform;
            var manager = canvasObject.AddComponent<MenuUIManager>();

            // ---- Backgrounds
            RectTransform bgContainer = NewRect("BG_Container", root);
            Stretch(bgContainer);
            CanvasGroup greenBg = NewScrollingBackground("BG_Green", bgContainer, art.BgGreen);
            CanvasGroup yellowBg = NewScrollingBackground("BG_Yellow", bgContainer, art.BgYellow);

            // ---- Logo and mascot (shared by Title and Main Menu)
            RectTransform branding = NewRect("Branding", root);
            Stretch(branding);
            var brandingGroup = branding.gameObject.AddComponent<CanvasGroup>();

            RawImage logo = NewRawImage("Logo", branding, art.TitleLogo, 880f);
            SetAnchored(logo.rectTransform, Center, Center, new Vector2(0f, 335f), logo.rectTransform.sizeDelta);
            AddJuice(logo.gameObject, 0f);

            Image mascot = NewImage("Mascot", branding, art.Mascot, 1f);
            SetAnchored(mascot.rectTransform, Center, Center, new Vector2(0f, 0f), mascot.rectTransform.sizeDelta);
            AddJuice(mascot.gameObject, 1.1f);

            // ---- Title panel
            RectTransform titlePanelRect = NewRect("Title_Panel", root);
            Stretch(titlePanelRect);
            var titlePanel = titlePanelRect.gameObject.AddComponent<CanvasGroup>();

            TMP_Text tapText = NewText("TapToStartText", titlePanelRect, "Tap to Start Game", 56f, TapTextColor, TextAlignmentOptions.Center, Center, Center, new Vector2(0f, -420f), new Vector2(800f, 100f));
            var tapJuice = tapText.gameObject.AddComponent<UIJuiceAnimation>();
            SetEnum(tapJuice, "mode", (int)JuiceMode.Pulse);

            // Invisible full-screen button that receives the tap.
            var tapButtonObject = NewRect("TapButton", titlePanelRect);
            Stretch(tapButtonObject);
            var tapImage = tapButtonObject.gameObject.AddComponent<Image>();
            tapImage.color = new Color(1f, 1f, 1f, 0f);
            tapImage.raycastTarget = true;
            var tapButton = tapButtonObject.gameObject.AddComponent<Button>();
            tapButton.targetGraphic = tapImage;
            tapButton.transition = Selectable.Transition.None;

            // ---- Main menu panel
            RectTransform mainPanelRect = NewRect("MainMenu_Panel", root);
            Stretch(mainPanelRect);
            var mainPanel = mainPanelRect.gameObject.AddComponent<CanvasGroup>();

            Button playButton = NewButton("PlayButton", mainPanelRect, art.Play, ButtonScale, new Vector2(-560f, -405f));
            Button speedRunButton = NewButton("SpeedRunButton", mainPanelRect, art.SpeedRunLocked, ButtonScale, new Vector2(0f, -410f));
            Button settingsButton = NewButton("SettingsButton", mainPanelRect, art.Settings, ButtonScale, new Vector2(560f, -405f));

            // ---- Level select panel
            RectTransform levelPanelRect = NewRect("LevelSelect_Panel", root);
            Stretch(levelPanelRect);
            var levelPanel = levelPanelRect.gameObject.AddComponent<CanvasGroup>();

            Button backButton = NewButton("BackButton", levelPanelRect, art.Back, 1f, Vector2.zero);
            SetAnchored(backButton.image.rectTransform, TopLeft, TopLeft, new Vector2(40f, -35f), backButton.image.rectTransform.sizeDelta);

            RawImage selectTitle = NewRawImage("SelectLevelTitle", levelPanelRect, art.SelectLevelTitle, 700f);
            SetAnchored(selectTitle.rectTransform, TopCenter, TopCenter, new Vector2(0f, -45f), selectTitle.rectTransform.sizeDelta);

            RectTransform gridRect = NewRect("LevelGrid", levelPanelRect);
            SetAnchored(gridRect, Center, Center, new Vector2(0f, -60f), new Vector2(1596f, 504f));
            var layout = gridRect.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(138f, 128f);
            layout.spacing = new Vector2(24f, 60f);
            layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            layout.startAxis = GridLayoutGroup.Axis.Horizontal;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 10;
            var grid = gridRect.gameObject.AddComponent<LevelSelectGrid>();
            SetRef(grid, "contentRoot", gridRect);
            SetRef(grid, "levelButtonPrefab", levelButtonPrefab);
            SetInt(grid, "slotCount", 30);

            Button letsShakeButton = NewButton("LetsShakeButton", levelPanelRect, art.LetsShake, ButtonScale, Vector2.zero);
            SetAnchored(letsShakeButton.image.rectTransform, BottomRight, BottomRight, new Vector2(-60f, 45f), letsShakeButton.image.rectTransform.sizeDelta);

            // ---- Settings popup
            RectTransform settingsRect = NewRect("Settings_Popup", root);
            Stretch(settingsRect);
            var settingsGroup = settingsRect.gameObject.AddComponent<CanvasGroup>();
            var settingsPopup = settingsRect.gameObject.AddComponent<SettingsPopup>();

            RectTransform dim = NewRect("DimOverlay", settingsRect);
            Stretch(dim);
            var dimImage = dim.gameObject.AddComponent<Image>();
            dimImage.color = new Color(0f, 0f, 0f, 0.6f);
            dimImage.raycastTarget = true;
            var dimButton = dim.gameObject.AddComponent<Button>();
            dimButton.targetGraphic = dimImage;
            dimButton.transition = Selectable.Transition.None;

            const float popupScale = 1.1f;
            Image box = NewImage("PopupBox", settingsRect, art.PopupBox, popupScale);
            box.raycastTarget = true; // swallow taps so only the dim area closes the popup
            SetAnchored(box.rectTransform, Center, Center, Vector2.zero, box.rectTransform.sizeDelta);
            Vector2 boxSize = box.rectTransform.sizeDelta;

            Button closeButton = NewButton("CloseButton", box.rectTransform, art.Close, 1f, Vector2.zero);
            SetAnchored(closeButton.image.rectTransform, TopRight, Center, new Vector2(-boxSize.x * 0.083f, -boxSize.y * 0.117f), closeButton.image.rectTransform.sizeDelta);

            Slider musicSlider = NewSlider("MusicSlider", box.rectTransform, art, new Vector2(0f, -boxSize.y * 0.51f), boxSize.x * 0.805f);
            Slider sfxSlider = NewSlider("SfxSlider", box.rectTransform, art, new Vector2(0f, -boxSize.y * 0.83f), boxSize.x * 0.805f);
            SetRef(settingsPopup, "musicSlider", musicSlider);
            SetRef(settingsPopup, "sfxSlider", sfxSlider);

            // ---- MenuUIManager references
            SetRef(manager, "titlePanel", titlePanel);
            SetRef(manager, "mainMenuPanel", mainPanel);
            SetRef(manager, "levelSelectPanel", levelPanel);
            SetRef(manager, "brandingGroup", brandingGroup);
            SetRef(manager, "greenBackground", greenBg);
            SetRef(manager, "yellowBackground", yellowBg);
            SetRef(manager, "settingsGroup", settingsGroup);
            SetRef(manager, "settingsBox", box.rectTransform);
            SetRef(manager, "speedRunImage", speedRunButton.image);
            SetRef(manager, "speedRunLockedSprite", art.SpeedRunLocked);
            SetRef(manager, "speedRunUnlockedSprite", art.SpeedRunUnlocked);
            SetRef(manager, "levelGrid", grid);

            // ---- onClick wiring
            UnityEventTools.AddPersistentListener(tapButton.onClick, manager.OnTitleTapped);
            UnityEventTools.AddPersistentListener(playButton.onClick, manager.OnPlayClicked);
            UnityEventTools.AddPersistentListener(speedRunButton.onClick, manager.OnSpeedRunClicked);
            UnityEventTools.AddPersistentListener(settingsButton.onClick, manager.OnSettingsClicked);
            UnityEventTools.AddPersistentListener(closeButton.onClick, manager.OnSettingsCloseClicked);
            UnityEventTools.AddPersistentListener(dimButton.onClick, manager.OnSettingsCloseClicked);
            UnityEventTools.AddPersistentListener(backButton.onClick, manager.OnBackClicked);
            UnityEventTools.AddPersistentListener(letsShakeButton.onClick, manager.OnLetsShakeClicked);

            // Scene is saved showing the Title screen only; MenuUIManager restores the right state at runtime.
            levelPanelRect.gameObject.SetActive(false);
            mainPanelRect.gameObject.SetActive(false);
            yellowBg.gameObject.SetActive(false);
            settingsRect.gameObject.SetActive(false);

            EditorSceneManager.SaveScene(scene, MenuScenePath);
        }

        private static void BuildCamera()
        {
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = GreenBackdrop;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        }

        private static void BuildSystems(List<string> levelPaths)
        {
            // Both managers are scene roots: DontDestroyOnLoad only works on root objects.
            // Scene flow, progress, speedrun timer. Persists across scene loads.
            var flowObject = new GameObject("GameSceneManager");
            var flow = flowObject.AddComponent<GameSceneManager>();
            SetString(flow, "titleSceneName", MenuSceneName);
            SetString(flow, "mainMenuSceneName", MenuSceneName);
            SetString(flow, "levelSelectSceneName", MenuSceneName);
            SetString(flow, "gameplaySceneName", GameplaySceneName);

            var serialized = new SerializedObject(flow);
            SerializedProperty levelsProperty = serialized.FindProperty("levels");
            levelsProperty.arraySize = levelPaths.Count;
            for (int i = 0; i < levelPaths.Count; i++)
            {
                // Loaded here (after NewScene) because creating a scene unloads unused assets.
                levelsProperty.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<LevelDataSO>(levelPaths[i]);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Black full-screen fade used for scene transitions (child of the persistent manager).
            var fadeObject = new GameObject("Fade", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(Image));
            fadeObject.transform.SetParent(flowObject.transform, false);
            var fadeCanvas = fadeObject.GetComponent<Canvas>();
            fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            fadeCanvas.sortingOrder = 999;
            var fadeImage = fadeObject.GetComponent<Image>();
            fadeImage.color = Color.black;
            fadeImage.raycastTarget = true;
            var fadeGroup = fadeObject.GetComponent<CanvasGroup>();
            fadeGroup.alpha = 0f;
            fadeGroup.blocksRaycasts = false;
            SetRef(flow, "fadeGroup", fadeGroup);

            // Music and SFX volume. Persists across scene loads.
            var audioObject = new GameObject("AudioManager");
            var music = audioObject.AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.loop = true;
            var sfx = audioObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            var audio = audioObject.AddComponent<AudioManager>();
            SetRef(audio, "musicSource", music);
            SetRef(audio, "sfxSource", sfx);
        }

        private static void BuildEventSystem()
        {
            var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            var module = eventSystemObject.AddComponent<InputSystemUIInputModule>();
            try
            {
                module.AssignDefaultActions();
            }
            catch (System.InvalidOperationException)
            {
                // The built-in default actions can be unloaded after a scene switch inside the same Editor session;
                // fall back to the project-wide actions asset.
                if (UnityEngine.InputSystem.InputSystem.actions != null)
                {
                    module.actionsAsset = UnityEngine.InputSystem.InputSystem.actions;
                }
                else
                {
                    Debug.LogWarning("Could not assign default UI actions to the EventSystem. Assign them on the Input System UI Input Module.");
                }
            }
#else
            eventSystemObject.AddComponent<StandaloneInputModule>();
#endif
        }

        private static void UpdateBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == MenuScenePath || s.path == GameplayScenePath);

            if (System.IO.File.Exists(GameplayScenePath))
            {
                scenes.Insert(0, new EditorBuildSettingsScene(GameplayScenePath, true));
            }
            else
            {
                Debug.LogWarning("SetupMenuUIEditor: " + GameplayScenePath + " not found. Run Catshakes/Auto Setup Gameplay Scene and add it to Build Settings.");
            }

            scenes.Insert(0, new EditorBuildSettingsScene(MenuScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ---------------------------------------------------------------- widget builders

        private static CanvasGroup NewScrollingBackground(string name, Transform parent, Texture2D texture)
        {
            RectTransform rect = NewRect(name, parent);
            Stretch(rect);
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.texture = texture;
            raw.raycastTarget = false;
            rect.gameObject.AddComponent<DiagonalScrollingBackground>();
            return rect.gameObject.AddComponent<CanvasGroup>();
        }

        private static void AddJuice(GameObject target, float phase)
        {
            var juice = target.AddComponent<UIJuiceAnimation>();
            SetEnum(juice, "mode", (int)JuiceMode.Idle);
            SetFloat(juice, "phaseOffset", phase);
        }

        private static Slider NewSlider(string name, RectTransform parent, Art art, Vector2 anchoredPositionFromTop, float trackWidth)
        {
            float k = trackWidth / art.SliderTrack.rect.width;       // sprite pixels -> canvas units
            float trackHeight = art.SliderTrack.rect.height * k;
            float inset = (art.SliderTrack.rect.width - art.SliderFill.rect.width) * 0.5f * k;
            float fillHeight = art.SliderFill.rect.height * k;
            Vector2 handleSize = art.SliderHandle.rect.size * k * 0.95f;

            RectTransform rootRect = NewRect(name, parent);
            SetAnchored(rootRect, TopCenter, Center, anchoredPositionFromTop, new Vector2(trackWidth, trackHeight));

            Image background = NewImage("Background", rootRect, art.SliderTrack, k);
            Stretch(background.rectTransform);
            background.preserveAspect = false;

            RectTransform fillArea = NewRect("Fill Area", rootRect);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.pivot = Center;
            fillArea.offsetMin = new Vector2(inset, -fillHeight * 0.5f);
            fillArea.offsetMax = new Vector2(-inset, fillHeight * 0.5f);

            Image fill = NewImage("Fill", fillArea, art.SliderFill, 1f);
            fill.preserveAspect = false;
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;

            RectTransform handleArea = NewRect("Handle Slide Area", rootRect);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(inset, 0f);
            handleArea.offsetMax = new Vector2(-inset, 0f);

            Image handle = NewImage("Handle", handleArea, art.SliderHandle, 1f);
            handle.raycastTarget = true;
            handle.rectTransform.sizeDelta = handleSize;

            var slider = rootRect.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.transition = Selectable.Transition.None;
            slider.value = 0.7f;
            return slider;
        }

        private static Button NewButton(string name, Transform parent, Sprite sprite, float scale, Vector2 anchoredPosition)
        {
            Image image = NewImage(name, parent, sprite, scale);
            image.raycastTarget = true;
            SetAnchored(image.rectTransform, Center, Center, anchoredPosition, image.rectTransform.sizeDelta);

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            image.gameObject.AddComponent<UIButtonPress>();
            return button;
        }

        private static Image NewImage(string name, Transform parent, Sprite sprite, float scale)
        {
            RectTransform rect = NewRect(name, parent);
            rect.sizeDelta = sprite.rect.size * scale;
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static RawImage NewRawImage(string name, Transform parent, Texture2D texture, float width)
        {
            RectTransform rect = NewRect(name, parent);
            rect.sizeDelta = new Vector2(width, width * texture.height / texture.width);
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.texture = texture;
            raw.raycastTarget = false;
            return raw;
        }

        private static TMP_Text NewText(string name, Transform parent, string text, float fontSize, Color color, TextAlignmentOptions alignment, Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rect = NewRect(name, parent);
            SetAnchored(rect, anchor, pivot, anchoredPosition, size);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = FontStyles.Bold;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            return label;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = Center;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetAnchored(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
        }

        // ---------------------------------------------------------------- serialized field helpers

        private static SerializedProperty Find(SerializedObject serialized, string field)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogError("SetupMenuUIEditor: field '" + field + "' not found on " + serialized.targetObject.GetType().Name);
            }

            return property;
        }

        private static void SetRef(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = Find(serialized, field);
            if (property != null)
            {
                property.objectReferenceValue = value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void SetFloat(Object target, string field, float value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = Find(serialized, field);
            if (property != null)
            {
                property.floatValue = value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void SetInt(Object target, string field, int value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = Find(serialized, field);
            if (property != null)
            {
                property.intValue = value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void SetString(Object target, string field, string value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = Find(serialized, field);
            if (property != null)
            {
                property.stringValue = value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void SetEnum(Object target, string field, int index)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = Find(serialized, field);
            if (property != null)
            {
                property.enumValueIndex = index;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
#endif
