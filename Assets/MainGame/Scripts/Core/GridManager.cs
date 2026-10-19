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
    ///  - The other cat's body is pushed one cell too (together with its whole arm) if every cell it would move to is free.
    ///  - Switch_1 (momentary) is active while a barrel, severed hand or a hand tip lies on it. Arm segments do not count.
    ///  - Switch_2 (toggle) flips each time a hand or barrel enters its cell. Arm segments do not count.
    ///  - A hand starts one cell in front of its cat (the facing stored in the level), never on the body.
    ///  - A door is open while any linked switch is active (a door that starts open: while none is). Standing in the
    ///    doorway does not keep it open. Doors stand between two walls (see LevelDataSO.Validate).
    ///  - When a door closes on a barrel or a severed hand, it is crushed and gone.
    ///  - When a door closes while an arm passes through it, the arm is cut: the part from the door on is removed,
    ///    the arm ends just in front of the door, and the old hand stays behind as a severed hand that acts like a barrel.
    ///  - When a door closes on a hand tip, the arm retracts the same way, but nothing is left behind (the hand is crushed).
    ///  - A cat can be pushed through an open doorway. If a door closes on its body or on the arm's start cell (the cell
    ///    next to the body), the cat is crushed and the level is lost: the board locks and the scene flow reloads it.
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

        [Header("Wall borders (edges facing outside the board)")]
        [Tooltip("Border on one side (drawn on the top edge, turned to fit). Leave all four empty to keep plain walls.")]
        [SerializeField] private Sprite wallSingleEdge;
        [Tooltip("Border on two neighbouring sides (drawn on the top and left edges).")]
        [SerializeField] private Sprite wallOuterCorner;
        [Tooltip("Border on two opposite sides (drawn on the top and bottom edges).")]
        [SerializeField] private Sprite wallStraight;
        [Tooltip("Border on three sides (drawn on the left, top and bottom edges; open to the right).")]
        [SerializeField] private Sprite wallEndCap;

        [SerializeField] private Barrel barrelPrefab;
        [Tooltip("Optional. Prefab of the severed hand (a Barrel component). When empty, the barrel prefab is used with the cat's hand sprite.")]
        [SerializeField] private Barrel severedHandPrefab;
        [Tooltip("Scale of a severed hand relative to the normal hand, so it reads as an obstacle like a crate.")]
        [SerializeField, Min(0.1f)] private float severedHandScale = 1.3f;
        [SerializeField] private SwitchEntity momentarySwitchPrefab;
        [SerializeField] private SwitchEntity toggleSwitchPrefab;
        [SerializeField] private DoorEntity doorPrefab;

        [Header("Cats")]
        [SerializeField] private CatArmController catA;
        [SerializeField] private CatArmController catB;

        private readonly HashSet<Vector2Int> walls = new HashSet<Vector2Int>();
        private readonly List<Barrel> barrels = new List<Barrel>();
        private readonly List<Barrel> severedHands = new List<Barrel>();
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
        private bool isLost;
        private bool interactiveEventsMuted;

        /// <summary>Raised after a level has been built and is ready to play.</summary>
        public event Action LevelLoaded;

        /// <summary>Raised after any change that the HUD may want to display (move, undo, reset, load).</summary>
        public event Action StateChanged;

        /// <summary>Raised once when the two hands meet on the same cell.</summary>
        public event Action Won;

        /// <summary>
        /// Raised when the victory effects have finished and the result may be shown (win popup). The scene flow
        /// calls <see cref="FinishWinPresentation"/>; <see cref="Won"/> itself fires immediately.
        /// </summary>
        public event Action WinPresentationFinished;

        /// <summary>Raised after every move attempt, successful or blocked (useful for sound and haptics).</summary>
        public event Action<CatId, MoveOutcome> MoveResolved;

        /// <summary>Raised when a closing door cuts the arm of a cat.</summary>
        public event Action<CatId> ArmCut;

        /// <summary>Raised when the controlled cat changes.</summary>
        public event Action<CatId> ActiveCatChanged;

        /// <summary>Raised when the player switches to the other cat (not when a level loads).</summary>
        public event Action<CatId> CatSwitched;

        /// <summary>Raised when a closing door cuts an arm, with the door cell.</summary>
        public event Action<CatId, Vector2Int> ArmCutAtCell;

        /// <summary>Raised when a closing door crushes a barrel or a severed hand during play (not on load, undo or reset). Argument: door cell.</summary>
        public event Action<Vector2Int> ObjectCrushed;

        /// <summary>Raised for every cat part a closing door crushes (the body or the hand cell next to it). Arguments: cat, door cell.</summary>
        public event Action<CatId, Vector2Int> CatCrushed;

        /// <summary>Raised once when a door crushed a cat: the level is lost and the board is locked until it is reset.</summary>
        public event Action Lost;

        /// <summary>Raised when a door opens or closes during play (not on load, undo or reset). Arguments: cell, open.</summary>
        public event Action<Vector2Int, bool> DoorChanged;

        /// <summary>Raised when a switch turns on or off during play (not on load, undo or reset). Arguments: cell, active.</summary>
        public event Action<Vector2Int, bool> SwitchChanged;

        /// <summary>Raised when a crate, severed hand or the other cat is pushed. Arguments: cell it moved to, direction.</summary>
        public event Action<Vector2Int, Vector2Int> ObjectPushed;

        public GridSpace Coords => coords;
        public LevelDataSO CurrentLevel => currentLevel;
        public bool IsLevelLoaded => currentLevel != null && coords != null;
        public bool IsWon => isWon;

        /// <summary>True after a door crushed a cat. No move is accepted until the level is reset.</summary>
        public bool IsLost => isLost;

        /// <summary>True while a cat is still playing the cut animation; moves are rejected until it is done.</summary>
        public bool IsBusy => cats != null && (cats[0].IsPlayingCut || cats[1].IsPlayingCut);
        public int MoveCount => moveCount;
        public bool CanUndo => IsLevelLoaded && !isWon && !isLost && undoStack.Count > 0;
        public CatId ActiveCat => activeCat;

        private void Awake()
        {
            cats = new[] { catA, catB };
        }

        private void OnEnable()
        {
            SoundManager.BindGrid(this);
        }

        private void OnDisable()
        {
            SoundManager.UnbindGrid(this);
        }

        /// <summary>Tells listeners (the win popup) that the victory presentation is over.</summary>
        public void FinishWinPresentation()
        {
            if (isWon)
            {
                WinPresentationFinished?.Invoke();
            }
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

            catA.Initialize(coords, level.GetCatStart(CatId.CatA), level.GetHandStart(CatId.CatA), level.GetFacing(CatId.CatA), level.GetMaxSteps(CatId.CatA));
            catB.Initialize(coords, level.GetCatStart(CatId.CatB), level.GetHandStart(CatId.CatB), level.GetFacing(CatId.CatB), level.GetMaxSteps(CatId.CatB));

            moveCount = 0;
            isWon = false;
            isLost = false;
            undoStack.Clear();
            interactiveEventsMuted = true;
            RefreshInteractiveState();
            interactiveEventsMuted = false;
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
            severedHands.Clear();
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
            isLost = false;
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
            isLost = false;
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
                CatSwitched?.Invoke(activeCat);
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

            return EvaluateEnter(targetPos, pushDir, mover, out _, out _, out _).IsSuccess();
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
            if (!IsLevelLoaded || isWon || isLost || IsBusy)
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

            MoveOutcome outcome = EvaluateEnter(target, direction, cat, out Barrel pushedBarrel, out Vector2Int barrelDestination, out CatArmController pushedCat);
            if (!outcome.IsSuccess())
            {
                return outcome;
            }

            PushUndoSnapshot();

            if (pushedCat != null)
            {
                PushCat(pushedCat, direction);
                ObjectPushed?.Invoke(pushedCat.RootPosition, direction);
            }

            if (pushedBarrel != null)
            {
                MoveBarrel(pushedBarrel, barrelDestination);
                FlipToggleAt(barrelDestination);
                ObjectPushed?.Invoke(barrelDestination, direction);
            }

            // A hand that meets the other cat's hand does not "step onto" that cell: whatever lies there
            // (for example a toggle switch under the other hand) is already pressed by the other hand.
            bool isHandshake = cats[(int)cat.Id.Other()].HandPosition == target;

            cat.Extend(target);
            if (!isHandshake)
            {
                FlipToggleAt(target);
            }

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
            out Vector2Int barrelDestination,
            out CatArmController pushedCat)
        {
            pushedBarrel = null;
            pushedCat = null;
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

            // The other cat's body can be pushed away, but only as a whole (body and arm move together).
            CatArmController other = mover != null ? cats[(int)mover.Id.Other()] : null;
            if (other != null && target == other.RootPosition)
            {
                if (!CanCatShift(other, direction, mover))
                {
                    return MoveOutcome.BlockedByArm;
                }

                pushedCat = other;
                return MoveOutcome.ExtendedAndPushedCat;
            }

            if (IsBlockedByCatBody(target, direction, mover))
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

        /// <summary>Can <paramref name="cat"/> (body and every arm cell) move one cell in <paramref name="direction"/>?</summary>
        private bool CanCatShift(CatArmController cat, Vector2Int direction, CatArmController pusher)
        {
            // An open doorway is fine (the body or the arm start cell may be pushed into it); a closed one blocks the push.
            // If the door shuts while the body or the arm start cell is in it, the cat is crushed (see CrushCatsAtClosedDoors).
            if (cat.IsPlayingCut || !CanCatEnterCell(cat.RootPosition + direction, pusher))
            {
                return false;
            }

            IReadOnlyList<Vector2Int> path = cat.ArmPath;
            for (int i = 0; i < path.Count; i++)
            {
                if (!CanCatEnterCell(path[i] + direction, pusher))
                {
                    return false;
                }
            }

            return true;
        }

        private bool CanCatEnterCell(Vector2Int cell, CatArmController pusher)
        {
            return coords.IsInside(cell)
                   && !walls.Contains(cell)
                   && !IsClosedDoor(cell)
                   && !barrelsByCell.ContainsKey(cell)
                   && !pusher.OccupiesCell(cell);
        }

        private void PushCat(CatArmController cat, Vector2Int direction)
        {
            var oldCells = new HashSet<Vector2Int>(cat.ArmPath);
            cat.ShiftBy(direction);

            // A body or hand that was shoved onto a toggle switch flips it, like a hand entering that cell.
            // Other arm cells that slide over a switch do not count. The body never lies on the arm, so its new cell is always new.
            FlipToggleAt(cat.RootPosition);
            Vector2Int hand = cat.HandPosition;
            if (!oldCells.Contains(hand))
            {
                FlipToggleAt(hand);
            }
        }

        private bool IsBlockedByCatBody(Vector2Int cell, Vector2Int direction, CatArmController mover)
        {
            for (int i = 0; i < cats.Length; i++)
            {
                CatArmController cat = cats[i];
                if (!cat.OccupiesCell(cell))
                {
                    continue;
                }

                // Only the other cat's hand can be shaken, and only head-on (the two hands point at each other);
                // a cat's body (root cell) and arm are never enterable.
                bool isHandshake = mover != null && cat != mover && cat.HandPosition == cell && cat.HandDirection == -direction;
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
                RaiseSwitchChanged(entry);
            }
        }

        private void CommitMove()
        {
            moveCount++;

            // Two hands on one cell is the win, whatever the doors do in the same moment: no arm is cut then.
            bool handsMet = catA.HandPosition == catB.HandPosition;
            if (handsMet)
            {
                UpdateSwitchesAndDoors();
            }
            else
            {
                RefreshInteractiveState();
            }

            StateChanged?.Invoke();

            if (isLost)
            {
                Lost?.Invoke();
                return;
            }

            if (handsMet)
            {
                isWon = true;
                Won?.Invoke();
            }
        }

        // ---------------------------------------------------------------- switches and doors

        /// <summary>
        /// Recomputes switches and doors, then cuts every arm that a closed door now divides. A cut moves the hand and
        /// shortens the arm, which can change switches again, so this repeats until nothing changes.
        /// </summary>
        private void RefreshInteractiveState()
        {
            const int maxPasses = 16;
            for (int pass = 0; pass < maxPasses; pass++)
            {
                UpdateSwitchesAndDoors();

                // A door shutting on a cat's body or on the hand next to it ends the level at once.
                if (!interactiveEventsMuted && CrushCatsAtClosedDoors())
                {
                    return;
                }

                // Both run every pass; crushing a barrel or cutting an arm can release a switch and change the doors again.
                bool crushed = CrushObjectsAtClosedDoors();
                bool cut = CutArmsAtClosedDoors();
                if (!crushed && !cut)
                {
                    return;
                }
            }

            UpdateSwitchesAndDoors();
        }

        private void UpdateSwitchesAndDoors()
        {
            for (int i = 0; i < switches.Count; i++)
            {
                SwitchEntity entry = switches[i];
                if (entry.IsMomentary)
                {
                    bool wasActive = entry.IsActive;
                    entry.SetActive(IsCellPressed(entry.GridPosition));
                    if (entry.IsActive != wasActive)
                    {
                        RaiseSwitchChanged(entry);
                    }
                }
            }

            for (int i = 0; i < doors.Count; i++)
            {
                DoorEntity door = doors[i];
                bool shouldBeOpen = IsLinkActive(door.LinkId) != door.StartsOpen;
                bool wasOpen = door.IsOpen;
                door.SetOpen(shouldBeOpen);
                if (door.IsOpen != wasOpen && !interactiveEventsMuted)
                {
                    DoorChanged?.Invoke(door.GridPosition, door.IsOpen);
                }
            }
        }

        private void RaiseSwitchChanged(SwitchEntity entry)
        {
            if (!interactiveEventsMuted)
            {
                SwitchChanged?.Invoke(entry.GridPosition, entry.IsActive);
            }
        }

        /// <summary>
        /// A momentary switch is pressed by a barrel / severed hand, by a hand tip or by a cat's body (a cat shoved onto it).
        /// Arm segments passing over it do not count.
        /// </summary>
        private bool IsCellPressed(Vector2Int cell)
        {
            return barrelsByCell.ContainsKey(cell)
                   || catA.HandPosition == cell || catB.HandPosition == cell
                   || catA.RootPosition == cell || catB.RootPosition == cell;
        }

        // ---------------------------------------------------------------- crushing and arm cutting

        /// <summary>
        /// A cat can be pushed through an open doorway. If the door closes while its body, or the first arm cell right
        /// next to the body, is in the doorway, the cat is crushed and the level is lost. Returns true when that happened.
        /// </summary>
        private bool CrushCatsAtClosedDoors()
        {
            var crushed = new List<KeyValuePair<CatId, Vector2Int>>();
            for (int c = 0; c < cats.Length; c++)
            {
                CatArmController cat = cats[c];
                if (IsClosedDoor(cat.RootPosition))
                {
                    crushed.Add(new KeyValuePair<CatId, Vector2Int>(cat.Id, cat.RootPosition));
                }

                if (cat.ArmPath.Count > 0 && IsClosedDoor(cat.ArmPath[0]))
                {
                    crushed.Add(new KeyValuePair<CatId, Vector2Int>(cat.Id, cat.ArmPath[0]));
                }
            }

            if (crushed.Count == 0)
            {
                return false;
            }

            isLost = true;
            for (int i = 0; i < crushed.Count; i++)
            {
                CatCrushed?.Invoke(crushed[i].Key, crushed[i].Value);
            }

            return true;
        }

        /// <summary>A closed door crushes every barrel and severed hand in its doorway. Returns true when something was crushed.</summary>
        private bool CrushObjectsAtClosedDoors()
        {
            bool crushedAny = CrushOnClosedDoors(barrels);
            return CrushOnClosedDoors(severedHands) || crushedAny;
        }

        private bool CrushOnClosedDoors(List<Barrel> list)
        {
            bool crushedAny = false;
            for (int i = 0; i < list.Count; i++)
            {
                Barrel barrel = list[i];
                if (barrel.IsCrushed || !IsClosedDoor(barrel.GridPosition))
                {
                    continue;
                }

                Vector2Int cell = barrel.GridPosition;
                if (barrelsByCell.TryGetValue(cell, out Barrel mapped) && mapped == barrel)
                {
                    barrelsByCell.Remove(cell);
                }

                barrel.Crush(interactiveEventsMuted ? 0f : 0.15f);
                crushedAny = true;
                if (!interactiveEventsMuted)
                {
                    ObjectCrushed?.Invoke(cell);
                }
            }

            return crushedAny;
        }

        private bool CutArmsAtClosedDoors()
        {
            bool cutAny = false;

            for (int c = 0; c < cats.Length; c++)
            {
                CatArmController cat = cats[c];
                int cutIndex = -1;

                // The door nearest to the cat wins when the arm passes through several closed doors.
                for (int d = 0; d < doors.Count; d++)
                {
                    DoorEntity door = doors[d];
                    if (!door.IsOpen && cat.TryFindPassThroughIndex(door.GridPosition, out int index) && (cutIndex < 0 || index < cutIndex))
                    {
                        cutIndex = index;
                    }
                }

                if (cutIndex < 0)
                {
                    continue;
                }

                CutArm(cat, cutIndex);
                cutAny = true;
            }

            return cutAny;
        }

        private void CutArm(CatArmController cat, int cutIndex)
        {
            cat.CompleteAnimationImmediately();
            Vector2Int doorCell = cat.ArmPath[cutIndex];

            // The old hand drops as a severed hand. If it lies on a closed door (the door closed on the tip itself)
            // it is crushed at once, so nothing is left behind.
            if (!IsClosedDoor(cat.HandPosition))
            {
                SpawnSeveredHand(cat);
            }

            cat.CutAt(cutIndex);
            ArmCut?.Invoke(cat.Id);
            ArmCutAtCell?.Invoke(cat.Id, doorCell);
        }

        /// <summary>Leaves the hand of <paramref name="cat"/> behind on its current cell as a pushable crate.</summary>
        private void SpawnSeveredHand(CatArmController cat)
        {
            bool hasDedicatedPrefab = severedHandPrefab != null;
            Barrel prefab = hasDedicatedPrefab ? severedHandPrefab : barrelPrefab;
            if (!RequirePrefab(prefab, "severedHandPrefab (or barrelPrefab)"))
            {
                return;
            }

            Vector2Int cell = cat.HandPosition;
            Barrel hand = Instantiate(prefab, levelRoot);
            hand.Initialize(coords, cell);
            hand.SetVisual(hasDedicatedPrefab ? null : cat.HandSprite, cat.HandAngle, cat.HandScale * severedHandScale);
            hand.PlayAppear(0.2f);

            severedHands.Add(hand);
            barrelsByCell[cell] = hand;
            spawnedObjects.Add(hand.gameObject);
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
            var barrelCrushed = new List<bool>(barrels.Count);
            for (int i = 0; i < barrels.Count; i++)
            {
                barrelCells.Add(barrels[i].GridPosition);
                barrelCrushed.Add(barrels[i].IsCrushed);
            }

            var toggleStates = new List<bool>(toggleSwitches.Count);
            for (int i = 0; i < toggleSwitches.Count; i++)
            {
                toggleStates.Add(toggleSwitches[i].IsActive);
            }

            var severedCells = new List<Vector2Int>(severedHands.Count);
            var severedCrushed = new List<bool>(severedHands.Count);
            for (int i = 0; i < severedHands.Count; i++)
            {
                severedCells.Add(severedHands[i].GridPosition);
                severedCrushed.Add(severedHands[i].IsCrushed);
            }

            return new GameStateSnapshot(
                catA.RootPosition, catB.RootPosition, catA.ArmPath, catB.ArmPath,
                barrelCells, barrelCrushed, severedCells, severedCrushed, toggleStates, moveCount);
        }

        private void ApplySnapshot(GameStateSnapshot snapshot)
        {
            catA.RestoreState(snapshot.GetRoot(CatId.CatA), snapshot.GetArmPath(CatId.CatA));
            catB.RestoreState(snapshot.GetRoot(CatId.CatB), snapshot.GetArmPath(CatId.CatB));

            barrelsByCell.Clear();
            IReadOnlyList<Vector2Int> cells = snapshot.BarrelPositions;
            IReadOnlyList<bool> barrelCrushed = snapshot.BarrelCrushed;
            for (int i = 0; i < barrels.Count; i++)
            {
                RestoreCrate(barrels[i], cells[i], barrelCrushed[i]);
            }

            // Severed hands are only ever added, so the snapshot holds a prefix of the current list.
            IReadOnlyList<Vector2Int> severedCells = snapshot.SeveredHandPositions;
            IReadOnlyList<bool> severedCrushed = snapshot.SeveredHandCrushed;
            while (severedHands.Count > severedCells.Count)
            {
                Barrel extra = severedHands[severedHands.Count - 1];
                severedHands.RemoveAt(severedHands.Count - 1);
                spawnedObjects.Remove(extra.gameObject);
                Destroy(extra.gameObject);
            }

            for (int i = 0; i < severedHands.Count; i++)
            {
                RestoreCrate(severedHands[i], severedCells[i], severedCrushed[i]);
            }

            IReadOnlyList<bool> states = snapshot.ToggleStates;
            for (int i = 0; i < toggleSwitches.Count; i++)
            {
                toggleSwitches[i].SetActive(states[i]);
            }

            moveCount = snapshot.MoveCount;
            interactiveEventsMuted = true;
            RefreshInteractiveState();
            interactiveEventsMuted = false;
        }

        /// <summary>Puts a barrel or severed hand back where the snapshot had it (crushed ones stay hidden and off the board).</summary>
        private void RestoreCrate(Barrel crate, Vector2Int cell, bool crushed)
        {
            crate.Restore();
            crate.SetGridPosition(cell, false);

            if (crushed)
            {
                crate.Crush(0f);
            }
            else
            {
                barrelsByCell[cell] = crate;
            }
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

        /// <summary>True when the cell is outside the board or is not a wall (floor, door, switch...).</summary>
        private bool IsOpenGround(Vector2Int cell)
        {
            return !coords.IsInside(cell) || !walls.Contains(cell);
        }

        /// <summary>
        /// Gives a wall the border picture that matches the sides where it meets open ground. One side, two neighbouring
        /// sides (corner), two opposite sides or three sides (end cap) get a picture; a wall buried among walls (or
        /// a lone pillar with four open sides) keeps the plain look. The pictures are drawn with the border on fixed edges
        /// and turned or flipped here. Call it once every wall of the level is known.
        /// </summary>
        private void ApplyWallBorder(GameObject wall, Vector2Int cell)
        {
            bool up = IsOpenGround(cell + Vector2Int.up);
            bool down = IsOpenGround(cell + Vector2Int.down);
            bool left = IsOpenGround(cell + Vector2Int.left);
            bool right = IsOpenGround(cell + Vector2Int.right);
            int sides = (up ? 1 : 0) + (down ? 1 : 0) + (left ? 1 : 0) + (right ? 1 : 0);
            if (sides == 0 || sides == 4)
            {
                return;
            }

            Sprite sprite = null;
            float angle = 0f;
            bool flipX = false;
            bool flipY = false;

            switch (sides)
            {
                case 1:
                    sprite = wallSingleEdge;
                    angle = up ? 0f : left ? 90f : down ? 180f : 270f;
                    break;

                case 2:
                    if (up && down)
                    {
                        sprite = wallStraight;
                    }
                    else if (left && right)
                    {
                        sprite = wallStraight;
                        angle = 90f;
                    }
                    else
                    {
                        sprite = wallOuterCorner;
                        flipX = right;
                        flipY = down;
                    }

                    break;

                case 3:
                    // Drawn open to the right; the open side is the one that faces into the board.
                    sprite = wallEndCap;
                    if (!right)
                    {
                        angle = 0f;
                    }
                    else if (!left)
                    {
                        flipX = true;
                    }
                    else if (!up)
                    {
                        angle = 90f;
                    }
                    else
                    {
                        angle = 270f;
                    }

                    break;
            }

            SpriteRenderer spriteRenderer = wall.GetComponent<SpriteRenderer>();
            if (sprite == null || spriteRenderer == null)
            {
                return;
            }

            spriteRenderer.sprite = sprite;
            spriteRenderer.color = Color.white;
            spriteRenderer.flipX = flipX;
            spriteRenderer.flipY = flipY;
            wall.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        private bool BuildObstacles(LevelDataSO level)
        {
            IReadOnlyList<ObstacleData> obstacles = level.Obstacles;
            var wallObjects = new List<KeyValuePair<Vector2Int, GameObject>>();

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
                        wallObjects.Add(new KeyValuePair<Vector2Int, GameObject>(data.position, wall));
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

            // Borders depend on the neighbours, so they are drawn once every wall is placed.
            for (int i = 0; i < wallObjects.Count; i++)
            {
                ApplyWallBorder(wallObjects[i].Value, wallObjects[i].Key);
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
