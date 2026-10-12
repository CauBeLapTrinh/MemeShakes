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

        [Tooltip("Initial state of a Toggle switch (Switch_2). Ignored for every other type.")]
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

        [Header("Obstacles (walls, barrels, switches, doors)")]
        [SerializeField] private List<ObstacleData> obstacles = new List<ObstacleData>();

        [Header("Quick authoring (right-click this asset > Import From ASCII Layout)")]
        [Tooltip("Legend: # wall | . empty | P cat A | Q cat B | O barrel | 1-9 Switch_1 (momentary, linkId = digit) | a-i Switch_2 (toggle, a=1..i=9) | A-I door (A=1..I=9). The first text line is the TOP row.")]
        [SerializeField, TextArea(6, 20)] private string asciiLayout = string.Empty;

        public string DisplayName => displayName;
        public int Width => width;
        public int Height => height;
        public Vector2Int CatAStart => catAStart;
        public Vector2Int CatBStart => catBStart;
        public int CatAMaxSteps => catAMaxSteps;
        public int CatBMaxSteps => catBMaxSteps;
        public IReadOnlyList<ObstacleData> Obstacles => obstacles;

        public Vector2Int GetCatStart(CatId id)
        {
            return id == CatId.CatA ? catAStart : catBStart;
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
                }
            }

            foreach (int link in doorLinks)
            {
                if (!switchLinks.Contains(link))
                {
                    warnings.AppendLine("Door link " + link + " has no switch and can never open.");
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

            string[] rawLines = asciiLayout.Replace("\r", string.Empty).Split('\n');
            int first = 0;
            int last = rawLines.Length - 1;
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

            width = Mathf.Max(2, parsedWidth);
            height = Mathf.Max(2, parsedHeight);
            catAStart = startA;
            catBStart = startB;
            obstacles = parsedObstacles;
            return true;
        }
    }
}
