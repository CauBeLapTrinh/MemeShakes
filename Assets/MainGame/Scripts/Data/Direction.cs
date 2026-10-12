using UnityEngine;

namespace Catshakes
{
    /// <summary>Four cardinal movement directions on the grid.</summary>
    public enum Direction
    {
        Up = 0,
        Down = 1,
        Left = 2,
        Right = 3
    }

    public static class DirectionExtensions
    {
        public static readonly Direction[] All =
        {
            Direction.Up, Direction.Down, Direction.Left, Direction.Right
        };

        /// <summary>Converts a direction to a unit Vector2Int (x right, y up).</summary>
        public static Vector2Int ToVector(this Direction direction)
        {
            switch (direction)
            {
                case Direction.Up: return Vector2Int.up;
                case Direction.Down: return Vector2Int.down;
                case Direction.Left: return Vector2Int.left;
                case Direction.Right: return Vector2Int.right;
                default: return Vector2Int.zero;
            }
        }

        public static Direction Opposite(this Direction direction)
        {
            switch (direction)
            {
                case Direction.Up: return Direction.Down;
                case Direction.Down: return Direction.Up;
                case Direction.Left: return Direction.Right;
                default: return Direction.Left;
            }
        }

        /// <summary>
        /// Z rotation in degrees for a sprite whose artwork faces right by default.
        /// </summary>
        public static float ToAngleDegrees(this Direction direction)
        {
            switch (direction)
            {
                case Direction.Up: return 90f;
                case Direction.Left: return 180f;
                case Direction.Down: return 270f;
                default: return 0f;
            }
        }

        /// <summary>Succeeds only for the four unit vectors (up, down, left, right).</summary>
        public static bool TryFromVector(Vector2Int vector, out Direction direction)
        {
            if (vector == Vector2Int.up) { direction = Direction.Up; return true; }
            if (vector == Vector2Int.down) { direction = Direction.Down; return true; }
            if (vector == Vector2Int.left) { direction = Direction.Left; return true; }
            if (vector == Vector2Int.right) { direction = Direction.Right; return true; }

            direction = Direction.Up;
            return false;
        }

        /// <summary>
        /// Picks the direction of the dominant axis of a delta. Ties favour the horizontal axis.
        /// Fails for a zero delta.
        /// </summary>
        public static bool TryFromDominantAxis(Vector2 delta, out Direction direction)
        {
            if (delta == Vector2.zero)
            {
                direction = Direction.Up;
                return false;
            }

            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            {
                direction = delta.x > 0f ? Direction.Right : Direction.Left;
            }
            else
            {
                direction = delta.y > 0f ? Direction.Up : Direction.Down;
            }

            return true;
        }
    }
}
