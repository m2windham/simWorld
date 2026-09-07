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
        /// This household's surname, drawn from the shared <see cref="NameBankDef"/> pools at founding — the
        /// same source individual pawn names come from. SimWorld's core is defName- and content-driven rather
        /// than string-free, so resolving the name here (as <see cref="PawnBioAndNameGenerator"/> already does)
        /// keeps one naming path instead of leaving the render layer to reinvent it from seeds.
        /// </summary>
        public string surname = string.Empty;

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
            Scribe_Values.Look(ref surname, "surname", string.Empty);
        }

        public override string ToString() => (surname.Length > 0 ? surname : "Family" + id.ToString(System.Globalization.CultureInfo.InvariantCulture)) + " (living " + livingCount + "/" + totalCount + ", gen " + generation + ")";
    }
}
