using SimWorld.Defs;

namespace SimWorld.Pawns
{
    /// <summary>Broad phase of a life stage (RimWorld: <c>RimWorld.DevelopmentalStage</c>).</summary>
    public enum DevelopmentalStage
    {
        Baby,
        Child,
        Adult,
    }

    /// <summary>
    /// One phase of a race's life (RimWorld: <c>RimWorld.LifeStageDef</c>): scales body size, hit points and
    /// hunger, and marks whether the pawn can reproduce or is always downed. The age at which a race enters
    /// this stage is not here — see <see cref="RaceProperties.lifeStageAges"/>, which pairs a LifeStageDef with
    /// a minimum age so the same stage defs can be reused (or not) across races at different ages.
    /// </summary>
    public class LifeStageDef : Def
    {
        public bool visible = true;
        public bool reproductive;
        public float bodySizeFactor = 1f;
        public float healthScaleFactor = 1f;
        public float hungerRateFactor = 1f;
        public float foodMaxFactor = 1f;
        public float marketValueFactor = 1f;
        public bool alwaysDowned;
        public DevelopmentalStage developmentalStage = DevelopmentalStage.Adult;
    }

    /// <summary>One race's life stage and the minimum biological age (in years) it starts at (RimWorld: <c>RimWorld.LifeStageAge</c>).</summary>
    public class LifeStageAge
    {
        public LifeStageDef? def;
        public float minAge;

        public override string ToString() => (def?.defName ?? "?") + "@" + minAge.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
