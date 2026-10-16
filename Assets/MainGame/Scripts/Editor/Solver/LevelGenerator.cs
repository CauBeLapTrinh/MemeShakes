using System;
using System.Collections.Generic;
using System.Text;

namespace Catshakes.Solver
{
    public enum GenDifficulty
    {
        Easy = 0,
        Medium = 1,
        Hard = 2
    }

    public sealed class GeneratorOptions
    {
        public int Width = 10;
        public int Height = 8;
        public GenDifficulty Difficulty = GenDifficulty.Medium;

        /// <summary>0 = a different level every time.</summary>
        public int Seed;

        /// <summary>-1 = decided by the difficulty.</summary>
        public int Doors = -1;
        public int Barrels = -1;

        /// <summary>Extra steps given to each cat on top of the minimum the puzzle needs (-1 = by difficulty).</summary>
        public int Slack = -1;

        public int MaxAttempts = 3000;
        public long TimeLimitMs = 30000;
        public int StateCapPerSolve = 120000;

        /// <summary>Called with (attempts so far, seconds elapsed). Return true to stop.</summary>
        public Func<int, long, bool> Cancel;
    }

    public sealed class GeneratedLevel
    {
        public PuzzleSpec Spec;
        public List<int> Solution;
        public SolutionStats Stats;
        public int Attempts;
        public long Ms;
        public int Seed;
        public string Summary = string.Empty;
    }

    /// <summary>
    /// Invents random levels and keeps only the ones the solver can prove solvable, whose solution needs the puzzle pieces,
    /// and whose step budgets are as small as the puzzle allows.
    /// </summary>
    public static class LevelGenerator
    {
        public static GeneratedLevel Generate(GeneratorOptions o)
        {
            int seed = o.Seed != 0 ? o.Seed : Environment.TickCount & 0x7FFFFFFF;
            var rng = new Random(seed);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            int attempts = 0;

            while (attempts < o.MaxAttempts && timer.ElapsedMilliseconds < o.TimeLimitMs)
            {
                if (o.Cancel != null && o.Cancel(attempts, timer.ElapsedMilliseconds)) break;
                attempts++;

                PuzzleSpec spec = BuildRandom(o, rng);
                if (spec == null) continue;

                GeneratedLevel result = Evaluate(spec, o);
                if (result == null) continue;

                result.Attempts = attempts;
                result.Ms = timer.ElapsedMilliseconds;
                result.Seed = seed;
                return result;
            }

            return null;
        }

        // ------------------------------------------------------------------ random layout

        private static PuzzleSpec BuildRandom(GeneratorOptions o, Random rng)
        {
            int w = Math.Max(5, o.Width);
            int h = Math.Max(4, o.Height);
            var spec = new PuzzleSpec(w, h);

            // Border walls plus scattered inner walls.
            double density = 0.10 + rng.NextDouble() * 0.16;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool border = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                    spec.Wall[spec.Cell(x, y)] = border || rng.NextDouble() < density;
                }
            }

            // A few wall segments make corridors instead of pure noise.
            int segments = 1 + rng.Next(3);
            for (int i = 0; i < segments; i++)
            {
                int x = 2 + rng.Next(Math.Max(1, w - 4));
                int y = 2 + rng.Next(Math.Max(1, h - 4));
                bool horizontal = rng.Next(2) == 0;
                int length = 2 + rng.Next(Math.Max(1, (horizontal ? w : h) / 2));
                for (int k = 0; k < length; k++)
                {
                    int cx = horizontal ? x + k : x;
                    int cy = horizontal ? y : y + k;
                    if (cx > 0 && cy > 0 && cx < w - 1 && cy < h - 1) spec.Wall[spec.Cell(cx, cy)] = true;
                }
            }

            // A door must stand between two walls, so every door gets a wall line with a one-cell gap: the gap is the doorway.
            int plannedDoors = o.Doors >= 0 ? o.Doors : o.Difficulty == GenDifficulty.Easy ? 1 : o.Difficulty == GenDifficulty.Medium ? 1 + rng.Next(2) : 2 + rng.Next(2);
            int dividers = Math.Min(plannedDoors, 2);
            for (int i = 0; i < dividers; i++)
            {
                if (rng.Next(10) < 8) AddDivider(spec, rng);
            }

            // Keep the largest connected floor area only.
            int[] component = Components(spec, -1, null);
            var sizes = new Dictionary<int, int>();
            for (int c = 0; c < component.Length; c++)
            {
                if (component[c] < 0) continue;
                int n;
                sizes.TryGetValue(component[c], out n);
                sizes[component[c]] = n + 1;
            }

            int best = -1, bestSize = 0;
            foreach (KeyValuePair<int, int> pair in sizes)
            {
                if (pair.Value > bestSize) { best = pair.Key; bestSize = pair.Value; }
            }

            int minFloor = (int)((w - 2) * (h - 2) * 0.45);
            if (best < 0 || bestSize < Math.Max(12, minFloor)) return null;
            for (int c = 0; c < component.Length; c++)
            {
                if (component[c] != best) spec.Wall[c] = true;
            }

            var floor = new List<int>();
            for (int c = 0; c < spec.Wall.Length; c++) if (!spec.Wall[c]) floor.Add(c);

            // Cats: B far from A.
            int minDistance = o.Difficulty == GenDifficulty.Easy ? 4 : o.Difficulty == GenDifficulty.Medium ? 6 : 8;
            minDistance = Math.Min(minDistance, (w + h) / 2);
            int a = floor[rng.Next(floor.Count)];
            spec.Root[0] = a;
            int[] distance = Distances(spec, a);
            var farCells = new List<int>();
            for (int i = 0; i < floor.Count; i++)
            {
                if (floor[i] != a && distance[floor[i]] >= minDistance) farCells.Add(floor[i]);
            }

            if (farCells.Count == 0) return null;
            spec.Root[1] = farCells[rng.Next(farCells.Count)];

            if (!ChooseFacing(spec, rng, 0, -1)) return null;
            if (!ChooseFacing(spec, rng, 1, spec.HandStart(0))) return null;

            // Doors that cut the level in two, each with a switch on a side the cats can reach without that door.
            int doorCount = plannedDoors;
            int barrelCount = o.Barrels >= 0 ? o.Barrels : o.Difficulty == GenDifficulty.Easy ? rng.Next(2) : o.Difficulty == GenDifficulty.Medium ? 1 + rng.Next(2) : 1 + rng.Next(3);

            for (int link = 1; link <= doorCount && link <= 9; link++)
            {
                // Doors only go between two walls. Prefer a doorway that really separates the cats.
                List<int> candidates = SeparatingCells(spec);
                if (candidates.Count == 0) candidates = DoorwayCells(spec);
                if (candidates.Count == 0) return null;
                int doorCell = candidates[rng.Next(candidates.Count)];

                bool startsOpen = o.Difficulty == GenDifficulty.Hard && rng.Next(5) == 0;
                spec.Doors.Add(new DoorSpec { Cell = doorCell, Link = link, StartsOpen = startsOpen });

                bool toggle = rng.Next(100) < (o.Difficulty == GenDifficulty.Easy ? 20 : 40);
                int switchCell = RandomSwitchCell(spec, rng);
                if (switchCell < 0) return null;
                spec.Switches.Add(new SwitchSpec { Cell = switchCell, Link = link, Toggle = toggle, StartsOn = toggle && rng.Next(4) == 0 });
            }

            for (int i = 0; i < barrelCount; i++)
            {
                int cell = RandomFreeCell(spec, rng, false);
                if (cell < 0) return null;
                spec.Barrels.Add(cell);
            }

            spec.MaxSteps[0] = spec.MaxSteps[1] = 3;
            return spec;
        }

        private static bool ChooseFacing(PuzzleSpec spec, Random rng, int cat, int avoidHand)
        {
            int[] order = { 0, 1, 2, 3 };
            for (int i = 3; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = order[i]; order[i] = order[j]; order[j] = t;
            }

            for (int i = 0; i < 4; i++)
            {
                spec.Facing[cat] = order[i];
                int hand = spec.HandStart(cat);
                if (hand >= 0 && !spec.Wall[hand] && hand != spec.Root[1 - cat] && hand != avoidHand) return true;
            }

            return false;
        }

        private static bool IsReserved(PuzzleSpec spec, int cell)
        {
            return cell == spec.Root[0] || cell == spec.Root[1] || cell == spec.HandStart(0) || cell == spec.HandStart(1);
        }

        private static int RandomFreeCell(PuzzleSpec spec, Random rng, bool avoidNextToCats)
        {
            for (int tries = 0; tries < 200; tries++)
            {
                int c = rng.Next(spec.Wall.Length);
                if (spec.Wall[c] || IsReserved(spec, c) || spec.IsOccupied(c)) continue;
                return c;
            }

            return -1;
        }

        /// <summary>A free cell either cat can walk to without crossing a door.</summary>
        private static int RandomSwitchCell(PuzzleSpec spec, Random rng)
        {
            var doors = new HashSet<int>();
            for (int i = 0; i < spec.Doors.Count; i++) doors.Add(spec.Doors[i].Cell);
            int[] da = Distances(spec, spec.Root[0], doors);
            int[] db = Distances(spec, spec.Root[1], doors);
            for (int tries = 0; tries < 300; tries++)
            {
                int c = rng.Next(spec.Wall.Length);
                if (spec.Wall[c] || IsReserved(spec, c) || spec.IsOccupied(c)) continue;
                if (da[c] < 0 && db[c] < 0) continue;
                return c;
            }

            return -1;
        }

        /// <summary>Free floor cells that, as a door, would separate cat A from cat B.</summary>
        private static List<int> SeparatingCells(PuzzleSpec spec)
        {
            var result = new List<int>();
            var doorCells = new HashSet<int>();
            for (int i = 0; i < spec.Doors.Count; i++) doorCells.Add(spec.Doors[i].Cell);

            for (int c = 0; c < spec.Wall.Length; c++)
            {
                if (spec.Wall[c] || IsReserved(spec, c) || spec.IsOccupied(c) || !IsBetweenWalls(spec, c)) continue;
                int[] d = Distances(spec, spec.Root[0], null, c);
                if (d[spec.Root[1]] < 0) result.Add(c);
            }

            return result;
        }

        /// <summary>Free cells with a wall on both sides (left and right, or above and below): the only places a door may stand.</summary>
        private static List<int> DoorwayCells(PuzzleSpec spec)
        {
            var result = new List<int>();
            for (int c = 0; c < spec.Wall.Length; c++)
            {
                if (spec.Wall[c] || IsReserved(spec, c) || spec.IsOccupied(c) || !IsBetweenWalls(spec, c)) continue;
                result.Add(c);
            }

            return result;
        }

        public static bool IsBetweenWalls(PuzzleSpec spec, int cell)
        {
            int x = cell % spec.Width;
            int y = cell / spec.Width;
            bool left = x == 0 || spec.Wall[cell - 1];
            bool right = x == spec.Width - 1 || spec.Wall[cell + 1];
            bool down = y == 0 || spec.Wall[cell - spec.Width];
            bool up = y == spec.Height - 1 || spec.Wall[cell + spec.Width];
            return (left && right) || (up && down);
        }

        /// <summary>A full wall line across the level with a single gap (the doorway), plus free cells on both sides of the gap.</summary>
        private static void AddDivider(PuzzleSpec spec, Random rng)
        {
            int w = spec.Width, h = spec.Height;
            bool vertical = rng.Next(2) == 0;
            if (vertical)
            {
                if (w < 7) return;
                int x = 2 + rng.Next(w - 4);
                int gap = 1 + rng.Next(h - 2);
                for (int y = 1; y < h - 1; y++) spec.Wall[spec.Cell(x, y)] = y != gap;
                spec.Wall[spec.Cell(x - 1, gap)] = false;
                spec.Wall[spec.Cell(x + 1, gap)] = false;
            }
            else
            {
                if (h < 7) return;
                int y = 2 + rng.Next(h - 4);
                int gap = 1 + rng.Next(w - 2);
                for (int x = 1; x < w - 1; x++) spec.Wall[spec.Cell(x, y)] = x != gap;
                spec.Wall[spec.Cell(gap, y - 1)] = false;
                spec.Wall[spec.Cell(gap, y + 1)] = false;
            }
        }

        private static int[] Components(PuzzleSpec spec, int blocked, HashSet<int> blockedSet)
        {
            int n = spec.Wall.Length;
            var comp = new int[n];
            for (int i = 0; i < n; i++) comp[i] = -1;
            int id = 0;
            var stack = new Stack<int>();
            for (int s = 0; s < n; s++)
            {
                if (spec.Wall[s] || comp[s] >= 0 || s == blocked) continue;
                comp[s] = id;
                stack.Push(s);
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    foreach (int nb in Around(spec, c))
                    {
                        if (spec.Wall[nb] || comp[nb] >= 0 || nb == blocked) continue;
                        comp[nb] = id;
                        stack.Push(nb);
                    }
                }

                id++;
            }

            return comp;
        }

        private static int[] Distances(PuzzleSpec spec, int from, HashSet<int> blockedSet = null, int blocked = -1)
        {
            int n = spec.Wall.Length;
            var dist = new int[n];
            for (int i = 0; i < n; i++) dist[i] = -1;
            var queue = new Queue<int>();
            dist[from] = 0;
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                foreach (int nb in Around(spec, c))
                {
                    if (spec.Wall[nb] || dist[nb] >= 0 || nb == blocked) continue;
                    if (blockedSet != null && blockedSet.Contains(nb)) continue;
                    dist[nb] = dist[c] + 1;
                    queue.Enqueue(nb);
                }
            }

            return dist;
        }

        private static IEnumerable<int> Around(PuzzleSpec spec, int c)
        {
            int x = c % spec.Width, y = c / spec.Width;
            if (x > 0) yield return c - 1;
            if (x < spec.Width - 1) yield return c + 1;
            if (y > 0) yield return c - spec.Width;
            if (y < spec.Height - 1) yield return c + spec.Width;
        }

        // ------------------------------------------------------------------ judging a layout

        private static bool TrySolve(PuzzleSpec spec, int stateCap, out SolveResult result)
        {
            var sim = new PuzzleSim(spec);
            result = PuzzleSolver.Solve(sim, new SolveOptions { MaxStates = stateCap, TimeLimitMs = 1500 });
            return result.Status == SolveStatus.Solved;
        }

        private static GeneratedLevel Evaluate(PuzzleSpec spec, GeneratorOptions o)
        {
            // 1. Smallest equal budget that makes the level solvable.
            SolveResult solved = null;
            int[] budgets = { 3, 4, 5, 6, 8, 10 };
            for (int i = 0; i < budgets.Length && solved == null; i++)
            {
                spec.MaxSteps[0] = spec.MaxSteps[1] = budgets[i];
                SolveResult r;
                if (TrySolve(spec, o.StateCapPerSolve, out r)) solved = r;
                else if (r.Status == SolveStatus.NoSolution && i >= 2) return null;
            }

            if (solved == null) return null;

            SolutionStats stats = PuzzleSolver.Replay(new PuzzleSim(spec), solved.Moves, null);
            if (!stats.Valid) return null;

            // Cheap early rejection: the solution must use something.
            int minMechanics = o.Difficulty == GenDifficulty.Easy ? 1 : o.Difficulty == GenDifficulty.Medium ? 2 : 3;
            if (stats.MechanicCount < minMechanics) return null;

            // 2. Remove pieces the shortest solution does not need.
            PuzzleSpec pruned = Prune(spec, solved.Moves.Count, o.StateCapPerSolve);

            // 3. Tighten the budgets.
            SolveResult finalSolve;
            if (!TrySolve(pruned, o.StateCapPerSolve, out finalSolve)) return null;
            stats = PuzzleSolver.Replay(new PuzzleSim(pruned), finalSolve.Moves, null);
            pruned.MaxSteps[0] = Math.Max(1, stats.MaxArm[0]);
            pruned.MaxSteps[1] = Math.Max(1, stats.MaxArm[1]);
            Shrink(pruned, o.StateCapPerSolve);

            if (!TrySolve(pruned, o.StateCapPerSolve, out finalSolve)) return null;
            stats = PuzzleSolver.Replay(new PuzzleSim(pruned), finalSolve.Moves, null);
            if (!stats.Valid) return null;

            // 4. Is it hard enough?
            int length = finalSolve.Moves.Count;
            int minLength = o.Difficulty == GenDifficulty.Easy ? 6 : o.Difficulty == GenDifficulty.Medium ? 12 : 20;
            int maxLength = o.Difficulty == GenDifficulty.Easy ? 26 : o.Difficulty == GenDifficulty.Medium ? 44 : 80;
            int pieces = pruned.Doors.Count + pruned.Barrels.Count + pruned.Switches.Count;
            if (length < minLength || length > maxLength) return null;
            if (stats.MechanicCount < minMechanics) return null;
            if (pieces < (o.Difficulty == GenDifficulty.Easy ? 2 : o.Difficulty == GenDifficulty.Medium ? 3 : 5)) return null;
            if (stats.MovesPerCat[0] == 0 || stats.MovesPerCat[1] == 0) return null;

            int slack = o.Slack >= 0 ? o.Slack : o.Difficulty == GenDifficulty.Easy ? 2 : o.Difficulty == GenDifficulty.Medium ? 1 : 0;
            pruned.MaxSteps[0] += slack;
            pruned.MaxSteps[1] += slack;

            // With the extra steps a shorter solution may exist; store the shortest one for the final budgets.
            SolveResult withSlack;
            if (slack > 0 && TrySolve(pruned, o.StateCapPerSolve, out withSlack))
            {
                SolutionStats slackStats = PuzzleSolver.Replay(new PuzzleSim(pruned), withSlack.Moves, null);
                if (slackStats.Valid)
                {
                    finalSolve = withSlack;
                    stats = slackStats;
                }
            }

            var level = new GeneratedLevel { Spec = pruned, Solution = finalSolve.Moves, Stats = stats };
            level.Summary = Describe(pruned, finalSolve.Moves.Count, stats);
            return level;
        }

        /// <summary>Drops every door, switch, barrel that the shortest solution does not need (removing it does not change the shortest length).</summary>
        private static PuzzleSpec Prune(PuzzleSpec spec, int length, int stateCap)
        {
            PuzzleSpec current = spec.Clone();
            bool changed = true;
            while (changed)
            {
                changed = false;

                for (int i = current.Barrels.Count - 1; i >= 0 && !changed; i--)
                {
                    PuzzleSpec trial = current.Clone();
                    trial.Barrels.RemoveAt(i);
                    if (SameLength(trial, length, stateCap)) { current = trial; changed = true; }
                }

                for (int i = current.Doors.Count - 1; i >= 0 && !changed; i--)
                {
                    PuzzleSpec trial = current.Clone();
                    int link = trial.Doors[i].Link;
                    trial.Doors.RemoveAt(i);
                    bool stillUsed = false;
                    for (int k = 0; k < trial.Doors.Count; k++) if (trial.Doors[k].Link == link) stillUsed = true;
                    if (!stillUsed) trial.Switches.RemoveAll(s => s.Link == link);
                    if (SameLength(trial, length, stateCap)) { current = trial; changed = true; }
                }

                for (int i = current.Switches.Count - 1; i >= 0 && !changed; i--)
                {
                    PuzzleSpec trial = current.Clone();
                    trial.Switches.RemoveAt(i);
                    if (SameLength(trial, length, stateCap)) { current = trial; changed = true; }
                }
            }

            return current;
        }

        private static bool SameLength(PuzzleSpec spec, int length, int stateCap)
        {
            SolveResult r;
            return TrySolve(spec, stateCap, out r) && r.Moves.Count == length;
        }

        /// <summary>Lowers the budgets while the level stays solvable. Reduces the larger one first.</summary>
        private static void Shrink(PuzzleSpec spec, int stateCap)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                int first = spec.MaxSteps[0] >= spec.MaxSteps[1] ? 0 : 1;
                for (int k = 0; k < 2; k++)
                {
                    int cat = k == 0 ? first : 1 - first;
                    if (spec.MaxSteps[cat] <= 1) continue;
                    spec.MaxSteps[cat]--;
                    SolveResult r;
                    if (TrySolve(spec, stateCap, out r)) { changed = true; break; }
                    spec.MaxSteps[cat]++;
                }
            }
        }

        public static string Describe(PuzzleSpec spec, int moves, SolutionStats stats)
        {
            var sb = new StringBuilder();
            sb.Append(spec.Width).Append('x').Append(spec.Height)
              .Append("  steps ").Append(spec.MaxSteps[0]).Append('/').Append(spec.MaxSteps[1])
              .Append("  solution ").Append(moves).Append(" moves")
              .Append("  doors ").Append(spec.Doors.Count)
              .Append(" barrels ").Append(spec.Barrels.Count)
              .Append(" switches ").Append(spec.Switches.Count);
            if (stats != null)
            {
                sb.Append("  uses:");
                if (stats.BarrelPushes > 0) sb.Append(" barrel-push");
                if (stats.MomentaryPresses > 0) sb.Append(" hold-switch");
                if (stats.ToggleFlips > 0) sb.Append(" toggle-switch");
                if (stats.DoorChanges > 0) sb.Append(" doors");
                if (stats.Cuts > 0) sb.Append(" arm-cut");
                if (stats.Crushes > 0) sb.Append(" crush");
                if (stats.CatPushes > 0) sb.Append(" cat-push");
            }

            return sb.ToString();
        }
    }
}
