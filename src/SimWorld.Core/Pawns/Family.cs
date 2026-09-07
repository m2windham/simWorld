using SimWorld.Sim;

namespace SimWorld.Pawns
{
    /// <summary>
    /// A household (RimWorld has no equivalent; SimWorld's own addition, following Epoch's <c>Family</c> shape
    /// — <c>docs/research/epoch-inspiration.md</c> §5 — but built around the fixed rule that a wedding always
    /// founds a brand new one; see <see cref="FamilyManager.FoundHousehold"/>). <see cref="livingCount"/> and
    /// <see cref="totalCount"/> track *current household membership*, not a whole bloodline: a member who
    /// marries out leaves this family's living count and starts a fresh one in the household they found, so
    /// this never becomes the merge-and-absorb lineage Epoch measured concentrating 90% of a population into
    /// one family by year 20.
    /// </summary>
    public sealed class Family : IExposable
    {
        public int id = -1;

        public int foundingTick;

        /// <summary>The pawns who founded this household by marrying (see <see cref="FamilyManager.FoundHousehold"/>).
        /// <see cref="Pawn_RelationsTracker.None"/> when there is no second founder.</summary>
        public int founderIdA = Pawn_RelationsTracker.None;

        public int founderIdB = Pawn_RelationsTracker.None;

        /// <summary>Generations removed from a founding, family-less line — the couple's own generation at
        /// founding time, carried forward rather than reset (Epoch's fix, §5: "makes every wedding start a
        /// fresh Family inheriting the couple's own generation").</summary>
        public int generation;

        /// <summary>Members of this household currently alive.</summary>
        public int livingCount;

        /// <summary>Every member this household has ever had, living or dead or since married out; never
        /// decremented, unlike <see cref="livingCount"/>.</summary>
        public int totalCount;

        /// <summary>Tick of this household's most recent birth, seeded at founding so a birth cooldown never
        /// blocks a newlywed couple's first roll (see <see cref="DemographyTuning.MinBirthIntervalTicks"/>).</summary>
        public int lastBirthTick;

        /// <summary>
        /// Two RNG draws captured once at founding (Epoch: <c>name_seed: [f64; 2]</c>, §5) so a future render
        /// layer can compose this household's display surname deterministically without the core holding or
        /// emitting the string itself — the same discipline <see cref="PawnBioAndNameGenerator"/> already
        /// follows for individual names. Deliberately not resolved to a string here: nothing in
        /// <c>SimWorld.Core</c> should hold prose the render layer owns.
        /// </summary>
        public float surnameSeedA;

        public float surnameSeedB;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", -1);
            Scribe_Values.Look(ref foundingTick, "foundingTick");
            Scribe_Values.Look(ref founderIdA, "founderIdA", Pawn_RelationsTracker.None);
            Scribe_Values.Look(ref founderIdB, "founderIdB", Pawn_RelationsTracker.None);
            Scribe_Values.Look(ref generation, "generation");
            Scribe_Values.Look(ref livingCount, "livingCount");
            Scribe_Values.Look(ref totalCount, "totalCount");
            Scribe_Values.Look(ref lastBirthTick, "lastBirthTick");
            Scribe_Values.Look(ref surnameSeedA, "surnameSeedA");
            Scribe_Values.Look(ref surnameSeedB, "surnameSeedB");
        }

        public override string ToString() => "Family" + id + " (living " + livingCount + "/" + totalCount + ", gen " + generation + ")";
    }
}
