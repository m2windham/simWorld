using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Research;
using SimWorld.Things;

namespace SimWorld.Map.View
{
    /// <summary>
    /// What the player may build, as one rule read by both sides of the seam: the build list
    /// (<see cref="MapViewSnapshot.CaptureBuildables()"/>) offers exactly what this lets through, and
    /// <see cref="MapCommands.PlaceBlueprint"/> refuses the rest with this class's own sentence. One rule in one
    /// place, so the list and the command cannot drift apart.
    ///
    /// <para/><b>The thing: researched.</b> RimWorld's architect menu shows a buildable only once its
    /// <c>researchPrerequisites</c> are finished (<c>Designator_Build.Visible</c>, recalled), and every one of this
    /// settlement's own initiatives asks <see cref="ThingDef.IsResearchFinished"/> before it plans a building. The
    /// player's command did not, and nothing downstream does — a builder raises whatever blueprint stands — so a
    /// stone-age player could have had a smelter built. That is a building with no causal path through the
    /// simulation, the one thing the lever test rules out, so the command now refuses it.
    ///
    /// <para/><b>The stuff: this age can make it, or the town holds some.</b> RimWorld's menu offers only stuffs
    /// the map holds some of. Across eight eras that is too narrow — a stone-age band holds no wood on the day it
    /// founds, and fells trees for it — and too wide is a bronze age offered steel. So a stuff is offered when its
    /// era (<see cref="EraAvailability.IsAvailableInCurrentEra(ThingDef)"/>, read from what makes it) is no later
    /// than the town's, or when the town holds some of it in either book, map or ledger, however it came by it
    /// (a trader's steel is steel). A later age's stuff that nobody holds has no path to the site: nobody here can
    /// make it and there is none to haul. Anything else is the player's call, unwise or not.
    /// </summary>
    internal static class BuildGate
    {
        /// <summary>Why <paramref name="def"/> cannot be built yet, or null when it can.</summary>
        internal static string? WhyNotBuildable(ThingDef def)
        {
            if (def.IsResearchFinished) return null;
            List<string> missing = MissingResearch(def);
            return def.LabelCap + " needs research first: " + string.Join(", ", missing) + ".";
        }

        /// <summary>Labels of the research <paramref name="def"/> still waits on, in its own order.</summary>
        internal static List<string> MissingResearch(ThingDef def)
        {
            var missing = new List<string>();
            if (def.researchPrerequisites == null) return missing;
            for (int i = 0; i < def.researchPrerequisites.Count; i++)
            {
                ResearchProjectDef project = def.researchPrerequisites[i];
                if (!Sim.Find.ResearchManager.IsFinished(project)) missing.Add(project.LabelCap);
            }
            return missing;
        }

        /// <summary>Why <paramref name="def"/> cannot be built of <paramref name="stuff"/> on
        /// <paramref name="map"/> for want of the stuff itself, or null when it can.</summary>
        internal static string? WhyNotStuff(ThingDef def, ThingDef stuff, SimWorld.Map.Map map)
        {
            if (StuffOffered(stuff, map)) return null;
            return stuff.LabelCap + " belongs to a later age and none is held here, so " + (def.label ?? def.defName)
                + " cannot be built of it.";
        }

        internal static bool StuffOffered(ThingDef stuff, SimWorld.Map.Map map) =>
            EraAvailability.IsAvailableInCurrentEra(stuff) || Held(map, stuff) > 0;

        /// <summary>Whether <paramref name="def"/> belongs on the build list at all: something a blueprint can be
        /// placed for, whose era the town has reached or which it has already learned to build. A later age's
        /// building stays off the list until then, where research cannot yet reach it.</summary>
        internal static bool Listed(ThingDef def)
        {
            if (def.IsResearchFinished) return true;
            EraDef? era = EraAvailability.EraOf(def);
            EraDef? now = EraAvailability.WorkingEra();
            return era == null || now == null || era.order <= now.order;
        }

        /// <summary>Units of <paramref name="def"/> the town on <paramref name="map"/> holds: spawned on the map and
        /// in its settlement's ledger, the two books <c>God.View.StockLine</c> sums.</summary>
        internal static int Held(SimWorld.Map.Map map, ThingDef def)
        {
            int have = 0;
            IReadOnlyList<Thing> stacks = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].Spawned) have += stacks[i].stackCount;
            }
            World.Settlement? settlement = AI.HuntingInitiative.SettlementFor(map);
            if (settlement != null && settlement.Stores.TryGetValue(def, out int stored)) have += stored;
            return have;
        }
    }
}
