using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Catshakes
{
    /// <summary>One static or interactive object placed on the grid.</summary>
    [Serializable]
    public struct ObstacleData
    {
        public TileType tileType;
        public Vector2Int position;

        [Tooltip("ID shared by switches and doors that belong together. Ignored for Wall and Barrel.")]
        public int linkId;

        [Tooltip("Toggle switch (Switch_2): starts ON. Door: starts open and a linked active switch closes it. Ignored for every other type.")]
        public bool initiallyOn;

        public ObstacleData(TileType tileType, Vector2Int position, int linkId = 0, bool initiallyOn = false)
        {
            this.tileType = tileType;
            this.position = position;
            this.linkId = linkId;
            this.initiallyOn = initiallyOn;
        }
    }

    /// <summary>
    /// Describes one level. Grid coordinates: (0,0) is the bottom-left cell, x grows to the right, y grows upwards.
    /// </summary>
    [CreateAssetMenu(fileName = "Level_01", menuName = "Catshakes/Level Data", order = 0)]
    public sealed class LevelDataSO : ScriptableObject
    {
        [Header("Meta")]
        [SerializeField] private string displayName = "Level";

        [Header("Grid")]
        [SerializeField, Min(2)] private int width = 5;
        [SerializeField, Min(2)] private int height = 5;

        [Header("Cats")]
        [SerializeField] private Vector2Int catAStart = new Vector2Int(0, 0);
        [SerializeField] private Vector2Int catBStart = new Vector2Int(4, 4);
        [SerializeField, Min(1)] private int catAMaxSteps = 6;
        [SerializeField, Min(1)] private int catBMaxSteps = 6;

        [Tooltip("Direction cat A faces at the start. Its hand starts on the neighbouring cell in this direction.")]
        [SerializeField] private Direction catAFacing = Direction.Right;

        [Tooltip("Direction cat B faces at the start. Its hand starts on the neighbouring cell in this direction.")]
        [SerializeField] private Direction catBFacing = Direction.Left;

        [Header("Obstacles (walls, barrels, switches, doors)")]
        [SerializeField] private List<ObstacleData> obstacles = new List<ObstacleData>();

        [Header("Quick authoring (right-click this asset > Import From ASCII Layout)")]
        [Tooltip("Legend: # wall | ~ void (nothing drawn, blocks like a wall) | . empty | P cat A | Q cat B | O barrel | 1-9 Switch_1 (momentary, linkId = digit) | a-i Switch_2 (toggle, a=1..i=9) | A-I door (A=1..I=9). The first text line is the TOP row. Optional last lines: '@A=R B=L' sets the facing (U/D/L/R) of each cat (its hand starts on the neighbouring cell in that direction); '@ON=2,3 5,1' lists toggle switches that start ON and '@OPEN=6,2' lists doors that start open, as column,row counted from 1 (column from the left, row from the top).")]
        [SerializeField, TextArea(6, 20)] private string asciiLayout = string.Empty;

        [Header("Solution (written by the Level Editor solver, e.g. \"A:RRU B:LL\")")]
        [Tooltip("Shortest known solution: moves grouped per cat, U/D/L/R. Cleared whenever the level is edited in the Level Editor.")]
        [SerializeField, TextArea(2, 6)] private string solutionText = string.Empty;

        public string DisplayName => displayName;
        public string SolutionText => solutionText;
        public int Width => width;
        public int Height => height;
        public Vector2Int CatAStart => catAStart;
        public Vector2Int CatBStart => catBStart;
        public int CatAMaxSteps => catAMaxSteps;
        public int CatBMaxSteps => catBMaxSteps;
        public Direction CatAFacing => catAFacing;
        public Direction CatBFacing => catBFacing;
        public IReadOnlyList<ObstacleData> Obstacles => obstacles;

        public Vector2Int GetCatStart(CatId id)
        {
            return id == CatId.CatA ? catAStart : catBStart;
        }

        public Direction GetFacing(CatId id)
        {
            return id == CatId.CatA ? catAFacing : catBFacing;
        }

        /// <summary>The cell the cat's hand starts on: the neighbour of its start cell in the facing direction.</summary>
        public Vector2Int GetHandStart(CatId id)
        {
            return GetCatStart(id) + GetFacing(id).ToVector();
        }

        public int GetMaxSteps(CatId id)
        {
            return id == CatId.CatA ? catAMaxSteps : catBMaxSteps;
        }

        public bool IsInside(Vector2Int cell)
        {
            return cell.x >= 0 && cell.y >= 0 && cell.x < width && cell.y < height;
        }

        /// <summary>
        /// Checks the level for authoring mistakes. Returns false when the level cannot be loaded.
        /// <paramref name="report"/> always contains the human-readable errors and warnings.
        /// </summary>
        public bool Validate(out string report)
        {
            var errors = new StringBuilder();
            var warnings = new StringBuilder();

            if (width < 2 || height < 2)
            {
                errors.AppendLine("Grid must be at least 2x2.");
            }

            if (catAMaxSteps < 1 || catBMaxSteps < 1)
            {
                errors.AppendLine("Each cat needs maxSteps >= 1.");
            }

            if (!IsInside(catAStart))
            {
                errors.AppendLine("Cat A start " + catAStart + " is outside the grid.");
            }

            if (!IsInside(catBStart))
            {
                errors.AppendLine("Cat B start " + catBStart + " is outside the grid.");
            }

            if (catAStart == catBStart)
            {
                errors.AppendLine("Both cats start on the same cell.");
            }

            var occupied = new HashSet<Vector2Int> { catAStart, catBStart };
            var switchLinks = new HashSet<int>();
            var doorLinks = new HashSet<int>();
            var openDoorLinks = new HashSet<int>();

            for (int i = 0; i < obstacles.Count; i++)
            {
                ObstacleData data = obstacles[i];

                if (data.tileType == TileType.Empty)
                {
                    warnings.AppendLine("Obstacle #" + i + " has type Empty and will be ignored.");
                    continue;
                }

                if (!IsInside(data.position))
                {
                    errors.AppendLine("Obstacle #" + i + " (" + data.tileType + ") at " + data.position + " is outside the grid.");
                    continue;
                }

                if (!occupied.Add(data.position))
                {
                    errors.AppendLine("Obstacle #" + i + " (" + data.tileType + ") at " + data.position + " overlaps a cat or another obstacle.");
                    continue;
                }

                if (data.tileType == TileType.SwitchMomentary || data.tileType == TileType.SwitchToggle)
                {
                    switchLinks.Add(data.linkId);
                }
                else if (data.tileType == TileType.Door)
                {
                    doorLinks.Add(data.linkId);
                    if (data.initiallyOn)
                    {
                        openDoorLinks.Add(data.linkId);
                    }
                }
            }

            CheckDoors(warnings);
            CheckHandStart(CatId.CatA, warnings);
            CheckHandStart(CatId.CatB, warnings);
            if (GetHandStart(CatId.CatA) == GetHandStart(CatId.CatB))
            {
                warnings.AppendLine("Both hands start on the same cell.");
            }

            foreach (int link in doorLinks)
            {
                if (!switchLinks.Contains(link))
                {
                    warnings.AppendLine("Door link " + link + " has no switch and can never " + (openDoorLinks.Contains(link) ? "close." : "open."));
                }
            }

            foreach (int link in switchLinks)
            {
                if (!doorLinks.Contains(link))
                {
                    warnings.AppendLine("Switch link " + link + " has no door.");
                }
            }

            report = errors.ToString() + warnings.ToString();
            return errors.Length == 0;
        }

        private bool IsSolidCell(Vector2Int cell)
        {
            for (int i = 0; i < obstacles.Count; i++)
            {
                ObstacleData data = obstacles[i];
                if (data.position == cell && (data.tileType == TileType.Wall || data.tileType == TileType.Void))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A barrel with a wall (or void) on at least one side of each axis can never be pushed, so it acts as a wall.</summary>
        private bool IsFrozenBarrel(Vector2Int cell)
        {
            return (IsSolidCell(cell + Vector2Int.left) || IsSolidCell(cell + Vector2Int.right))
                && (IsSolidCell(cell + Vector2Int.up) || IsSolidCell(cell + Vector2Int.down));
        }

        /// <summary>
        /// True when the cell has a wall on both sides, left and right or above and below. Doors may only stand there.
        /// A barrel that is stuck for good (see <see cref="IsFrozenBarrel"/>) counts as a wall.
        /// </summary>
        public bool IsBetweenWalls(Vector2Int cell)
        {
            bool left = false, right = false, up = false, down = false;
            for (int i = 0; i < obstacles.Count; i++)
            {
                ObstacleData data = obstacles[i];
                bool solid = data.tileType == TileType.Wall || data.tileType == TileType.Void
                    || (data.tileType == TileType.Barrel && IsFrozenBarrel(data.position));
                if (!solid)
                {
                    continue;
                }

                Vector2Int d = data.position - cell;
                if (d == Vector2Int.left) left = true;
                else if (d == Vector2Int.right) right = true;
                else if (d == Vector2Int.up) up = true;
                else if (d == Vector2Int.down) down = true;
            }

            return (left && right) || (up && down);
        }

        /// <summary>A door must stand between two walls (a closing door crushes whatever is in the doorway), and not under a hand start.</summary>
        private void CheckDoors(StringBuilder warnings)
        {
            for (int i = 0; i < obstacles.Count; i++)
            {
                ObstacleData data = obstacles[i];
                if (data.tileType != TileType.Door)
                {
                    continue;
                }

                if (!IsBetweenWalls(data.position))
                {
                    warnings.AppendLine("Door at " + data.position + " is not between two walls (a wall on both the left and right, or above and below).");
                }

                if (data.position == GetHandStart(CatId.CatA) || data.position == GetHandStart(CatId.CatB))
                {
                    warnings.AppendLine("Door at " + data.position + " is on the start cell of a hand.");
                }
            }
        }

        private void CheckHandStart(CatId id, StringBuilder warnings)
        {
            string cat = id == CatId.CatA ? "Cat A" : "Cat B";
            Vector2Int hand = GetHandStart(id);
            if (!IsInside(hand))
            {
                warnings.AppendLine(cat + " faces " + GetFacing(id) + " and its hand starts outside the grid at " + hand + ".");
                return;
            }

            if (hand == GetCatStart(id.Other()))
            {
                warnings.AppendLine(cat + "'s hand starts on the other cat at " + hand + ".");
                return;
            }

            for (int i = 0; i < obstacles.Count; i++)
            {
                ObstacleData data = obstacles[i];
                if (data.position == hand && (data.tileType == TileType.Wall || data.tileType == TileType.Void || data.tileType == TileType.Barrel))
                {
                    warnings.AppendLine(cat + "'s hand starts on a " + data.tileType + " at " + hand + ".");
                    return;
                }
            }
        }

        private void OnValidate()
        {
            width = Mathf.Max(2, width);
            height = Mathf.Max(2, height);
            catAMaxSteps = Mathf.Max(1, catAMaxSteps);
            catBMaxSteps = Mathf.Max(1, catBMaxSteps);
        }

        /// <summary>
        /// Replaces the grid, cat positions and obstacles from an ASCII layout (see the legend on the asciiLayout field)
        /// and sets the display name and each cat's max steps. Returns false and an error message when the layout is invalid.
        /// </summary>
        public bool ApplyAsciiLayout(string levelName, string layout, int stepsCatA, int stepsCatB, out string error)
        {
            displayName = levelName;
            asciiLayout = layout;
            catAMaxSteps = Mathf.Max(1, stepsCatA);
            catBMaxSteps = Mathf.Max(1, stepsCatB);
            return TryParseAscii(out error);
        }

        [ContextMenu("Validate Level")]
        private void ValidateFromMenu()
        {
            bool isValid = Validate(out string report);
            string message = isValid ? "Level '" + name + "' is valid." : "Level '" + name + "' has errors.";
            if (!string.IsNullOrEmpty(report))
            {
                message += "\n" + report;
            }

            if (isValid)
            {
                Debug.Log(message, this);
            }
            else
            {
                Debug.LogError(message, this);
            }
        }

        [ContextMenu("Import From ASCII Layout")]
        private void ImportFromAscii()
        {
            if (!TryParseAscii(out string error))
            {
                Debug.LogError("Import failed for '" + name + "': " + error, this);
                return;
            }

            Debug.Log("Imported layout into '" + name + "' (" + width + "x" + height + ", " + obstacles.Count + " obstacles).", this);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        private bool TryParseAscii(out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(asciiLayout))
            {
                error = "The ASCII layout is empty.";
                return false;
            }

            // Lines starting with '@' hold the cat facings (e.g. "@A=R B=L") and the start states ("@ON=2,3 5,1" for toggle
            // switches that start ON, "@OPEN=6,2" for doors that start open); every other line is a grid row.
            var rawLines = new List<string>();
            var onMarks = new List<Vector2Int>();
            var openMarks = new List<Vector2Int>();
            Direction facingA = catAFacing;
            Direction facingB = catBFacing;
            foreach (string rawLine in asciiLayout.Replace("\r", string.Empty).Split('\n'))
            {
                string trimmed = rawLine.Trim();
                if (!trimmed.StartsWith("@", StringComparison.Ordinal))
                {
                    rawLines.Add(rawLine);
                    continue;
                }

                bool isMarkLine;
                if (!TryParseMarkLine(trimmed.Substring(1), onMarks, openMarks, out isMarkLine, out error))
                {
                    return false;
                }

                if (isMarkLine)
                {
                    continue;
                }

                if (!TryParseFacingLine(trimmed.Substring(1), ref facingA, ref facingB, out error))
                {
                    return false;
                }
            }

            int first = 0;
            int last = rawLines.Count - 1;
            while (first <= last && string.IsNullOrWhiteSpace(rawLines[first])) first++;
            while (last >= first && string.IsNullOrWhiteSpace(rawLines[last])) last--;

            var lines = new List<string>();
            int parsedWidth = 0;
            for (int i = first; i <= last; i++)
            {
                string line = rawLines[i].TrimEnd();
                lines.Add(line);
                parsedWidth = Mathf.Max(parsedWidth, line.Length);
            }

            int parsedHeight = lines.Count;
            var parsedObstacles = new List<ObstacleData>();
            bool foundA = false;
            bool foundB = false;
            Vector2Int startA = Vector2Int.zero;
            Vector2Int startB = Vector2Int.zero;

            for (int row = 0; row < parsedHeight; row++)
            {
                string line = lines[row];
                int y = parsedHeight - 1 - row;

                for (int x = 0; x < line.Length; x++)
                {
                    char c = line[x];
                    var cell = new Vector2Int(x, y);

                    if (c == '.' || c == ' ')
                    {
                        continue;
                    }

                    if (c == '#')
                    {
                        parsedObstacles.Add(new ObstacleData(TileType.Wall, cell));
                    }
                    else if (c == '~')
                    {
                        parsedObstacles.Add(new ObstacleData(TileType.Void, cell));
                    }
                    else if (c == 'P')
                    {
                        if (foundA)
                        {
                            error = "More than one 'P' (cat A) found.";
                            return false;
                        }

                        foundA = true;
                        startA = cell;
                    }
                    else if (c == 'Q')
                    {
                        if (foundB)
                        {
                            error = "More than one 'Q' (cat B) found.";
                            return false;
                        }

                        foundB = true;
                        startB = cell;
                    }
                    else if (c == 'O')
                    {
                        parsedObstacles.Add(new ObstacleData(TileType.Barrel, cell));
                    }
                    else if (c >= '1' && c <= '9')
                    {
                        parsedObstacles.Add(new ObstacleData(TileType.SwitchMomentary, cell, c - '0'));
                    }
                    else if (c >= 'a' && c <= 'i')
                    {
                        parsedObstacles.Add(new ObstacleData(TileType.SwitchToggle, cell, c - 'a' + 1));
                    }
                    else if (c >= 'A' && c <= 'I')
                    {
                        parsedObstacles.Add(new ObstacleData(TileType.Door, cell, c - 'A' + 1));
                    }
                    else
                    {
                        error = "Unknown character '" + c + "' at row " + (row + 1) + ", column " + (x + 1) + ".";
                        return false;
                    }
                }
            }

            if (!foundA || !foundB)
            {
                error = "Both 'P' (cat A) and 'Q' (cat B) must appear exactly once.";
                return false;
            }

            if (!ApplyMarks(parsedObstacles, onMarks, TileType.SwitchToggle, "ON", parsedHeight, out error)
                || !ApplyMarks(parsedObstacles, openMarks, TileType.Door, "OPEN", parsedHeight, out error))
            {
                return false;
            }

            width = Mathf.Max(2, parsedWidth);
            height = Mathf.Max(2, parsedHeight);
            catAStart = startA;
            catBStart = startB;
            catAFacing = facingA;
            catBFacing = facingB;
            obstacles = parsedObstacles;
            return true;
        }

        /// <summary>
        /// Parses the body of a start-state line: "ON=2,3 5,1" (toggle switches that start ON) or "OPEN=6,2" (doors that start open).
        /// Each cell is "column,row", both counted from 1, the column from the left and the row from the top of the text grid.
        /// <paramref name="isMarkLine"/> is false for any other line (a facing line).
        /// </summary>
        private static bool TryParseMarkLine(string body, List<Vector2Int> onMarks, List<Vector2Int> openMarks, out bool isMarkLine, out string error)
        {
            error = string.Empty;
            isMarkLine = false;
            string upper = body.Trim().ToUpperInvariant();
            List<Vector2Int> target;
            string rest;
            if (upper.StartsWith("ON=", StringComparison.Ordinal))
            {
                target = onMarks;
                rest = upper.Substring(3);
            }
            else if (upper.StartsWith("OPEN=", StringComparison.Ordinal))
            {
                target = openMarks;
                rest = upper.Substring(5);
            }
            else
            {
                return true;
            }

            isMarkLine = true;
            string[] parts = rest.Split(new[] { ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string[] pair = parts[i].Split(',');
                int column, row;
                if (pair.Length != 2 || !int.TryParse(pair[0], out column) || !int.TryParse(pair[1], out row) || column < 1 || row < 1)
                {
                    error = "Bad cell '" + parts[i] + "' in '@" + body.Trim() + "'. Use column,row counted from 1 (column from the left, row from the top), e.g. '@ON=2,3 5,1'.";
                    return false;
                }

                target.Add(new Vector2Int(column, row));
            }

            return true;
        }

        /// <summary>Sets initiallyOn on the obstacles named by 1-based (column, row-from-top) marks; each must be of <paramref name="expected"/> type.</summary>
        private static bool ApplyMarks(List<ObstacleData> list, List<Vector2Int> marks, TileType expected, string label, int gridHeight, out string error)
        {
            error = string.Empty;
            for (int m = 0; m < marks.Count; m++)
            {
                var cell = new Vector2Int(marks[m].x - 1, gridHeight - marks[m].y);
                int index = -1;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].position == cell && list[i].tileType == expected)
                    {
                        index = i;
                        break;
                    }
                }

                if (index < 0)
                {
                    string what = expected == TileType.Door ? "a door (A-I)" : "a toggle switch (a-i)";
                    error = "@" + label + " cell " + marks[m].x + "," + marks[m].y + " is not " + what + ".";
                    return false;
                }

                ObstacleData data = list[index];
                data.initiallyOn = true;
                list[index] = data;
            }

            return true;
        }

        /// <summary>Parses the body of a facing line, e.g. "A=R B=L". Cats that are not mentioned keep their facing.</summary>
        private static bool TryParseFacingLine(string body, ref Direction facingA, ref Direction facingB, out string error)
        {
            error = string.Empty;
            string[] parts = body.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].ToUpperInvariant();
                Direction direction;
                if (part.Length != 3 || part[1] != '=' || !TryParseFacingLetter(part[2], out direction))
                {
                    error = "Bad facing entry '" + parts[i] + "'. Use A=U/D/L/R and B=U/D/L/R, e.g. '@A=R B=L'.";
                    return false;
                }

                if (part[0] == 'A')
                {
                    facingA = direction;
                }
                else if (part[0] == 'B')
                {
                    facingB = direction;
                }
                else
                {
                    error = "Bad facing entry '" + parts[i] + "'. Only cats A and B exist.";
                    return false;
                }
            }

            return true;
        }

        private static bool TryParseFacingLetter(char letter, out Direction direction)
        {
            switch (letter)
            {
                case 'U': direction = Direction.Up; return true;
                case 'D': direction = Direction.Down; return true;
                case 'L': direction = Direction.Left; return true;
                case 'R': direction = Direction.Right; return true;
                default: direction = Direction.Right; return false;
            }
        }
    }
}
