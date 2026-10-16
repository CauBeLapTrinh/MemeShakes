using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Catshakes.Solver
{
    public enum SolveStatus
    {
        Solved,
        NoSolution,
        LimitReached,
        Cancelled,
        Invalid
    }

    public sealed class SolveOptions
    {
        public int MaxStates = 1500000;
        public long TimeLimitMs;

        /// <summary>Called now and then with the number of known states; return true to stop the search.</summary>
        public Func<int, bool> Cancel;
    }

    public sealed class SolveResult
    {
        public SolveStatus Status;
        public string Message = string.Empty;

        /// <summary>Each move is cat * 4 + direction (0 Up, 1 Down, 2 Left, 3 Right).</summary>
        public List<int> Moves = new List<int>();

        public int Explored;
        public long Ms;
    }

    /// <summary>What a solution makes use of. Used to rate how interesting a level is.</summary>
    public sealed class SolutionStats
    {
        public bool Valid;
        public int[] MaxArm = new int[2];
        public int[] MovesPerCat = new int[2];
        public int BarrelPushes;
        public int CatPushes;
        public int Cuts;
        public int Crushes;
        public int ToggleFlips;
        public int MomentaryPresses;
        public int DoorChanges;
        public int Retracts;
        public int CatSwitches;

        /// <summary>How many different mechanics the solution needs (barrel, cat push, cut, toggle, hold switch, door).</summary>
        public int MechanicCount
        {
            get
            {
                int n = 0;
                if (BarrelPushes > 0) n++;
                if (CatPushes > 0) n++;
                if (Cuts > 0 || Crushes > 0) n++;
                if (ToggleFlips > 0) n++;
                if (MomentaryPresses > 0) n++;
                if (DoorChanges > 0) n++;
                return n;
            }
        }
    }

    /// <summary>Breadth-first search over the complete game state. Finds a solution with the fewest moves.</summary>
    public static class PuzzleSolver
    {
        public static SolveResult Solve(PuzzleSim sim, SolveOptions options)
        {
            var result = new SolveResult();
            var timer = Stopwatch.StartNew();

            if (sim.Error != null)
            {
                result.Status = SolveStatus.Invalid;
                result.Message = sim.Error;
                return result;
            }

            PuzzleState initial = sim.NewInitialState();
            var keys = new List<string>(1024);
            var parent = new List<int>(1024);
            var moveOf = new List<byte>(1024);
            var seen = new HashSet<string>();

            string first = sim.Encode(initial);
            keys.Add(first);
            parent.Add(-1);
            moveOf.Add(0);
            seen.Add(first);

            bool stopped = false;
            PuzzleState current = sim.NewBlankState();
            PuzzleState scratch = sim.NewBlankState();

            for (int head = 0; head < keys.Count; head++)
            {
                if ((head & 1023) == 0)
                {
                    if (options.Cancel != null && options.Cancel(keys.Count))
                    {
                        result.Status = SolveStatus.Cancelled;
                        stopped = true;
                        break;
                    }

                    if (options.TimeLimitMs > 0 && timer.ElapsedMilliseconds > options.TimeLimitMs)
                    {
                        result.Status = SolveStatus.LimitReached;
                        stopped = true;
                        result.Message = "Time limit reached.";
                        break;
                    }
                }

                if (keys.Count > options.MaxStates)
                {
                    result.Status = SolveStatus.LimitReached;
                    stopped = true;
                    result.Message = "State limit reached.";
                    break;
                }

                sim.Decode(keys[head], current);

                for (int m = 0; m < 8; m++)
                {
                    scratch.CopyFrom(current);
                    MoveEvents ev;
                    if (!sim.TryMove(scratch, m >> 2, m & 3, out ev)) continue;

                    if (scratch.Won)
                    {
                        result.Status = SolveStatus.Solved;
                        result.Moves.Add(m);
                        for (int at = head; parent[at] >= 0; at = parent[at]) result.Moves.Add(moveOf[at]);
                        result.Moves.Reverse();
                        result.Explored = keys.Count;
                        result.Ms = timer.ElapsedMilliseconds;
                        return result;
                    }

                    string key = sim.Encode(scratch);
                    if (seen.Add(key))
                    {
                        keys.Add(key);
                        parent.Add(head);
                        moveOf.Add((byte)m);
                    }
                }

                result.Explored = head + 1;
            }

            if (!stopped)
            {
                result.Status = SolveStatus.NoSolution;
                result.Message = "Every reachable state was searched: the hands can never meet.";
            }

            result.Explored = keys.Count;
            result.Ms = timer.ElapsedMilliseconds;
            return result;
        }

        /// <summary>Plays a move list on a fresh game. Fills <paramref name="states"/> with the state before the first move and after each move.</summary>
        public static SolutionStats Replay(PuzzleSim sim, IList<int> moves, List<PuzzleState> states)
        {
            var stats = new SolutionStats();
            PuzzleState s = sim.NewInitialState();
            if (states != null) states.Add(Clone(sim, s));

            int lastCat = -1;
            for (int i = 0; i < moves.Count; i++)
            {
                int cat = moves[i] >> 2;
                bool[] switchBefore = (bool[])s.SwitchActive.Clone();
                bool[] doorBefore = (bool[])s.DoorOpen.Clone();

                MoveEvents ev;
                if (!sim.TryMove(s, cat, moves[i] & 3, out ev))
                {
                    return stats;
                }

                if (cat != lastCat && lastCat >= 0) stats.CatSwitches++;
                lastCat = cat;
                stats.MovesPerCat[cat]++;
                if (ev.Retracted) stats.Retracts++;
                if (ev.PushedBarrel) stats.BarrelPushes++;
                if (ev.PushedCat) stats.CatPushes++;
                stats.Cuts += ev.Cuts;
                stats.Crushes += ev.Crushed;
                stats.ToggleFlips += ev.ToggleFlips;
                for (int k = 0; k < switchBefore.Length; k++)
                {
                    if (!sim.Switches[k].Toggle && !switchBefore[k] && s.SwitchActive[k]) stats.MomentaryPresses++;
                }

                for (int k = 0; k < doorBefore.Length; k++)
                {
                    if (doorBefore[k] != s.DoorOpen[k]) stats.DoorChanges++;
                }

                for (int c = 0; c < 2; c++)
                {
                    stats.MaxArm[c] = Math.Max(stats.MaxArm[c], s.Len[c] - 1);
                }

                if (states != null) states.Add(Clone(sim, s));
            }

            stats.Valid = s.Won;
            return stats;
        }

        private static PuzzleState Clone(PuzzleSim sim, PuzzleState s)
        {
            PuzzleState copy = sim.NewBlankState();
            copy.CopyFrom(s);
            return copy;
        }

        // ------------------------------------------------------------------ text form of a solution

        /// <summary>"A:RRU B:LL A:D" - consecutive moves of one cat are grouped. U/D/L/R = up/down/left/right.</summary>
        public static string Format(IList<int> moves)
        {
            var sb = new StringBuilder();
            int lastCat = -1;
            for (int i = 0; i < moves.Count; i++)
            {
                int cat = moves[i] >> 2;
                if (cat != lastCat)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(cat == 0 ? "A:" : "B:");
                    lastCat = cat;
                }

                sb.Append("UDLR"[moves[i] & 3]);
            }

            return sb.ToString();
        }

        public static bool TryParse(string text, List<int> moves)
        {
            moves.Clear();
            if (string.IsNullOrWhiteSpace(text)) return false;

            int cat = -1;
            foreach (string token in text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int p = 0;
                if (token.Length >= 2 && token[1] == ':')
                {
                    char c = char.ToUpperInvariant(token[0]);
                    if (c != 'A' && c != 'B') return false;
                    cat = c == 'A' ? 0 : 1;
                    p = 2;
                }

                if (cat < 0) return false;
                for (; p < token.Length; p++)
                {
                    int d = "UDLR".IndexOf(char.ToUpperInvariant(token[p]));
                    if (d < 0) return false;
                    moves.Add(cat * 4 + d);
                }
            }

            return moves.Count > 0;
        }
    }
}
