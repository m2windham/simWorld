using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Research;

namespace SimWorld.God.View
{
    /// <summary>
    /// The civilization as one readout: how many people, in what condition, how far along the era ladder.
    /// Every mean is population-weighted across the whole civilization including its Statistical cohorts —
    /// see <see cref="GodRollup"/>, which is where these come from and where the sampling is explained.
    /// </summary>
    public sealed class CivilizationSummary
    {
        internal CivilizationSummary(
            int totalPopulation, int fullCount, int intervalCount, int statisticalCount,
            float meanMood, float meanHealth, float meanFoodNeed, float meanIndustrySkill,
            string? eraDefName, string? eraLabel, float eraProgress, int settlementCount)
        {
            TotalPopulation = totalPopulation;
            FullCount = fullCount;
            IntervalCount = intervalCount;
            StatisticalCount = statisticalCount;
            MeanMood = meanMood;
            MeanHealth = meanHealth;
            MeanFoodNeed = meanFoodNeed;
            MeanIndustrySkill = meanIndustrySkill;
            EraDefName = eraDefName;
            EraLabel = eraLabel;
            EraProgress = eraProgress;
            SettlementCount = settlementCount;
        }

        public int TotalPopulation { get; }

        /// <summary>Citizens simulated in full. The tier counts are exposed because the god view is the one
        /// place the player can see the cost of their own attention — a civilization with everyone Full is
        /// paying for that, and §11.3 exists so they can be told.</summary>
        public int FullCount { get; }

        public int IntervalCount { get; }

        public int StatisticalCount { get; }

        /// <summary>Mean mood, 0-1.</summary>
        public float MeanMood { get; }

        /// <summary>Mean fraction of health, 0-1.</summary>
        public float MeanHealth { get; }

        /// <summary>Mean food need, 0-1. High is fed, low is hungry — it is a need level, not a shortage.</summary>
        public float MeanFoodNeed { get; }

        /// <summary>Mean production-skill level, a stand-in for industrial capacity until a settlement's own
        /// output ledger exists. <see cref="GodRollup.MeanIndustrySkill"/> says why.</summary>
        public float MeanIndustrySkill { get; }

        /// <summary>The current era's defName, or null before any era is complete.</summary>
        public string? EraDefName { get; }

        /// <summary>The current era's label, or null before any era is complete.</summary>
        public string? EraLabel { get; }

        /// <summary>The share of <i>every</i> project tagged with the current era that is finished, 0-1. Not
        /// what turns the era: an era completes when its spine is done, which this does not count. For the
        /// bar a player watches, and the projects still in the way, read <see cref="GodViewSnapshot.Era"/>.</summary>
        public float EraProgress { get; }

        public int SettlementCount { get; }

        internal static CivilizationSummary From(GodRollup rollup, int settlementCount)
        {
            EraDef? era = rollup.CurrentEra;
            return new CivilizationSummary(
                rollup.TotalPopulation, rollup.FullCount, rollup.IntervalCount, rollup.StatisticalCount,
                rollup.MeanMood, rollup.MeanHealth, rollup.MeanFoodNeed, rollup.MeanIndustrySkill,
                era?.defName, era?.LabelCap, rollup.EraProgress, settlementCount);
        }
    }

    /// <summary>One settlement, as much as a civilization-scale view needs of it.</summary>
    public sealed class SettlementSummary
    {
        internal SettlementSummary(
            string name, int tile, int foundingTick,
            int totalPopulation, int citizenCount, int statisticalPopulation, bool hasInteriorMap,
            float meanMood, float meanHealth, float meanFoodNeed,
            IReadOnlyList<StockLine> stock, int storedUnits, float storedNutrition)
        {
            Name = name;
            Tile = tile;
            FoundingTick = foundingTick;
            TotalPopulation = totalPopulation;
            CitizenCount = citizenCount;
            StatisticalPopulation = statisticalPopulation;
            HasInteriorMap = hasInteriorMap;
            MeanMood = meanMood;
            MeanHealth = meanHealth;
            MeanFoodNeed = meanFoodNeed;
            Stock = stock;
            StoredUnits = storedUnits;
            StoredNutrition = storedNutrition;
        }

        public string Name { get; }

        /// <summary>The world tile it sits on — the handle a host uses to place it on a globe.</summary>
        public int Tile { get; }

        public int FoundingTick { get; }

        public int TotalPopulation { get; }

        /// <summary>Citizens with a live <c>Pawn</c> (Full and Interval).</summary>
        public int CitizenCount { get; }

        /// <summary>People with no <c>Pawn</c> object at all, by the Statistical tier's own design.</summary>
        public int StatisticalPopulation { get; }

        /// <summary>Whether its interior map has been generated yet. Not the map itself: what a settlement
        /// looks like inside belongs to the map layer, and a civilization view only needs to know whether
        /// there is something to descend into.</summary>
        public bool HasInteriorMap { get; }

        /// <summary>Mean mood of this settlement's people, 0-1. The same number as
        /// <see cref="CivilizationSummary.MeanMood"/> taken over one settlement instead of all of them: a
        /// <see cref="GodRollup"/> of this settlement alone, so its Statistical cohort is folded in by the
        /// same weighted sample and nobody in it is walked. 0 when nobody lives here — a mean over nobody,
        /// not a miserable town, so check <see cref="TotalPopulation"/> before drawing it.</summary>
        public float MeanMood { get; }

        /// <summary>Mean fraction of health, 0-1, over this settlement alone. 1 when nobody lives here.</summary>
        public float MeanHealth { get; }

        /// <summary>Mean food need, 0-1, over this settlement alone. High is fed, low is hungry — a need
        /// level, not a shortage, and not the share of people who have eaten. 0 when nobody lives here.</summary>
        public float MeanFoodNeed { get; }

        /// <summary>The resources this settlement holds, one line per kind, most important first (by the
        /// def's readout priority, then defName). Empty — never null — when it holds none. See
        /// <see cref="StockLine"/> for what counts: resources in either book, never gear, rubble or bodies.</summary>
        public IReadOnlyList<StockLine> Stock { get; }

        /// <summary>Every unit in <see cref="Stock"/>, summed: how much there is to store, whatever it is.</summary>
        public int StoredUnits { get; }

        /// <summary>Nutrition in <see cref="Stock"/>: each edible line's count times its def's nutrition per
        /// unit, in the same units a citizen's food need is filled in. Every food shipped counts as a
        /// resource, so this is the settlement-scale figure <c>AI.HuntingInitiative.NutritionAvailable</c>
        /// answers "how much food does this town have" with — ledger and map together — and
        /// <c>Economy.SettlementLarder.StoredNutrition</c> for a settlement that has no map.</summary>
        public float StoredNutrition { get; }

        internal static SettlementSummary From(World.Settlement settlement)
        {
            // A rollup of this settlement alone rather than a second way of averaging: the per-settlement
            // means are then the civilization's own definition, cohort sampling included, and cannot drift
            // from the number beside them.
            var rollup = new GodRollup();
            rollup.Recompute(settlement);

            Dictionary<ThingDef, int> held = StockLine.Tally(settlement);
            int storedUnits = 0;
            float storedNutrition = 0f;
            foreach (KeyValuePair<ThingDef, int> entry in held)
            {
                storedUnits += entry.Value;
                if (entry.Key.IsNutritionGivingIngestible) storedNutrition += entry.Key.ingestible!.nutrition * entry.Value;
            }

            return new SettlementSummary(
                settlement.name,
                settlement.tile,
                settlement.foundingTick,
                settlement.TotalPopulation,
                settlement.Citizens.Count,
                settlement.StatisticalPopulation,
                settlement.InteriorMap != null,
                rollup.MeanMood, rollup.MeanHealth, rollup.MeanFoodNeed,
                StockLine.AllOf(held), storedUnits, storedNutrition);
        }
    }

    /// <summary>Why an edict can or cannot be issued right now.</summary>
    public enum EdictAvailability
    {
        /// <summary>Already in force.</summary>
        Active,

        /// <summary>Could be issued right now.</summary>
        Available,

        /// <summary>The civilization has not reached the era it requires.</summary>
        RequiresLaterEra,

        /// <summary>The civilization has moved past the era in which it meant anything.</summary>
        Obsolete,

        /// <summary>Nothing wrong with the edict — every slot is simply taken.</summary>
        NoSlotFree,

        /// <summary>The seat it needs (<see cref="EdictDef.requiredOffice"/>) is vacant everywhere.</summary>
        NeedsSeatedOffice,

        /// <summary>Issued, and holding its slot, but out of force until its seat is filled again.</summary>
        Suspended,

        /// <summary>Ended too recently: its <see cref="EdictDef.cooldownDays"/> have not run out.</summary>
        Cooldown,
    }

    /// <summary>
    /// One edict as the view needs it: what it is, whether it is in force, and — when it is not issuable —
    /// which of the reasons applies and how to say so.
    ///
    /// <para/><see cref="GodManager.CanActivate"/> answers a bare yes or no, which is all the simulation needs
    /// and strictly less than a UI does: a greyed-out button with no explanation is a bug report waiting to be
    /// filed. <see cref="Availability"/> is that same decision with its reason preserved, and
    /// <c>GodViewTests</c> pins the two to agree for every edict in content, so the view can never offer
    /// something the simulation would then refuse.
    ///
    /// <para/>The reasons are reported in the order a person would want to hear them, which is not the order
    /// <see cref="GodManager.CanActivate"/> tests them in — an edict that is both era-locked and slot-blocked
    /// reports the era, because that is the fact about <em>this</em> edict; the slot is a fact about the
    /// others. Both orderings agree on the only thing that has to hold, which is whether it can be issued.
    /// </summary>
    public sealed class EdictOption
    {
        internal EdictOption(
            string defName, string label, string description,
            bool isActive, EdictAvailability availability, string reason,
            string? requiredEraDefName, string? obsoleteEraDefName,
            float durationDays, float cooldownDays, float? daysRemaining, float cooldownDaysLeft,
            IReadOnlyList<string> effects)
        {
            DefName = defName;
            Label = label;
            Description = description;
            IsActive = isActive;
            Availability = availability;
            Reason = reason;
            RequiredEraDefName = requiredEraDefName;
            ObsoleteEraDefName = obsoleteEraDefName;
            DurationDays = durationDays;
            CooldownDays = cooldownDays;
            DaysRemaining = daysRemaining;
            CooldownDaysLeft = cooldownDaysLeft;
            Effects = effects;
        }

        /// <summary>The handle to pass back to <see cref="GodCommands"/>. Not a Def — see
        /// <see cref="GodViewSnapshot"/> on why the seam is a string.</summary>
        public string DefName { get; }

        public string Label { get; }

        public string Description { get; }

        public bool IsActive { get; }

        public EdictAvailability Availability { get; }

        /// <summary>A sentence a view can show without composing one. Always populated, including for an
        /// available edict, so a caller never has to special-case the happy path to render a tooltip.</summary>
        public string Reason { get; }

        /// <summary>The era it needs, or null if it needs none.</summary>
        public string? RequiredEraDefName { get; }

        /// <summary>The era that retires it, or null if nothing does.</summary>
        public string? ObsoleteEraDefName { get; }

        /// <summary>Days it stands once issued before it lapses on its own; 0 when it stands until rescinded.</summary>
        public float DurationDays { get; }

        /// <summary>Days after it ends before it can be issued again; 0 for none.</summary>
        public float CooldownDays { get; }

        /// <summary>For an issued edict with a duration: days before it lapses. Null otherwise.</summary>
        public float? DaysRemaining { get; }

        /// <summary>Days before it can be issued again; 0 when it is not cooling down.</summary>
        public float CooldownDaysLeft { get; }

        /// <summary>
        /// What it does, line by line, in the player's words, built from the def's own fields
        /// (<see cref="EffectsOf"/>) so a line can never describe a mechanism the edict does not carry: the work
        /// it puts first, any rule it sets (lean rations, a day of rest), its research focus, its mood cost while
        /// in force, what it leaves behind, how long it stands and how long it then rests.
        /// </summary>
        public IReadOnlyList<string> Effects { get; }

        /// <summary>The <see cref="Effects"/> lines for <paramref name="def"/>.</summary>
        internal static List<string> EffectsOf(EdictDef def)
        {
            var lines = new List<string>();
            if (def.prioritizedWork.Count > 0)
            {
                var work = new List<string>(def.prioritizedWork.Count);
                if (def.exclusiveWork)
                {
                    for (int i = 0; i < def.prioritizedWork.Count; i++) work.Add(def.prioritizedWork[i].gerundLabel ?? def.prioritizedWork[i].defName);
                    lines.Add("Every able hand turns to " + string.Join(" and ", work) + " before anything else.");
                }
                else
                {
                    // The work's own name, not its gerund: "Construction comes before other work.", not
                    // "constructing come before other work." (the host lane's reading of the edict drawer).
                    for (int i = 0; i < def.prioritizedWork.Count; i++) work.Add(def.prioritizedWork[i].label ?? def.prioritizedWork[i].defName);
                    string first = string.Join(" and ", work);
                    lines.Add(char.ToUpperInvariant(first[0]) + first.Substring(1) + (work.Count == 1 ? " comes" : " come") + " before other work.");
                }
            }
            if (def.prioritizedConstruction.Count > 0)
            {
                var things = new List<string>(def.prioritizedConstruction.Count);
                for (int i = 0; i < def.prioritizedConstruction.Count; i++) things.Add(def.prioritizedConstruction[i].label ?? def.prioritizedConstruction[i].defName);
                lines.Add("The settlement builds " + string.Join(" and ", things) + " first.");
            }
            if (def.rationFood) lines.Add("People eat only once very hungry, so the stores last longer.");
            if (def.restDay) lines.Add("No routine work: the day is given to rest and leisure. Emergencies are still answered.");
            if (def.workerClass == typeof(EdictWorker_ExemptMinors)) lines.Add("Children are spared it.");
            if (def.researchFocus != null) lines.Add("Research turns to " + def.researchFocus.LabelCap + ".");
            float moodWhile = MoodOf(def.moodThought);
            if (moodWhile != 0f) lines.Add("Mood " + Signed(moodWhile) + " while in force.");
            if (def.completedThought != null)
            {
                lines.Add("When it runs its course: mood " + Signed(MoodOf(def.completedThought)) + " for " +
                          def.completedThought.durationDays + " days.");
            }
            if (def.durationDays > 0f) lines.Add("Lasts " + Days(def.durationDays) + ".");
            if (def.cooldownDays > 0f) lines.Add("Can be issued again " + Days(def.cooldownDays) + " after it ends.");
            if (def.requiredOffice != null) lines.Add("Needs a seated " + def.requiredOffice.role.LabelCap + ".");
            return lines;
        }

        private static float MoodOf(Thoughts.ThoughtDef? thought) =>
            thought == null || thought.stages == null || thought.stages.Count == 0 || thought.stages[0] == null
                ? 0f
                : thought.stages[0]!.baseMoodEffect;

        /// <summary>An era named the way a sentence wants it: "Bronze age" stands alone, "Agrarian" takes "era"
        /// ("Needs the Bronze age era." read badly in the host's edict drawer).</summary>
        internal static string EraName(EraDef era)
        {
            string name = era.LabelCap;
            return name.EndsWith(" age", StringComparison.Ordinal) ? name : name + " era";
        }

        private static string Signed(float value) => (value > 0f ? "+" : "") + value.ToString("0.#");

        internal static string Days(float days) => days == 1f ? "1 day" : days.ToString("0.#") + " days";

        /// <summary>The option for <paramref name="def"/> as <paramref name="god"/> stands right now: the one
        /// place both the snapshot and <see cref="GodCommands.IssueEdict"/> build it from, so the two can never
        /// read the state differently. Only an edict actually in force is asked whether anyone may carry it out
        /// — a suspended one is already explained by its vacant office, and one not issued has nobody to fail.</summary>
        internal static EdictOption For(EdictDef def, GodManager god)
        {
            bool isActive = god.IsActive(def);
            int? lapse = god.TicksUntilLapse(def);
            return For(def, isActive, Sim.Find.ResearchManager.CurrentEra,
                slotFree: god.IssuedEdicts.Count < GodTuning.MaxActiveEdicts,
                isSuspended: god.IsSuspended(def),
                officeSeated: def.requiredOffice == null || GodManager.IsSeated(def.requiredOffice),
                workNobodyMayDo: isActive ? GodViewSnapshot.NobodyMayDo(def) : null,
                cooldownDaysLeft: god.CooldownTicksLeft(def) / (float)Sim.GenDate.TicksPerDay,
                daysRemaining: lapse == null ? (float?)null : lapse.Value / (float)Sim.GenDate.TicksPerDay);
        }

        /// <param name="workNobodyMayDo">For an edict in force: the gerund labels of its work that no citizen
        /// it applies to may do (<see cref="EdictWorkUtility.WorkNobodyMayDo"/>). When there are any, the reason
        /// says so instead of a bare "In force." — an edict nobody can carry out is stalled, and the player
        /// should hear it from the edict rather than infer it from nothing happening. Ignored for any edict not
        /// in force.</param>
        internal static EdictOption For(
            EdictDef def, bool isActive, EraDef? currentEra, bool slotFree, bool isSuspended, bool officeSeated,
            IReadOnlyList<string>? workNobodyMayDo = null, float cooldownDaysLeft = 0f, float? daysRemaining = null)
        {
            EdictAvailability availability;
            string reason;

            if (isActive)
            {
                availability = EdictAvailability.Active;
                reason = workNobodyMayDo == null || workNobodyMayDo.Count == 0
                    ? "In force."
                    : "In force, but nobody is allowed to do " + string.Join(" or ", workNobodyMayDo) +
                      ": everyone it applies to has it set to never or cannot do it.";
            }
            else if (isSuspended)
            {
                // Issued, so the host offers to rescind it; the reason says what brings it back.
                availability = EdictAvailability.Suspended;
                reason = "Suspended. " + GodManager.NeedsSeatedReason(def.requiredOffice!);
            }
            else if (def.obsoleteEra != null && currentEra != null && currentEra.order >= def.obsoleteEra.order)
            {
                availability = EdictAvailability.Obsolete;
                reason = "Belongs to the times before the " + EraName(def.obsoleteEra) + ".";
            }
            else if (def.requiredEra != null && (currentEra == null || currentEra.order < def.requiredEra.order))
            {
                availability = EdictAvailability.RequiresLaterEra;
                reason = "Opens in the " + EraName(def.requiredEra) + ".";
            }
            else if (cooldownDaysLeft > 0f)
            {
                // After the era (a fact about the age) and before the seat and the slot: a fact about this edict.
                availability = EdictAvailability.Cooldown;
                reason = "Can be issued again in " + Days((float)System.Math.Ceiling(cooldownDaysLeft * 10f) / 10f) + ".";
            }
            else if (!officeSeated)
            {
                // Before the slot, for the reason the era comes first: the seat is a fact about this edict.
                availability = EdictAvailability.NeedsSeatedOffice;
                reason = GodManager.NeedsSeatedReason(def.requiredOffice!);
            }
            else if (!slotFree)
            {
                availability = EdictAvailability.NoSlotFree;
                reason = "No room — rescind another edict first.";
            }
            else
            {
                availability = EdictAvailability.Available;
                reason = "Ready to issue.";
            }

            return new EdictOption(
                def.defName,
                def.LabelCap,
                def.description ?? "",
                isActive,
                availability,
                reason,
                def.requiredEra?.defName,
                def.obsoleteEra?.defName,
                def.durationDays,
                def.cooldownDays,
                isActive || isSuspended ? daysRemaining : null,
                cooldownDaysLeft,
                EffectsOf(def));
        }
    }

    /// <summary>What sort of thing a <see cref="ChronicleLine"/> records — which of the storyteller's three
    /// writers produced it.</summary>
    public enum ChronicleKind
    {
        /// <summary>An incident fired (<c>Storyteller.TryFire</c>): a raid, a trader, a disease.
        /// <see cref="ChronicleLine.IncidentDefName"/> names it.</summary>
        Incident,

        /// <summary>Somebody died (<c>Storyteller.RecordDeath</c>). <see cref="ChronicleLine.DeathCause"/> says
        /// of what.</summary>
        Death,

        /// <summary>A line some system wrote in its own words (<c>Storyteller.RecordChronicle(string)</c>): an
        /// accession, a birth, a treaty, an edict.</summary>
        Event,
    }

    /// <summary>One line of the civilization's history, flattened for display.</summary>
    public sealed class ChronicleLine
    {
        internal ChronicleLine(
            int tick, string text, bool isMoment, ChronicleKind kind, string? incidentDefName,
            Pawns.DeathCause? deathCause, IReadOnlyList<int> involvedThingIds, EventLocationView? location)
        {
            Location = location;
            Tick = tick;
            Text = text;
            IsMoment = isMoment;
            Kind = kind;
            IncidentDefName = incidentDefName;
            DeathCause = deathCause;
            InvolvedThingIds = involvedThingIds;
        }

        /// <summary>Which writer produced this line — see <see cref="ChronicleKind"/>.</summary>
        public ChronicleKind Kind { get; }

        /// <summary>The incident's defName for <see cref="ChronicleKind.Incident"/>; null otherwise.</summary>
        public string? IncidentDefName { get; }

        /// <summary>What they died of, for <see cref="ChronicleKind.Death"/>; null otherwise. The same enum
        /// <see cref="DeathCauseLine.Cause"/> carries.</summary>
        public Pawns.DeathCause? DeathCause { get; }

        /// <summary>Thing ids of the people the line is about, where the writer recorded them (a death names
        /// the one who died). Empty when it did not — most event lines carry names only in
        /// <see cref="Text"/>.</summary>
        public IReadOnlyList<int> InvolvedThingIds { get; }

        /// <summary>Where it happened, for a "jump to" and a mark on the map; null when the writer recorded
        /// no place (a treaty, an edict, an era reached) or the entry predates locations.</summary>
        public EventLocationView? Location { get; }

        public int Tick { get; }

        /// <summary>What happened, already in words. A <see cref="ChronicleEntry"/> carries both a free-form
        /// line and an incident defName plus target, depending on which system recorded it; this resolves that
        /// to the one string a reader wants rather than making every host repeat the same choice.</summary>
        public string Text { get; }

        /// <summary>Whether the storyteller kept this as a landmark rather than passing news.</summary>
        public bool IsMoment { get; }

        /// <summary>The last <paramref name="count"/> entries, oldest first.</summary>
        internal static List<ChronicleLine> TailOf(IReadOnlyList<ChronicleEntry> entries, int count)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));

            int start = Math.Max(0, entries.Count - count);
            var lines = new List<ChronicleLine>(entries.Count - start);
            for (int i = start; i < entries.Count; i++)
            {
                lines.Add(Of(entries[i]));
            }
            return lines;
        }

        internal static ChronicleLine Of(ChronicleEntry e)
        {
            ChronicleKind kind = e.deathCause != null
                ? ChronicleKind.Death
                : !string.IsNullOrEmpty(e.targetLabel) ? ChronicleKind.Incident : ChronicleKind.Event;

            IReadOnlyList<int> involved = e.involvedThingIds == null || e.involvedThingIds.Count == 0
                ? Array.Empty<int>()
                : e.involvedThingIds.ToArray();

            return new ChronicleLine(
                e.tick, Describe(e), e.isMoment, kind,
                kind == ChronicleKind.Incident ? e.incidentDefName : null,
                e.deathCause, involved, EventLocationView.Of(e.location));
        }

        private static string Describe(ChronicleEntry entry)
        {
            // A free-form line (Storyteller.RecordChronicle(string)) carries its whole text in targetLabel with
            // no incident behind it; an incident firing carries both. Prefer the incident-plus-target reading
            // when there is an incident, since "Raid on the Ashfell" says more than either half alone.
            bool hasIncident = !string.IsNullOrEmpty(entry.incidentDefName);
            bool hasTarget = !string.IsNullOrEmpty(entry.targetLabel);

            if (hasIncident && hasTarget)
            {
                // An incident firing names its def and its target. The def's own label is what a player reads
                // ("Wanderer joins", not "WandererJoin"), and "the civilization" is the only target there is,
                // so it says nothing and is left off. A line that is not an incident (a death, "Death: Ash")
                // keeps its headline as written.
                IncidentDef? incident = DefDatabase<IncidentDef>.GetNamedSilentFail(entry.incidentDefName);
                if (incident == null) return entry.incidentDefName + ": " + entry.targetLabel;
                return entry.targetLabel == Storyteller.CivilizationTargetLabel
                    ? incident.LabelCap
                    : incident.LabelCap + ": " + entry.targetLabel;
            }
            if (hasIncident) return entry.incidentDefName;
            return hasTarget ? entry.targetLabel : "";
        }
    }
}
