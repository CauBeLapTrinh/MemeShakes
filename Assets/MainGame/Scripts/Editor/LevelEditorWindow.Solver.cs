using System;
using System.Collections.Generic;
using Catshakes.Solver;
using UnityEditor;
using UnityEngine;

namespace Catshakes.EditorTools
{
    /// <summary>Solver (is the level solvable? show the shortest solution) and level generator of the Level Editor.</summary>
    public sealed partial class LevelEditorWindow
    {
        private static readonly Color CatAColor = new Color(0.95f, 0.55f, 0.15f);
        private static readonly Color CatBColor = new Color(0.25f, 0.55f, 0.95f);
        private static readonly string[] DifficultyLabels = { "Easy", "Medium", "Hard" };
        private static readonly string[] MoveNames = { "Up", "Down", "Left", "Right" };

        [SerializeField] private bool autoSolve = true;
        [SerializeField] private bool showSolver = true;
        [SerializeField] private bool showGenerator = true;
        [SerializeField] private int solverStateLimit = 1500000;

        [SerializeField] private int genWidth = 10;
        [SerializeField] private int genHeight = 8;
        [SerializeField] private GenDifficulty genDifficulty = GenDifficulty.Medium;
        [SerializeField] private int genSeed;
        [SerializeField] private int genDoors = -1;
        [SerializeField] private int genBarrels = -1;
        [SerializeField] private int genSlack = -1;
        [SerializeField] private int genSeconds = 30;

        [NonSerialized] private PuzzleSim solverSim;
        [NonSerialized] private List<PuzzleState> solverStates;
        [NonSerialized] private List<int> solverMoves;
        [NonSerialized] private SolutionStats solverStats;
        [NonSerialized] private string solverMessage = string.Empty;
        [NonSerialized] private MessageType solverMessageType = MessageType.None;
        [NonSerialized] private string solverSignature = string.Empty;
        [NonSerialized] private string generatorMessage = string.Empty;
        [NonSerialized] private int solverStep;

        // ----------------------------------------------------------------------------------------------------
        // Hooks called from the main window file
        // ----------------------------------------------------------------------------------------------------

        private void ClearSolverResult()
        {
            solverSim = null;
            solverStates = null;
            solverMoves = null;
            solverStats = null;
            solverMessage = string.Empty;
            solverMessageType = MessageType.None;
            solverSignature = string.Empty;
            solverStep = 0;
        }

        private void SetTab(int tab)
        {
            mainTab = tab;
            mainScroll = Vector2.zero;

            // The solver result is only kept up to date while its tab is open; re-check when it is shown again.
            solverSignature = string.Empty;
            validationDirty = true;
        }

        /// <summary>The "Solve + Generate" tab: the solver for the selected level and the level generator.</summary>
        private void DrawSolveGenerateTab()
        {
            EditorGUILayout.Space(4f);
            if (level == null || so == null)
            {
                EditorGUILayout.HelpBox("Pick a level on the left to solve it. The generator below works without a level.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(level.name + "   " + level.Width + "x" + level.Height, EditorStyles.boldLabel);
                if (GUILayout.Button("Edit this level", GUILayout.Width(100f)))
                {
                    Defer(() => SetTab(TabEdit));
                }

                EditorGUILayout.EndHorizontal();

                if (isValid && string.IsNullOrEmpty(report))
                {
                    EditorGUILayout.HelpBox("Level is valid.", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(report.TrimEnd(), isValid ? MessageType.Warning : MessageType.Error);
                }

                DrawSolverSection();
            }

            EditorGUILayout.Space(6f);
            DrawGeneratorSection();
        }

        /// <summary>Runs after every validation (start of a Layout pass): re-checks that the level is solvable.</summary>
        private void AfterValidation()
        {
            if (level == null || mainTab != TabSolveGenerate)
            {
                return;
            }

            if (!isValid)
            {
                ClearSolverResult();
                return;
            }

            string signature = EditorJsonUtility.ToJson(level);
            if (signature == solverSignature && !string.IsNullOrEmpty(solverMessage))
            {
                return;
            }

            if (autoSolve)
            {
                RunSolver(true, false);
            }
            else if (solverStates == null)
            {
                TryReplayStored();
            }

            solverSignature = signature;
        }

        // ----------------------------------------------------------------------------------------------------
        // Solver
        // ----------------------------------------------------------------------------------------------------

        private void RunSolver(bool quick, bool storeSolution)
        {
            if (level == null || !isValid)
            {
                return;
            }

            var sim = new PuzzleSim(LevelSolverBridge.ToSpec(level));
            var options = new SolveOptions();
            if (quick)
            {
                options.MaxStates = 40000;
                options.TimeLimitMs = 250;
            }
            else
            {
                options.MaxStates = Mathf.Max(1000, solverStateLimit);
                options.TimeLimitMs = 120000;
                options.Cancel = count => EditorUtility.DisplayCancelableProgressBar("Solving " + level.name, count + " states searched", 0.5f);
            }

            SolveResult result;
            try
            {
                result = PuzzleSolver.Solve(sim, options);
            }
            finally
            {
                if (!quick)
                {
                    EditorUtility.ClearProgressBar();
                }
            }

            solverSignature = EditorJsonUtility.ToJson(level);
            if (result.Status != SolveStatus.Solved)
            {
                solverSim = null;
                solverStates = null;
                solverMoves = null;
                solverStats = null;
                string text = LevelSolverBridge.Describe(result, null);
                if (quick && result.Status == SolveStatus.LimitReached)
                {
                    text = "Not checked automatically (the level is too big for a quick check). Press Solve.";
                }

                solverMessage = text;
                solverMessageType = result.Status == SolveStatus.NoSolution || result.Status == SolveStatus.Invalid ? MessageType.Error : MessageType.Warning;
                return;
            }

            ShowSolution(sim, result.Moves, LevelSolverBridge.Describe(result, null));

            if (storeSolution && so != null)
            {
                so.Update();
                so.FindProperty("solutionText").stringValue = PuzzleSolver.Format(result.Moves);
                so.ApplyModifiedProperties();
                SetStatus("Solution stored in the level asset.", MessageType.Info);
            }
        }

        private void ShowSolution(PuzzleSim sim, List<int> moves, string message)
        {
            var states = new List<PuzzleState>();
            SolutionStats stats = PuzzleSolver.Replay(sim, moves, states);
            if (!stats.Valid)
            {
                solverMessage = "The solution does not replay correctly (solver bug?).";
                solverMessageType = MessageType.Error;
                return;
            }

            solverSim = sim;
            solverMoves = moves;
            solverStates = states;
            solverStats = stats;
            solverStep = 0;
            solverMessage = message + "\nUses: " + UsesText(stats);
            solverMessageType = MessageType.Info;
        }

        private static string UsesText(SolutionStats stats)
        {
            var parts = new List<string>();
            if (stats.BarrelPushes > 0) parts.Add("push barrel x" + stats.BarrelPushes);
            if (stats.MomentaryPresses > 0) parts.Add("hold switch x" + stats.MomentaryPresses);
            if (stats.ToggleFlips > 0) parts.Add("toggle switch x" + stats.ToggleFlips);
            if (stats.DoorChanges > 0) parts.Add("doors change x" + stats.DoorChanges);
            if (stats.Cuts > 0) parts.Add("arm cut x" + stats.Cuts);
            if (stats.CatPushes > 0) parts.Add("push the other cat x" + stats.CatPushes);
            if (stats.Retracts > 0) parts.Add("retract x" + stats.Retracts);
            return parts.Count == 0 ? "nothing special (hands just walk to each other)" : string.Join(", ", parts);
        }

        private void TryReplayStored()
        {
            if (level == null || !isValid || string.IsNullOrEmpty(level.SolutionText))
            {
                return;
            }

            var moves = new List<int>();
            if (!PuzzleSolver.TryParse(level.SolutionText, moves))
            {
                return;
            }

            var sim = new PuzzleSim(LevelSolverBridge.ToSpec(level));
            var states = new List<PuzzleState>();
            SolutionStats stats = PuzzleSolver.Replay(sim, moves, states);
            if (stats.Valid)
            {
                ShowSolution(sim, moves, "Stored solution replays correctly: " + moves.Count + " moves.");
            }
            else
            {
                solverMessage = "The stored solution does not solve this level any more. Press Solve.";
                solverMessageType = MessageType.Warning;
            }
        }

        private void DrawSolverSection()
        {
            bool open = EditorGUILayout.Foldout(showSolver, "Solver - is this level solvable?", true);
            if (open != showSolver)
            {
                Defer(() => showSolver = open);
            }

            if (!showSolver)
            {
                return;
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!isValid))
            {
                if (GUILayout.Button("Solve (shortest solution)", GUILayout.Height(22f)))
                {
                    Defer(() => RunSolver(false, true));
                }

                if (GUILayout.Button("Replay stored solution", GUILayout.Height(22f), GUILayout.Width(150f)))
                {
                    Defer(() =>
                    {
                        solverMessage = string.Empty;
                        TryReplayStored();
                        if (solverStates == null && string.IsNullOrEmpty(solverMessage))
                        {
                            solverMessage = "This level has no stored solution yet. Press Solve.";
                            solverMessageType = MessageType.Info;
                        }
                    });
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            bool wantAuto = EditorGUILayout.ToggleLeft(new GUIContent("Auto-check on edit", "Quick check (limited to 250 ms) every time the level changes."), autoSolve, GUILayout.Width(140f));
            if (wantAuto != autoSolve)
            {
                Defer(() =>
                {
                    autoSolve = wantAuto;
                    solverSignature = string.Empty;
                    validationDirty = true;
                });
            }

            solverStateLimit = Mathf.Max(1000, EditorGUILayout.IntField(new GUIContent("State limit", "Most game states the Solve button explores. Larger levels need more."), solverStateLimit));
            EditorGUILayout.EndHorizontal();

            if (!isValid)
            {
                EditorGUILayout.HelpBox("Fix the validation errors first.", MessageType.None);
                return;
            }

            if (!string.IsNullOrEmpty(solverMessage))
            {
                EditorGUILayout.HelpBox(solverMessage, solverMessageType);
            }

            if (solverStates == null || solverMoves == null || solverSim == null)
            {
                return;
            }

            string solutionText = PuzzleSolver.Format(solverMoves);
            EditorGUILayout.LabelField("Solution (A = cat A, B = cat B; U/D/L/R = hand direction)", EditorStyles.miniBoldLabel);
            EditorGUILayout.SelectableLabel(solutionText, EditorStyles.textArea, GUILayout.Height(Mathf.Clamp(20f + 14f * (solutionText.Length / 70), 36f, 90f)));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(28f))) solverStep = Mathf.Max(0, solverStep - 1);
            solverStep = EditorGUILayout.IntSlider(solverStep, 0, solverMoves.Count);
            if (GUILayout.Button(">", GUILayout.Width(28f))) solverStep = Mathf.Min(solverMoves.Count, solverStep + 1);
            EditorGUILayout.EndHorizontal();

            solverStep = Mathf.Clamp(solverStep, 0, solverStates.Count - 1);
            string stepText = solverStep == 0
                ? "Start"
                : "Move " + solverStep + "/" + solverMoves.Count + ": cat " + (solverMoves[solverStep - 1] >> 2 == 0 ? "A" : "B") + " hand " + MoveNames[solverMoves[solverStep - 1] & 3];
            EditorGUILayout.LabelField(stepText + (solverStates[solverStep].Won ? "   - the hands meet!" : string.Empty), EditorStyles.boldLabel);

            DrawSolutionBoard(solverSim, solverStates[solverStep]);
            EditorGUILayout.LabelField("Orange = cat A, blue = cat B (bright cell = hand, tinted = arm). Brown O = barrel / severed hand. S = hold switch, T = toggle switch (* = on). D = closed door, d = open door.", EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawSolutionBoard(PuzzleSim sim, PuzzleState s)
        {
            float cs = Mathf.Clamp((position.width - 260f) / Mathf.Max(1, sim.W), 14f, Mathf.Min(cellSize, 48f));
            Rect area = GUILayoutUtility.GetRect(sim.W * cs + 2f, sim.H * cs + 2f, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            cellLabel.fontSize = Mathf.Max(8, Mathf.RoundToInt(cs * 0.36f));
            for (int y = 0; y < sim.H; y++)
            {
                for (int x = 0; x < sim.W; x++)
                {
                    int c = y * sim.W + x;
                    var r = new Rect(area.x + x * cs + 1f, area.y + (sim.H - 1 - y) * cs + 1f, cs - 2f, cs - 2f);
                    EditorGUI.DrawRect(r, ((x + y) & 1) == 0 ? new Color(0.30f, 0.30f, 0.32f) : new Color(0.26f, 0.26f, 0.28f));
                    string text = null;

                    if (sim.Wall[c])
                    {
                        EditorGUI.DrawRect(r, new Color(0.12f, 0.12f, 0.14f));
                    }

                    for (int i = 0; i < sim.Switches.Length; i++)
                    {
                        if (sim.Switches[i].Cell != c) continue;
                        Color link = Color.HSVToRGB(Mathf.Repeat((sim.Switches[i].Link - 1) / 9f, 1f), 0.55f, s.SwitchActive[i] ? 1f : 0.6f);
                        EditorGUI.DrawRect(r, link);
                        text = (sim.Switches[i].Toggle ? "T" : "S") + sim.Switches[i].Link + (s.SwitchActive[i] ? "*" : string.Empty);
                    }

                    for (int i = 0; i < sim.Doors.Length; i++)
                    {
                        if (sim.Doors[i].Cell != c) continue;
                        Color link = Color.HSVToRGB(Mathf.Repeat((sim.Doors[i].Link - 1) / 9f, 1f), 0.55f, 0.85f);
                        EditorGUI.DrawRect(r, s.DoorOpen[i] ? Color.Lerp(link, new Color(0.3f, 0.3f, 0.32f), 0.7f) : link * 0.8f);
                        text = (s.DoorOpen[i] ? "d" : "D") + sim.Doors[i].Link;
                    }

                    if (s.HasBarrel(c))
                    {
                        EditorGUI.DrawRect(r, new Color(0.62f, 0.40f, 0.20f));
                        text = "O";
                    }

                    for (int cat = 0; cat < 2; cat++)
                    {
                        Color color = cat == 0 ? CatAColor : CatBColor;
                        if (s.ArmContains(cat, c))
                        {
                            bool hand = s.Hand(cat) == c;
                            EditorGUI.DrawRect(r, hand ? color : new Color(color.r, color.g, color.b, 0.5f));
                            text = hand ? (cat == 0 ? "a" : "b") : text;
                        }

                        if (s.Root[cat] == c)
                        {
                            EditorGUI.DrawRect(r, color * 0.8f);
                            text = cat == 0 ? "A" : "B";
                        }
                    }

                    if (text != null)
                    {
                        GUI.Label(r, text, cellLabel);
                    }
                }
            }
        }

        // ----------------------------------------------------------------------------------------------------
        // Generator
        // ----------------------------------------------------------------------------------------------------

        private void DrawGeneratorSection()
        {
            bool open = EditorGUILayout.Foldout(showGenerator, "Generator - invent a new level (the solver proves it can be solved)", true);
            if (open != showGenerator)
            {
                Defer(() => showGenerator = open);
            }

            if (!showGenerator)
            {
                return;
            }

            EditorGUILayout.BeginHorizontal();
            genWidth = Mathf.Clamp(EditorGUILayout.IntField("Width", genWidth), 5, MaxGridSize);
            genHeight = Mathf.Clamp(EditorGUILayout.IntField("Height", genHeight), 4, MaxGridSize);
            EditorGUILayout.EndHorizontal();

            genDifficulty = (GenDifficulty)GUILayout.Toolbar((int)genDifficulty, DifficultyLabels);

            EditorGUILayout.BeginHorizontal();
            genDoors = Mathf.Clamp(EditorGUILayout.IntField(new GUIContent("Doors (-1 auto)", "Number of doors tried. Doors the solution does not need are removed again."), genDoors), -1, 6);
            genBarrels = Mathf.Clamp(EditorGUILayout.IntField(new GUIContent("Barrels (-1 auto)"), genBarrels), -1, 8);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            genSlack = Mathf.Clamp(EditorGUILayout.IntField(new GUIContent("Extra steps (-1 auto)", "Steps given to each cat on top of the minimum the puzzle needs."), genSlack), -1, 10);
            genSeed = EditorGUILayout.IntField(new GUIContent("Seed (0 random)", "The same seed and settings give the same level."), genSeed);
            genSeconds = Mathf.Clamp(EditorGUILayout.IntField("Time limit (s)", genSeconds), 3, 600);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate NEW level asset", GUILayout.Height(24f)))
            {
                Defer(() => RunGenerator(false));
            }

            using (new EditorGUI.DisabledScope(level == null))
            {
                if (GUILayout.Button("Generate into THIS level", GUILayout.Height(24f)))
                {
                    Defer(() => RunGenerator(true));
                }
            }

            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(generatorMessage))
            {
                EditorGUILayout.HelpBox(generatorMessage, MessageType.None);
            }
        }

        private void RunGenerator(bool replaceCurrent)
        {
            if (replaceCurrent && (level == null || so == null))
            {
                return;
            }

            if (replaceCurrent && !EditorUtility.DisplayDialog("Generate into this level", "Replace the whole content of '" + level.name + "'? (Ctrl+Z undoes it.)", "Replace", "Cancel"))
            {
                return;
            }

            var options = new GeneratorOptions
            {
                Width = genWidth,
                Height = genHeight,
                Difficulty = genDifficulty,
                Seed = genSeed,
                Doors = genDoors,
                Barrels = genBarrels,
                Slack = genSlack,
                TimeLimitMs = genSeconds * 1000L,
                MaxAttempts = 1000000
            };

            GeneratedLevel generated;
            try
            {
                long limit = options.TimeLimitMs;
                options.Cancel = (attempts, ms) => EditorUtility.DisplayCancelableProgressBar(
                    "Generating a " + genDifficulty + " level", "Attempt " + attempts + "  (" + (ms / 1000) + " s)", Mathf.Clamp01(ms / (float)limit));
                generated = LevelGenerator.Generate(options);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (generated == null)
            {
                generatorMessage = "No level found within the limits. Try again (new seed), a different size or difficulty, or more time.";
                SetStatus(generatorMessage, MessageType.Warning);
                return;
            }

            string solution = PuzzleSolver.Format(generated.Solution);
            LevelDataSO target;
            if (replaceCurrent)
            {
                Undo.RecordObject(level, "Generate level");
                so.Update();
                LevelSolverBridge.ApplySpec(so, generated.Spec, so.FindProperty("displayName").stringValue, solution);
                target = level;
            }
            else
            {
                target = CreateGeneratedAsset(generated.Spec, solution);
                if (target == null)
                {
                    return;
                }

                listDirty = true;
                SetLevel(target);
                EditorGUIUtility.PingObject(target);
            }

            asciiText = BuildAscii();
            validationDirty = true;
            ClearSolverResult();
            ShowSolution(new PuzzleSim(generated.Spec), generated.Solution, "Generated level (seed " + generated.Seed + "): solvable in " + generated.Solution.Count + " moves.");

            generatorMessage = generated.Summary + "\nSeed " + generated.Seed + ", " + generated.Attempts + " layouts tried in " + generated.Ms + " ms.";
            SetStatus("Generated " + AssetDatabase.GetAssetPath(target) + ". Add it to GameSceneManager > Levels to see it in the menu.", MessageType.Info);
        }

        private LevelDataSO CreateGeneratedAsset(PuzzleSpec spec, string solution)
        {
            if (!AssetDatabase.IsValidFolder(LevelFolder))
            {
                AssetDatabase.CreateFolder("Assets/MainGame", "Levels");
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
            AssetDatabase.CreateAsset(created, path);
            LevelSolverBridge.ApplySpec(new SerializedObject(created), spec, "Level " + (number - 1), solution);
            AssetDatabase.SaveAssets();
            return created;
        }
    }
}
