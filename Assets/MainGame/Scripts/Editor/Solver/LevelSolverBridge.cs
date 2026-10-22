using System.Collections.Generic;
using System.Text;
using Catshakes.Solver;
using UnityEditor;
using UnityEngine;

namespace Catshakes.EditorTools
{
    /// <summary>Converts between <see cref="LevelDataSO"/> assets and the pure solver model, and checks every level from a menu.</summary>
    public static class LevelSolverBridge
    {
        public static PuzzleSpec ToSpec(LevelDataSO level)
        {
            int w = level.Width;
            int h = level.Height;
            var spec = new PuzzleSpec(w, h);

            spec.Root[0] = Cell(level.CatAStart, w, h);
            spec.Root[1] = Cell(level.CatBStart, w, h);
            spec.Facing[0] = (int)level.CatAFacing;
            spec.Facing[1] = (int)level.CatBFacing;
            spec.MaxSteps[0] = level.CatAMaxSteps;
            spec.MaxSteps[1] = level.CatBMaxSteps;

            IReadOnlyList<ObstacleData> obstacles = level.Obstacles;
            for (int i = 0; i < obstacles.Count; i++)
            {
                ObstacleData o = obstacles[i];
                int cell = Cell(o.position, w, h);
                if (cell < 0)
                {
                    continue;
                }

                switch (o.tileType)
                {
                    case TileType.Wall:
                    case TileType.Void: spec.Wall[cell] = true; break;
                    case TileType.Barrel: spec.Barrels.Add(cell); break;
                    case TileType.SwitchMomentary: spec.Switches.Add(new SwitchSpec { Cell = cell, Link = o.linkId, Toggle = false }); break;
                    case TileType.SwitchToggle: spec.Switches.Add(new SwitchSpec { Cell = cell, Link = o.linkId, Toggle = true, StartsOn = o.initiallyOn }); break;
                    case TileType.Door: spec.Doors.Add(new DoorSpec { Cell = cell, Link = o.linkId, StartsOpen = o.initiallyOn }); break;
                }
            }

            return spec;
        }

        private static int Cell(Vector2Int p, int w, int h)
        {
            return p.x < 0 || p.y < 0 || p.x >= w || p.y >= h ? -1 : p.y * w + p.x;
        }

        /// <summary>Writes a generated level into the asset behind <paramref name="so"/> (one undo step).</summary>
        public static void ApplySpec(SerializedObject so, PuzzleSpec spec, string displayName, string solution)
        {
            so.Update();
            so.FindProperty("displayName").stringValue = displayName;
            so.FindProperty("width").intValue = spec.Width;
            so.FindProperty("height").intValue = spec.Height;
            so.FindProperty("catAStart").vector2IntValue = new Vector2Int(spec.Root[0] % spec.Width, spec.Root[0] / spec.Width);
            so.FindProperty("catBStart").vector2IntValue = new Vector2Int(spec.Root[1] % spec.Width, spec.Root[1] / spec.Width);
            so.FindProperty("catAMaxSteps").intValue = spec.MaxSteps[0];
            so.FindProperty("catBMaxSteps").intValue = spec.MaxSteps[1];
            so.FindProperty("catAFacing").enumValueIndex = spec.Facing[0];
            so.FindProperty("catBFacing").enumValueIndex = spec.Facing[1];

            var items = new List<ObstacleData>();
            for (int c = 0; c < spec.Wall.Length; c++)
            {
                if (spec.Wall[c]) items.Add(new ObstacleData(TileType.Wall, At(spec, c)));
            }

            for (int i = 0; i < spec.Barrels.Count; i++) items.Add(new ObstacleData(TileType.Barrel, At(spec, spec.Barrels[i])));
            for (int i = 0; i < spec.Switches.Count; i++)
            {
                SwitchSpec s = spec.Switches[i];
                items.Add(new ObstacleData(s.Toggle ? TileType.SwitchToggle : TileType.SwitchMomentary, At(spec, s.Cell), s.Link, s.Toggle && s.StartsOn));
            }

            for (int i = 0; i < spec.Doors.Count; i++)
            {
                DoorSpec d = spec.Doors[i];
                items.Add(new ObstacleData(TileType.Door, At(spec, d.Cell), d.Link, d.StartsOpen));
            }

            SerializedProperty list = so.FindProperty("obstacles");
            list.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("tileType").enumValueIndex = (int)items[i].tileType;
                element.FindPropertyRelative("position").vector2IntValue = items[i].position;
                element.FindPropertyRelative("linkId").intValue = items[i].linkId;
                element.FindPropertyRelative("initiallyOn").boolValue = items[i].initiallyOn;
            }

            so.FindProperty("asciiLayout").stringValue = spec.ToAscii();
            so.FindProperty("solutionText").stringValue = solution ?? string.Empty;
            so.ApplyModifiedProperties();
        }

        private static Vector2Int At(PuzzleSpec spec, int cell)
        {
            return new Vector2Int(cell % spec.Width, cell / spec.Width);
        }

        public static string Describe(SolveResult r, SolutionStats s)
        {
            switch (r.Status)
            {
                case SolveStatus.Solved:
                    var sb = new StringBuilder();
                    sb.Append("Solvable in ").Append(r.Moves.Count).Append(" moves");
                    if (s != null)
                    {
                        sb.Append(" (cat A ").Append(s.MovesPerCat[0]).Append(", cat B ").Append(s.MovesPerCat[1])
                          .Append(", longest arm ").Append(s.MaxArm[0]).Append(" / ").Append(s.MaxArm[1]).Append(')');
                    }

                    sb.Append(". Searched ").Append(r.Explored).Append(" states in ").Append(r.Ms).Append(" ms.");
                    return sb.ToString();
                case SolveStatus.NoSolution:
                    return "NOT solvable. " + r.Message + " (" + r.Explored + " states searched)";
                case SolveStatus.LimitReached:
                    return "Unknown: " + r.Message + " (" + r.Explored + " states searched, no solution found yet).";
                case SolveStatus.Cancelled:
                    return "Search cancelled.";
                default:
                    return "Cannot be solved: " + r.Message;
            }
        }

        [MenuItem("Catshakes/Check All Levels (Solver)")]
        private static void CheckAllLevels()
        {
            string[] guids = AssetDatabase.FindAssets("t:LevelDataSO");
            var lines = new List<string>();
            int solved = 0, broken = 0, unknown = 0;

            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    LevelDataSO level = AssetDatabase.LoadAssetAtPath<LevelDataSO>(path);
                    if (level == null)
                    {
                        continue;
                    }

                    if (EditorUtility.DisplayCancelableProgressBar("Checking levels", level.name, (float)i / guids.Length))
                    {
                        lines.Add("Cancelled.");
                        break;
                    }

                    string text;
                    if (!level.Validate(out text))
                    {
                        lines.Add(level.name + ": INVALID LEVEL DATA\n" + text);
                        broken++;
                        continue;
                    }

                    if (!string.IsNullOrEmpty(text))
                    {
                        lines.Add(level.name + ": warning - " + text.Trim().Replace("\n", " | "));
                    }

                    var sim = new PuzzleSim(ToSpec(level));
                    SolveResult result = PuzzleSolver.Solve(sim, new SolveOptions { MaxStates = 2000000, TimeLimitMs = 20000 });
                    SolutionStats stats = result.Status == SolveStatus.Solved ? PuzzleSolver.Replay(sim, result.Moves, null) : null;
                    string prefix = level.name + ": ";
                    if (result.Status == SolveStatus.Solved)
                    {
                        solved++;
                        lines.Add(prefix + "OK, " + result.Moves.Count + " moves  " + PuzzleSolver.Format(result.Moves));
                    }
                    else if (result.Status == SolveStatus.NoSolution)
                    {
                        broken++;
                        lines.Add(prefix + "NOT SOLVABLE (" + result.Explored + " states searched)");
                    }
                    else
                    {
                        unknown++;
                        lines.Add(prefix + "unknown, " + Describe(result, stats));
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            string summary = "Level check: " + solved + " solvable, " + broken + " broken, " + unknown + " unknown (too big to prove).\n" + string.Join("\n", lines);
            if (broken > 0)
            {
                Debug.LogWarning(summary);
            }
            else
            {
                Debug.Log(summary);
            }
        }
    }
}
