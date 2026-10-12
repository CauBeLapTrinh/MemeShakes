using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// Converts between grid cells (Vector2Int) and world positions.
    /// Cell (0,0) is the bottom-left cell; a cell's world position is its center.
    /// </summary>
    public sealed class GridSpace
    {
        public GridSpace(Vector3 origin, float cellSize, int width, int height)
        {
            Origin = origin;
            CellSize = Mathf.Max(0.01f, cellSize);
            Width = width;
            Height = height;
        }

        public Vector3 Origin { get; }
        public float CellSize { get; }
        public int Width { get; }
        public int Height { get; }

        /// <summary>World position of the middle of the whole board.</summary>
        public Vector3 Center
        {
            get
            {
                return Origin + new Vector3((Width - 1) * CellSize * 0.5f, (Height - 1) * CellSize * 0.5f, 0f);
            }
        }

        public Vector3 GridToWorld(Vector2Int cell)
        {
            return Origin + new Vector3(cell.x * CellSize, cell.y * CellSize, 0f);
        }

        public Vector2Int WorldToGrid(Vector3 world)
        {
            Vector3 local = world - Origin;
            return new Vector2Int(
                Mathf.RoundToInt(local.x / CellSize),
                Mathf.RoundToInt(local.y / CellSize));
        }

        public bool IsInside(Vector2Int cell)
        {
            return cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height;
        }
    }
}
