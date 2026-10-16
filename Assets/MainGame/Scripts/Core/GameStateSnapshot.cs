using System.Collections.Generic;
using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// Immutable copy of everything that can change during play. Used for Undo and Reset.
    /// Door states are not stored because they are derived from switches.
    /// </summary>
    public sealed class GameStateSnapshot
    {
        private readonly Vector2Int[][] armPaths;
        private readonly Vector2Int[] roots;
        private readonly Vector2Int[] barrelPositions;
        private readonly bool[] barrelCrushed;
        private readonly Vector2Int[] severedHandPositions;
        private readonly bool[] severedHandCrushed;
        private readonly bool[] toggleStates;

        public GameStateSnapshot(
            Vector2Int rootCatA,
            Vector2Int rootCatB,
            IReadOnlyList<Vector2Int> armPathCatA,
            IReadOnlyList<Vector2Int> armPathCatB,
            IReadOnlyList<Vector2Int> barrelPositions,
            IReadOnlyList<bool> barrelCrushed,
            IReadOnlyList<Vector2Int> severedHandPositions,
            IReadOnlyList<bool> severedHandCrushed,
            IReadOnlyList<bool> toggleStates,
            int moveCount)
        {
            roots = new[] { rootCatA, rootCatB };
            armPaths = new[] { Copy(armPathCatA), Copy(armPathCatB) };
            this.barrelPositions = Copy(barrelPositions);
            this.barrelCrushed = Copy(barrelCrushed);
            this.severedHandPositions = Copy(severedHandPositions);
            this.severedHandCrushed = Copy(severedHandCrushed);
            this.toggleStates = Copy(toggleStates);
            MoveCount = moveCount;
        }

        public int MoveCount { get; }
        public IReadOnlyList<Vector2Int> BarrelPositions => barrelPositions;

        /// <summary>Which barrels a door has crushed in this state (same order as <see cref="BarrelPositions"/>).</summary>
        public IReadOnlyList<bool> BarrelCrushed => barrelCrushed;

        /// <summary>Cells of the severed hands that exist in this state, in the order they were created.</summary>
        public IReadOnlyList<Vector2Int> SeveredHandPositions => severedHandPositions;

        /// <summary>Which severed hands a door has crushed in this state (same order as <see cref="SeveredHandPositions"/>).</summary>
        public IReadOnlyList<bool> SeveredHandCrushed => severedHandCrushed;
        public IReadOnlyList<bool> ToggleStates => toggleStates;

        /// <summary>The cell of the cat's body. It changes when the other cat pushes it.</summary>
        public Vector2Int GetRoot(CatId id)
        {
            return roots[(int)id];
        }

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
