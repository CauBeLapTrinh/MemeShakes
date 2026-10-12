using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// Base class for objects that live on a grid cell. It only stores the cell and places the
    /// transform; game rules are decided by <see cref="GridManager"/>, never by the entity itself.
    /// </summary>
    public abstract class GridEntity : MonoBehaviour
    {
        public Vector2Int GridPosition { get; protected set; }

        protected GridSpace Coords { get; private set; }

        public virtual void Initialize(GridSpace coords, Vector2Int gridPosition)
        {
            Coords = coords;
            GridPosition = gridPosition;
            transform.position = coords.GridToWorld(gridPosition);
        }
    }
}
