using System;
using System.Collections.Generic;
using System.Text;

namespace Catshakes.Solver
{
    // Pure C# model of the puzzle rules (no UnityEngine), so the solver and generator can run fast and be tested
    // outside Unity. It mirrors GridManager / CatArmController. Keep both in sync when a rule changes.
    //
    // Directions use the same numbers as Catshakes.Direction: 0 Up, 1 Down, 2 Left, 3 Right.
    // Cells are linear indices: index = y * width + x, (0,0) is the bottom-left cell.

    public struct DoorSpec
    {
        public int Cell;
        public int Link;
        public bool StartsOpen;
    }

    public struct SwitchSpec
    {
        public int Cell;
        public int Link;
        public bool Toggle;
        public bool StartsOn;
    }

    /// <summary>Editable description of a level (what a LevelDataSO stores).</summary>
    public sealed class PuzzleSpec
    {
        public int Width;
        public int Height;
        public bool[] Wall;
        public List<DoorSpec> Doors = new List<DoorSpec>();
        public List<SwitchSpec> Switches = new List<SwitchSpec>();
        public List<int> Barrels = new List<int>();
        public int[] Root = new int[2];
        public int[] Facing = new int[2];
        public int[] MaxSteps = new int[2];

        public PuzzleSpec(int width, int height)
        {
            Width = width;
            Height = height;
            Wall = new bool[width * height];
        }

        public int Cell(int x, int y)
        {
            return y * Width + x;
        }

        public PuzzleSpec Clone()
        {
            var c = new PuzzleSpec(Width, Height);
            Array.Copy(Wall, c.Wall, Wall.Length);
            c.Doors.AddRange(Doors);
            c.Switches.AddRange(Switches);
            c.Barrels.AddRange(Barrels);
            c.Root[0] = Root[0]; c.Root[1] = Root[1];
            c.Facing[0] = Facing[0]; c.Facing[1] = Facing[1];
            c.MaxSteps[0] = MaxSteps[0]; c.MaxSteps[1] = MaxSteps[1];
            return c;
        }

        /// <summary>Cell the hand starts on (neighbour of the root in the facing direction), or -1 when outside the grid.</summary>
        public int HandStart(int cat)
        {
            int x = Root[cat] % Width;
            int y = Root[cat] / Width;
            switch (Facing[cat])
            {
                case 0: y++; break;
                case 1: y--; break;
                case 2: x--; break;
                default: x++; break;
            }

            return x < 0 || y < 0 || x >= Width || y >= Height ? -1 : y * Width + x;
        }

        public bool IsOccupied(int cell)
        {
            if (cell == Root[0] || cell == Root[1] || Wall[cell] || Barrels.Contains(cell))
            {
                return true;
            }

            for (int i = 0; i < Doors.Count; i++) if (Doors[i].Cell == cell) return true;
            for (int i = 0; i < Switches.Count; i++) if (Switches[i].Cell == cell) return true;
            return false;
        }

        /// <summary>Same letters as LevelDataSO's ASCII layout (top row first), plus the facing line.</summary>
        public string ToAscii()
        {
            var grid = new char[Width * Height];
            for (int i = 0; i < grid.Length; i++) grid[i] = Wall[i] ? '#' : '.';
            for (int i = 0; i < Barrels.Count; i++) grid[Barrels[i]] = 'O';
            for (int i = 0; i < Switches.Count; i++)
            {
                SwitchSpec s = Switches[i];
                grid[s.Cell] = s.Toggle ? (char)('a' + s.Link - 1) : (char)('0' + s.Link);
            }

            for (int i = 0; i < Doors.Count; i++) grid[Doors[i].Cell] = (char)('A' + Doors[i].Link - 1);
            grid[Root[0]] = 'P';
            grid[Root[1]] = 'Q';

            var sb = new StringBuilder();
            for (int row = 0; row < Height; row++)
            {
                int y = Height - 1 - row;
                for (int x = 0; x < Width; x++) sb.Append(grid[y * Width + x]);
                sb.Append('\n');
            }

            sb.Append("@A=").Append(FacingLetter(Facing[0])).Append(" B=").Append(FacingLetter(Facing[1]));
            return sb.ToString();
        }

        public static string FacingLetter(int d)
        {
            return d == 0 ? "U" : d == 1 ? "D" : d == 2 ? "L" : "R";
        }
    }

    public struct MoveEvents
    {
        public bool Retracted;
        public bool PushedBarrel;
        public bool PushedCat;
        public bool HandsMet;
        public int Cuts;
        public int ToggleFlips;
        public bool Overflow;
    }

    /// <summary>Mutable game state. Doors and momentary switches are derived from it.</summary>
    public sealed class PuzzleState
    {
        public int[] Root = new int[2];
        public int[][] Arm = new int[2][];
        public int[] Len = new int[2];
        public int[] Barrels;
        public int BarrelCount;
        public ulong Toggles;
        public bool[] SwitchActive;
        public bool[] DoorOpen;
        public bool Won;
        public bool Overflow;

        public int Hand(int cat)
        {
            return Arm[cat][Len[cat] - 1];
        }

        public int IndexOfBarrel(int cell)
        {
            for (int i = 0; i < BarrelCount; i++)
            {
                if (Barrels[i] == cell) return i;
            }

            return -1;
        }

        public bool HasBarrel(int cell)
        {
            return IndexOfBarrel(cell) >= 0;
        }

        public bool ArmContains(int cat, int cell)
        {
            int[] arm = Arm[cat];
            for (int i = 0, n = Len[cat]; i < n; i++)
            {
                if (arm[i] == cell) return true;
            }

            return false;
        }

        public bool Occupies(int cat, int cell)
        {
            return Root[cat] == cell || ArmContains(cat, cell);
        }

        public void RemoveBarrelAt(int index)
        {
            for (int i = index; i < BarrelCount - 1; i++) Barrels[i] = Barrels[i + 1];
            BarrelCount--;
        }

        /// <summary>Inserts keeping the array sorted. Returns false (and sets Overflow) when the array is full.</summary>
        public bool AddBarrel(int cell)
        {
            if (BarrelCount >= Barrels.Length)
            {
                Overflow = true;
                return false;
            }

            int i = BarrelCount++;
            while (i > 0 && Barrels[i - 1] > cell)
            {
                Barrels[i] = Barrels[i - 1];
                i--;
            }

            Barrels[i] = cell;
            return true;
        }

        public void CopyFrom(PuzzleState o)
        {
            Root[0] = o.Root[0]; Root[1] = o.Root[1];
            Len[0] = o.Len[0]; Len[1] = o.Len[1];
            Array.Copy(o.Arm[0], Arm[0], o.Len[0]);
            Array.Copy(o.Arm[1], Arm[1], o.Len[1]);
            BarrelCount = o.BarrelCount;
            Array.Copy(o.Barrels, Barrels, o.BarrelCount);
            Toggles = o.Toggles;
            Array.Copy(o.SwitchActive, SwitchActive, o.SwitchActive.Length);
            Array.Copy(o.DoorOpen, DoorOpen, o.DoorOpen.Length);
            Won = o.Won;
            Overflow = false;
        }
    }

    /// <summary>The rules of one level, compiled for fast simulation.</summary>
    public sealed class PuzzleSim
    {
        /// <summary>Most severed hands a state may hold. A cut that would exceed it makes the move illegal (solver limit).</summary>
        public const int MaxSevered = 6;

        public readonly int W;
        public readonly int H;
        public readonly int N;
        public readonly bool[] Wall;
        public readonly int[] Neighbor;
        public readonly int[] DoorAt;
        public readonly DoorSpec[] Doors;
        public readonly SwitchSpec[] Switches;
        public readonly int[] ToggleIndex;
        public readonly int ToggleCount;
        public readonly int[] Root;
        public readonly int[] HandStart;
        public readonly int[] Facing;
        public readonly int[] MaxSteps;
        public readonly int[] StartBarrels;
        public readonly ulong StartToggles;
        public readonly string Error;

        /// <summary>Old rule, kept for comparison only: arm segments (not just the hand) press and flip switches.</summary>
        public bool ArmsPressSwitches;

        public PuzzleSim(PuzzleSpec spec)
        {
            W = spec.Width;
            H = spec.Height;
            N = W * H;
            Wall = spec.Wall;
            Doors = spec.Doors.ToArray();
            Switches = spec.Switches.ToArray();
            Root = (int[])spec.Root.Clone();
            Facing = (int[])spec.Facing.Clone();
            MaxSteps = (int[])spec.MaxSteps.Clone();
            StartBarrels = spec.Barrels.ToArray();
            Array.Sort(StartBarrels);

            Neighbor = new int[N * 4];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int c = y * W + x;
                    Neighbor[c * 4 + 0] = y + 1 < H ? c + W : -1;
                    Neighbor[c * 4 + 1] = y > 0 ? c - W : -1;
                    Neighbor[c * 4 + 2] = x > 0 ? c - 1 : -1;
                    Neighbor[c * 4 + 3] = x + 1 < W ? c + 1 : -1;
                }
            }

            DoorAt = new int[N];
            for (int i = 0; i < N; i++) DoorAt[i] = -1;
            for (int i = 0; i < Doors.Length; i++) DoorAt[Doors[i].Cell] = i;

            ToggleIndex = new int[Switches.Length];
            ulong toggles = 0;
            int count = 0;
            for (int i = 0; i < Switches.Length; i++)
            {
                if (!Switches[i].Toggle)
                {
                    ToggleIndex[i] = -1;
                    continue;
                }

                if (count >= 64)
                {
                    Error = "More than 64 toggle switches.";
                    break;
                }

                ToggleIndex[i] = count;
                if (Switches[i].StartsOn) toggles |= 1UL << count;
                count++;
            }

            ToggleCount = count;
            StartToggles = toggles;

            HandStart = new int[2];
            for (int cat = 0; cat < 2; cat++)
            {
                HandStart[cat] = spec.HandStart(cat);
                if (HandStart[cat] < 0 && Error == null)
                {
                    Error = "The hand of cat " + (cat == 0 ? "A" : "B") + " starts outside the grid.";
                }
            }
        }

        // ------------------------------------------------------------------ state

        public PuzzleState NewBlankState()
        {
            var s = new PuzzleState();
            s.Arm[0] = new int[MaxSteps[0] + 1];
            s.Arm[1] = new int[MaxSteps[1] + 1];
            s.Barrels = new int[StartBarrels.Length + MaxSevered];
            s.SwitchActive = new bool[Switches.Length];
            s.DoorOpen = new bool[Doors.Length];
            return s;
        }

        public PuzzleState NewInitialState()
        {
            PuzzleState s = NewBlankState();
            for (int cat = 0; cat < 2; cat++)
            {
                s.Root[cat] = Root[cat];
                s.Len[cat] = 1;
                s.Arm[cat][0] = HandStart[cat];
            }

            for (int i = 0; i < StartBarrels.Length; i++) s.Barrels[i] = StartBarrels[i];
            s.BarrelCount = StartBarrels.Length;
            s.Toggles = StartToggles;
            var ev = new MoveEvents();
            Refresh(s, ref ev);
            return s;
        }

        /// <summary>Compact exact key of a state (doors and momentary switches are derived and not stored).</summary>
        public string Encode(PuzzleState s)
        {
            int words = (ToggleCount + 15) / 16;
            int n = 5 + s.Len[0] + s.Len[1] + s.BarrelCount + words;
            var chars = new char[n];
            int p = 0;
            chars[p++] = (char)s.Root[0];
            chars[p++] = (char)s.Root[1];
            chars[p++] = (char)s.Len[0];
            chars[p++] = (char)s.Len[1];
            chars[p++] = (char)s.BarrelCount;
            for (int i = 0; i < s.Len[0]; i++) chars[p++] = (char)s.Arm[0][i];
            for (int i = 0; i < s.Len[1]; i++) chars[p++] = (char)s.Arm[1][i];
            for (int i = 0; i < s.BarrelCount; i++) chars[p++] = (char)s.Barrels[i];
            for (int w = 0; w < words; w++) chars[p++] = (char)((s.Toggles >> (16 * w)) & 0xFFFF);
            return new string(chars);
        }

        public void Decode(string key, PuzzleState s)
        {
            int p = 0;
            s.Root[0] = key[p++];
            s.Root[1] = key[p++];
            s.Len[0] = key[p++];
            s.Len[1] = key[p++];
            s.BarrelCount = key[p++];
            for (int i = 0; i < s.Len[0]; i++) s.Arm[0][i] = key[p++];
            for (int i = 0; i < s.Len[1]; i++) s.Arm[1][i] = key[p++];
            for (int i = 0; i < s.BarrelCount; i++) s.Barrels[i] = key[p++];
            int words = (ToggleCount + 15) / 16;
            ulong t = 0;
            for (int w = 0; w < words; w++) t |= (ulong)key[p++] << (16 * w);
            s.Toggles = t;
            s.Won = false;
            s.Overflow = false;
            UpdateSwitchesAndDoors(s);
        }

        // ------------------------------------------------------------------ moves

        /// <summary>
        /// Moves the hand of <paramref name="cat"/> one cell in <paramref name="dir"/>. Returns false (state may be
        /// modified when the move overflowed, so call it on a scratch copy) when the move is not allowed.
        /// </summary>
        public bool TryMove(PuzzleState s, int cat, int dir, out MoveEvents ev)
        {
            ev = default(MoveEvents);
            if (s.Won) return false;

            int[] arm = s.Arm[cat];
            int len = s.Len[cat];
            int hand = arm[len - 1];
            int target = Neighbor[hand * 4 + dir];

            if (len > 1 && target == arm[len - 2])
            {
                s.Len[cat] = len - 1;
                ev.Retracted = true;
                Commit(s, ref ev);
                return !s.Overflow;
            }

            if (len - 1 >= MaxSteps[cat]) return false;
            if (target < 0 || Wall[target]) return false;
            if (IsClosedDoor(s, target)) return false;

            int other = 1 - cat;
            bool pushCat = false;
            int barrelIndex = -1;
            int barrelDest = -1;

            if (target == s.Root[other])
            {
                if (!CanCatShift(s, other, dir, cat)) return false;
                pushCat = true;
            }
            else
            {
                if (IsBlockedByCatBody(s, target, dir, cat)) return false;
                barrelIndex = s.IndexOfBarrel(target);
                if (barrelIndex >= 0)
                {
                    barrelDest = Neighbor[target * 4 + dir];
                    if (!CanBarrelEnter(s, barrelDest)) return false;
                }
            }

            if (pushCat)
            {
                PushCat(s, other, dir, ref ev);
                ev.PushedCat = true;
            }

            if (barrelIndex >= 0)
            {
                s.RemoveBarrelAt(barrelIndex);
                s.AddBarrel(barrelDest);
                FlipToggleAt(s, barrelDest, ref ev);
                ev.PushedBarrel = true;
            }

            bool handshake = s.Hand(other) == target;
            arm[len] = target;
            s.Len[cat] = len + 1;
            if (!handshake) FlipToggleAt(s, target, ref ev);

            Commit(s, ref ev);
            return !s.Overflow;
        }

        private bool IsClosedDoor(PuzzleState s, int cell)
        {
            int d = DoorAt[cell];
            return d >= 0 && !s.DoorOpen[d];
        }

        private bool CanCatShift(PuzzleState s, int cat, int dir, int pusher)
        {
            if (!CanCatEnterCell(s, Neighbor[s.Root[cat] * 4 + dir], pusher)) return false;
            for (int i = 0; i < s.Len[cat]; i++)
            {
                if (!CanCatEnterCell(s, Neighbor[s.Arm[cat][i] * 4 + dir], pusher)) return false;
            }

            return true;
        }

        private bool CanCatEnterCell(PuzzleState s, int cell, int pusher)
        {
            return cell >= 0 && !Wall[cell] && !IsClosedDoor(s, cell) && !s.HasBarrel(cell) && !s.Occupies(pusher, cell);
        }

        private void PushCat(PuzzleState s, int cat, int dir, ref MoveEvents ev)
        {
            int len = s.Len[cat];
            int[] arm = s.Arm[cat];
            bool handWasInArm = false;
            int[] oldCells = new int[len];
            for (int i = 0; i < len; i++) oldCells[i] = arm[i];

            s.Root[cat] = Neighbor[s.Root[cat] * 4 + dir];
            for (int i = 0; i < len; i++) arm[i] = Neighbor[arm[i] * 4 + dir];

            // Only the hand tip flips a toggle switch; other arm cells sliding over a switch do not count.
            int newHand = arm[len - 1];
            for (int i = 0; i < len; i++)
            {
                if (oldCells[i] == newHand) handWasInArm = true;
            }

            if (!handWasInArm) FlipToggleAt(s, newHand, ref ev);
            if (ArmsPressSwitches)
            {
                for (int i = 0; i < len - 1; i++)
                {
                    bool wasThere = false;
                    for (int k = 0; k < len; k++) if (oldCells[k] == arm[i]) wasThere = true;
                    if (!wasThere) FlipToggleAt(s, arm[i], ref ev);
                }
            }
        }

        private bool IsBlockedByCatBody(PuzzleState s, int cell, int dir, int mover)
        {
            for (int i = 0; i < 2; i++)
            {
                if (!s.Occupies(i, cell)) continue;
                bool handshake = i != mover && s.Hand(i) == cell && HandDirection(s, i) == (dir ^ 1);
                if (!handshake) return true;
            }

            return false;
        }

        private int HandDirection(PuzzleState s, int cat)
        {
            int len = s.Len[cat];
            if (len <= 1) return Facing[cat];
            int from = s.Arm[cat][len - 2];
            int to = s.Arm[cat][len - 1];
            for (int d = 0; d < 4; d++)
            {
                if (Neighbor[from * 4 + d] == to) return d;
            }

            return -1;
        }

        private bool CanBarrelEnter(PuzzleState s, int cell)
        {
            return cell >= 0 && !Wall[cell] && !IsClosedDoor(s, cell) && !s.HasBarrel(cell) && !s.Occupies(0, cell) && !s.Occupies(1, cell);
        }

        private void FlipToggleAt(PuzzleState s, int cell, ref MoveEvents ev)
        {
            for (int i = 0; i < Switches.Length; i++)
            {
                if (Switches[i].Cell == cell && Switches[i].Toggle)
                {
                    s.Toggles ^= 1UL << ToggleIndex[i];
                    ev.ToggleFlips++;
                    return;
                }
            }
        }

        private void Commit(PuzzleState s, ref MoveEvents ev)
        {
            bool met = s.Hand(0) == s.Hand(1);
            if (met)
            {
                UpdateSwitchesAndDoors(s);
                s.Won = true;
                ev.HandsMet = true;
            }
            else
            {
                Refresh(s, ref ev);
            }

            if (ev.Overflow) s.Overflow = true;
        }

        // ------------------------------------------------------------------ switches, doors, cutting

        private void Refresh(PuzzleState s, ref MoveEvents ev)
        {
            for (int pass = 0; pass < 16; pass++)
            {
                UpdateSwitchesAndDoors(s);
                if (!CutArms(s, ref ev)) return;
            }

            UpdateSwitchesAndDoors(s);
        }

        /// <summary>Recomputes momentary switches (pressed by a barrel / severed hand or a hand tip) and doors.</summary>
        public void UpdateSwitchesAndDoors(PuzzleState s)
        {
            int hand0 = s.Hand(0);
            int hand1 = s.Hand(1);
            for (int i = 0; i < Switches.Length; i++)
            {
                SwitchSpec sw = Switches[i];
                if (sw.Toggle)
                {
                    s.SwitchActive[i] = ((s.Toggles >> ToggleIndex[i]) & 1UL) != 0;
                }
                else
                {
                    s.SwitchActive[i] = hand0 == sw.Cell || hand1 == sw.Cell || s.HasBarrel(sw.Cell)
                                        || (ArmsPressSwitches && (s.ArmContains(0, sw.Cell) || s.ArmContains(1, sw.Cell)));
                }
            }

            for (int d = 0; d < Doors.Length; d++)
            {
                DoorSpec door = Doors[d];
                bool linkActive = false;
                for (int i = 0; i < Switches.Length; i++)
                {
                    if (Switches[i].Link == door.Link && s.SwitchActive[i])
                    {
                        linkActive = true;
                        break;
                    }
                }

                bool occupied = s.HasBarrel(door.Cell)
                                || s.Root[0] == door.Cell || s.Root[1] == door.Cell
                                || HoldsDoorAt(s, 0, door.Cell) || HoldsDoorAt(s, 1, door.Cell);
                s.DoorOpen[d] = linkActive != door.StartsOpen || occupied;
            }
        }

        private static bool HoldsDoorAt(PuzzleState s, int cat, int cell)
        {
            return s.Hand(cat) == cell || s.Arm[cat][0] == cell;
        }

        private bool CutArms(PuzzleState s, ref MoveEvents ev)
        {
            bool cutAny = false;
            for (int cat = 0; cat < 2; cat++)
            {
                int cutIndex = -1;
                int len = s.Len[cat];
                int[] arm = s.Arm[cat];
                for (int d = 0; d < Doors.Length; d++)
                {
                    if (s.DoorOpen[d]) continue;
                    int cell = Doors[d].Cell;
                    for (int i = 1; i < len - 1; i++)
                    {
                        if (arm[i] == cell)
                        {
                            if (cutIndex < 0 || i < cutIndex) cutIndex = i;
                            break;
                        }
                    }
                }

                if (cutIndex < 0) continue;

                // The old hand stays behind as a severed hand (acts like a barrel); the arm is back at its start cell.
                if (!s.AddBarrel(arm[len - 1])) ev.Overflow = true;
                s.Len[cat] = 1;
                ev.Cuts++;
                cutAny = true;
            }

            return cutAny;
        }
    }
}
