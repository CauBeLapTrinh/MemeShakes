using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Catshakes.EditorTools
{
    /// <summary>
    /// Visual editor for <see cref="LevelDataSO"/> assets. Menu: Catshakes / Level Editor.
    ///
    /// - Left: every LevelDataSO in the project (New / Duplicate).
    /// - Right: click-to-paint grid (top row = highest y, exactly like the game), palette, grid size,
    ///   cat steps, live validation, ASCII import/export and a play-test button.
    /// - Left click paints, right click erases, Ctrl/Cmd+click picks the tool of the cell under the cursor.
    ///
    /// - Cats can be rotated (toolbar, or click a cat with its tool). The hand starts on the cell in front of the cat.
    /// - Drop a level picture (PNG/JPG) on the window to measure its width and height in cells (and read the floor shape).
    ///
    /// All edits go through SerializedObject, so Undo/Redo (Ctrl+Z) works and the asset is marked dirty.
    /// </summary>
    public sealed class LevelEditorWindow : EditorWindow
    {
        private enum PaintTool
        {
            Wall = 0,
            Barrel = 1,
            SwitchHold = 2,
            SwitchToggle = 3,
            Door = 4,
            CatA = 5,
            CatB = 6,
            Eraser = 7
        }

        private static readonly string[] ToolLabels =
        {
            "Wall (#)", "Barrel (O)", "Switch 1 - hold", "Switch 2 - toggle",
            "Door", "Cat A start (P)", "Cat B start (Q)", "Eraser"
        };

        private const string LevelFolder = "Assets/Levels";
        private const string NewLevelTemplate = ".......\n.......\n.......\n.......\nP.....Q";
        private const int MaxGridSize = 40;

        private static readonly Vector2Int NoCell = new Vector2Int(-1, -1);

        [SerializeField] private LevelDataSO level;
        [SerializeField] private PaintTool tool = PaintTool.Wall;
        [SerializeField] private int linkId = 1;
        [SerializeField] private bool toggleStartsOn;
        [SerializeField] private float cellSize = 52f;
        [SerializeField] private string asciiText = string.Empty;
        [SerializeField] private bool showAscii = true;
        [SerializeField] private bool showImage = true;
        [SerializeField] private string imagePath = string.Empty;
        [SerializeField] private Color floorColor = new Color32(252, 236, 200, 255);
        [SerializeField] private bool floorColorPicked;
        [SerializeField] private float floorTolerance = 55f;
        [SerializeField] private int wallBorder = 1;
        [SerializeField] private float cellSizeOverride;

        private SerializedObject so;
        private readonly List<LevelDataSO> allLevels = new List<LevelDataSO>();
        private readonly Queue<Action> deferred = new Queue<Action>();
        private bool listDirty = true;
        private bool validationDirty = true;
        private bool isValid;
        private string report = string.Empty;
        private string status = string.Empty;
        private MessageType statusType = MessageType.None;
        private Vector2 listScroll;
        private Vector2 gridScroll;
        private Vector2 asciiScroll;
        private Vector2 mainScroll;
        [NonSerialized] private Texture2D imageTexture;
        [NonSerialized] private Color32[] imagePixels;
        [NonSerialized] private LevelImageAnalysis analysis;
        private bool analysisDirty;
        private bool imageReloadTried;
        private bool blockColorLooked;
        private Vector2Int hoverCell = NoCell;
        private Vector2Int lastPaintCell = NoCell;
        [NonSerialized] private GUIStyle cellLabel;
        [NonSerialized] private GUIStyle monoArea;
        [NonSerialized] private GUIStyle monoLabel;
        [NonSerialized] private Font monoFont;

        [MenuItem("Catshakes/Level Editor")]
        public static void Open()
        {
            LevelEditorWindow window = GetWindow<LevelEditorWindow>("Level Editor");
            window.minSize = new Vector2(780f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += HandleUndoRedo;
            wantsMouseMove = true;
            listDirty = true;

            if (level == null && Selection.activeObject is LevelDataSO selected)
            {
                level = selected;
            }
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= HandleUndoRedo;
            ReleaseImage();

            // Drop the OS font before a script reload so Unity does not warn about an invalid font reference.
            cellLabel = null;
            monoLabel = null;
            if (monoArea != null)
            {
                monoArea.font = null;
            }

            monoArea = null;
            if (monoFont != null)
            {
                DestroyImmediate(monoFont);
                monoFont = null;
            }
        }

        private void OnSelectionChange()
        {
            if (Selection.activeObject is LevelDataSO selected && selected != level)
            {
                Defer(() => SetLevel(selected));
            }
        }

        private void OnProjectChange()
        {
            listDirty = true;
            validationDirty = true;
            Repaint();
        }

        private void HandleUndoRedo()
        {
            if (so != null && level != null)
            {
                so.Update();
                asciiText = BuildAscii();
            }

            validationDirty = true;
            Repaint();
        }

        // ----------------------------------------------------------------------------------------------------
        // GUI
        // ----------------------------------------------------------------------------------------------------

        private void OnGUI()
        {
            EnsureStyles();
            Event e = Event.current;

            // All state changes happen at the start of the Layout pass, so the control tree stays identical
            // between Layout and the Repaint/mouse events that follow.
            if (e.type == EventType.Layout)
            {
                if (level == null)
                {
                    so = null;
                }
                else if (so == null || so.targetObject != level)
                {
                    so = new SerializedObject(level);
                    asciiText = string.IsNullOrEmpty(asciiText) ? BuildAscii() : asciiText;
                }

                if (so != null)
                {
                    so.Update();
                }

                while (deferred.Count > 0)
                {
                    deferred.Dequeue().Invoke();
                }

                if (listDirty)
                {
                    RebuildList();
                }

                if (level != null && validationDirty)
                {
                    RunValidation();
                }

                UpdateImageState();
            }

            EditorGUILayout.BeginHorizontal();
            DrawSidebar();
            EditorGUILayout.BeginVertical();

            mainScroll = EditorGUILayout.BeginScrollView(mainScroll);
            if (level == null || so == null)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.HelpBox("Pick a level on the left, or press New to create one.", MessageType.Info);
            }
            else
            {
                DrawEditor();
            }

            EditorGUILayout.EndScrollView();
            DrawStatus();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            HandleDragAndDrop(e);
        }

        private void DrawSidebar()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(200f));
            EditorGUILayout.LabelField("Levels", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("New"))
            {
                Defer(CreateNewLevel);
            }

            using (new EditorGUI.DisabledScope(level == null))
            {
                if (GUILayout.Button("Duplicate"))
                {
                    Defer(DuplicateLevel);
                }
            }

            if (GUILayout.Button("Reload", GUILayout.Width(54f)))
            {
                Defer(() => listDirty = true);
            }

            EditorGUILayout.EndHorizontal();

            listScroll = EditorGUILayout.BeginScrollView(listScroll, GUI.skin.box);
            for (int i = 0; i < allLevels.Count; i++)
            {
                LevelDataSO entry = allLevels[i];
                if (entry == null)
                {
                    continue;
                }

                bool selected = entry == level;
                string label = entry.name + "   " + entry.Width + "x" + entry.Height;
                bool wanted = GUILayout.Toggle(selected, label, EditorStyles.miniButton);
                if (wanted && !selected)
                {
                    Defer(() => SetLevel(entry));
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawEditor()
        {
            // Asset row
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            Object picked = EditorGUILayout.ObjectField("Level asset", level, typeof(LevelDataSO), false);
            if (EditorGUI.EndChangeCheck())
            {
                LevelDataSO pickedLevel = picked as LevelDataSO;
                Defer(() => SetLevel(pickedLevel));
            }

            if (GUILayout.Button("Ping", GUILayout.Width(48f)))
            {
                EditorGUIUtility.PingObject(level);
            }

            EditorGUILayout.EndHorizontal();

            // Meta
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(so.FindProperty("displayName"), new GUIContent("Display name"));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(so.FindProperty("catAMaxSteps"), new GUIContent("Cat A max steps"));
            EditorGUILayout.PropertyField(so.FindProperty("catBMaxSteps"), new GUIContent("Cat B max steps"));
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
            {
                so.ApplyModifiedProperties();
                validationDirty = true;
            }

            // Size and bulk actions
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            int newWidth = EditorGUILayout.DelayedIntField("Width", level.Width);
            int newHeight = EditorGUILayout.DelayedIntField("Height", level.Height);
            if (EditorGUI.EndChangeCheck())
            {
                Defer(() => ResizeGrid(newWidth, newHeight));
            }

            if (GUILayout.Button("Border walls", GUILayout.Width(92f)))
            {
                Defer(AddBorderWalls);
            }

            if (GUILayout.Button("Clear objects", GUILayout.Width(92f)))
            {
                Defer(ClearObstacles);
            }

            EditorGUILayout.EndHorizontal();

            // Palette
            tool = (PaintTool)GUILayout.SelectionGrid((int)tool, ToolLabels, 4, GUILayout.Height(46f));

            bool usesLink = tool == PaintTool.SwitchHold || tool == PaintTool.SwitchToggle || tool == PaintTool.Door;
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!usesLink))
            {
                linkId = EditorGUILayout.IntSlider("Link ID (1-9)", linkId, 1, 9);
            }

            using (new EditorGUI.DisabledScope(tool != PaintTool.SwitchToggle))
            {
                toggleStartsOn = EditorGUILayout.ToggleLeft("Starts ON", toggleStartsOn, GUILayout.Width(90f));
            }

            EditorGUILayout.EndHorizontal();

            DrawFacingRow("Cat A facing", "catAFacing", CatId.CatA);
            DrawFacingRow("Cat B facing", "catBFacing", CatId.CatB);

            cellSize = EditorGUILayout.Slider("Zoom", cellSize, 28f, 96f);

            // Grid
            gridScroll = EditorGUILayout.BeginScrollView(gridScroll, GUI.skin.box,
                GUILayout.Height(Mathf.Clamp(level.Height * cellSize + 12f, 150f, 420f)));
            Rect area = GUILayoutUtility.GetRect(level.Width * cellSize + 2f, level.Height * cellSize + 2f,
                GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
            DrawGrid(area);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField(
                "Left click: paint   |   Right click: erase   |   Ctrl/Cmd + click: pick   |   Click a cat with its tool: rotate it   |   h = where the hand starts",
                EditorStyles.miniLabel);

            DrawImageSection();

            // ASCII
            bool foldout = EditorGUILayout.Foldout(showAscii, "ASCII layout (first line = top row)", true);
            if (foldout != showAscii)
            {
                Defer(() => showAscii = foldout);
            }

            if (showAscii)
            {
                asciiScroll = EditorGUILayout.BeginScrollView(asciiScroll, GUILayout.Height(96f));
                asciiText = EditorGUILayout.TextArea(asciiText, monoArea, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Import from text"))
                {
                    Defer(ImportAscii);
                }

                if (GUILayout.Button("Refresh text from grid"))
                {
                    Defer(() => asciiText = BuildAscii());
                }

                if (GUILayout.Button("Copy"))
                {
                    GUIUtility.systemCopyBuffer = asciiText;
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField(
                    "# wall  . empty  P cat A  Q cat B  O barrel  1-9 switch(hold)  a-i switch(toggle)  A-I door.  (ASCII cannot store 'Starts ON'.)",
                    EditorStyles.miniLabel);
            }

            // Validation + actions
            if (isValid && string.IsNullOrEmpty(report))
            {
                EditorGUILayout.HelpBox("Level is valid.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(report.TrimEnd(), isValid ? MessageType.Warning : MessageType.Error);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save", GUILayout.Height(26f)))
            {
                Defer(SaveLevel);
            }

            if (GUILayout.Button("Play-test this level", GUILayout.Height(26f)))
            {
                Defer(PlayTest);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatus()
        {
            string hover = string.Empty;
            if (level != null && level.IsInside(hoverCell))
            {
                hover = "Cell (" + hoverCell.x + ", " + hoverCell.y + "): " + Describe(hoverCell);
            }

            EditorGUILayout.LabelField(hover, EditorStyles.miniLabel);

            if (!string.IsNullOrEmpty(status))
            {
                EditorGUILayout.HelpBox(status, statusType);
            }
        }

        private void DrawGrid(Rect area)
        {
            Event e = Event.current;
            int width = level.Width;
            int height = level.Height;

            var map = new Dictionary<Vector2Int, ObstacleData>();
            IReadOnlyList<ObstacleData> obstacles = level.Obstacles;
            for (int i = 0; i < obstacles.Count; i++)
            {
                if (obstacles[i].tileType != TileType.Empty)
                {
                    map[obstacles[i].position] = obstacles[i];
                }
            }

            if (e.type == EventType.Repaint)
            {
                cellLabel.fontSize = Mathf.Max(9, Mathf.RoundToInt(cellSize * 0.3f));

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Rect inner = CellRect(area, x, y, height);
                        inner = new Rect(inner.x + 1f, inner.y + 1f, inner.width - 2f, inner.height - 2f);
                        var cell = new Vector2Int(x, y);

                        EditorGUI.DrawRect(inner, ((x + y) & 1) == 0 ? new Color(0.30f, 0.30f, 0.32f) : new Color(0.26f, 0.26f, 0.28f));

                        Color color;
                        string text;
                        if (TryGetCellVisual(cell, map, out color, out text))
                        {
                            EditorGUI.DrawRect(inner, color);
                            GUI.Label(inner, text, cellLabel);
                        }

                        if (cell == hoverCell)
                        {
                            DrawOutline(inner, Color.white, 2f);
                        }
                    }
                }

                DrawHandStart(area, level.CatAStart, level.CatAFacing, new Color(0.95f, 0.55f, 0.15f), map, width, height);
                DrawHandStart(area, level.CatBStart, level.CatBFacing, new Color(0.25f, 0.55f, 0.95f), map, width, height);

                DrawOutline(new Rect(area.x, area.y, width * cellSize + 2f, height * cellSize + 2f), new Color(0.1f, 0.1f, 0.1f), 1f);
            }

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.MouseDown)
            {
                Vector2Int hovered = area.Contains(e.mousePosition) ? ToCell(area, e.mousePosition, width, height) : NoCell;
                if (hovered != hoverCell)
                {
                    hoverCell = hovered;
                    Repaint();
                }
            }

            if (e.rawType == EventType.MouseUp)
            {
                lastPaintCell = NoCell;
            }

            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button <= 1 && area.Contains(e.mousePosition))
            {
                Vector2Int cell = ToCell(area, e.mousePosition, width, height);
                if (e.type == EventType.MouseDown)
                {
                    lastPaintCell = NoCell;
                }

                if (cell != lastPaintCell)
                {
                    lastPaintCell = cell;

                    if (e.type == EventType.MouseDown && e.button == 0 && (e.control || e.command))
                    {
                        PickFromCell(cell, map);
                    }
                    else
                    {
                        PaintTool used = e.button == 1 ? PaintTool.Eraser : tool;
                        Defer(() => Paint(cell, used));
                    }
                }

                e.Use();
            }
        }

        private bool TryGetCellVisual(Vector2Int cell, Dictionary<Vector2Int, ObstacleData> map, out Color color, out string text)
        {
            if (cell == level.CatAStart)
            {
                color = new Color(0.95f, 0.55f, 0.15f);
                text = "A" + ArrowOf(level.CatAFacing);
                return true;
            }

            if (cell == level.CatBStart)
            {
                color = new Color(0.25f, 0.55f, 0.95f);
                text = "B" + ArrowOf(level.CatBFacing);
                return true;
            }

            ObstacleData data;
            if (!map.TryGetValue(cell, out data))
            {
                color = default(Color);
                text = null;
                return false;
            }

            Color linkColor = Color.HSVToRGB(Mathf.Repeat((data.linkId - 1) / 9f, 1f), 0.55f, 0.85f);
            switch (data.tileType)
            {
                case TileType.Wall:
                    color = new Color(0.12f, 0.12f, 0.14f);
                    text = "#";
                    return true;
                case TileType.Barrel:
                    color = new Color(0.62f, 0.40f, 0.20f);
                    text = "O";
                    return true;
                case TileType.SwitchMomentary:
                    color = linkColor;
                    text = "S" + data.linkId;
                    return true;
                case TileType.SwitchToggle:
                    color = linkColor;
                    text = "T" + data.linkId + (data.initiallyOn ? "*" : string.Empty);
                    return true;
                case TileType.Door:
                    color = linkColor * 0.8f;
                    color.a = 1f;
                    text = "D" + data.linkId;
                    return true;
                default:
                    color = default(Color);
                    text = null;
                    return false;
            }
        }

        private void PickFromCell(Vector2Int cell, Dictionary<Vector2Int, ObstacleData> map)
        {
            if (cell == level.CatAStart)
            {
                tool = PaintTool.CatA;
                return;
            }

            if (cell == level.CatBStart)
            {
                tool = PaintTool.CatB;
                return;
            }

            ObstacleData data;
            if (!map.TryGetValue(cell, out data))
            {
                tool = PaintTool.Eraser;
                return;
            }

            switch (data.tileType)
            {
                case TileType.Wall: tool = PaintTool.Wall; break;
                case TileType.Barrel: tool = PaintTool.Barrel; break;
                case TileType.SwitchMomentary: tool = PaintTool.SwitchHold; break;
                case TileType.SwitchToggle: tool = PaintTool.SwitchToggle; break;
                case TileType.Door: tool = PaintTool.Door; break;
            }

            if (data.linkId >= 1)
            {
                linkId = Mathf.Clamp(data.linkId, 1, 9);
            }

            toggleStartsOn = data.initiallyOn;
        }

        // ----------------------------------------------------------------------------------------------------
        // Edits (always run through Defer, i.e. at the start of a Layout pass)
        // ----------------------------------------------------------------------------------------------------

        private void Paint(Vector2Int cell, PaintTool paintTool)
        {
            if (level == null || so == null || !level.IsInside(cell))
            {
                return;
            }

            so.Update();
            SerializedProperty list = so.FindProperty("obstacles");
            SerializedProperty catA = so.FindProperty("catAStart");
            SerializedProperty catB = so.FindProperty("catBStart");
            Vector2Int a = catA.vector2IntValue;
            Vector2Int b = catB.vector2IntValue;
            int index = IndexOf(list, cell);
            bool changed = false;

            switch (paintTool)
            {
                case PaintTool.Eraser:
                    if (index >= 0)
                    {
                        list.DeleteArrayElementAtIndex(index);
                        changed = true;
                    }

                    break;

                case PaintTool.CatA:
                    if (cell == b)
                    {
                        SetStatus("Cat B already starts on that cell.", MessageType.Warning);
                        return;
                    }

                    if (cell != a)
                    {
                        if (index >= 0)
                        {
                            list.DeleteArrayElementAtIndex(index);
                        }

                        catA.vector2IntValue = cell;
                    }
                    else
                    {
                        RotateFacing("catAFacing");
                    }

                    changed = true;
                    break;

                case PaintTool.CatB:
                    if (cell == a)
                    {
                        SetStatus("Cat A already starts on that cell.", MessageType.Warning);
                        return;
                    }

                    if (cell != b)
                    {
                        if (index >= 0)
                        {
                            list.DeleteArrayElementAtIndex(index);
                        }

                        catB.vector2IntValue = cell;
                    }
                    else
                    {
                        RotateFacing("catBFacing");
                    }

                    changed = true;
                    break;

                default:
                    if (cell == a || cell == b)
                    {
                        SetStatus("A cat starts on that cell. Move the cat first.", MessageType.Warning);
                        return;
                    }

                    TileType type = ToTileType(paintTool);
                    bool usesLink = type == TileType.SwitchMomentary || type == TileType.SwitchToggle || type == TileType.Door;
                    int link = usesLink ? linkId : 0;
                    bool on = type == TileType.SwitchToggle && toggleStartsOn;

                    SerializedProperty element;
                    if (index >= 0)
                    {
                        element = list.GetArrayElementAtIndex(index);
                        if (element.FindPropertyRelative("tileType").enumValueIndex == (int)type
                            && element.FindPropertyRelative("linkId").intValue == link
                            && element.FindPropertyRelative("initiallyOn").boolValue == on)
                        {
                            return;
                        }
                    }
                    else
                    {
                        list.arraySize++;
                        element = list.GetArrayElementAtIndex(list.arraySize - 1);
                    }

                    WriteElement(element, type, cell, link, on);
                    changed = true;
                    break;
            }

            if (changed)
            {
                SetStatus(string.Empty, MessageType.None);
                Commit();
            }
        }

        private void ResizeGrid(int newWidth, int newHeight)
        {
            if (level == null || so == null)
            {
                return;
            }

            newWidth = Mathf.Clamp(newWidth, 2, MaxGridSize);
            newHeight = Mathf.Clamp(newHeight, 2, MaxGridSize);
            if (newWidth == level.Width && newHeight == level.Height)
            {
                return;
            }

            so.Update();
            so.FindProperty("width").intValue = newWidth;
            so.FindProperty("height").intValue = newHeight;

            // The grid keeps its bottom-left corner, so objects outside the new size are dropped.
            SerializedProperty list = so.FindProperty("obstacles");
            int removed = 0;
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                Vector2Int p = list.GetArrayElementAtIndex(i).FindPropertyRelative("position").vector2IntValue;
                if (p.x >= newWidth || p.y >= newHeight)
                {
                    list.DeleteArrayElementAtIndex(i);
                    removed++;
                }
            }

            SerializedProperty catA = so.FindProperty("catAStart");
            SerializedProperty catB = so.FindProperty("catBStart");
            catA.vector2IntValue = new Vector2Int(Mathf.Min(catA.vector2IntValue.x, newWidth - 1), Mathf.Min(catA.vector2IntValue.y, newHeight - 1));
            catB.vector2IntValue = new Vector2Int(Mathf.Min(catB.vector2IntValue.x, newWidth - 1), Mathf.Min(catB.vector2IntValue.y, newHeight - 1));

            SetStatus(removed > 0 ? removed + " object(s) outside the new size were removed." : string.Empty,
                removed > 0 ? MessageType.Warning : MessageType.None);
            Commit();
        }

        private void AddBorderWalls()
        {
            if (level == null || so == null)
            {
                return;
            }

            so.Update();
            SerializedProperty list = so.FindProperty("obstacles");
            Vector2Int a = so.FindProperty("catAStart").vector2IntValue;
            Vector2Int b = so.FindProperty("catBStart").vector2IntValue;
            int width = so.FindProperty("width").intValue;
            int height = so.FindProperty("height").intValue;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                    var cell = new Vector2Int(x, y);
                    if (!border || cell == a || cell == b || IndexOf(list, cell) >= 0)
                    {
                        continue;
                    }

                    list.arraySize++;
                    WriteElement(list.GetArrayElementAtIndex(list.arraySize - 1), TileType.Wall, cell, 0, false);
                }
            }

            Commit();
        }

        private void ClearObstacles()
        {
            if (level == null || so == null)
            {
                return;
            }

            if (!EditorUtility.DisplayDialog("Clear objects", "Remove every wall, barrel, switch and door from this level? (Ctrl+Z undoes it.)", "Clear", "Cancel"))
            {
                return;
            }

            so.Update();
            so.FindProperty("obstacles").arraySize = 0;
            Commit();
        }

        private void ImportAscii()
        {
            if (level == null || so == null)
            {
                return;
            }

            so.Update();
            string levelName = so.FindProperty("displayName").stringValue;
            int stepsA = so.FindProperty("catAMaxSteps").intValue;
            int stepsB = so.FindProperty("catBMaxSteps").intValue;

            Undo.RecordObject(level, "Import ASCII Layout");
            string error;
            bool ok = level.ApplyAsciiLayout(levelName, asciiText, stepsA, stepsB, out error);
            EditorUtility.SetDirty(level);
            so.Update();

            if (ok)
            {
                SetStatus("Imported " + level.Width + "x" + level.Height + " layout with " + level.Obstacles.Count + " objects.", MessageType.Info);
                Commit();
            }
            else
            {
                // ApplyAsciiLayout stores the text before parsing; put the real grid back into the asset.
                so.FindProperty("asciiLayout").stringValue = BuildAscii();
                so.ApplyModifiedProperties();
                SetStatus("Import failed: " + error, MessageType.Error);
                validationDirty = true;
            }
        }

        private void SaveLevel()
        {
            if (level == null)
            {
                return;
            }

            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssets();
            SetStatus("Saved " + AssetDatabase.GetAssetPath(level), MessageType.Info);
        }

        private void CreateNewLevel()
        {
            if (!AssetDatabase.IsValidFolder(LevelFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Levels");
            }

            int number = 1;
            string path;
            do
            {
                path = LevelFolder + "/Level_" + number.ToString("00") + ".asset";
                number++;
            }
            while (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)));

            LevelDataSO created = CreateInstance<LevelDataSO>();
            string error;
            if (!created.ApplyAsciiLayout("Level " + (number - 1), NewLevelTemplate, 8, 8, out error))
            {
                Debug.LogError("Could not build the template level: " + error);
            }

            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();
            listDirty = true;
            SetLevel(created);
            EditorGUIUtility.PingObject(created);
            SetStatus("Created " + path + ". Add it to GameSceneManager > Levels to see it in the menu.", MessageType.Info);
        }

        private void DuplicateLevel()
        {
            if (level == null)
            {
                return;
            }

            string source = AssetDatabase.GetAssetPath(level);
            string target = AssetDatabase.GenerateUniqueAssetPath(source);
            if (!AssetDatabase.CopyAsset(source, target))
            {
                SetStatus("Could not duplicate " + source, MessageType.Error);
                return;
            }

            AssetDatabase.SaveAssets();
            listDirty = true;
            SetLevel(AssetDatabase.LoadAssetAtPath<LevelDataSO>(target));
            SetStatus("Duplicated to " + target, MessageType.Info);
        }

        private void PlayTest()
        {
            if (level == null)
            {
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SetStatus("Stop Play Mode first.", MessageType.Warning);
                return;
            }

            if (!isValid)
            {
                SetStatus("Fix the validation errors before play-testing.", MessageType.Error);
                return;
            }

            AssetDatabase.SaveAssets();

            string scenePath = null;
            foreach (string guid in AssetDatabase.FindAssets("GameplayScene t:Scene"))
            {
                string candidate = AssetDatabase.GUIDToAssetPath(guid);
                if (candidate.EndsWith("/GameplayScene.unity", StringComparison.Ordinal))
                {
                    scenePath = candidate;
                    break;
                }
            }

            if (scenePath == null)
            {
                SetStatus("GameplayScene.unity not found. Run Catshakes > Auto Setup Gameplay Scene first.", MessageType.Error);
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            GameplayController controller = FindFirstObjectByType<GameplayController>();
            if (controller == null)
            {
                SetStatus("No GameplayController in GameplayScene.", MessageType.Error);
                return;
            }

            // Only the in-memory scene is changed; nothing is saved to the scene file.
            var controllerObject = new SerializedObject(controller);
            controllerObject.FindProperty("fallbackLevel").objectReferenceValue = level;
            controllerObject.ApplyModifiedPropertiesWithoutUndo();

            EditorApplication.isPlaying = true;
        }

        // ----------------------------------------------------------------------------------------------------
        // Cat facing
        // ----------------------------------------------------------------------------------------------------

        private static readonly string[] FacingLabels = { "Up", "Down", "Left", "Right" };

        private void DrawFacingRow(string label, string propertyName, CatId cat)
        {
            Direction current = cat == CatId.CatA ? level.CatAFacing : level.CatBFacing;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            EditorGUI.BeginChangeCheck();
            int picked = GUILayout.Toolbar((int)current, FacingLabels);
            if (EditorGUI.EndChangeCheck())
            {
                Defer(() => SetFacing(propertyName, (Direction)picked));
            }

            EditorGUILayout.EndHorizontal();
        }

        private void SetFacing(string propertyName, Direction direction)
        {
            if (level == null || so == null)
            {
                return;
            }

            so.Update();
            so.FindProperty(propertyName).enumValueIndex = (int)direction;
            SetStatus(string.Empty, MessageType.None);
            Commit();
        }

        /// <summary>Turns a cat 90 degrees clockwise: Up, Right, Down, Left. Caller commits.</summary>
        private void RotateFacing(string propertyName)
        {
            // Direction order is Up 0, Down 1, Left 2, Right 3.
            int[] next = { 3, 2, 0, 1 };
            SerializedProperty facing = so.FindProperty(propertyName);
            facing.enumValueIndex = next[Mathf.Clamp(facing.enumValueIndex, 0, 3)];
        }

        private static string ArrowOf(Direction direction)
        {
            switch (direction)
            {
                case Direction.Up: return "^";
                case Direction.Down: return "v";
                case Direction.Left: return "<";
                default: return ">";
            }
        }

        private static string FacingLetter(int directionIndex)
        {
            switch (directionIndex)
            {
                case 0: return "U";
                case 1: return "D";
                case 2: return "L";
                default: return "R";
            }
        }

        private void DrawHandStart(Rect area, Vector2Int root, Direction facing, Color color, Dictionary<Vector2Int, ObstacleData> map, int width, int height)
        {
            Vector2Int hand = root + facing.ToVector();
            if (hand.x < 0 || hand.y < 0 || hand.x >= width || hand.y >= height)
            {
                return;
            }

            Rect cell = CellRect(area, hand.x, hand.y, height);
            float inset = 1f + cellSize * 0.2f;
            var inner = new Rect(cell.x + inset, cell.y + inset, cell.width - 2f * inset, cell.height - 2f * inset);

            ObstacleData data;
            bool blocked = map.TryGetValue(hand, out data) && (data.tileType == TileType.Wall || data.tileType == TileType.Barrel);
            Color fill = blocked ? new Color(0.9f, 0.1f, 0.1f, 0.85f) : new Color(color.r, color.g, color.b, 0.6f);
            EditorGUI.DrawRect(inner, fill);
            GUI.Label(inner, "h", cellLabel);
        }

        // ----------------------------------------------------------------------------------------------------
        // Level picture reader
        // ----------------------------------------------------------------------------------------------------

        private void UpdateImageState()
        {
            if (imageTexture == null && !imageReloadTried && !string.IsNullOrEmpty(imagePath))
            {
                imageReloadTried = true;
                if (File.Exists(imagePath))
                {
                    LoadImageFile(imagePath);
                }
            }

            if (!floorColorPicked && !blockColorLooked)
            {
                blockColorLooked = true;
                Color found;
                if (TryFindBlockColor(out found))
                {
                    floorColor = found;
                    analysisDirty = true;
                }
            }

            if (analysisDirty && imageTexture != null && imagePixels != null)
            {
                analysis = LevelImageReader.Analyze(imagePixels, imageTexture.width, imageTexture.height, floorColor, floorTolerance, cellSizeOverride);
                analysisDirty = false;
            }
        }

        /// <summary>The floor colour of the game's own Block tile, so pictures made from the game art work without setup.</summary>
        private static bool TryFindBlockColor(out Color color)
        {
            color = default(Color);
            foreach (string guid in AssetDatabase.FindAssets("Block t:Texture2D"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("/Block.png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
                    {
                        continue;
                    }

                    Color32[] pixels = texture.GetPixels32();
                    int w = texture.width;
                    int h = texture.height;
                    long r = 0, g = 0, b = 0, count = 0;
                    for (int y = h / 4; y < h * 3 / 4; y++)
                    {
                        for (int x = w / 4; x < w * 3 / 4; x++)
                        {
                            Color32 c = pixels[y * w + x];
                            if (c.a < 200)
                            {
                                continue;
                            }

                            r += c.r;
                            g += c.g;
                            b += c.b;
                            count++;
                        }
                    }

                    if (count > 0)
                    {
                        color = new Color32((byte)(r / count), (byte)(g / count), (byte)(b / count), 255);
                        return true;
                    }
                }
                catch (Exception)
                {
                    // Fall through to the default cream colour.
                }
                finally
                {
                    DestroyImmediate(texture);
                }
            }

            return false;
        }

        private void LoadImageFile(string path)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                if (!ImageConversion.LoadImage(texture, bytes))
                {
                    DestroyImmediate(texture);
                    SetStatus("Could not read the picture '" + path + "'. Use a PNG or JPG file.", MessageType.Error);
                    return;
                }

                ReleaseImage();
                imageTexture = texture;
                imagePixels = texture.GetPixels32();
                imagePath = path;
                imageReloadTried = true;
                cellSizeOverride = 0f;
                analysisDirty = true;
                showImage = true;
                SetStatus(string.Empty, MessageType.None);
            }
            catch (Exception exception)
            {
                SetStatus("Could not open the picture: " + exception.Message, MessageType.Error);
            }
        }

        private void ReleaseImage()
        {
            if (imageTexture != null)
            {
                DestroyImmediate(imageTexture);
            }

            imageTexture = null;
            imagePixels = null;
            analysis = null;
        }

        private void ClearImage()
        {
            ReleaseImage();
            imagePath = string.Empty;
            cellSizeOverride = 0f;
        }

        private void BrowseImage()
        {
            string path = EditorUtility.OpenFilePanel("Level picture", string.Empty, "png,jpg,jpeg");
            if (!string.IsNullOrEmpty(path))
            {
                LoadImageFile(path);
            }
        }

        private static bool IsImageFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg";
        }

        private static string FirstDroppedImage()
        {
            string[] paths = DragAndDrop.paths;
            if (paths != null)
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    if (IsImageFile(paths[i]))
                    {
                        return paths[i];
                    }
                }
            }

            Object[] references = DragAndDrop.objectReferences;
            if (references != null)
            {
                for (int i = 0; i < references.Length; i++)
                {
                    if (references[i] is Texture2D)
                    {
                        string assetPath = AssetDatabase.GetAssetPath(references[i]);
                        if (IsImageFile(assetPath))
                        {
                            return assetPath;
                        }
                    }
                }
            }

            return null;
        }

        private void HandleDragAndDrop(Event e)
        {
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)
            {
                return;
            }

            string dropped = FirstDroppedImage();
            if (dropped == null)
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                Defer(() => LoadImageFile(dropped));
            }

            e.Use();
        }

        private void DrawImageSection()
        {
            bool open = EditorGUILayout.Foldout(showImage, "Read size from a level picture (drop a PNG/JPG on this window)", true);
            if (open != showImage)
            {
                Defer(() => showImage = open);
            }

            if (!showImage)
            {
                return;
            }

            if (imageTexture == null)
            {
                EditorGUILayout.HelpBox(
                    "Drag a level picture into this window (or press Browse). The tool finds the cream floor tiles, measures the cell size and counts the columns and rows.",
                    MessageType.None);
                if (GUILayout.Button("Browse..."))
                {
                    Defer(BrowseImage);
                }

                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(Path.GetFileName(imagePath) + "   " + imageTexture.width + " x " + imageTexture.height + " px", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("Browse...", GUILayout.Width(80f)))
            {
                Defer(BrowseImage);
            }

            if (GUILayout.Button("Clear", GUILayout.Width(60f)))
            {
                Defer(ClearImage);
            }

            EditorGUILayout.EndHorizontal();

            DrawImagePreview();
            EditorGUILayout.LabelField("Cyan = floor cell, red = not floor, yellow = whole level grid. Click the preview on a floor tile to pick the floor colour.", EditorStyles.miniLabel);

            EditorGUI.BeginChangeCheck();
            Color newColor = EditorGUILayout.ColorField(new GUIContent("Floor colour"), floorColor, false, false, false);
            float newTolerance = EditorGUILayout.Slider("Colour tolerance", floorTolerance, 10f, 120f);
            int newBorder = EditorGUILayout.IntSlider("Wall border (cells)", wallBorder, 0, 3);
            if (EditorGUI.EndChangeCheck())
            {
                Defer(() =>
                {
                    if (newColor != floorColor)
                    {
                        floorColor = newColor;
                        floorColorPicked = true;
                    }

                    floorTolerance = newTolerance;
                    wallBorder = newBorder;
                    analysisDirty = true;
                });
            }

            EditorGUILayout.BeginHorizontal();
            float shownCell = analysis != null ? analysis.CellSize : 0f;
            EditorGUI.BeginChangeCheck();
            float typedCell = EditorGUILayout.DelayedFloatField(cellSizeOverride > 0f ? "Cell size px (manual)" : "Cell size px (auto)", shownCell);
            if (EditorGUI.EndChangeCheck() && typedCell > 4f)
            {
                Defer(() =>
                {
                    cellSizeOverride = typedCell;
                    analysisDirty = true;
                });
            }

            if (GUILayout.Button("x2", GUILayout.Width(34f)))
            {
                Defer(() => SetCellSizeOverride(analysis != null ? analysis.CellSize * 2f : 0f));
            }

            if (GUILayout.Button("/2", GUILayout.Width(34f)))
            {
                Defer(() => SetCellSizeOverride(analysis != null ? analysis.CellSize * 0.5f : 0f));
            }

            if (GUILayout.Button("Auto", GUILayout.Width(50f)))
            {
                Defer(() => SetCellSizeOverride(0f));
            }

            EditorGUILayout.EndHorizontal();

            bool ready = analysis != null && analysis.HasFloor;
            int totalColumns = ready ? analysis.Columns + 2 * wallBorder : 0;
            int totalRows = ready ? analysis.Rows + 2 * wallBorder : 0;

            if (analysis != null && !analysis.HasFloor)
            {
                EditorGUILayout.HelpBox(analysis.Message, MessageType.Warning);
            }
            else if (ready)
            {
                string info = "Floor: " + analysis.Columns + " columns x " + analysis.Rows + " rows  (" + analysis.FloorCellCount
                              + " floor tiles, cell " + analysis.CellSize.ToString("0.#") + " px)\n"
                              + "With a " + wallBorder + "-cell wall border the level grid is  " + totalColumns + " wide x " + totalRows + " tall.";
                EditorGUILayout.HelpBox(info, MessageType.Info);
                if (!string.IsNullOrEmpty(analysis.Message))
                {
                    EditorGUILayout.HelpBox(analysis.Message, MessageType.Warning);
                }

                GUILayout.Label(BuildFloorText(), monoLabel);
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!ready))
            {
                if (GUILayout.Button("Set grid size  " + totalColumns + " x " + totalRows, GUILayout.Height(24f)))
                {
                    Defer(() => ApplyImageResult(false));
                }

                if (GUILayout.Button("Set size + floor shape", GUILayout.Height(24f)))
                {
                    Defer(() => ApplyImageResult(true));
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void SetCellSizeOverride(float value)
        {
            cellSizeOverride = value > 4f ? value : 0f;
            analysisDirty = true;
        }

        private string BuildFloorText()
        {
            var builder = new StringBuilder();
            for (int r = 0; r < analysis.Rows; r++)
            {
                for (int c = 0; c < analysis.Columns; c++)
                {
                    builder.Append(analysis.Floor[c, r] ? '.' : '#');
                }

                if (r < analysis.Rows - 1)
                {
                    builder.Append('\n');
                }
            }

            return builder.ToString();
        }

        private void DrawImagePreview()
        {
            float aspect = (float)imageTexture.width / imageTexture.height;
            Rect rect = GUILayoutUtility.GetAspectRect(aspect, GUILayout.MaxHeight(260f), GUILayout.MaxWidth(260f * aspect));
            float scale = rect.width / imageTexture.width;
            Event e = Event.current;

            if (e.type == EventType.Repaint)
            {
                GUI.DrawTexture(rect, imageTexture, ScaleMode.StretchToFill, true);

                if (analysis != null && analysis.HasFloor)
                {
                    float p = analysis.CellSize;
                    for (int c = 0; c < analysis.Columns; c++)
                    {
                        for (int r = 0; r < analysis.Rows; r++)
                        {
                            var cell = new Rect(
                                rect.x + (analysis.OriginX + c * p) * scale,
                                rect.y + (analysis.OriginY + r * p) * scale,
                                p * scale,
                                p * scale);
                            DrawOutline(cell, analysis.Floor[c, r] ? new Color(0f, 1f, 1f, 0.9f) : new Color(1f, 0.2f, 0.2f, 0.45f), 1f);
                        }
                    }

                    var total = new Rect(
                        rect.x + (analysis.OriginX - wallBorder * p) * scale,
                        rect.y + (analysis.OriginY - wallBorder * p) * scale,
                        (analysis.Columns + 2 * wallBorder) * p * scale,
                        (analysis.Rows + 2 * wallBorder) * p * scale);
                    DrawOutline(total, new Color(1f, 0.9f, 0.1f, 1f), 2f);
                }
            }

            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition) && imagePixels != null)
            {
                int w = imageTexture.width;
                int h = imageTexture.height;
                int ix = Mathf.Clamp((int)((e.mousePosition.x - rect.x) / scale), 0, w - 1);
                int iy = Mathf.Clamp((int)((e.mousePosition.y - rect.y) / scale), 0, h - 1);
                Color32 picked = imagePixels[(h - 1 - iy) * w + ix];
                Defer(() =>
                {
                    floorColor = picked;
                    floorColorPicked = true;
                    analysisDirty = true;
                });
                e.Use();
            }
        }

        /// <summary>
        /// Resizes the level to the measured grid (floor + wall border). With <paramref name="withShape"/> every cell that is
        /// not floor becomes a wall; barrels, switches and doors that still sit on floor are kept, cats are moved if needed.
        /// </summary>
        private void ApplyImageResult(bool withShape)
        {
            if (level == null || so == null || analysis == null || !analysis.HasFloor)
            {
                return;
            }

            int columns = analysis.Columns + 2 * wallBorder;
            int rows = analysis.Rows + 2 * wallBorder;
            bool clamped = columns > MaxGridSize || rows > MaxGridSize;
            columns = Mathf.Clamp(columns, 2, MaxGridSize);
            rows = Mathf.Clamp(rows, 2, MaxGridSize);

            if (!withShape)
            {
                ResizeGrid(columns, rows);
                SetStatus("Grid set to " + columns + " x " + rows + " from the picture." + (clamped ? " (Limited to " + MaxGridSize + ".)" : string.Empty), clamped ? MessageType.Warning : MessageType.Info);
                return;
            }

            LevelImageAnalysis shape = analysis;
            int border = wallBorder;
            Func<int, int, bool> isFloor = (x, y) =>
            {
                int c = x - border;
                int r = rows - 1 - y - border;
                return c >= 0 && r >= 0 && c < shape.Columns && r < shape.Rows && shape.Floor[c, r];
            };

            var kept = new List<ObstacleData>();
            foreach (ObstacleData o in level.Obstacles)
            {
                if (o.tileType != TileType.Wall && o.tileType != TileType.Empty && isFloor(o.position.x, o.position.y))
                {
                    kept.Add(o);
                }
            }

            so.Update();
            so.FindProperty("width").intValue = columns;
            so.FindProperty("height").intValue = rows;

            SerializedProperty list = so.FindProperty("obstacles");
            list.arraySize = 0;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    if (!isFloor(x, y))
                    {
                        list.arraySize++;
                        WriteElement(list.GetArrayElementAtIndex(list.arraySize - 1), TileType.Wall, new Vector2Int(x, y), 0, false);
                    }
                }
            }

            for (int i = 0; i < kept.Count; i++)
            {
                list.arraySize++;
                WriteElement(list.GetArrayElementAtIndex(list.arraySize - 1), kept[i].tileType, kept[i].position, kept[i].linkId, kept[i].initiallyOn);
            }

            // Keep the cats where they are if that is still floor; otherwise put them on the first free floor cells.
            var taken = new HashSet<Vector2Int>();
            for (int i = 0; i < kept.Count; i++)
            {
                taken.Add(kept[i].position);
            }

            Vector2Int a = so.FindProperty("catAStart").vector2IntValue;
            Vector2Int b = so.FindProperty("catBStart").vector2IntValue;

            // Candidate start cells: free floor that has a free floor neighbour for the hand. A takes the first one, B the last.
            var candidates = new List<Vector2Int>();
            for (int x = 0; x < columns; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!isFloor(x, y) || taken.Contains(cell))
                    {
                        continue;
                    }

                    for (int d = 0; d < DirectionExtensions.All.Length; d++)
                    {
                        Vector2Int neighbour = cell + DirectionExtensions.All[d].ToVector();
                        if (isFloor(neighbour.x, neighbour.y) && !taken.Contains(neighbour))
                        {
                            candidates.Add(cell);
                            break;
                        }
                    }
                }
            }

            if (!isFloor(a.x, a.y) || taken.Contains(a))
            {
                a = candidates.Count > 0
                    ? candidates[0]
                    : FirstFreeFloor(isFloor, columns, rows, taken, new Vector2Int(-1, -1));
            }

            taken.Add(a);
            if (!isFloor(b.x, b.y) || taken.Contains(b))
            {
                b = Vector2Int.zero;
                bool found = false;
                for (int i = candidates.Count - 1; i >= 0 && !found; i--)
                {
                    if (candidates[i] != a)
                    {
                        b = candidates[i];
                        found = true;
                    }
                }

                if (!found)
                {
                    b = FirstFreeFloor(isFloor, columns, rows, taken, a);
                }
            }

            so.FindProperty("catAStart").vector2IntValue = a;
            so.FindProperty("catBStart").vector2IntValue = b;

            // Turn each cat so that its hand starts on a free floor cell.
            var barrelCells = new HashSet<Vector2Int>();
            for (int i = 0; i < kept.Count; i++)
            {
                if (kept[i].tileType == TileType.Barrel)
                {
                    barrelCells.Add(kept[i].position);
                }
            }

            Func<Vector2Int, bool> freeForHand = cell => isFloor(cell.x, cell.y) && !barrelCells.Contains(cell) && cell != a && cell != b;
            SerializedProperty facingA = so.FindProperty("catAFacing");
            facingA.enumValueIndex = (int)ChooseFacing(a, (Direction)facingA.enumValueIndex, freeForHand);
            Vector2Int handA = a + ((Direction)facingA.enumValueIndex).ToVector();
            SerializedProperty facingB = so.FindProperty("catBFacing");
            facingB.enumValueIndex = (int)ChooseFacing(b, (Direction)facingB.enumValueIndex, cell => freeForHand(cell) && cell != handA);

            SetStatus("Level set to " + columns + " x " + rows + " with the floor shape from the picture. Cats and hands were placed on free floor cells; check them in the grid.", MessageType.Info);
            Commit();
        }

        private static Vector2Int FirstFreeFloor(Func<int, int, bool> isFloor, int columns, int rows, HashSet<Vector2Int> taken, Vector2Int avoid)
        {
            for (int x = 0; x < columns; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (isFloor(x, y) && !taken.Contains(cell) && cell != avoid)
                    {
                        return cell;
                    }
                }
            }

            return Vector2Int.zero;
        }

        private static Direction ChooseFacing(Vector2Int root, Direction current, Func<Vector2Int, bool> isFree)
        {
            if (isFree(root + current.ToVector()))
            {
                return current;
            }

            Direction[] order = { Direction.Right, Direction.Left, Direction.Up, Direction.Down };
            for (int i = 0; i < order.Length; i++)
            {
                if (isFree(root + order[i].ToVector()))
                {
                    return order[i];
                }
            }

            return current;
        }

        // ----------------------------------------------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------------------------------------------

        private void Defer(Action action)
        {
            deferred.Enqueue(action);
            Repaint();
        }

        private void SetStatus(string message, MessageType type)
        {
            status = message;
            statusType = type;
        }

        private void SetLevel(LevelDataSO newLevel)
        {
            level = newLevel;
            so = newLevel != null ? new SerializedObject(newLevel) : null;
            asciiText = so != null ? BuildAscii() : string.Empty;
            hoverCell = NoCell;
            lastPaintCell = NoCell;
            validationDirty = true;
            SetStatus(string.Empty, MessageType.None);
            Repaint();
        }

        /// <summary>Writes the ASCII mirror into the asset and applies every pending change as one undo step.</summary>
        private void Commit()
        {
            string ascii = BuildAscii();
            so.FindProperty("asciiLayout").stringValue = ascii;
            so.ApplyModifiedProperties();
            asciiText = ascii;
            validationDirty = true;
            Repaint();
        }

        private void RunValidation()
        {
            string text;
            isValid = level.Validate(out text);
            report = text ?? string.Empty;
            validationDirty = false;
        }

        private void RebuildList()
        {
            allLevels.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:LevelDataSO"))
            {
                LevelDataSO asset = AssetDatabase.LoadAssetAtPath<LevelDataSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null)
                {
                    allLevels.Add(asset);
                }
            }

            allLevels.Sort((x, y) => string.Compare(x.name, y.name, StringComparison.OrdinalIgnoreCase));
            listDirty = false;
        }

        private string Describe(Vector2Int cell)
        {
            if (cell == level.CatAStart)
            {
                return "Cat A start, facing " + level.CatAFacing;
            }

            if (cell == level.CatBStart)
            {
                return "Cat B start, facing " + level.CatBFacing;
            }

            if (cell == level.GetHandStart(CatId.CatA))
            {
                return "Cat A hand start";
            }

            if (cell == level.GetHandStart(CatId.CatB))
            {
                return "Cat B hand start";
            }

            IReadOnlyList<ObstacleData> obstacles = level.Obstacles;
            for (int i = 0; i < obstacles.Count; i++)
            {
                ObstacleData o = obstacles[i];
                if (o.position == cell && o.tileType != TileType.Empty)
                {
                    string text = o.tileType.ToString();
                    if (o.tileType == TileType.SwitchMomentary || o.tileType == TileType.SwitchToggle || o.tileType == TileType.Door)
                    {
                        text += ", link " + o.linkId;
                    }

                    if (o.tileType == TileType.SwitchToggle && o.initiallyOn)
                    {
                        text += ", starts ON";
                    }

                    return text;
                }
            }

            return "Empty";
        }

        /// <summary>Builds the ASCII text from the serialized (possibly not yet applied) state. First line = top row.</summary>
        private string BuildAscii()
        {
            int width = Mathf.Max(2, so.FindProperty("width").intValue);
            int height = Mathf.Max(2, so.FindProperty("height").intValue);
            var grid = new char[width, height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    grid[x, y] = '.';
                }
            }

            SerializedProperty list = so.FindProperty("obstacles");
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                Vector2Int p = element.FindPropertyRelative("position").vector2IntValue;
                if (p.x < 0 || p.y < 0 || p.x >= width || p.y >= height)
                {
                    continue;
                }

                TileType type = (TileType)element.FindPropertyRelative("tileType").enumValueIndex;
                int link = Mathf.Clamp(element.FindPropertyRelative("linkId").intValue, 1, 9);
                switch (type)
                {
                    case TileType.Wall: grid[p.x, p.y] = '#'; break;
                    case TileType.Barrel: grid[p.x, p.y] = 'O'; break;
                    case TileType.SwitchMomentary: grid[p.x, p.y] = (char)('0' + link); break;
                    case TileType.SwitchToggle: grid[p.x, p.y] = (char)('a' + link - 1); break;
                    case TileType.Door: grid[p.x, p.y] = (char)('A' + link - 1); break;
                }
            }

            Vector2Int a = so.FindProperty("catAStart").vector2IntValue;
            Vector2Int b = so.FindProperty("catBStart").vector2IntValue;
            if (a.x >= 0 && a.y >= 0 && a.x < width && a.y < height)
            {
                grid[a.x, a.y] = 'P';
            }

            if (b.x >= 0 && b.y >= 0 && b.x < width && b.y < height)
            {
                grid[b.x, b.y] = 'Q';
            }

            var builder = new StringBuilder();
            for (int row = 0; row < height; row++)
            {
                int y = height - 1 - row;
                for (int x = 0; x < width; x++)
                {
                    builder.Append(grid[x, y]);
                }

                builder.Append('\n');
            }

            builder.Append("@A=").Append(FacingLetter(so.FindProperty("catAFacing").enumValueIndex));
            builder.Append(" B=").Append(FacingLetter(so.FindProperty("catBFacing").enumValueIndex));
            return builder.ToString();
        }

        private static int IndexOf(SerializedProperty list, Vector2Int cell)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).FindPropertyRelative("position").vector2IntValue == cell)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void WriteElement(SerializedProperty element, TileType type, Vector2Int position, int link, bool initiallyOn)
        {
            element.FindPropertyRelative("tileType").enumValueIndex = (int)type;
            element.FindPropertyRelative("position").vector2IntValue = position;
            element.FindPropertyRelative("linkId").intValue = link;
            element.FindPropertyRelative("initiallyOn").boolValue = initiallyOn;
        }

        private static TileType ToTileType(PaintTool paintTool)
        {
            switch (paintTool)
            {
                case PaintTool.Wall: return TileType.Wall;
                case PaintTool.Barrel: return TileType.Barrel;
                case PaintTool.SwitchHold: return TileType.SwitchMomentary;
                case PaintTool.SwitchToggle: return TileType.SwitchToggle;
                case PaintTool.Door: return TileType.Door;
                default: return TileType.Empty;
            }
        }

        private Rect CellRect(Rect area, int x, int y, int height)
        {
            return new Rect(area.x + x * cellSize, area.y + (height - 1 - y) * cellSize, cellSize, cellSize);
        }

        private Vector2Int ToCell(Rect area, Vector2 mouse, int width, int height)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((mouse.x - area.x) / cellSize), 0, width - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt((mouse.y - area.y) / cellSize), 0, height - 1);
            return new Vector2Int(x, height - 1 - row);
        }

        private static void DrawOutline(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private void EnsureStyles()
        {
            if (cellLabel == null)
            {
                cellLabel = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
                cellLabel.normal.textColor = Color.white;
            }

            if (monoArea == null)
            {
                monoArea = new GUIStyle(EditorStyles.textArea) { wordWrap = false };
                monoFont = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Menlo", "Courier New" }, 13);
                if (monoFont != null)
                {
                    monoFont.hideFlags = HideFlags.HideAndDontSave;
                    monoArea.font = monoFont;
                }
            }

            if (monoLabel == null)
            {
                monoLabel = new GUIStyle(EditorStyles.label) { wordWrap = false };
                if (monoArea.font != null)
                {
                    monoLabel.font = monoArea.font;
                }
            }
        }
    }
}
