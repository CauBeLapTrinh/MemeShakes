using System.Collections.Generic;
using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// Immutable copy of everything that can change during play. Used for Undo and Reset.
    /// Door states are not stored because they are derived from switches and occupancy.
    /// </summary>
    public sealed class GameStateSnapshot
    {
        private readonly Vector2Int[][] armPaths;
        private readonly Vector2Int[] barrelPositions;
        private readonly bool[] toggleStates;

        public GameStateSnapshot(
            IReadOnlyList<Vector2Int> armPathCatA,
            IReadOnlyList<Vector2Int> armPathCatB,
            IReadOnlyList<Vector2Int> barrelPositions,
            IReadOnlyList<bool> toggleStates,
            int moveCount)
        {
            armPaths = new[] { Copy(armPathCatA), Copy(armPathCatB) };
            this.barrelPositions = Copy(barrelPositions);
            this.toggleStates = Copy(toggleStates);
            MoveCount = moveCount;
        }

        public int MoveCount { get; }
        public IReadOnlyList<Vector2Int> BarrelPositions => barrelPositions;
        public IReadOnlyList<bool> ToggleStates => toggleStates;

        public IReadOnlyList<Vector2Int> GetArmPath(CatId id)
        {
            return armPaths[(int)id];
        }

        private static T[] Copy<T>(IReadOnlyList<T> source)
        {
            var copy = new T[source.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }
    }
}
