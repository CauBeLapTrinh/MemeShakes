namespace Catshakes
{
    /// <summary>Result of asking a cat to move its hand one cell.</summary>
    public enum MoveOutcome
    {
        Extended,
        ExtendedAndPushedBarrel,
        Retracted,
        BlockedOutOfBounds,
        BlockedByWall,
        BlockedByClosedDoor,
        BlockedByArm,
        BlockedByBarrel,
        BlockedNoStepsLeft,
        BlockedNotAdjacent,
        BlockedInputLocked
    }

    public static class MoveOutcomeExtensions
    {
        public static bool IsSuccess(this MoveOutcome outcome)
        {
            return outcome == MoveOutcome.Extended
                   || outcome == MoveOutcome.ExtendedAndPushedBarrel
                   || outcome == MoveOutcome.Retracted;
        }
    }
}
