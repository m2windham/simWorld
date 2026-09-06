namespace SimWorld.Defs
{
    /// <summary>
    /// Technology era of a def, faction or civilization (RimWorld: <c>Verse.TechLevel</c>). Research cost,
    /// faction gear and settlement generation key off it; SimWorld's era ladder maps onto these bands.
    /// </summary>
    public enum TechLevel
    {
        Undefined,
        Animal,
        Neolithic,
        Medieval,
        Industrial,
        Spacer,
        Ultra,
        Archotech,
    }
}
