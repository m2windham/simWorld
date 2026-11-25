using System;
using System.Collections.Generic;

using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Research;
using SimWorld.Sim;

namespace SimWorld.Map.View
{
    /// <summary>
    /// The build list (host UX request after playtest 4): <see cref="CaptureBuildables()"/> and its overloads. Its
    /// own file so the per-frame capture in <c>MapViewSnapshot.cs</c> is untouched; see <see cref="BuildCatalogue"/>
    /// for the gate.
    /// </summary>
    public sealed partial class MapViewSnapshot
    {
        /// <summary>What may be built in whichever settlement the god has open, the focus every
        /// <see cref="MapCommands"/> method places into.</summary>
        public static BuildCatalogue CaptureBuildables()
        {
            int? tile = FocusedTile(out string? reason);
            if (tile == null) return AbsentBuildables(reason!);
            return CaptureBuildables(tile.Value);
        }

        /// <summary>What may be built in the settlement on <paramref name="tile"/>.</summary>
        public static BuildCatalogue CaptureBuildables(int tile)
        {
            SimWorld.Map.Map? map = InteriorOf(tile, out string? reason, out _);
            if (map == null) return AbsentBuildables(reason!);
            return CaptureBuildables(map);
        }

        /// <summary>What may be built on a map held directly, for the core's own tests and tools, as
        /// <see cref="Capture(SimWorld.Map.Map)"/> is.</summary>
        public static BuildCatalogue CaptureBuildables(SimWorld.Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            var listed = new List<(int Era, BuildableOption Option)>();
            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            var seen = new HashSet<ThingDef>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].category != ThingCategory.Blueprint || !(all[i].entityToBuild is ThingDef def)) continue;
                if (!seen.Add(def) || !BuildGate.Listed(def)) continue;
                EraDef? era = EraAvailability.EraOf(def);
                listed.Add((era?.order ?? -1, OptionFor(map, def, era)));
            }
            listed.Sort(static (a, b) => a.Era != b.Era
                ? a.Era.CompareTo(b.Era)
                : string.CompareOrdinal(a.Option.Label, b.Option.Label));

            var options = new List<BuildableOption>(listed.Count);
            for (int i = 0; i < listed.Count; i++) options.Add(listed[i].Option);

            EraDef? now = EraAvailability.WorkingEra();
            return new BuildCatalogue(true, null, Find.TickManager.TicksGame, now?.defName, now?.LabelCap, options);
        }

        private static BuildableOption OptionFor(SimWorld.Map.Map map, ThingDef def, EraDef? era)
        {
            string? reason = BuildGate.WhyNotBuildable(def);

            var cost = new List<MaterialCost>();
            if (def.costList != null)
            {
                for (int i = 0; i < def.costList.Count; i++)
                {
                    ThingDef material = def.costList[i].thingDef;
                    cost.Add(new MaterialCost(material.defName, material.LabelCap, def.costList[i].count, BuildGate.Held(map, material)));
                }
            }

            var stuffs = new List<MaterialCost>();
            if (def.MadeFromStuff)
            {
                IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    ThingDef stuff = all[i];
                    if (!GenStuff.CanMake(stuff, def) || !BuildGate.StuffOffered(stuff, map)) continue;
                    stuffs.Add(new MaterialCost(stuff.defName, stuff.LabelCap, def.costStuffCount, BuildGate.Held(map, stuff)));
                }
            }

            return new BuildableOption(
                def.defName,
                def.LabelCap,
                string.IsNullOrEmpty(def.description) ? null : def.description,
                def.size,
                def.rotatable,
                era?.defName,
                era?.LabelCap,
                reason == null,
                reason,
                BuildGate.MissingResearch(def),
                cost,
                def.MadeFromStuff ? def.costStuffCount : 0,
                stuffs,
                GenStuff.DefaultStuffFor(def)?.defName);
        }

        private static BuildCatalogue AbsentBuildables(string reason) =>
            new BuildCatalogue(false, reason, Find.TickManager.TicksGame, null, null, Array.Empty<BuildableOption>());
    }
}
