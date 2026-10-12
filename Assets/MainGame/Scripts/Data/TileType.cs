namespace Catshakes
{
    /// <summary>Kinds of objects that can occupy a grid cell.</summary>
    public enum TileType
    {
        Empty = 0,

        /// <summary>Static, impassable.</summary>
        Wall = 1,

        /// <summary>Wooden barrel, can be pushed if the cell behind it is free.</summary>
        Barrel = 2,

        /// <summary>Switch_1 - momentary: active only while something is pressing it.</summary>
        SwitchMomentary = 3,

        /// <summary>Switch_2 - toggle: flips on/off each time something steps onto it.</summary>
        SwitchToggle = 4,

        /// <summary>Door linked to switches by linkId; closed unless a linked switch is active.</summary>
        Door = 5
    }
}
