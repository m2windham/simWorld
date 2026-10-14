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

        /// <summary>
        /// This stage's body may breed. Read by <see cref="FamilyManager"/>'s birth sweep and, as the
        /// maturity gate for animal produce, by <c>CompHasGatherableBodyResource</c> — see that property for
        /// why one flag covers eggs, milk and wool here where RimWorld has four.
        /// </summary>
        public bool reproductive;

        public float bodySizeFactor = 1f;
        public float healthScaleFactor = 1f;
        public float hungerRateFactor = 1f;

        /// <summary>Scales how much nutrition the stomach holds, on top of body size
        /// (<see cref="Needs.Need_Food.MaxLevel"/>).</summary>
        public float foodMaxFactor = 1f;

        /// <summary>
        /// Scales what a pawn in this stage is worth (RimWorld: read by the market-value stat when the thing
        /// being priced is a pawn). Read by <see cref="Economy.PriceUtility.PawnQualityPriceFactor"/>, which
        /// is how a baby counts for less than an adult in the storyteller's wealth term.
        /// <para/>The values below are this port's own and were authored before anything read them; the
        /// RimWorld wiki's current table for humans (rimworldwiki.com/wiki/Child) reads 0.5 for ages 0-2, 0.9 for
        /// 3-9, 0.95 for 9-13 and 1 from 13, so Baby agrees and Child and Teenager do not. They are left as
        /// authored: reading them is not a reason to retune them, and which numbers 1.0 gave these stages (whose
        /// ages differ from the wiki's) was not checked.
        /// </summary>
        public float marketValueFactor = 1f;

        /// <summary>
        /// A pawn in this stage cannot be on its feet (an infant). Read by
        /// <c>Pawn_HealthTracker.LifeStageForcesDowned</c>, which explains why it is a gate beside
        /// <c>ForceDowned</c> rather than an answer from the capacity system.
        /// </summary>
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
