namespace Catshakes
{
    /// <summary>Identifies one of the two cats in a level.</summary>
    public enum CatId
    {
        CatA = 0,
        CatB = 1
    }

    public static class CatIdExtensions
    {
        public static CatId Other(this CatId id)
        {
            return id == CatId.CatA ? CatId.CatB : CatId.CatA;
        }
    }
}
