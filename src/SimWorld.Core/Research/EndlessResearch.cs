using System;
using System.Collections.Generic;
using System.Globalization;

using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Research
{
    /// <summary>
    /// Mints the research projects past the end of the authored tree.
    ///
    /// <para/><b>Composed, not enumerated.</b> The first version of this generated three tracks of three
    /// authored titles and then appended Roman numerals, so a civilization that finished the tree found
    /// "matter compilation IV" waiting for it — a counter wearing a name. What it generates now is a
    /// four-word taxonomy: an <i>age</i> supplies the register every project of that age shares
    /// (<see cref="EndlessAgeDef"/>), a <i>track</i> supplies the domain's own nouns
    /// (<see cref="EndlessResearchDef"/>), and a project is a substrate of that domain worked into either a
    /// foundational or an applied form. The names are a product rather than a list, and the age register
    /// compounds rather than counts, so the supply of distinct, readable names is not bounded at any depth a
    /// game reaches — see <see cref="EndlessAgeDef"/> for why no two are ever the same.
    ///
    /// <para/><b>Derived from a seed, not rolled.</b> Everything about a generated project — its
    /// <c>defName</c>, label, cost and prerequisites — is a pure function of (game seed, track, age, slot).
    /// Nothing reads <see cref="Rand.Current"/>, so the tail is identical however much else drew from the
    /// shared stream in between, and a save stores two integers — the seed and how many ages exist — rather
    /// than a growing list of invented defs. The seed is what makes two civilizations' tails <i>differ</i>:
    /// before it, every game past the authored tree researched the same things in the same order.
    ///
    /// <para/><b>Registered in the global <see cref="DefDatabase"/>,</b> so every consumer works unchanged: a
    /// generated project is finished, gated, costed and displayed by exactly the code that handles an
    /// authored one, and nothing else in the codebase has to learn that endless tech exists. The cost of that
    /// is a runtime write into a database otherwise built once from content — deliberate, and the reason
    /// <see cref="ResearchManager"/> re-mints on load before reading its own progress dictionary, and the
    /// reason a <c>defName</c> carries the seed it was generated from: the database outlives a game, and two
    /// civilizations in one process must not be handed each other's inventions.
    ///
    /// <para/><b>No era.</b> Generated projects carry no <see cref="ResearchProjectDef.era"/> and never name
    /// an authored project as a prerequisite, so they are invisible to both <see cref="EraDef.Projects"/> and
    /// <see cref="EraDef.SpineProjects"/> and cannot move <see cref="EraDef.IsComplete"/> by a hair. The era
    /// ladder is a finite authored artefact that ends at Exotic (<c>docs/spec/simworld-spec.md</c> §10); what
    /// a civilization gets past it is <i>ages</i>, which are this module's own and which the rest of the
    /// simulation never sees as Defs.
    /// </summary>
    public static class EndlessResearch
    {
        /// <summary>Prefix of every generated <c>defName</c>, so one can always be told from an authored
        /// project by name alone — by a test, a save, or a person reading a log.</summary>
        public const string DefNamePrefix = "Endless_";

        /// <summary>Tag every generated project carries, beside its track's own tag.</summary>
        public const string EndlessTag = "Endless";

        public static bool IsGenerated(ResearchProjectDef? def) =>
            def != null && def.defName.StartsWith(DefNamePrefix, StringComparison.Ordinal);

        /// <summary>
        /// The seed, as it appears inside a generated <c>defName</c>. Fixed width and hexadecimal so the
        /// names sort and read predictably, and so <see cref="BelongsTo"/> is a substring test rather than a
        /// parse.
        /// </summary>
        public static string SeedToken(int seed) => unchecked((uint)seed).ToString("X8", CultureInfo.InvariantCulture);

        /// <summary>
        /// True when <paramref name="def"/> is endless tech this game generated, rather than another
        /// civilization's left behind in the same process.
        /// <para/>
        /// This distinction is load-bearing, not hygiene: <see cref="ResearchManager.NothingLeftToResearch"/>
        /// scans the whole database, and without it another game's unfinished tail reads as something this
        /// civilization could still be working on, so this one would never extend its own.
        /// </summary>
        public static bool BelongsTo(ResearchProjectDef? def, int seed) =>
            IsGenerated(def) && def!.defName.StartsWith(DefNamePrefix + SeedToken(seed) + "_", StringComparison.Ordinal);

        /// <summary>Depth of a slot within its track, 1-based: how many generated projects of this track come
        /// at or before it.</summary>
        public static int DepthOf(EndlessAgeDef ages, int ageIndex, int slot) => (ageIndex * ages.projectsPerTrack) + slot + 1;

        public static string DefNameFor(EndlessAgeDef ages, EndlessResearchDef track, int ageIndex, int slot, int seed) =>
            DefNamePrefix + SeedToken(seed) + "_" + track.trackTag + "_"
            + DepthOf(ages, ageIndex, slot).ToString(CultureInfo.InvariantCulture);

        /// <summary>True for the one project per track per age that the rest of that age's work hangs off, and
        /// that the next age's foundation follows from.</summary>
        public static bool IsFoundationSlot(int slot) => slot == 0;

        /// <summary>
        /// The label a generated project will have, without minting it or anything before it. Pure in
        /// (seed, track, age, slot) — which is the property that makes the tail an <i>index</i> rather than a
        /// sequence: the thousandth project can be named without the first nine hundred and ninety-nine.
        /// </summary>
        public static string LabelFor(EndlessAgeDef ages, EndlessResearchDef track, int ageIndex, int slot, int seed)
        {
            if (ages == null) throw new ArgumentNullException(nameof(ages));
            if (track == null) throw new ArgumentNullException(nameof(track));
            if (slot < 0 || slot >= ages.projectsPerTrack) throw new ArgumentOutOfRangeException(nameof(slot), slot, "Slot is outside the age.");

            string theme = ages.ThemeFor(ageIndex, seed);
            int trackSeed = TrackSeed(track, seed);

            string substrate;
            string form;
            if (IsFoundationSlot(slot))
            {
                // The foundation walks the whole substrate x foundation-form space before revisiting any of
                // it, so successive ages of one track read as a discipline moving on rather than as the same
                // pair with a different adjective in front.
                int capacity = track.substrates.Count * track.foundationForms.Count;
                int index = EndlessSequence.Permute(ageIndex % capacity, capacity, MurmurHash.Combine(trackSeed, ageIndex / capacity));
                substrate = track.substrates[index / track.foundationForms.Count];
                form = track.foundationForms[index % track.foundationForms.Count];
            }
            else
            {
                // Two independent permutations rather than one over the product: an age's applied work then
                // differs from its siblings in *both* words, where a single index over substrate x form walks
                // the pairs like an odometer and reads as one noun with three endings ("precedent appeal",
                // "precedent convening", "precedent auditing"). Injective in the slot either way, which is all
                // distinctness needs — across ages it is the theme that separates them, not this.
                substrate = track.substrates[
                    EndlessSequence.Permute(slot - 1, track.substrates.Count, MurmurHash.Combine(trackSeed, ageIndex, SubstrateSalt))];
                form = track.appliedForms[
                    EndlessSequence.Permute(slot - 1, track.appliedForms.Count, MurmurHash.Combine(trackSeed, ageIndex, FormSalt))];
            }

            return theme + " " + substrate + " " + form;
        }

        /// <summary>
        /// Cost of a generated project. Rises with a <i>continuous</i> depth — the age plus how far through
        /// the age the slot is — so it is strictly increasing over the whole tail rather than only between
        /// ages, and so an age's foundation is always the cheapest thing in it. That is the deliberate shape:
        /// the frontier is the cheap move and consolidating an age you have already opened is the expensive
        /// one, so a civilization that sweeps up every application of every age pays for breadth in depth it
        /// did not gain.
        /// <para/>
        /// Unsourced (RimWorld has no endless tech); the tests pin the trend — always rising, marginal step
        /// always shrinking — rather than the exponent.
        /// </summary>
        public static float CostFor(EndlessAgeDef ages, EndlessResearchDef track, int ageIndex, int slot)
        {
            if (ages == null) throw new ArgumentNullException(nameof(ages));
            if (track == null) throw new ArgumentNullException(nameof(track));
            double depth = 1.0 + ageIndex + (slot / (double)ages.projectsPerTrack);
            return (float)(track.baseCost * Math.Pow(depth, track.costExponent));
        }

        /// <summary>
        /// The project at one slot, creating it if it does not exist yet and returning the existing one if it
        /// does — so re-minting after a load is idempotent rather than a duplicate. Mints whatever earlier
        /// projects it must depend on, so asking for a deep slot directly is safe.
        /// </summary>
        public static ResearchProjectDef MintOrGet(
            EndlessAgeDef ages,
            IReadOnlyList<EndlessResearchDef> tracks,
            int trackIndex,
            int ageIndex,
            int slot,
            int seed,
            DefDatabase database)
        {
            if (ages == null) throw new ArgumentNullException(nameof(ages));
            if (tracks == null) throw new ArgumentNullException(nameof(tracks));
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (trackIndex < 0 || trackIndex >= tracks.Count) throw new ArgumentOutOfRangeException(nameof(trackIndex));
            if (ageIndex < 0) throw new ArgumentOutOfRangeException(nameof(ageIndex), ageIndex, "Ages start at 0.");
            if (slot < 0 || slot >= ages.projectsPerTrack) throw new ArgumentOutOfRangeException(nameof(slot), slot, "Slot is outside the age.");

            EndlessResearchDef track = tracks[trackIndex];
            string defName = DefNameFor(ages, track, ageIndex, slot, seed);
            ResearchProjectDef? existing = database.For<ResearchProjectDef>().GetNamedSilentFail(defName);
            if (existing != null) return existing;

            string label = LabelFor(ages, track, ageIndex, slot, seed);
            var project = new ResearchProjectDef
            {
                defName = defName,
                label = label,
                description = DescriptionFor(ages, track, ageIndex, slot, seed, label),
                baseCost = CostFor(ages, track, ageIndex, slot),
                techLevel = track.techLevel,
                tab = FirstTab(database),
                tags = new List<string> { track.trackTag, EndlessTag, ages.ThemeFor(ageIndex, seed) },
                prerequisites = Prerequisites(ages, tracks, trackIndex, ageIndex, slot, seed, database),
                researchViewX = AuthoredTreeWidth + DepthOf(ages, ageIndex, slot),
                researchViewY = trackIndex,
            };

            database.Add(project);
            return project;
        }

        /// <summary>
        /// Mints a whole age — every track's foundation and every track's applied work — and returns it in a
        /// stable order (track, then slot). An age is the unit because the age's own projects are what make
        /// its foundations structural: mint half an age and the half that exists has a different shape from
        /// the one it will end up with.
        /// </summary>
        public static List<ResearchProjectDef> MintAge(
            EndlessAgeDef ages,
            IReadOnlyList<EndlessResearchDef> tracks,
            int ageIndex,
            int seed,
            DefDatabase database)
        {
            if (ages == null) throw new ArgumentNullException(nameof(ages));
            if (tracks == null) throw new ArgumentNullException(nameof(tracks));

            var minted = new List<ResearchProjectDef>(tracks.Count * ages.projectsPerTrack);
            for (int trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
            {
                for (int slot = 0; slot < ages.projectsPerTrack; slot++)
                {
                    minted.Add(MintOrGet(ages, tracks, trackIndex, ageIndex, slot, seed, database));
                }
            }
            return minted;
        }

        /// <summary>Every foundation of one age — the projects a civilization must finish to have reached the
        /// frontier of that age. Mints them if they are not there.</summary>
        public static List<ResearchProjectDef> FoundationsOf(
            EndlessAgeDef ages,
            IReadOnlyList<EndlessResearchDef> tracks,
            int ageIndex,
            int seed,
            DefDatabase database)
        {
            if (tracks == null) throw new ArgumentNullException(nameof(tracks));
            var foundations = new List<ResearchProjectDef>(tracks.Count);
            for (int trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
            {
                foundations.Add(MintOrGet(ages, tracks, trackIndex, ageIndex, 0, seed, database));
            }
            return foundations;
        }

        /// <summary>The project at one slot if it has already been minted, and null otherwise — for callers
        /// asking a question about the tail rather than extending it.</summary>
        public static ResearchProjectDef? Existing(
            EndlessAgeDef ages, EndlessResearchDef track, int ageIndex, int slot, int seed, DefDatabase database)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            return database.For<ResearchProjectDef>().GetNamedSilentFail(DefNameFor(ages, track, ageIndex, slot, seed));
        }

        /// <summary>
        /// What a generated project follows from.
        ///
        /// <para/>An age's applied work follows that age's foundation in the same track: one prerequisite, so
        /// it is genuinely optional — nothing depends on it, and a civilization can leave it and still reach
        /// the next age. A foundation follows the previous age's foundation in its own track <i>and</i> in one
        /// other, which is what makes the tail a civilization rather than a set of parallel counters: no
        /// single line of enquiry runs away from the rest, and an age opens for everyone at once or for
        /// nobody.
        ///
        /// <para/>Never an authored project, at any depth. Naming one would put it into
        /// <see cref="EraDef.SpineProjects"/> and so change what completing its era requires — a run-time edit
        /// to the authored ladder, made from content that did not exist when the ladder was authored.
        /// </summary>
        private static List<ResearchProjectDef>? Prerequisites(
            EndlessAgeDef ages,
            IReadOnlyList<EndlessResearchDef> tracks,
            int trackIndex,
            int ageIndex,
            int slot,
            int seed,
            DefDatabase database)
        {
            if (!IsFoundationSlot(slot))
            {
                return new List<ResearchProjectDef> { MintOrGet(ages, tracks, trackIndex, ageIndex, 0, seed, database) };
            }
            if (ageIndex == 0)
            {
                // The first generated project of each track is a root: the authored tree it grows out of is
                // finished by the time anything here is minted, and naming a piece of it would reshape an
                // authored era's spine. What makes it reachable rather than free-floating is that it only
                // exists once there is nothing else left to research.
                return null;
            }

            var prerequisites = new List<ResearchProjectDef>
            {
                MintOrGet(ages, tracks, trackIndex, ageIndex - 1, 0, seed, database),
            };
            if (tracks.Count > 1)
            {
                int offset = RandomStream.RangeSeeded(0, tracks.Count - 1, MurmurHash.Combine(seed, ageIndex, trackIndex));
                int otherIndex = (trackIndex + 1 + offset) % tracks.Count;
                prerequisites.Add(MintOrGet(ages, tracks, otherIndex, ageIndex - 1, 0, seed, database));
            }
            return prerequisites;
        }

        /// <summary>The tab the tail shares with the authored tree — whichever the content registered first,
        /// rather than one named in code, so a content set that calls its tab something else still works.
        /// Null only when a content set ships no tab at all.</summary>
        private static ResearchTabDef? FirstTab(DefDatabase database)
        {
            IReadOnlyList<ResearchTabDef> tabs = database.For<ResearchTabDef>().AllDefsListForReading;
            return tabs.Count > 0 ? tabs[0] : null;
        }

        private static string DescriptionFor(
            EndlessAgeDef ages, EndlessResearchDef track, int ageIndex, int slot, int seed, string label)
        {
            string template = IsFoundationSlot(slot) ? ages.foundationDescription : ages.appliedDescription;
            if (string.IsNullOrEmpty(template))
            {
                return "Work past the end of what anyone wrote down: " + label + ".";
            }
            return template
                .Replace("{age}", ages.LabelFor(ageIndex, seed))
                .Replace("{theme}", ages.ThemeFor(ageIndex, seed))
                .Replace("{track}", track.label ?? track.trackTag)
                .Replace("{subject}", label);
        }

        /// <summary>Salts a track's own identity into the game seed, so two tracks never draw the same
        /// permutation and a track keeps its own line of enquiry across a whole game.</summary>
        private static int TrackSeed(EndlessResearchDef track, int seed) =>
            MurmurHash.Combine(seed, SimWorld.World.GenText.StableStringHash(track.defName));

        /// <summary>Where the generated tree starts on the research view's x axis — past the authored tree,
        /// whose own coordinates run to about 80. Cosmetic; nothing reads it yet.</summary>
        private const int AuthoredTreeWidth = 100;

        /// <summary>Salts so the two words of an applied project's name are drawn from independent
        /// permutations rather than from one walk over their product.</summary>
        private const int SubstrateSalt = 0x5B57;

        private const int FormSalt = 0x0F0E;
    }
}
