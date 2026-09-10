using System;
using System.Collections.Generic;

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

        /// <summary>Progress through the current era, 0-1.</summary>
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
            int totalPopulation, int citizenCount, int statisticalPopulation, bool hasInteriorMap)
        {
            Name = name;
            Tile = tile;
            FoundingTick = foundingTick;
            TotalPopulation = totalPopulation;
            CitizenCount = citizenCount;
            StatisticalPopulation = statisticalPopulation;
            HasInteriorMap = hasInteriorMap;
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
            string? requiredEraDefName, string? obsoleteEraDefName)
        {
            DefName = defName;
            Label = label;
            Description = description;
            IsActive = isActive;
            Availability = availability;
            Reason = reason;
            RequiredEraDefName = requiredEraDefName;
            ObsoleteEraDefName = obsoleteEraDefName;
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

        internal static EdictOption For(EdictDef def, bool isActive, EraDef? currentEra, bool slotFree)
        {
            EdictAvailability availability;
            string reason;

            if (isActive)
            {
                availability = EdictAvailability.Active;
                reason = "In force.";
            }
            else if (def.obsoleteEra != null && currentEra != null && currentEra.order >= def.obsoleteEra.order)
            {
                availability = EdictAvailability.Obsolete;
                reason = "Left behind in the " + def.obsoleteEra.LabelCap + " era.";
            }
            else if (def.requiredEra != null && (currentEra == null || currentEra.order < def.requiredEra.order))
            {
                availability = EdictAvailability.RequiresLaterEra;
                reason = "Needs the " + def.requiredEra.LabelCap + " era.";
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
                def.obsoleteEra?.defName);
        }
    }

    /// <summary>One line of the civilization's history, flattened for display.</summary>
    public sealed class ChronicleLine
    {
        internal ChronicleLine(int tick, string text, bool isMoment)
        {
            Tick = tick;
            Text = text;
            IsMoment = isMoment;
        }

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
                ChronicleEntry e = entries[i];
                lines.Add(new ChronicleLine(e.tick, Describe(e), e.isMoment));
            }
            return lines;
        }

        private static string Describe(ChronicleEntry entry)
        {
            // A free-form line (Storyteller.RecordChronicle(string)) carries its whole text in targetLabel with
            // no incident behind it; an incident firing carries both. Prefer the incident-plus-target reading
            // when there is an incident, since "Raid on the Ashfell" says more than either half alone.
            bool hasIncident = !string.IsNullOrEmpty(entry.incidentDefName);
            bool hasTarget = !string.IsNullOrEmpty(entry.targetLabel);

            if (hasIncident && hasTarget) return entry.incidentDefName + ": " + entry.targetLabel;
            if (hasIncident) return entry.incidentDefName;
            return hasTarget ? entry.targetLabel : "";
        }
    }
}
