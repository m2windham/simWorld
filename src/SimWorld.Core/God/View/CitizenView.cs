using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Health;
using SimWorld.MindState;
using SimWorld.Needs;
using SimWorld.Offices;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Social;
using SimWorld.Work;

namespace SimWorld.God.View
{
    /// <summary>
    /// How much of a citizen's record the tier they sit at actually maintains — the one thing every readout
    /// below has to say alongside its number, because a number with no fidelity beside it is read as "measured"
    /// whatever it really is.
    ///
    /// <para/><b>This exists because of the failure mode this project keeps finding.</b> A zero that means "we
    /// did not look" is indistinguishable, on screen, from a zero that means "there is nothing there" — and the
    /// second is the reading a player takes. An empty hediff list on a <see cref="PawnTier.Statistical"/>
    /// citizen is not a healthy person; it is a person nobody is tracking the health of. Every section of
    /// <see cref="CitizenView"/> carries one of these so that distinction survives the seam.
    /// </summary>
    public enum CitizenFidelity
    {
        /// <summary>Computed every tick, as ported. Current as of <see cref="CitizenView.TicksGame"/>.</summary>
        Tracked,

        /// <summary>
        /// Real, this person's own, but not advanced every tick. Stale by up to one coarse interval where the
        /// tier advances it in bulk (<see cref="Pawn_TierTracker.CoarseTick"/> does this for needs at
        /// <see cref="PawnTier.Interval"/>), and by however long they have sat at this tier where the tier does
        /// not run the activity that would change it at all (skills, which §11.3 says Interval does not tick).
        /// A real value that was really earned — just not a fresh one.
        /// </summary>
        Coarse,

        /// <summary>
        /// Drawn from the cohort's distribution, not from this person's own history
        /// (<c>Pawn_TierTracker.ApplyCohortSample</c>). It is a plausible current value for somebody in their
        /// cohort; it is not a measurement of them. Deterministic for a given (citizen, tick), so it does not
        /// flicker between two reads — which makes it easier, not harder, to mistake for a measurement.
        /// </summary>
        Sampled,

        /// <summary>
        /// Not maintained at this tier. <b>An empty list beside this value means "we did not look", never
        /// "there is nothing".</b> The host must say so rather than draw a zero, a blank or a clean bill of
        /// health.
        /// </summary>
        NotTracked,
    }

    /// <summary>
    /// One citizen as a roster row: enough to pick a person out of a settlement, and deliberately not enough
    /// to reconstruct a pawn. <see cref="ThingId"/> and <see cref="Name"/> are the two handles the drill-down
    /// takes — see <see cref="CitizenView"/> for why a host wants to keep both.
    /// </summary>
    public sealed class CitizenLine
    {
        internal CitizenLine(
            int thingId, string name, string fullName, Pawns.Gender gender, int ageBiologicalYears,
            PawnTier tier, string? officeDefName, string? officeLabel, string? roleDefName,
            float? moodLevel, MentalBreakIntensity? moodBand, CitizenFidelity moodFidelity,
            float healthFraction, CitizenFidelity healthFidelity,
            bool downed, bool dead)
        {
            ThingId = thingId;
            Name = name;
            FullName = fullName;
            Gender = gender;
            AgeBiologicalYears = ageBiologicalYears;
            Tier = tier;
            OfficeDefName = officeDefName;
            OfficeLabel = officeLabel;
            RoleDefName = roleDefName;
            MoodLevel = moodLevel;
            MoodBand = moodBand;
            MoodFidelity = moodFidelity;
            HealthFraction = healthFraction;
            HealthFidelity = healthFidelity;
            Downed = downed;
            Dead = dead;
        }

        /// <summary>The handle to pass to <see cref="GodViewSnapshot.Citizen"/>. An <c>int</c>, not a
        /// <c>Pawn</c> — see <see cref="GodViewSnapshot"/> on why the seam is values.</summary>
        public int ThingId { get; }

        /// <summary>What they are called (<c>Pawn.Label</c>): the short form a roster row shows, and the
        /// handle <see cref="GodViewSnapshot.Remembered"/> takes once they are gone.</summary>
        public string Name { get; }

        /// <summary>"First 'Nick' Last", or the same as <see cref="Name"/> for anyone with one name.</summary>
        public string FullName { get; }

        public Pawns.Gender Gender { get; }

        /// <summary>Biological age in whole years. Real at every tier: <c>Pawn_TierTracker.ApplyElapsed</c>
        /// gives age the whole elapsed span at every tier, on the stated grounds that nobody owed this citizen
        /// an opportunity to grow older.</summary>
        public int AgeBiologicalYears { get; }

        /// <summary>Which tier is simulating them (§11.3). Carried as the enum rather than a string for the
        /// same reason <see cref="DeathCauseLine.Cause"/> is: it names no worker and resolves to nothing, and
        /// the host would otherwise reinvent it to interpret <see cref="MoodFidelity"/> and the rest.</summary>
        public PawnTier Tier { get; }

        /// <summary>The office they hold, or null. <see cref="Offices.OfficeManager.OfficeOf"/> — the office
        /// <i>is</i> the role they carry, so this and <see cref="RoleDefName"/> are two readings of one fact
        /// rather than two facts.</summary>
        public string? OfficeDefName { get; }

        public string? OfficeLabel { get; }

        /// <summary>Their standing work role (<c>Pawn_WorkSettings.Role</c>), or null for no standing
        /// role.</summary>
        public string? RoleDefName { get; }

        /// <summary>Mood, 0-1, or null when this citizen has no mood need at all (an animal, or anyone
        /// <c>Pawn_NeedsTracker.ShouldHaveNeed</c> refused one) — never zero for "we did not look". Read
        /// <see cref="MoodFidelity"/> before believing the number.</summary>
        public float? MoodLevel { get; }

        /// <summary>
        /// The band <see cref="MentalBreaker.CurMoodBreakIntensity"/> puts that mood in — the simulation's own
        /// banding, against the citizen's own <c>MentalBreakThreshold</c> stat, not a display scale invented
        /// here. <see cref="MentalBreakIntensity.None"/> means "above the minor break threshold", which is
        /// "not at risk" rather than "happy": read <see cref="MoodLevel"/> for how far above.
        /// </summary>
        public MentalBreakIntensity? MoodBand { get; }

        public CitizenFidelity MoodFidelity { get; }

        /// <summary>
        /// Fraction of health, 0-1 — <c>SummaryHealthHandler.SummaryHealthPercent</c> for a citizen whose
        /// hediffs are real, <see cref="Pawn_TierTracker.SampledHealthFraction"/> for a Statistical one. The
        /// same split <see cref="GodRollup.MeanHealth"/> already makes, so the roster and the civilization
        /// rollup cannot disagree about one person by reading two different numbers.
        /// </summary>
        public float HealthFraction { get; }

        public CitizenFidelity HealthFidelity { get; }

        /// <summary>The body cannot act. Real at every tier (a flag off <c>Pawn_HealthTracker</c>), though at
        /// <see cref="PawnTier.Statistical"/> nothing is advancing the hediffs that would change it.</summary>
        public bool Downed { get; }

        /// <summary>True for a citizen who has died and has not yet been pruned off the roster
        /// (<c>Settlement.SyncCitizenSpawns</c> runs on a rare cadence, not at the moment of death). Once
        /// pruned they leave the roster entirely — see <see cref="GodViewSnapshot.Remembered"/>.</summary>
        public bool Dead { get; }

        /// <summary>
        /// One roster row for a live citizen. Every read here is a field read or a cached one
        /// (<c>SummaryHealthPercent</c> caches; <c>OfficeOf</c> is a handful of reference comparisons), and
        /// nothing here writes: <b>looking at a citizen must not change what tier they are simulated at.</b>
        /// Nothing below touches <c>Pawn_TierTracker</c>'s notify surface, and
        /// <c>CitizenViewTests.Listing_a_settlements_people_changes_nobodys_tier</c> pins that.
        /// </summary>
        internal static CitizenLine Of(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));

            PawnTier tier = pawn.tier?.Tier ?? PawnTier.Full;
            OfficeDef? office = OfficeManager.OfficeOf(pawn);
            Need_Mood? mood = pawn.needs?.mood;

            return new CitizenLine(
                pawn.thingIDNumber,
                pawn.Label,
                pawn.Name?.ToStringFull ?? pawn.Label,
                pawn.gender,
                pawn.ageTracker?.AgeBiologicalYears ?? 0,
                tier,
                office?.defName,
                office?.LabelCap,
                pawn.workSettings?.Role?.defName,
                mood?.CurLevelPercentage,
                mood == null ? (MentalBreakIntensity?)null : pawn.mindState?.mentalBreaker?.CurMoodBreakIntensity,
                mood == null ? CitizenFidelity.NotTracked : FidelityOfLiveState(tier),
                HealthFractionOf(pawn, tier),
                FidelityOfLiveState(tier),
                pawn.Downed,
                pawn.Dead);
        }

        /// <summary>Full ticks it; Interval advances it in bulk on the long bucket; Statistical redraws it from
        /// the cohort every coarse tick. One mapping, used by every section whose value the tier keeps real.</summary>
        internal static CitizenFidelity FidelityOfLiveState(PawnTier tier) => tier switch
        {
            PawnTier.Full => CitizenFidelity.Tracked,
            PawnTier.Interval => CitizenFidelity.Coarse,
            _ => CitizenFidelity.Sampled,
        };

        /// <summary>The same tier split <see cref="GodRollup"/> makes: a Statistical citizen's real hediff set
        /// is never opened to answer a health question, because opening it is precisely the cost §11.3
        /// exists to avoid.</summary>
        internal static float HealthFractionOf(Pawn pawn, PawnTier tier) =>
            tier == PawnTier.Statistical
                ? pawn.tier?.SampledHealthFraction ?? 0f
                : pawn.health?.summaryHealth?.SummaryHealthPercent ?? 0f;
    }

    /// <summary>
    /// A settlement's people as a list — step one of the drill-down <c>docs/design/player-first.md</c> §7 makes
    /// a hard requirement ("there must be an unbroken path from the civilization aggregate down to one named
    /// person's actual state and history").
    ///
    /// <para/><b>This is the narrow query, not a wider rollup.</b> <see cref="GodViewSnapshot"/>'s own doc
    /// refuses per-citizen detail because "opening every person to paint a civilization is exactly what §11.3's
    /// tiering exists to prevent", and that refusal stands: nothing here is folded into the civilization
    /// snapshot. It is asked for one settlement at a time, by a host that has already chosen one.
    ///
    /// <para/><b>Not everybody can be listed, and that is stated rather than hidden.</b>
    /// <see cref="Citizens"/> holds a row for every citizen with a live <c>Pawn</c> object — Full, Interval,
    /// and the Statistical citizens whose pawn survived a demotion. <see cref="UnlistedCohortPopulation"/> is
    /// the rest: seats in <c>Settlement.StatisticalPopulation</c> with no <c>Pawn</c> behind them, who by that
    /// tier's own design have no individual record to list. A settlement of forty thousand does not become
    /// forty thousand rows here; it becomes the rows it really has and an honest count of the people it does
    /// not.
    /// </summary>
    public sealed class SettlementRoster
    {
        internal SettlementRoster(
            int ticksGame, string settlementName, int tile,
            IReadOnlyList<CitizenLine> citizens, int unlistedCohortPopulation, int totalPopulation)
        {
            TicksGame = ticksGame;
            SettlementName = settlementName;
            Tile = tile;
            Citizens = citizens;
            UnlistedCohortPopulation = unlistedCohortPopulation;
            TotalPopulation = totalPopulation;
        }

        /// <summary>The tick this roster was read at. Carried because this is a query taken when it is called,
        /// not a slice of whatever <see cref="GodViewSnapshot"/> the host is holding — see
        /// <see cref="GodViewSnapshot.CitizensOf"/>.</summary>
        public int TicksGame { get; }

        public string SettlementName { get; }

        /// <summary>The world tile — the same handle <see cref="SettlementSummary.Tile"/> carries, and what
        /// this roster was asked for by.</summary>
        public int Tile { get; }

        /// <summary>Everyone with an individual record, in roster order.</summary>
        public IReadOnlyList<CitizenLine> Citizens { get; }

        /// <summary>People this settlement has who are <b>not</b> in <see cref="Citizens"/>: bare seats in the
        /// Statistical cohort with no <c>Pawn</c> object, and therefore no name, no age and no history to show.
        /// Not zero-padding — a real count of people the drill-down genuinely cannot reach. Read it as "and
        /// N more nobody is tracking individually", never as a rounding error.</summary>
        public int UnlistedCohortPopulation { get; }

        /// <summary>Everyone, listed or not: <c>Settlement.TotalPopulation</c>. Equals
        /// <see cref="Citizens"/>.Count + <see cref="UnlistedCohortPopulation"/>.</summary>
        public int TotalPopulation { get; }
    }

    /// <summary>One need and where it stands. <see cref="Fidelity"/> is not decoration: at
    /// <see cref="PawnTier.Statistical"/> this level was drawn from the cohort, not from them.</summary>
    public sealed class NeedLine
    {
        internal NeedLine(string defName, string label, float level, CitizenFidelity fidelity)
        {
            DefName = defName;
            Label = label;
            Level = level;
            Fidelity = fidelity;
        }

        public string DefName { get; }

        public string Label { get; }

        /// <summary>0-1, as a fraction of the need's maximum (<c>Need.CurLevelPercentage</c>).</summary>
        public float Level { get; }

        public CitizenFidelity Fidelity { get; }
    }

    /// <summary>One thing wrong with (or added to) a body, by label and severity.</summary>
    public sealed class HediffLine
    {
        internal HediffLine(
            string defName, string label, float severity, string? partLabel,
            bool isPermanent, bool isTended, float bleedRate)
        {
            DefName = defName;
            Label = label;
            Severity = severity;
            PartLabel = partLabel;
            IsPermanent = isPermanent;
            IsTended = isTended;
            BleedRate = bleedRate;
        }

        public string DefName { get; }

        /// <summary>Already in words (<c>Hediff.Label</c>), including the stage where the hediff has one, so a
        /// host does not reimplement stage selection to print "extreme" versus "minor".</summary>
        public string Label { get; }

        public float Severity { get; }

        /// <summary>Which body part, or null for a whole-body hediff.</summary>
        public string? PartLabel { get; }

        public bool IsPermanent { get; }

        public bool IsTended { get; }

        public float BleedRate { get; }
    }

    /// <summary>One skill: what they can do, and whether they care about it.</summary>
    public sealed class SkillLine
    {
        internal SkillLine(string defName, string label, int level, Work.Passion passion, bool totallyDisabled)
        {
            DefName = defName;
            Label = label;
            Level = level;
            Passion = passion;
            TotallyDisabled = totallyDisabled;
        }

        public string DefName { get; }

        public string Label { get; }

        /// <summary>0-20 (<c>SkillRecord.MinLevel</c>/<c>MaxLevel</c>).</summary>
        public int Level { get; }

        public Work.Passion Passion { get; }

        /// <summary>Barred outright by a backstory or a trait — a level of zero here means "cannot", not
        /// "has not learned yet".</summary>
        public bool TotallyDisabled { get; }
    }

    /// <summary>One trait, at the degree this person has it.</summary>
    public sealed class TraitLine
    {
        internal TraitLine(string defName, string label, int degree)
        {
            DefName = defName;
            Label = label;
            Degree = degree;
        }

        public string DefName { get; }

        /// <summary>The degree's own label ("kind", "abrasive"), not the def's — a trait reads differently at
        /// each degree and the degree is what the person actually is.</summary>
        public string Label { get; }

        public int Degree { get; }
    }

    /// <summary>Somebody this citizen is connected to, by name and by what the connection is.</summary>
    public sealed class RelationshipLine
    {
        internal RelationshipLine(string relationDefName, string relationLabel, int otherThingId, string? otherName)
        {
            RelationDefName = relationDefName;
            RelationLabel = relationLabel;
            OtherThingId = otherThingId;
            OtherName = otherName;
        }

        public string RelationDefName { get; }

        /// <summary>"spouse", "child", "rival" — the relation def's own label, so the wording on screen is the
        /// simulation's wording.</summary>
        public string RelationLabel { get; }

        /// <summary>The other person's handle, for drilling sideways into them.</summary>
        public int OtherThingId { get; }

        /// <summary>The other person's name, or null when no live record for them could be found — they may
        /// have died and been pruned, or never have been more than a cohort seat. A null here is "we could not
        /// name them", not "they have no name".</summary>
        public string? OtherName { get; }
    }

    /// <summary>One entry from this person's own life history (<c>Pawn_StoryTracker.lifeEvents</c>) — their
    /// record, as distinct from the civilization's chronicle.</summary>
    public sealed class LifeEventLine
    {
        internal LifeEventLine(int tick, string kind, string text)
        {
            Tick = tick;
            Kind = kind;
            Text = text;
        }

        public int Tick { get; }

        /// <summary>A short category the sim wrote ("Birth", ...).</summary>
        public string Kind { get; }

        public string Text { get; }
    }

    /// <summary>
    /// One named person, in depth — step two of §7's drill-down, and the step that makes the requirement
    /// worth having. Frostpunk 2 kept the aggregates and lost this one, and was reviewed on exactly what it
    /// lost: <i>"when a handful of them die from exposure to the cold, it doesn't sting the way it did in the
    /// first game."</i> A number that dropped is a statistic; this is the person it happened to.
    ///
    /// <para/><b>This is the expensive query, and it is asked for one person.</b> Building it walks that
    /// citizen's needs, hediffs, skills and traits, runs every <see cref="PawnRelationDef"/>'s own worker
    /// against the live roster to find who they are connected to, and scans the chronicle for lines that name
    /// them. All of that is bounded by the live Full/Interval population and the chronicle's own cap, never by
    /// the civilization's head count — but it is not a thing to call in a loop over a settlement. That is what
    /// <see cref="SettlementRoster"/> is for.
    ///
    /// <para/><b>Looking at somebody does not promote them.</b> Nothing here writes, and in particular nothing
    /// touches <c>Pawn_TierTracker</c>'s notify surface, so a Statistical citizen stays Statistical while being
    /// read. That is deliberate: a view that promoted whoever it drew would make attention a function of
    /// curiosity, and §11.3's whole budget would leak through the drill-down.
    ///
    /// <para/><b>What a Statistical-tier citizen's view can and cannot report.</b> Under-claiming is the rule
    /// here; over-claiming is the failure this project keeps finding.
    /// <list type="bullet">
    /// <item><description><b>Real, at every tier:</b> name, gender, biological age, traits, backstory, skills
    /// as last earned, relationships, their own <see cref="LifeEvents"/>, and everything in
    /// <see cref="History"/>. Identity, family and demography participation are real at Statistical by
    /// <see cref="PawnTier"/>'s own definition; the chronicle is the civilization's record and does not depend
    /// on the tier at all.</description></item>
    /// <item><description><b>Sampled, not measured:</b> <see cref="Needs"/> and <see cref="HealthFraction"/>.
    /// At Statistical these come from the cohort's distribution — a plausible value for somebody like them,
    /// deterministic for a given tick, and <i>not</i> a reading of them.
    /// <see cref="NeedsFidelity"/>/<see cref="HealthFidelity"/> say so.</description></item>
    /// <item><description><b>Not reported at all:</b> <see cref="Hediffs"/>.
    /// <see cref="HediffFidelity"/> is <see cref="CitizenFidelity.NotTracked"/> at Statistical and the list is
    /// empty — because the tier does not advance a hediff set, so whatever it still holds is frozen at
    /// whatever it was when they left Interval, and reporting stale wounds as current would be a lie the host
    /// could not detect. An empty list here is "we did not look", and the host must say that rather than draw
    /// a clean bill of health.</description></item>
    /// <item><description><b>One honest gap at Interval, inherited rather than introduced:</b> bleeding and
    /// natural wound-healing do not run in bulk at that tier (see <c>Pawn_TierTracker.ApplyElapsed</c>), so an
    /// injury an Interval citizen was carrying reads exactly as severe as it was when they dropped. That is
    /// why Interval is <see cref="CitizenFidelity.Coarse"/> and not <see cref="CitizenFidelity.Tracked"/>.
    /// Disease is not part of that gap — <c>AbstractDiseaseResolver</c> resolves it coarsely.</description></item>
    /// </list>
    /// </summary>
    public sealed class CitizenView
    {
        internal CitizenView(
            int ticksGame, bool isLive, int? thingId, string name, string fullName,
            Pawns.Gender? gender, int? ageBiologicalYears, int? ageChronologicalYears,
            PawnTier? tier, string? officeDefName, string? officeLabel, string? roleDefName,
            string? childhoodTitle, string? adulthoodTitle,
            float? moodLevel, MentalBreakIntensity? moodBand, CitizenFidelity moodFidelity,
            IReadOnlyList<NeedLine> needs, CitizenFidelity needsFidelity,
            float? healthFraction, CitizenFidelity healthFidelity,
            IReadOnlyList<HediffLine> hediffs, CitizenFidelity hediffFidelity,
            IReadOnlyList<SkillLine> skills, CitizenFidelity skillsFidelity,
            IReadOnlyList<TraitLine> traits,
            IReadOnlyList<RelationshipLine> relationships,
            IReadOnlyList<LifeEventLine> lifeEvents,
            IReadOnlyList<ChronicleLine> history,
            bool downed, bool dead)
        {
            TicksGame = ticksGame;
            IsLive = isLive;
            ThingId = thingId;
            Name = name;
            FullName = fullName;
            Gender = gender;
            AgeBiologicalYears = ageBiologicalYears;
            AgeChronologicalYears = ageChronologicalYears;
            Tier = tier;
            OfficeDefName = officeDefName;
            OfficeLabel = officeLabel;
            RoleDefName = roleDefName;
            ChildhoodTitle = childhoodTitle;
            AdulthoodTitle = adulthoodTitle;
            MoodLevel = moodLevel;
            MoodBand = moodBand;
            MoodFidelity = moodFidelity;
            Needs = needs;
            NeedsFidelity = needsFidelity;
            HealthFraction = healthFraction;
            HealthFidelity = healthFidelity;
            Hediffs = hediffs;
            HediffFidelity = hediffFidelity;
            Skills = skills;
            SkillsFidelity = skillsFidelity;
            Traits = traits;
            Relationships = relationships;
            LifeEvents = lifeEvents;
            History = history;
            Downed = downed;
            Dead = dead;
        }

        /// <summary>The tick this was read at.</summary>
        public int TicksGame { get; }

        /// <summary>
        /// True when a live record was found for this person; false for a view assembled from the chronicle
        /// alone (<see cref="GodViewSnapshot.Remembered"/>) — somebody the civilization still remembers but no
        /// longer holds a record of. On a remembered view every fidelity is
        /// <see cref="CitizenFidelity.NotTracked"/>, every list but <see cref="History"/> is empty, and the
        /// scalars are null. Nothing here is guessed to fill the shape.
        /// </summary>
        public bool IsLive { get; }

        /// <summary>Their handle, or null on a remembered view — the chronicle records names, not ids.</summary>
        public int? ThingId { get; }

        public string Name { get; }

        public string FullName { get; }

        public Pawns.Gender? Gender { get; }

        public int? AgeBiologicalYears { get; }

        /// <summary>Years since birth, which differs from <see cref="AgeBiologicalYears"/> for anyone whose
        /// body has been slowed or stopped.</summary>
        public int? AgeChronologicalYears { get; }

        /// <summary>Which tier is simulating them, or null on a remembered view. Read it before reading
        /// anything below — it is what every fidelity on this view is derived from.</summary>
        public PawnTier? Tier { get; }

        public string? OfficeDefName { get; }

        public string? OfficeLabel { get; }

        public string? RoleDefName { get; }

        /// <summary>Their childhood backstory's title, or null.</summary>
        public string? ChildhoodTitle { get; }

        /// <summary>Their adulthood backstory's title, or null — most people generated young have none.</summary>
        public string? AdulthoodTitle { get; }

        /// <summary>See <see cref="CitizenLine.MoodLevel"/>.</summary>
        public float? MoodLevel { get; }

        /// <summary>See <see cref="CitizenLine.MoodBand"/>.</summary>
        public MentalBreakIntensity? MoodBand { get; }

        public CitizenFidelity MoodFidelity { get; }

        /// <summary>Every need they have, in the tracker's own order. Empty with
        /// <see cref="NeedsFidelity"/> = <see cref="CitizenFidelity.NotTracked"/> means not looked at.</summary>
        public IReadOnlyList<NeedLine> Needs { get; }

        public CitizenFidelity NeedsFidelity { get; }

        /// <summary>Fraction of health, 0-1. See <see cref="CitizenLine.HealthFraction"/>.</summary>
        public float? HealthFraction { get; }

        public CitizenFidelity HealthFidelity { get; }

        /// <summary>
        /// Every hediff on them, by label and severity — <b>or an empty list meaning nobody is tracking their
        /// health at this tier</b>. <see cref="HediffFidelity"/> is the only thing that tells those two apart,
        /// and the class doc says which tier gives which.
        /// </summary>
        public IReadOnlyList<HediffLine> Hediffs { get; }

        public CitizenFidelity HediffFidelity { get; }

        /// <summary>Every skill, with passion. Real at every tier; see <see cref="SkillsFidelity"/> for
        /// whether it is still advancing.</summary>
        public IReadOnlyList<SkillLine> Skills { get; }

        public CitizenFidelity SkillsFidelity { get; }

        /// <summary>Their traits. Real at every tier — a trait is identity, and no tier drops it.</summary>
        public IReadOnlyList<TraitLine> Traits { get; }

        /// <summary>Who they are connected to and how, by name. Found by running each
        /// <see cref="PawnRelationDef"/>'s own worker against the live roster rather than by reimplementing
        /// the family rules here, so a relation this view reports is a relation the simulation agrees
        /// holds.</summary>
        public IReadOnlyList<RelationshipLine> Relationships { get; }

        /// <summary>Their own life history, oldest first.</summary>
        public IReadOnlyList<LifeEventLine> LifeEvents { get; }

        /// <summary>
        /// The civilization's own record of this person: every chronicle line and every curated moment that
        /// names them, oldest first. <b>This is the half that makes the drill-down worth building</b> — the
        /// state above says how they are, this says what happened to them.
        ///
        /// <para/><b>Matched by name, because a name is all the chronicle records.</b>
        /// <c>Storyteller.RecordDeath</c> writes <c>pawn.Label</c> into the entry and nothing anywhere writes
        /// a thing id, so this scans the rendered text of each entry for this person's short name, full name
        /// or nickname. Two consequences, stated rather than hidden: two citizens who share a name share a
        /// history here, and an entry that happens to contain somebody's name for an unrelated reason is
        /// picked up. Giving the chronicle a citizen id would fix both, and would be a change to
        /// <c>Director/Storyteller.cs</c> rather than to this seam.
        /// </summary>
        public IReadOnlyList<ChronicleLine> History { get; }

        public bool Downed { get; }

        public bool Dead { get; }

        // ---- construction ----

        /// <summary>The full view of a live citizen. See the class doc for what each tier can and cannot
        /// answer; every read below is a read, and none of it changes their tier.</summary>
        internal static CitizenView Of(Pawn pawn, IReadOnlyList<Pawn> livePeople)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (livePeople == null) throw new ArgumentNullException(nameof(livePeople));

            PawnTier tier = pawn.tier?.Tier ?? PawnTier.Full;
            CitizenFidelity live = CitizenLine.FidelityOfLiveState(tier);
            OfficeDef? office = OfficeManager.OfficeOf(pawn);
            Need_Mood? mood = pawn.needs?.mood;

            // The one section a coarse tier cannot answer at all. Statistical does not advance a hediff set,
            // so what it still holds is frozen at whatever it was when the citizen left Interval — reporting
            // it would be reporting a wound that may have healed or killed them a decade ago. Empty plus
            // NotTracked is the honest answer; SampledHealthFraction is what stands in its place.
            bool hediffsReportable = tier != PawnTier.Statistical;

            return new CitizenView(
                Find.TickManager.TicksGame,
                true,
                pawn.thingIDNumber,
                pawn.Label,
                pawn.Name?.ToStringFull ?? pawn.Label,
                pawn.gender,
                pawn.ageTracker?.AgeBiologicalYears,
                pawn.ageTracker?.AgeChronologicalYears,
                tier,
                office?.defName,
                office?.LabelCap,
                pawn.workSettings?.Role?.defName,
                pawn.story?.childhood?.title,
                pawn.story?.adulthood?.title,
                mood?.CurLevelPercentage,
                mood == null ? (MentalBreakIntensity?)null : pawn.mindState?.mentalBreaker?.CurMoodBreakIntensity,
                mood == null ? CitizenFidelity.NotTracked : live,
                NeedsOf(pawn, live),
                pawn.needs == null ? CitizenFidelity.NotTracked : live,
                CitizenLine.HealthFractionOf(pawn, tier),
                live,
                hediffsReportable ? HediffsOf(pawn) : (IReadOnlyList<HediffLine>)Array.Empty<HediffLine>(),
                hediffsReportable ? live : CitizenFidelity.NotTracked,
                SkillsOf(pawn),
                pawn.skills == null ? CitizenFidelity.NotTracked : live,
                TraitsOf(pawn),
                RelationshipsOf(pawn, livePeople),
                LifeEventsOf(pawn),
                HistoryNaming(NamesOf(pawn)),
                pawn.Downed,
                pawn.Dead);
        }

        /// <summary>
        /// Somebody the civilization remembers and no longer holds a record of. Everything but
        /// <see cref="History"/> is empty or null on purpose — this view states that it knows a name and a
        /// history and nothing else, rather than filling the shape with zeroes a host would draw as facts.
        /// </summary>
        internal static CitizenView Remembering(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));

            return new CitizenView(
                Find.TickManager.TicksGame,
                false,
                null,
                name,
                name,
                null, null, null, null, null, null, null, null, null,
                null, null, CitizenFidelity.NotTracked,
                Array.Empty<NeedLine>(), CitizenFidelity.NotTracked,
                null, CitizenFidelity.NotTracked,
                Array.Empty<HediffLine>(), CitizenFidelity.NotTracked,
                Array.Empty<SkillLine>(), CitizenFidelity.NotTracked,
                Array.Empty<TraitLine>(),
                Array.Empty<RelationshipLine>(),
                Array.Empty<LifeEventLine>(),
                HistoryNaming(new List<string> { name }),
                false,
                false);
        }

        private static List<NeedLine> NeedsOf(Pawn pawn, CitizenFidelity fidelity)
        {
            var lines = new List<NeedLine>();
            IReadOnlyList<Need>? needs = pawn.needs?.AllNeeds;
            if (needs == null) return lines;

            for (int i = 0; i < needs.Count; i++)
            {
                Need need = needs[i];
                lines.Add(new NeedLine(need.def.defName, need.LabelCap, need.CurLevelPercentage, fidelity));
            }
            return lines;
        }

        private static List<HediffLine> HediffsOf(Pawn pawn)
        {
            var lines = new List<HediffLine>();
            List<Hediff>? hediffs = pawn.health?.hediffSet?.hediffs;
            if (hediffs == null) return lines;

            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff h = hediffs[i];
                // Invisible hediffs are ones the simulation itself does not show; carrying them here would be
                // telling the host something the game does not consider known about this body.
                if (!h.Visible) continue;
                lines.Add(new HediffLine(
                    h.def.defName, h.Label, h.Severity, h.Part?.LabelCap,
                    h.IsPermanent, h.IsTended, h.BleedRate));
            }
            return lines;
        }

        private static List<SkillLine> SkillsOf(Pawn pawn)
        {
            var lines = new List<SkillLine>();
            List<SkillRecord>? skills = pawn.skills?.skills;
            if (skills == null) return lines;

            for (int i = 0; i < skills.Count; i++)
            {
                SkillRecord s = skills[i];
                lines.Add(new SkillLine(s.def.defName, s.def.LabelCap, s.Level, s.passion, s.TotallyDisabled));
            }
            return lines;
        }

        private static List<TraitLine> TraitsOf(Pawn pawn)
        {
            var lines = new List<TraitLine>();
            List<Trait>? traits = pawn.story?.traits?.allTraits;
            if (traits == null) return lines;

            for (int i = 0; i < traits.Count; i++)
            {
                Trait t = traits[i];
                lines.Add(new TraitLine(t.def.defName, t.Label, t.degree));
            }
            return lines;
        }

        private static List<LifeEventLine> LifeEventsOf(Pawn pawn)
        {
            var lines = new List<LifeEventLine>();
            List<LifeEvent>? events = pawn.story?.lifeEvents;
            if (events == null) return lines;

            for (int i = 0; i < events.Count; i++)
            {
                LifeEvent e = events[i];
                lines.Add(new LifeEventLine(e.tick, e.kind, e.text));
            }
            return lines;
        }

        /// <summary>
        /// Every relation the simulation says holds between this citizen and anybody with a live record,
        /// asked of each <see cref="PawnRelationDef"/>'s own <see cref="PawnRelationWorker"/>.
        ///
        /// <para/>Running the workers rather than reading <c>Pawn_RelationsTracker</c>'s fields is what keeps
        /// this from being a second, drifting copy of the family rules: siblings, for instance, are derived
        /// from shared parents and are stored nowhere, so a field reader would simply never report one. The
        /// cost is (live people × relation defs) reference comparisons for one citizen, which is bounded by
        /// the Full/Interval slice and never by a Statistical cohort — that cohort has no <c>Pawn</c> objects
        /// to compare against, which is the same reason a relation to one of its members cannot be named.
        /// </summary>
        private static List<RelationshipLine> RelationshipsOf(Pawn pawn, IReadOnlyList<Pawn> livePeople)
        {
            var lines = new List<RelationshipLine>();
            if (pawn.relations == null) return lines;

            IReadOnlyList<PawnRelationDef> relations = DefDatabase<PawnRelationDef>.AllDefsListForReading;
            for (int i = 0; i < livePeople.Count; i++)
            {
                Pawn other = livePeople[i];
                if (ReferenceEquals(other, pawn) || other.relations == null) continue;

                for (int r = 0; r < relations.Count; r++)
                {
                    PawnRelationDef def = relations[r];
                    if (!def.Worker.InRelation(pawn, other)) continue;
                    lines.Add(new RelationshipLine(
                        def.defName, def.label ?? def.defName, other.thingIDNumber, other.Label));
                }
            }
            return lines;
        }

        /// <summary>Every name this person answers to, for matching against the chronicle's own text. Short
        /// fragments are dropped: a one- or two-letter name would match almost every line and the result would
        /// read as a rich history nobody lived.</summary>
        internal static List<string> NamesOf(Pawn pawn)
        {
            var names = new List<string>();
            AddName(names, pawn.Label);
            AddName(names, pawn.Name?.ToStringShort);
            AddName(names, pawn.Name?.ToStringFull);
            AddName(names, pawn.name);
            return names;
        }

        private static void AddName(List<string> names, string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return;
            string name = candidate!.Trim();
            if (name.Length < 3) return;
            if (!names.Contains(name)) names.Add(name);
        }

        /// <summary>
        /// The chronicle and the curated moments, filtered to the lines naming this person, oldest first.
        ///
        /// <para/>Both lists are scanned because they diverge: <c>Storyteller.Chronicle</c> is capped at
        /// <c>ChronicleCapacity</c> and drops its oldest, while <c>MomentCurator.Moments</c> is never trimmed —
        /// so a landmark from early in a long run survives only in the second. Entries present in both are
        /// merged, not listed twice.
        /// </summary>
        internal static List<ChronicleLine> HistoryNaming(IReadOnlyList<string> names)
        {
            var lines = new List<ChronicleLine>();
            if (names.Count == 0) return lines;

            Storyteller storyteller = Find.Storyteller;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Collect(ChronicleLine.TailOf(storyteller.Chronicle, storyteller.Chronicle.Count), names, seen, lines);
            Collect(ChronicleLine.TailOf(storyteller.Moments, storyteller.Moments.Count), names, seen, lines);

            // OrderBy is stable, so two entries recorded on the same tick keep the order the chronicle wrote
            // them in rather than being shuffled by a sort that does not care.
            return lines.OrderBy(l => l.Tick).ToList();
        }

        private static void Collect(
            List<ChronicleLine> candidates, IReadOnlyList<string> names, HashSet<string> seen, List<ChronicleLine> into)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                ChronicleLine line = candidates[i];
                if (!Names(line.Text, names)) continue;
                // Tick plus text identifies an entry across the two lists: the moment list holds the very same
                // ChronicleEntry objects the chronicle does, so this is deduplication, not a heuristic.
                if (!seen.Add(line.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\u0000" + line.Text)) continue;
                into.Add(line);
            }
        }

        private static bool Names(string text, IReadOnlyList<string> names)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < names.Count; i++)
            {
                if (text.IndexOf(names[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }
}
