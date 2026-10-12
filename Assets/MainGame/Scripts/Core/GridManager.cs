using System;
using System.Collections.Generic;
using UnityEngine;

namespace Catshakes
{
    /// <summary>
    /// Authoritative spatial state of the board and the single place where puzzle rules are applied.
    /// Everything is decided with <see cref="Vector2Int"/> data (pure grid / Sokoban logic): no Rigidbody2D,
    /// no Physics2D. Visual components only mirror the decisions made here.
    ///
    /// Rules:
    ///  - A hand moves one cell per call. Moving onto the cell behind the hand retracts the arm.
    ///  - An arm cannot enter walls, closed doors, or any cell occupied by a cat root or arm segment,
    ///    except the cell of the other cat's extended hand, which is the winning handshake.
    ///  - A barrel in the target cell is pushed one cell further if that cell is free.
    ///  - Switch_1 (momentary) is active while a barrel or an arm segment lies on it.
    ///  - Switch_2 (toggle) flips each time an arm or barrel enters its cell.
    ///  - A door is open while any linked switch is active, and also while something stands in its doorway.
    /// </summary>
    public sealed class GridManager : MonoBehaviour
    {
        [Header("Grid")]
        [SerializeField, Min(0.1f)] private float cellSize = 1f;
        [Tooltip("World position of cell (0,0). Defaults to this transform.")]
        [SerializeField] private Transform gridOrigin;
        [Tooltip("Parent for spawned tiles and entities. Created automatically when empty.")]
        [SerializeField] private Transform levelRoot;

        [Header("Prefabs")]
        [SerializeField] private GameObject floorTilePrefab;
        [SerializeField] private GameObject wallPrefab;
        [SerializeField] private Barrel barrelPrefab;
        [SerializeField] private SwitchEntity momentarySwitchPrefab;
        [SerializeField] private SwitchEntity toggleSwitchPrefab;
        [SerializeField] private DoorEntity doorPrefab;

        [Header("Cats")]
        [SerializeField] private CatArmController catA;
        [SerializeField] private CatArmController catB;

        private readonly HashSet<Vector2Int> walls = new HashSet<Vector2Int>();
        private readonly List<Barrel> barrels = new List<Barrel>();
        private readonly Dictionary<Vector2Int, Barrel> barrelsByCell = new Dictionary<Vector2Int, Barrel>();
        private readonly List<SwitchEntity> switches = new List<SwitchEntity>();
        private readonly List<SwitchEntity> toggleSwitches = new List<SwitchEntity>();
        private readonly Dictionary<Vector2Int, SwitchEntity> switchesByCell = new Dictionary<Vector2Int, SwitchEntity>();
        private readonly List<DoorEntity> doors = new List<DoorEntity>();
        private readonly Dictionary<Vector2Int, DoorEntity> doorsByCell = new Dictionary<Vector2Int, DoorEntity>();
        private readonly List<GameObject> spawnedObjects = new List<GameObject>();
        private readonly Stack<GameStateSnapshot> undoStack = new Stack<GameStateSnapshot>();

        private CatArmController[] cats;
        private LevelDataSO currentLevel;
        private GridSpace coords;
        private GameStateSnapshot initialSnapshot;
        private CatId activeCat = CatId.CatA;
        private int moveCount;
        private bool isWon;

        /// <summary>Raised after a level has been built and is ready to play.</summary>
        public event Action LevelLoaded;

        /// <summary>Raised after any change that the HUD may want to display (move, undo, reset, load).</summary>
        public event Action StateChanged;

        /// <summary>Raised once when the two hands meet on the same cell.</summary>
        public event Action Won;

        /// <summary>Raised after every move attempt, successful or blocked (useful for sound and haptics).</summary>
        public event Action<CatId, MoveOutcome> MoveResolved;

        /// <summary>Raised when the controlled cat changes.</summary>
        public event Action<CatId> ActiveCatChanged;

        public GridSpace Coords => coords;
        public LevelDataSO CurrentLevel => currentLevel;
        public bool IsLevelLoaded => currentLevel != null && coords != null;
        public bool IsWon => isWon;
        public int MoveCount => moveCount;
        public bool CanUndo => IsLevelLoaded && !isWon && undoStack.Count > 0;
        public CatId ActiveCat => activeCat;

        private void Awake()
        {
            cats = new[] { catA, catB };
        }

        public CatArmController GetCat(CatId id)
        {
            return id == CatId.CatA ? catA : catB;
        }

        // ---------------------------------------------------------------- level lifecycle

        /// <summary>Builds the level. Returns false (and leaves the board empty) when the data or prefabs are invalid.</summary>
        public bool LoadLevel(LevelDataSO level)
        {
            ClearLevel();

            if (level == null)
            {
                Debug.LogError("GridManager.LoadLevel received a null level.", this);
                return false;
            }

            if (catA == null || catB == null)
            {
                Debug.LogError("GridManager needs both cat references assigned.", this);
                return false;
            }

            if (cats == null)
            {
                cats = new[] { catA, catB };
            }

            if (!level.Validate(out string report))
            {
                Debug.LogError("Level '" + level.name + "' is invalid:\n" + report, level);
                return false;
            }

            if (!string.IsNullOrEmpty(report))
            {
                Debug.LogWarning("Level '" + level.name + "' warnings:\n" + report, level);
            }

            currentLevel = level;
            Vector3 origin = gridOrigin != null ? gridOrigin.position : transform.position;
            coords = new GridSpace(origin, cellSize, level.Width, level.Height);
            EnsureLevelRoot();

            if (!BuildFloor() || !BuildObstacles(level))
            {
                ClearLevel();
                return false;
            }

            OrientDoors();

            catA.Initialize(coords, level.GetCatStart(CatId.CatA), level.GetMaxSteps(CatId.CatA));
            catB.Initialize(coords, level.GetCatStart(CatId.CatB), level.GetMaxSteps(CatId.CatB));

            moveCount = 0;
            isWon = false;
            undoStack.Clear();
            RefreshInteractiveState();
            initialSnapshot = CaptureSnapshot();

            activeCat = CatId.CatA;
            ApplySelection();
            ActiveCatChanged?.Invoke(activeCat);

            LevelLoaded?.Invoke();
            StateChanged?.Invoke();
            return true;
        }

        /// <summary>Destroys all spawned tiles and entities and forgets the current level.</summary>
        public void ClearLevel()
        {
            for (int i = 0; i < spawnedObjects.Count; i++)
            {
                if (spawnedObjects[i] != null)
                {
                    Destroy(spawnedObjects[i]);
                }
            }

            spawnedObjects.Clear();
            walls.Clear();
            barrels.Clear();
            barrelsByCell.Clear();
            switches.Clear();
            toggleSwitches.Clear();
            switchesByCell.Clear();
            doors.Clear();
            doorsByCell.Clear();
            undoStack.Clear();

            currentLevel = null;
            coords = null;
            initialSnapshot = null;
            moveCount = 0;
            isWon = false;
        }

        /// <summary>Restores the initial state of the level and clears the undo history.</summary>
        public void ResetLevel()
        {
            if (!IsLevelLoaded || initialSnapshot == null)
            {
                return;
            }

            undoStack.Clear();
            isWon = false;
            ApplySnapshot(initialSnapshot);
            StateChanged?.Invoke();
        }

        /// <summary>Reverts the last successful move. Returns false when there is nothing to undo.</summary>
        public bool Undo()
        {
            if (!CanUndo)
            {
                return false;
            }

            ApplySnapshot(undoStack.Pop());
            StateChanged?.Invoke();
            return true;
        }

        // ---------------------------------------------------------------- selection

        public void SelectCat(CatId id)
        {
            bool changed = id != activeCat;
            activeCat = id;
            ApplySelection();

            if (changed)
            {
                ActiveCatChanged?.Invoke(activeCat);
            }
        }

        public void ToggleActiveCat()
        {
            SelectCat(activeCat.Other());
        }

        /// <summary>Finds the cat whose hand, arm or root occupies the cell. Hands win over bodies.</summary>
        public bool TryGetCatAtCell(Vector2Int cell, out CatArmController cat)
        {
            cat = null;
            if (!IsLevelLoaded)
            {
                return false;
            }

            for (int i = 0; i < cats.Length; i++)
            {
                if (cats[i].HandPosition == cell)
                {
                    cat = cats[i];
                    return true;
                }
            }

            for (int i = 0; i < cats.Length; i++)
            {
                if (cats[i].OccupiesCell(cell))
                {
                    cat = cats[i];
                    return true;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------- movement

        /// <summary>
        /// Can an arm enter <paramref name="targetPos"/> while travelling in <paramref name="pushDir"/>?
        /// Checks bounds, walls, doors, arm cells and whether a barrel there can be pushed.
        /// No cat is exempt here, so the other cat's hand cell counts as blocked.
        /// </summary>
        public bool CanMoveTo(Vector2Int targetPos, Vector2Int pushDir)
        {
            return CanMoveTo(targetPos, pushDir, null);
        }

        /// <summary>Same as the two-argument overload, but the other cat's extended hand cell is allowed for <paramref name="mover"/> (handshake).</summary>
        public bool CanMoveTo(Vector2Int targetPos, Vector2Int pushDir, CatArmController mover)
        {
            if (!IsLevelLoaded)
            {
                return false;
            }

            return EvaluateEnter(targetPos, pushDir, mover, out _, out _).IsSuccess();
        }

        /// <summary>Moves the hand of a cat one cell in a direction (extend, push, or retract).</summary>
        public MoveOutcome TryMoveHand(CatId id, Direction direction)
        {
            MoveOutcome outcome = ResolveMove(GetCat(id), direction.ToVector());
            MoveResolved?.Invoke(id, outcome);
            return outcome;
        }

        /// <summary>Moves the hand onto an adjacent cell. Fails with BlockedNotAdjacent for any other cell.</summary>
        public MoveOutcome TryMoveHandToCell(CatId id, Vector2Int cell)
        {
            CatArmController cat = GetCat(id);
            Vector2Int delta = cell - cat.HandPosition;

            if (!DirectionExtensions.TryFromVector(delta, out Direction direction))
            {
                MoveResolved?.Invoke(id, MoveOutcome.BlockedNotAdjacent);
                return MoveOutcome.BlockedNotAdjacent;
            }

            return TryMoveHand(id, direction);
        }

        private MoveOutcome ResolveMove(CatArmController cat, Vector2Int direction)
        {
            if (!IsLevelLoaded || isWon)
            {
                return MoveOutcome.BlockedInputLocked;
            }

            cat.CompleteAnimationImmediately();
            Vector2Int target = cat.HandPosition + direction;

            if (cat.IsRetractTarget(target))
            {
                PushUndoSnapshot();
                cat.RetractLast();
                CommitMove();
                return MoveOutcome.Retracted;
            }

            if (!cat.CanExtend)
            {
                return MoveOutcome.BlockedNoStepsLeft;
            }

            MoveOutcome outcome = EvaluateEnter(target, direction, cat, out Barrel pushedBarrel, out Vector2Int barrelDestination);
            if (!outcome.IsSuccess())
            {
                return outcome;
            }

            PushUndoSnapshot();

            if (pushedBarrel != null)
            {
                MoveBarrel(pushedBarrel, barrelDestination);
                FlipToggleAt(barrelDestination);
            }

            cat.Extend(target);
            FlipToggleAt(target);
            CommitMove();
            return outcome;
        }

        /// <summary>
        /// Returns Extended or ExtendedAndPushedBarrel when the arm may enter the cell, otherwise the reason it cannot.
        /// </summary>
        private MoveOutcome EvaluateEnter(
            Vector2Int target,
            Vector2Int direction,
            CatArmController mover,
            out Barrel pushedBarrel,
            out Vector2Int barrelDestination)
        {
            pushedBarrel = null;
            barrelDestination = target + direction;

            if (!coords.IsInside(target))
            {
                return MoveOutcome.BlockedOutOfBounds;
            }

            if (walls.Contains(target))
            {
                return MoveOutcome.BlockedByWall;
            }

            if (IsClosedDoor(target))
            {
                return MoveOutcome.BlockedByClosedDoor;
            }

            if (IsBlockedByCatBody(target, mover))
            {
                return MoveOutcome.BlockedByArm;
            }

            if (barrelsByCell.TryGetValue(target, out Barrel barrel))
            {
                if (!CanBarrelEnter(barrelDestination))
                {
                    return MoveOutcome.BlockedByBarrel;
                }

                pushedBarrel = barrel;
                return MoveOutcome.ExtendedAndPushedBarrel;
            }

            return MoveOutcome.Extended;
        }

        private bool IsBlockedByCatBody(Vector2Int cell, CatArmController mover)
        {
            for (int i = 0; i < cats.Length; i++)
            {
                CatArmController cat = cats[i];
                if (!cat.OccupiesCell(cell))
                {
                    continue;
                }

                // Only an extended hand can be shaken; a cat's body (root cell) is never enterable.
                bool isHandshake = mover != null && cat != mover && cat.HasExtendedArm && cat.HandPosition == cell;
                if (!isHandshake)
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanBarrelEnter(Vector2Int cell)
        {
            return coords.IsInside(cell)
                   && !walls.Contains(cell)
                   && !IsClosedDoor(cell)
                   && !barrelsByCell.ContainsKey(cell)
                   && !IsOccupiedByCat(cell);
        }

        private bool IsClosedDoor(Vector2Int cell)
        {
            return doorsByCell.TryGetValue(cell, out DoorEntity door) && !door.IsOpen;
        }

        private bool IsOccupiedByCat(Vector2Int cell)
        {
            return catA.OccupiesCell(cell) || catB.OccupiesCell(cell);
        }

        private void MoveBarrel(Barrel barrel, Vector2Int destination)
        {
            barrelsByCell.Remove(barrel.GridPosition);
            barrel.SetGridPosition(destination, true);
            barrelsByCell[destination] = barrel;
        }

        private void FlipToggleAt(Vector2Int cell)
        {
            if (switchesByCell.TryGetValue(cell, out SwitchEntity entry) && entry.IsToggle)
            {
                entry.Flip();
            }
        }

        private void CommitMove()
        {
            moveCount++;
            RefreshInteractiveState();
            StateChanged?.Invoke();

            if (catA.HasExtendedArm && catB.HasExtendedArm && catA.HandPosition == catB.HandPosition)
            {
                isWon = true;
                Won?.Invoke();
            }
        }

        // ---------------------------------------------------------------- switches and doors

        /// <summary>Recomputes momentary switches first, then every door.</summary>
        private void RefreshInteractiveState()
        {
            for (int i = 0; i < switches.Count; i++)
            {
                SwitchEntity entry = switches[i];
                if (entry.IsMomentary)
                {
                    entry.SetActive(IsCellPressed(entry.GridPosition));
                }
            }

            for (int i = 0; i < doors.Count; i++)
            {
                DoorEntity door = doors[i];
                bool shouldBeOpen = IsLinkActive(door.LinkId) || IsDoorwayOccupied(door.GridPosition);
                door.SetOpen(shouldBeOpen);
            }
        }

        private bool IsCellPressed(Vector2Int cell)
        {
            return barrelsByCell.ContainsKey(cell) || catA.ArmContains(cell) || catB.ArmContains(cell);
        }

        private bool IsDoorwayOccupied(Vector2Int cell)
        {
            return barrelsByCell.ContainsKey(cell) || IsOccupiedByCat(cell);
        }

        private bool IsLinkActive(int linkId)
        {
            for (int i = 0; i < switches.Count; i++)
            {
                if (switches[i].LinkId == linkId && switches[i].IsActive)
                {
                    return true;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------- snapshots

        private void PushUndoSnapshot()
        {
            undoStack.Push(CaptureSnapshot());
        }

        private GameStateSnapshot CaptureSnapshot()
        {
            var barrelCells = new List<Vector2Int>(barrels.Count);
            for (int i = 0; i < barrels.Count; i++)
            {
                barrelCells.Add(barrels[i].GridPosition);
            }

            var toggleStates = new List<bool>(toggleSwitches.Count);
            for (int i = 0; i < toggleSwitches.Count; i++)
            {
                toggleStates.Add(toggleSwitches[i].IsActive);
            }

            return new GameStateSnapshot(catA.ArmPath, catB.ArmPath, barrelCells, toggleStates, moveCount);
        }

        private void ApplySnapshot(GameStateSnapshot snapshot)
        {
            catA.RestorePath(snapshot.GetArmPath(CatId.CatA));
            catB.RestorePath(snapshot.GetArmPath(CatId.CatB));

            barrelsByCell.Clear();
            IReadOnlyList<Vector2Int> cells = snapshot.BarrelPositions;
            for (int i = 0; i < barrels.Count; i++)
            {
                barrels[i].SetGridPosition(cells[i], false);
                barrelsByCell[cells[i]] = barrels[i];
            }

            IReadOnlyList<bool> states = snapshot.ToggleStates;
            for (int i = 0; i < toggleSwitches.Count; i++)
            {
                toggleSwitches[i].SetActive(states[i]);
            }

            moveCount = snapshot.MoveCount;
            RefreshInteractiveState();
        }

        private void ApplySelection()
        {
            if (cats == null)
            {
                return;
            }

            for (int i = 0; i < cats.Length; i++)
            {
                cats[i].SetSelected(cats[i].Id == activeCat);
            }
        }

        // ---------------------------------------------------------------- level construction

        private void EnsureLevelRoot()
        {
            if (levelRoot != null)
            {
                return;
            }

            var rootObject = new GameObject("LevelRoot");
            rootObject.transform.SetParent(transform, false);
            levelRoot = rootObject.transform;
        }

        private bool BuildFloor()
        {
            if (floorTilePrefab == null)
            {
                return true;
            }

            for (int y = 0; y < coords.Height; y++)
            {
                for (int x = 0; x < coords.Width; x++)
                {
                    Vector3 position = coords.GridToWorld(new Vector2Int(x, y));
                    GameObject tile = Instantiate(floorTilePrefab, position, Quaternion.identity, levelRoot);
                    spawnedObjects.Add(tile);
                }
            }

            return true;
        }

        private bool BuildObstacles(LevelDataSO level)
        {
            IReadOnlyList<ObstacleData> obstacles = level.Obstacles;

            for (int i = 0; i < obstacles.Count; i++)
            {
                ObstacleData data = obstacles[i];

                switch (data.tileType)
                {
                    case TileType.Wall:
                    {
                        if (!RequirePrefab(wallPrefab, "wallPrefab"))
                        {
                            return false;
                        }

                        walls.Add(data.position);
                        GameObject wall = Instantiate(wallPrefab, coords.GridToWorld(data.position), Quaternion.identity, levelRoot);
                        spawnedObjects.Add(wall);
                        break;
                    }

                    case TileType.Barrel:
                    {
                        if (!RequirePrefab(barrelPrefab, "barrelPrefab"))
                        {
                            return false;
                        }

                        Barrel barrel = Instantiate(barrelPrefab, levelRoot);
                        barrel.Initialize(coords, data.position);
                        barrels.Add(barrel);
                        barrelsByCell[data.position] = barrel;
                        spawnedObjects.Add(barrel.gameObject);
                        break;
                    }

                    case TileType.SwitchMomentary:
                    case TileType.SwitchToggle:
                    {
                        bool isToggle = data.tileType == TileType.SwitchToggle;
                        SwitchEntity prefab = isToggle ? toggleSwitchPrefab : momentarySwitchPrefab;
                        if (!RequirePrefab(prefab, isToggle ? "toggleSwitchPrefab" : "momentarySwitchPrefab"))
                        {
                            return false;
                        }

                        SwitchEntity entry = Instantiate(prefab, levelRoot);
                        entry.Configure(coords, data);
                        switches.Add(entry);
                        switchesByCell[data.position] = entry;
                        if (isToggle)
                        {
                            toggleSwitches.Add(entry);
                        }

                        spawnedObjects.Add(entry.gameObject);
                        break;
                    }

                    case TileType.Door:
                    {
                        if (!RequirePrefab(doorPrefab, "doorPrefab"))
                        {
                            return false;
                        }

                        DoorEntity door = Instantiate(doorPrefab, levelRoot);
                        door.Configure(coords, data);
                        doors.Add(door);
                        doorsByCell[data.position] = door;
                        spawnedObjects.Add(door.gameObject);
                        break;
                    }
                }
            }

            return true;
        }

        /// <summary>Turns doors that sit in a vertical corridor (walls left and right) by 90 degrees.</summary>
        private void OrientDoors()
        {
            for (int i = 0; i < doors.Count; i++)
            {
                Vector2Int cell = doors[i].GridPosition;
                bool wallsAboveAndBelow = walls.Contains(cell + Vector2Int.up) && walls.Contains(cell + Vector2Int.down);
                bool wallsLeftAndRight = walls.Contains(cell + Vector2Int.left) && walls.Contains(cell + Vector2Int.right);
                doors[i].SetVertical(wallsLeftAndRight && !wallsAboveAndBelow);
            }
        }

        private bool RequirePrefab(UnityEngine.Object prefab, string fieldName)
        {
            if (prefab != null)
            {
                return true;
            }

            Debug.LogError("GridManager is missing the prefab reference '" + fieldName + "'.", this);
            return false;
        }
    }
}
