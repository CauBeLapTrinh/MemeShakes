#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Catshakes.EditorTools
{
    /// <summary>
    /// One-click setup of the playable gameplay scene (menu: Catshakes > Auto Setup Gameplay Scene).
    /// Builds prefabs from the sprites in Assets/MainGame/Sprites/Assetgame, creates the Level0_Tutorial asset,
    /// creates GameplayScene with camera, managers, cats and HUD, and wires every serialized reference.
    /// Safe to run again: prefabs, the level asset and the scene are overwritten in place.
    /// </summary>
    public static class AutoSceneSetup
    {
        private const string SpriteFolder = "Assets/MainGame/Sprites/Assetgame/";
        private const string PrefabFolder = "Assets/MainGame/Prefabs";
        private const string MaterialFolder = "Assets/MainGame/Prefabs/Materials";
        private const string LevelFolder = "Assets/MainGame/Levels";
        private const string SceneFolder = "Assets/MainGame/Scenes";
        private const string ScenePath = SceneFolder + "/GameplayScene.unity";
        private const string LevelPath = LevelFolder + "/Level0_Tutorial.asset";

        // The art cells are 172 px wide at 100 pixels per unit. Scaling every prefab by this factor makes one cell exactly 1 world unit.
        private const float SpriteScale = 100f / 172f;
        private const float CellSize = 1f;

        private static readonly Color BackgroundColor = new Color32(139, 150, 118, 255);
        private static readonly Color WallColor = new Color32(33, 42, 34, 255);
        private static readonly Color ArmColor = new Color32(27, 30, 30, 255);
        private static readonly Color CatAAccent = new Color32(255, 170, 60, 255);
        private static readonly Color CatBAccent = new Color32(90, 200, 255, 255);
        private static readonly Color PanelColor = new Color32(248, 232, 200, 255);
        private static readonly Color DarkText = new Color32(43, 30, 18, 255);
        private static readonly Color LightText = new Color32(255, 241, 201, 255);

        private sealed class Art
        {
            public Sprite Floor;
            public Sprite Barrel;
            public Sprite Hand;
            public Sprite Cat;
            public Sprite RailTop;
            public Sprite RailBottom;
            public Sprite Bar;
            public Sprite Switch1Off;
            public Sprite Switch1On;
            public Sprite Switch2Off;
            public Sprite Switch2On;
            public Sprite Back;
            public Sprite Reset;
            public Sprite Exit;
            public Sprite LevelLabel;
        }

        private sealed class PrefabSet
        {
            public GameObject Tile;
            public GameObject Wall;
            public Barrel Barrel;
            public SwitchEntity Switch1;
            public SwitchEntity Switch2;
            public DoorEntity Door;
            public GameObject CatA;
            public GameObject CatB;
        }

        [MenuItem("Catshakes/Auto Setup Gameplay Scene")]
        public static void SetupGameplayScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    Debug.LogError("AutoSceneSetup: save or discard your open scene first (it has unsaved changes), then run the setup again.");
                    return;
                }
            }

            if (TMP_Settings.instance == null)
            {
                Debug.LogError("AutoSceneSetup: TextMeshPro Essential Resources are missing. Run Window > TextMeshPro > Import TMP Essential Resources first.");
                return;
            }

            if (!TryLoadArt(out Art art))
            {
                return;
            }

            EnsureFolder(PrefabFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(LevelFolder);
            EnsureFolder(SceneFolder);

            Material unlit = LoadOrCreateMaterial("SpriteUnlit", null);
            Material armLineMaterial = LoadOrCreateMaterial("ArmLine", unlit);

            // Work in a fresh empty scene so the temporary objects never touch the user's open scene.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            PrefabSet prefabs = BuildPrefabs(art, unlit, armLineMaterial);
            LevelDataSO level = CreateLevel();
            if (level == null)
            {
                return;
            }

            BuildScene(art, prefabs, level);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("AutoSceneSetup: done. Scene: " + ScenePath + ", level: " + LevelPath + ", prefabs: " + PrefabFolder);
        }

        // ---------------------------------------------------------------- assets

        private static bool TryLoadArt(out Art art)
        {
            var missing = new List<string>();
            art = new Art
            {
                Floor = LoadSprite("Block.png", "Block_0", missing),
                Barrel = LoadSprite("Barrel.png", "Barrel_0", missing),
                Hand = LoadSprite("Hand.png", "Hand_0", missing),
                Cat = LoadSprite("Skin 1.png", "Skin 1_0", missing),
                RailTop = LoadSprite("Door 1.png", "Door 1_0", missing),
                RailBottom = LoadSprite("Door 1.png", "Door 1_1", missing),
                Bar = LoadSprite("Door 2.png", "Door 2_0", missing),
                Switch1Off = LoadSprite("Switch_1 Inactive.png", "Switch_1 Inactive_0", missing),
                Switch1On = LoadSprite("Switch_1 active.png", "Switch_1 active_0", missing),
                Switch2Off = LoadSprite("Switch_2 inactive.png", "Switch_2 inactive_0", missing),
                Switch2On = LoadSprite("Switch_2 active.png", "Switch_2 active_0", missing),
                Back = LoadSprite("Back.png", "Back_0", missing),
                Reset = LoadSprite("Reset.png", "Reset_0", missing),
                Exit = LoadSprite("Exit.png", "Exit_0", missing),
                LevelLabel = LoadSprite("Level.png", "Level_0", missing)
            };

            if (missing.Count > 0)
            {
                Debug.LogError("AutoSceneSetup: missing sprites:\n" + string.Join("\n", missing));
                return false;
            }

            return true;
        }

        private static Sprite LoadSprite(string file, string spriteName, List<string> missing)
        {
            string path = SpriteFolder + file;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Sprite sprite && sprite.name == spriteName)
                {
                    return sprite;
                }
            }

            missing.Add(path + " [" + spriteName + "]");
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
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
        }

        /// <summary>Unlit sprite material: sprites stay visible without any Light2D in the scene.</summary>
        private static Material LoadOrCreateMaterial(string name, Material copyFrom)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            var material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // ---------------------------------------------------------------- prefabs

        private static PrefabSet BuildPrefabs(Art art, Material unlit, Material armLineMaterial)
        {
            var set = new PrefabSet();

            GameObject tile = MakeSprite("TilePrefab", art.Floor, 0, unlit, null);
            tile.transform.localScale = Vector3.one * SpriteScale;
            set.Tile = SavePrefab(tile, "TilePrefab");

            GameObject wall = MakeSprite("WallPrefab", art.Floor, 1, unlit, null);
            wall.GetComponent<SpriteRenderer>().color = WallColor;
            wall.transform.localScale = Vector3.one * SpriteScale;
            set.Wall = SavePrefab(wall, "WallPrefab");

            GameObject barrel = MakeSprite("BarrelPrefab", art.Barrel, 5, unlit, null);
            barrel.AddComponent<Barrel>();
            barrel.transform.localScale = Vector3.one * SpriteScale;
            set.Barrel = SavePrefab(barrel, "BarrelPrefab").GetComponent<Barrel>();

            set.Switch1 = SavePrefab(BuildSwitch("Switch1Prefab", art.Switch1Off, art.Switch1On, unlit), "Switch1Prefab").GetComponent<SwitchEntity>();
            set.Switch2 = SavePrefab(BuildSwitch("Switch2Prefab", art.Switch2Off, art.Switch2On, unlit), "Switch2Prefab").GetComponent<SwitchEntity>();
            set.Door = SavePrefab(BuildDoor(art, unlit), "DoorPrefab").GetComponent<DoorEntity>();

            GameObject catBase = SavePrefab(BuildCat(art, unlit, armLineMaterial), "CatPrefab");
            set.CatA = CreateCatVariant(catBase, "CatAPrefab", 0, 3, CatAAccent);
            set.CatB = CreateCatVariant(catBase, "CatBPrefab", 1, 2, CatBAccent);

            return set;
        }

        private static GameObject BuildSwitch(string name, Sprite inactive, Sprite active, Material unlit)
        {
            GameObject root = MakeSprite(name, inactive, 2, unlit, null);
            root.transform.localScale = Vector3.one * SpriteScale;
            var entity = root.AddComponent<SwitchEntity>();
            SetRef(entity, "spriteRenderer", root.GetComponent<SpriteRenderer>());
            SetRef(entity, "inactiveSprite", inactive);
            SetRef(entity, "activeSprite", active);
            return root;
        }

        private static GameObject BuildDoor(Art art, Material unlit)
        {
            var root = new GameObject("DoorPrefab");
            root.transform.localScale = Vector3.one * SpriteScale;

            // The rail slices keep their position inside the original 172 x 172 texture: top rail centre +0.75, bottom -0.74.
            GameObject railTop = MakeSprite("RailTop", art.RailTop, 6, unlit, root.transform);
            railTop.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            GameObject railBottom = MakeSprite("RailBottom", art.RailBottom, 6, unlit, root.transform);
            railBottom.transform.localPosition = new Vector3(0f, -0.74f, 0f);

            var bars = new GameObject("Bars");
            bars.transform.SetParent(root.transform, false);
            float[] barPositions = { -0.62f, -0.21f, 0.21f, 0.62f };
            for (int i = 0; i < barPositions.Length; i++)
            {
                GameObject bar = MakeSprite("Bar" + i, art.Bar, 6, unlit, bars.transform);
                bar.transform.localPosition = new Vector3(barPositions[i], 0f, 0f);
            }

            var door = root.AddComponent<DoorEntity>();
            SetRef(door, "closedVisualRoot", bars);
            return root;
        }

        private static GameObject BuildCat(Art art, Material unlit, Material armLineMaterial)
        {
            var root = new GameObject("CatPrefab");
            root.transform.localScale = Vector3.one * SpriteScale;

            GameObject body = MakeSprite("Body", art.Cat, 8, unlit, root.transform);

            GameObject indicator = MakeSprite("SelectionIndicator", art.Floor, 7, unlit, body.transform);
            indicator.GetComponent<SpriteRenderer>().color = new Color(1f, 0.85f, 0.2f, 0.45f);

            GameObject hand = MakeSprite("Hand", art.Hand, 10, unlit, root.transform);

            var lineObject = new GameObject("ArmLine");
            lineObject.transform.SetParent(root.transform, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 0;
            line.widthMultiplier = 0.29f * SpriteScale;
            line.startColor = ArmColor;
            line.endColor = ArmColor;
            line.numCornerVertices = 4;
            line.numCapVertices = 2;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.sharedMaterial = armLineMaterial;
            line.sortingOrder = 9;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            var labelObject = new GameObject("StepsLabel", typeof(RectTransform));
            labelObject.transform.SetParent(body.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, -0.2f, 0f);
            var label = labelObject.AddComponent<TextMeshPro>();
            label.rectTransform.sizeDelta = new Vector2(1.2f, 0.6f);
            label.text = "0";
            label.fontSize = 6f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.GetComponent<MeshRenderer>().sortingOrder = 20;

            var controller = root.AddComponent<CatArmController>();
            SetRef(controller, "bodyVisual", body.transform);
            SetRef(controller, "handVisual", hand.transform);
            SetRef(controller, "armLine", line);
            SetRef(controller, "selectionIndicator", indicator);
            SetRef(controller, "stepsLabel", label);
            return root;
        }

        private static GameObject CreateCatVariant(GameObject basePrefab, string name, int catIdIndex, int restFacingIndex, Color accent)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            instance.name = name;

            var controller = instance.GetComponent<CatArmController>();
            SetEnum(controller, "catId", catIdIndex);
            SetEnum(controller, "restFacing", restFacingIndex);

            instance.transform.Find("Body/StepsLabel").GetComponent<TextMeshPro>().color = accent;
            instance.transform.Find("Body/SelectionIndicator").GetComponent<SpriteRenderer>().color = new Color(accent.r, accent.g, accent.b, 0.45f);

            GameObject variant = PrefabUtility.SaveAsPrefabAsset(instance, PrefabFolder + "/" + name + ".prefab");
            Object.DestroyImmediate(instance);
            return variant;
        }

        private static GameObject MakeSprite(string name, Sprite sprite, int sortingOrder, Material material, Transform parent)
        {
            var go = new GameObject(name);
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }

            return go;
        }

        private static GameObject SavePrefab(GameObject temporary, string name)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temporary, PrefabFolder + "/" + name + ".prefab");
            Object.DestroyImmediate(temporary);
            return prefab;
        }

        // ---------------------------------------------------------------- level

        private static LevelDataSO CreateLevel()
        {
            var level = AssetDatabase.LoadAssetAtPath<LevelDataSO>(LevelPath);
            bool isNew = level == null;
            if (isNew)
            {
                level = ScriptableObject.CreateInstance<LevelDataSO>();
            }

            // 7 x 7 empty board. Cat A at (1,3), cat B at (5,3): each extends two cells and the hands meet on (3,3).
            const string layout =
                ".......\n" +
                ".......\n" +
                ".......\n" +
                ".P...Q.\n" +
                ".......\n" +
                ".......\n" +
                ".......";

            if (!level.ApplyAsciiLayout("Tutorial", layout, 2, 2, out string error))
            {
                Debug.LogError("AutoSceneSetup: could not build the tutorial level: " + error);
                return null;
            }

            if (isNew)
            {
                AssetDatabase.CreateAsset(level, LevelPath);
            }

            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssets();
            return level;
        }

        // ---------------------------------------------------------------- scene

        private static void BuildScene(Art art, PrefabSet prefabs, LevelDataSO level)
        {
            // Main Camera
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 7f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackgroundColor;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            cameraObject.transform.position = new Vector3(3f, 3f, -10f);
            cameraObject.AddComponent<AudioListener>();
            var fitter = cameraObject.AddComponent<CameraFitter>();
            SetFloat(fitter, "topUiFraction", 0.15f);
            SetFloat(fitter, "bottomUiFraction", 0.13f);

            // [WORLD] holds the cats and every tile or entity spawned by GridManager.
            var world = new GameObject("[WORLD]");
            var catA = (GameObject)PrefabUtility.InstantiatePrefab(prefabs.CatA, world.transform);
            catA.name = "Cat A";
            var catB = (GameObject)PrefabUtility.InstantiatePrefab(prefabs.CatB, world.transform);
            catB.name = "Cat B";

            // [MANAGERS]
            var managers = new GameObject("[MANAGERS]");
            var grid = managers.AddComponent<GridManager>();
            SetFloat(grid, "cellSize", CellSize);
            SetRef(grid, "levelRoot", world.transform);
            SetRef(grid, "floorTilePrefab", prefabs.Tile);
            SetRef(grid, "wallPrefab", prefabs.Wall);
            SetRef(grid, "barrelPrefab", prefabs.Barrel);
            SetRef(grid, "momentarySwitchPrefab", prefabs.Switch1);
            SetRef(grid, "toggleSwitchPrefab", prefabs.Switch2);
            SetRef(grid, "doorPrefab", prefabs.Door);
            SetRef(grid, "catA", catA.GetComponent<CatArmController>());
            SetRef(grid, "catB", catB.GetComponent<CatArmController>());

            var input = managers.AddComponent<TouchInputHandler>();
            SetRef(input, "gridManager", grid);
            SetRef(input, "gameCamera", camera);

            var controller = managers.AddComponent<GameplayController>();
            SetRef(controller, "gridManager", grid);
            SetRef(controller, "cameraFitter", fitter);
            SetRef(controller, "fallbackLevel", level);

            BuildBackground(camera);
            BuildUi(art, grid);
            BuildEventSystem();
        }

        /// <summary>The scrolling purple pattern (BG 3) behind the board, on a canvas far behind every sprite.</summary>
        private static void BuildBackground(Camera camera)
        {
            var canvasObject = new GameObject("[BACKGROUND]", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 90f;
            canvas.sortingOrder = -100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            var background = new GameObject("BG_Purple", typeof(RectTransform), typeof(RawImage));
            background.transform.SetParent(canvasObject.transform, false);
            var rect = (RectTransform)background.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = background.GetComponent<RawImage>();
            image.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/MainGame/Sprites/Assetgame/BG 3.png");
            image.raycastTarget = false;
            background.AddComponent<DiagonalScrollingBackground>();
        }

        private static void BuildUi(Art art, GridManager grid)
        {
            var canvasObject = new GameObject("[UI_CANVAS]", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            Transform root = canvasObject.transform;

            Vector2 topLeft = new Vector2(0f, 1f);
            Vector2 topCenter = new Vector2(0.5f, 1f);
            Vector2 topRight = new Vector2(1f, 1f);
            Vector2 bottomLeft = new Vector2(0f, 0f);
            Vector2 bottomCenter = new Vector2(0.5f, 0f);
            Vector2 bottomRight = new Vector2(1f, 0f);
            Vector2 center = new Vector2(0.5f, 0.5f);

            // Top bar (landscape 1920 x 1080)
            Button exitButton = NewButton("ExitButton", root, art.Exit, topLeft, topLeft, new Vector2(40f, -25f), new Vector2(110f, 108f));
            TMP_Text timer = NewText("TimerText", root, "00:00.00", 54f, DarkText, TextAlignmentOptions.MidlineLeft, topLeft, new Vector2(0f, 1f), new Vector2(180f, -35f), new Vector2(420f, 80f));
            NewImage("LevelLabel", root, art.LevelLabel, Color.white, topCenter, new Vector2(1f, 0.5f), new Vector2(-10f, -70f), new Vector2(230f, 87f));
            TMP_Text levelName = NewText("LevelName", root, "Tutorial", 56f, LightText, TextAlignmentOptions.MidlineLeft, topCenter, new Vector2(0f, 0.5f), new Vector2(20f, -70f), new Vector2(520f, 90f));
            TMP_Text moves = NewText("MoveCountText", root, "Moves: 0", 50f, DarkText, TextAlignmentOptions.MidlineRight, topRight, new Vector2(1f, 1f), new Vector2(-40f, -35f), new Vector2(420f, 80f));

            // Bottom bar
            Button undoButton = NewButton("UndoButton", root, art.Back, bottomLeft, bottomLeft, new Vector2(40f, 20f), new Vector2(130f, 120f));
            Button resetButton = NewButton("ResetButton", root, art.Reset, bottomRight, bottomRight, new Vector2(-40f, 20f), new Vector2(120f, 120f));
            TMP_Text activeCat = NewText("ActiveCatText", root, "Controlling: Cat A", 44f, DarkText, TextAlignmentOptions.Center, bottomCenter, new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(700f, 70f));

            // Win panel (hidden until the handshake)
            Image panel = NewImage("WinPanel", root, art.Floor, PanelColor, center, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860f, 560f));
            panel.preserveAspect = false;
            panel.raycastTarget = true;
            Transform panelRoot = panel.transform;
            TMP_Text winTitle = NewText("WinTitle", panelRoot, "Handshake!", 84f, DarkText, TextAlignmentOptions.Center, topCenter, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(760f, 120f));
            TMP_Text winSummary = NewText("WinSummary", panelRoot, "Moves: 0", 52f, DarkText, TextAlignmentOptions.Center, topCenter, new Vector2(0.5f, 1f), new Vector2(0f, -210f), new Vector2(760f, 100f));
            Button replayButton = NewButton("ReplayButton", panelRoot, art.Reset, bottomCenter, new Vector2(0.5f, 0f), new Vector2(-250f, 70f), new Vector2(150f, 150f));
            Button nextButton = NewButton("NextLevelButton", panelRoot, art.Back, bottomCenter, new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(160f, 148f));
            nextButton.transform.localScale = new Vector3(-1f, 1f, 1f);
            Button levelSelectButton = NewButton("LevelSelectButton", panelRoot, art.Exit, bottomCenter, new Vector2(0.5f, 0f), new Vector2(250f, 70f), new Vector2(150f, 148f));

            var hud = canvasObject.AddComponent<GameplayHUD>();
            SetRef(hud, "gridManager", grid);
            SetRef(hud, "levelNameText", levelName);
            SetRef(hud, "moveCountText", moves);
            SetRef(hud, "timerText", timer);
            SetRef(hud, "activeCatText", activeCat);
            SetRef(hud, "undoButton", undoButton);
            SetRef(hud, "resetButton", resetButton);
            SetRef(hud, "menuButton", exitButton);
            SetRef(hud, "winPanel", panel.gameObject);
            SetRef(hud, "winTitleText", winTitle);
            SetRef(hud, "winSummaryText", winSummary);
            SetRef(hud, "nextLevelButton", nextButton);
            SetRef(hud, "levelSelectButton", levelSelectButton);
            SetRef(hud, "replayButton", replayButton);

            panel.gameObject.SetActive(false);
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

        // ---------------------------------------------------------------- UI helpers

        private static RectTransform NewUiObject(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return rect;
        }

        private static Image NewImage(string name, Transform parent, Sprite sprite, Color color, Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rect = NewUiObject(name, parent, anchor, pivot, anchoredPosition, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static Button NewButton(string name, Transform parent, Sprite sprite, Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            Image image = NewImage(name, parent, sprite, Color.white, anchor, pivot, anchoredPosition, size);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            return button;
        }

        private static TMP_Text NewText(string name, Transform parent, string text, float fontSize, Color color, TextAlignmentOptions alignment, Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rect = NewUiObject(name, parent, anchor, pivot, anchoredPosition, size);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = FontStyles.Bold;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            return label;
        }

        // ---------------------------------------------------------------- serialized field helpers

        private static void SetRef(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogError("AutoSceneSetup: field '" + field + "' not found on " + target.GetType().Name);
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string field, float value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogError("AutoSceneSetup: field '" + field + "' not found on " + target.GetType().Name);
                return;
            }

            property.floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEnum(Object target, string field, int index)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogError("AutoSceneSetup: field '" + field + "' not found on " + target.GetType().Name);
                return;
            }

            property.enumValueIndex = index;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
