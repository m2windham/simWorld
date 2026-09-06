namespace SimWorld.World
{
    /// <summary>How rugged a tile's terrain is (RimWorld: <c>RimWorld.Hilliness</c>). Drives movement cost and settling.</summary>
    public enum Hilliness
    {
        Undefined,
        Flat,
        SmallHills,
        LargeHills,
        Mountainous,
        Impassable,
    }

    /// <summary>World-generation rainfall setting; scales the rainfall field everywhere (RimWorld: <c>RimWorld.OverallRainfall</c>).</summary>
    public enum OverallRainfall
    {
        AlmostNone,
        Little,
        LittleBitLess,
        Normal,
        LittleBitMore,
        High,
        VeryHigh,
    }

    /// <summary>World-generation temperature setting; offsets the temperature field everywhere (RimWorld: <c>RimWorld.OverallTemperature</c>).</summary>
    public enum OverallTemperature
    {
        VeryCold,
        Cold,
        LittleBitColder,
        Normal,
        LittleBitWarmer,
        Hot,
        VeryHot,
    }

    /// <summary>
    /// World-generation population setting. RimWorld has no direct equivalent (its world gen takes a raw
    /// faction count); this mirrors the shape of <see cref="OverallRainfall"/>/<see cref="OverallTemperature"/>
    /// and scales faction and settlement counts (see <c>Gen.WorldGenStep_Factions</c>).
    /// </summary>
    public enum OverallPopulation
    {
        AlmostNone,
        Little,
        LittleBitLess,
        Normal,
        LittleBitMore,
        High,
        VeryHigh,
    }
}
