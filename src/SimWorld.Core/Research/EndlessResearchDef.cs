using System.Collections.Generic;

using SimWorld.Defs;

namespace SimWorld.Research
{
    /// <summary>
    /// One endless research track: a line of enquiry a civilization that has finished the authored tree keeps
    /// working on (<c>docs/status.json</c>, <c>research.endless</c>).
    ///
    /// <para/><b>A track is a vocabulary, not a list.</b> The first version of this Def carried three authored
    /// titles per track and appended Roman numerals once they ran out, so a civilization that reached the end
    /// of the authored tree found "matter compilation IV" waiting for it. What a track carries now is the
    /// three word-lists a name is <i>composed</i> from — the things this domain studies
    /// (<see cref="substrates"/>), the shapes a foundational result takes (<see cref="foundationForms"/>) and
    /// the shapes an applied one takes (<see cref="appliedForms"/>) — so the number of distinct names a track
    /// can produce is the product of the lists and the age register above them, not the length of one list.
    /// See <see cref="EndlessAgeDef"/> for the fourth word, and for why the product never has to repeat.
    /// </summary>
    public class EndlessResearchDef : Def
    {
        /// <summary>The tag on the authored tree this track continues — the tracks are already there
        /// (<c>ResearchProjectDef.tags</c>), so an endless track is a continuation of one rather than a new
        /// taxonomy laid over the tree.</summary>
        public string trackTag = "";

        /// <summary>What this domain studies: the noun at the centre of every name the track generates
        /// ("lattice", "plasma", "cognition"). Disjoint between tracks — a matter project never borrows a
        /// mind noun, which is both what keeps a generated name coherent and what makes two tracks' labels
        /// provably distinct. Cross-track disjointness is a property of the shipped set rather than of one
        /// Def, so the content test asserts it, not <see cref="ConfigErrors"/>.</summary>
        public List<string> substrates = new List<string>();

        /// <summary>What a <i>foundational</i> result is called — the theory end of the vocabulary
        /// ("synthesis", "first principles"). Used for the one project per age that the rest of that age's
        /// work in this track hangs off.</summary>
        public List<string> foundationForms = new List<string>();

        /// <summary>What an <i>applied</i> result is called — the practice end ("metallurgy", "metrology").
        /// Used for the optional projects hanging off an age's foundation. Disjoint from
        /// <see cref="foundationForms"/>, so a name's form alone says whether it is spine or leaf.</summary>
        public List<string> appliedForms = new List<string>();

        /// <summary>Cost of this track's first generated project, before the age factor.
        /// <para/>
        /// <b>Unsourced</b> — RimWorld has no endless tech to take a number from. What pins it is a
        /// relationship rather than a literal: the first generated project must cost at least as much as the
        /// most expensive project in the authored tree, because it comes after all of them, and not so much
        /// more that the tail opens with a wall. The test asserts that band against the shipped tree rather
        /// than against this number.</summary>
        public float baseCost = 9000f;

        /// <summary>Exponent on age depth (see <see cref="EndlessResearch.CostFor"/>).
        /// <para/>
        /// <b>Polynomial, deliberately.</b> The first version of this Def grew costs geometrically at 1.35 per
        /// tier, which ends the tail: 1.35^20 is 400x, so the twentieth generated project cost more than the
        /// whole authored tree and no civilization would ever see a twenty-first. A polynomial still makes
        /// every step harder than the last — which is the point of an endless tail, that research can always
        /// be spent — while the <i>marginal</i> step shrinks toward nothing, so more depth stays reachable at
        /// any depth. Unsourced; the tests pin the trend (always rising, never a wall), not the exponent.</summary>
        public float costExponent = 1.35f;

        /// <summary>Tech level a generated project sits at. Archotech is the top of the ladder and where the
        /// authored Exotic era already is, so the tail never prices itself <i>below</i> the last authored
        /// era — see <see cref="ResearchProjectDef.CostFactor"/>.</summary>
        public TechLevel techLevel = TechLevel.Archotech;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (string.IsNullOrEmpty(trackTag)) yield return "trackTag is empty — an endless track continues a track of the authored tree.";
            if (substrates.Count == 0) yield return "substrates is empty — a generated name needs something to be about.";
            if (foundationForms.Count == 0) yield return "foundationForms is empty — every age needs a foundational project in this track.";
            if (appliedForms.Count == 0) yield return "appliedForms is empty — an age with no applied work is a line, not an age.";
            if (baseCost <= 0f) yield return "baseCost must be positive.";
            if (costExponent <= 0f) yield return "costExponent must be positive — a tail that never gets harder is a tail a civilization outruns.";

            foreach (string form in foundationForms)
            {
                if (appliedForms.Contains(form))
                {
                    yield return "'" + form + "' is both a foundationForm and an appliedForm; the two lists must be disjoint so a "
                        + "generated label's form says which of the two it is, and so a foundation can never be named the same as a leaf.";
                }
            }
        }
    }
}
