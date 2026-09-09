using System;
using System.Collections.Generic;
using System.Globalization;

using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Research
{
    /// <summary>
    /// One endless research track: what a civilization that has finished the authored tree keeps working on
    /// along a given line of enquiry (<c>docs/status.json</c>, <c>research.endless</c>).
    ///
    /// <para/>Content, not code, and deliberately thin. The authored tree — 232 projects across eight eras —
    /// is where the game's actual history of ideas lives, and nothing procedural should try to compete with
    /// it. What this describes is the tail past its end: a civilization that already knows everything anyone
    /// wrote down, still spending its researchers on something. So a track is a tag on the authored tree, a
    /// bag of title fragments to name the next refinement with, and a cost that grows.
    /// </summary>
    public class EndlessResearchDef : Def
    {
        /// <summary>The tag on the authored tree this track continues — the tracks are already there
        /// (<c>ResearchProjectDef.tags</c>), so an endless track is a continuation of one rather than a new
        /// taxonomy laid over the tree.</summary>
        public string trackTag = "";

        /// <summary>Title fragments a generated project's label is built from. More than one so the tail does
        /// not read as the same word with a number after it forever; picked deterministically per tier, never
        /// rolled, so a save never has to store which one it got.</summary>
        public List<string> titles = new List<string>();

        /// <summary>Cost of this track's first generated project.</summary>
        public float baseCost = 4000f;

        /// <summary>Multiplier applied per tier. Above 1 so the tail gets harder rather than flat — the point
        /// of endless tech is that a civilization can always spend research, not that it can always finish
        /// something. Unsourced (RimWorld has no equivalent); pinned by a test that asserts the trend.</summary>
        public float costGrowth = 1.35f;

        public TechLevel techLevel = TechLevel.Ultra;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (string.IsNullOrEmpty(trackTag)) yield return "trackTag is empty — an endless track continues a track of the authored tree.";
            if (titles.Count == 0) yield return "titles is empty — a generated project needs something to be called.";
            if (baseCost <= 0f) yield return "baseCost must be positive.";
            if (costGrowth <= 1f) yield return "costGrowth must exceed 1 — a tail that never gets harder is a tail a civilization outruns.";
        }
    }

    /// <summary>
    /// Mints the research projects past the end of the authored tree.
    ///
    /// <para/><b>Derived, not rolled.</b> Everything about a generated project — its <c>defName</c>, label,
    /// cost and prerequisite — is a pure function of its track and its tier. That is stronger than seeding a
    /// <see cref="RandomStream"/> would be: there is no stream to save, no ordering to preserve, and a save
    /// stores one integer (how many tiers exist) rather than a growing list of invented defs. Two saves that
    /// have reached the same tier have byte-identical tech.
    ///
    /// <para/><b>Registered in the global <see cref="DefDatabase"/>,</b> so every consumer works unchanged: a
    /// generated project is finished, gated, costed and displayed by exactly the code that handles an
    /// authored one, and nothing else in the codebase has to learn that endless tech exists. The cost of
    /// that is a runtime write into a database otherwise built once from content — deliberate, and the reason
    /// <see cref="ResearchManager"/> re-mints on load before reading its own progress dictionary.
    ///
    /// <para/><b>No era.</b> Generated projects carry no <see cref="ResearchProjectDef.era"/>, so they are
    /// invisible to <see cref="EraDef.Projects"/> and cannot hold an era open. The era ladder is a finite
    /// authored artefact that ends at Exotic; this is what happens after it, not a ninth age.
    /// </summary>
    public static class EndlessResearch
    {
        /// <summary>Prefix of every generated <c>defName</c>, so one can always be told from an authored
        /// project by name alone — by a test, a save, or a person reading a log.</summary>
        public const string DefNamePrefix = "Endless_";

        public static bool IsGenerated(ResearchProjectDef def) =>
            def != null && def.defName.StartsWith(DefNamePrefix, StringComparison.Ordinal);

        public static string DefNameFor(EndlessResearchDef track, int tier) =>
            DefNamePrefix + track.trackTag + "_" + tier.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// The project one tier along a track, creating it if it does not exist yet and returning the
        /// existing one if it does — so re-minting after a load is idempotent rather than a duplicate.
        /// </summary>
        public static ResearchProjectDef MintOrGet(EndlessResearchDef track, int tier, DefDatabase database)
        {
            if (track == null) throw new ArgumentNullException(nameof(track));
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (tier < 1) throw new ArgumentOutOfRangeException(nameof(tier), tier, "Tiers start at 1.");

            string defName = DefNameFor(track, tier);
            ResearchProjectDef? existing = database.For<ResearchProjectDef>().GetNamedSilentFail(defName);
            if (existing != null) return existing;

            var project = new ResearchProjectDef
            {
                defName = defName,
                label = LabelFor(track, tier),
                description = "Work past the end of what anyone wrote down: the " + tier.ToString(CultureInfo.InvariantCulture)
                    + (tier == 1 ? "st" : tier == 2 ? "nd" : tier == 3 ? "rd" : "th")
                    + " refinement of " + (track.label ?? track.trackTag) + " this civilization has had to derive for itself.",
                baseCost = CostFor(track, tier),
                techLevel = track.techLevel,
                tags = new List<string> { track.trackTag },
            };

            // Chained one tier at a time, so a track is a line rather than a fan: a civilization works its
            // way along it, and the tier it has reached is exactly how far it got.
            if (tier > 1)
            {
                project.prerequisites = new List<ResearchProjectDef> { MintOrGet(track, tier - 1, database) };
            }

            database.Add(project);
            return project;
        }

        /// <summary>Cost grows geometrically from the track's own base.</summary>
        public static float CostFor(EndlessResearchDef track, int tier) =>
            track.baseCost * (float)Math.Pow(track.costGrowth, tier - 1);

        private static string LabelFor(EndlessResearchDef track, int tier)
        {
            string title = track.titles[(tier - 1) % track.titles.Count];
            int cycle = (tier - 1) / track.titles.Count + 1;
            return cycle == 1 ? title : title + " " + Roman(cycle);
        }

        /// <summary>Small Roman numerals, which is all a suffix ever needs; past the table it falls back to
        /// the number, because "MMMCLXVII" as a suffix helps nobody.</summary>
        private static string Roman(int n)
        {
            string[] table = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
            return n >= 0 && n < table.Length ? table[n] : n.ToString(CultureInfo.InvariantCulture);
        }
    }
}
