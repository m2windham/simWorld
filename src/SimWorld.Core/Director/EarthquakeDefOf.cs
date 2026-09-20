using SimWorld.Defs;

namespace SimWorld.Director
{
    /// <summary>The EARTHQUAKE incident's own <c>[DefOf]</c> binding, in its own file and its own class per
    /// CLAUDE.md's "a new [DefOf] binding goes in its own class, never appended to a shared one" —
    /// <see cref="IncidentDefOf"/> in <c>DirectorDefOf.cs</c> already binds <c>RaidEnemy</c>/<c>Disease_Flu</c>
    /// and stays untouched by this addition.</summary>
    [DefOf]
    public static class EarthquakeIncidentDefOf
    {
        public static IncidentDef Earthquake = null!;
    }
}
