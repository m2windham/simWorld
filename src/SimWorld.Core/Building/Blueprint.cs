using System;
using System.Collections.Generic;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// The player's construction intent, no materials delivered yet (RimWorld: <c>Verse.Blueprint</c> /
    /// <c>Verse.Blueprint_Build</c>, folded into one — this pass builds nothing else a Blueprint subclass
    /// would differ for). Never blocks movement or the edifice grid (RimWorld: blueprints are walkable);
    /// the moment a hauler delivers its first resource it is replaced by a <see cref="Frame"/> — see
    /// <see cref="ReplaceWithFrame"/>.
    /// </summary>
    public class Blueprint : Thing
    {
        /// <summary>The buildable Def this stands in for (RimWorld: <c>Blueprint.EntityToBuild</c>).</summary>
        public ThingDef EntityToBuild => def.entityToBuild!;

        /// <summary>
        /// A Blueprint occupies what it stands in for, not what its own def says. The hand-authored
        /// <c>Blueprint_X</c> defs carry no size at all, so without this a Blueprint for a 2x2 building would
        /// reserve one cell and a second one could be laid across the other three.
        /// </summary>
        public override Map.IntVec2 Size => EntityToBuild.size;

        /// <summary>
        /// The stuff the finished thing will be made of when <see cref="EntityToBuild"/> is made from stuff
        /// (RimWorld: <c>Blueprint_Build.stuffToUse</c>). A placer that names none gets
        /// <see cref="GenStuff.DefaultStuffFor"/> when the blueprint spawns — RimWorld's own fallback for a
        /// stuff-built def priced without a stuff — so every placement site in this port, none of which chose a
        /// material before stuff cost existed, keeps building what it built (a wall in wood).
        /// </summary>
        public ThingDef? stuffToUse;

        /// <summary>What this blueprint's frame will need delivered in all (RimWorld:
        /// <c>Blueprint_Build.TotalMaterialCost</c> → <c>CostListAdjusted(stuffToUse)</c>).</summary>
        public List<ThingDefCountClass> TotalMaterialCost() => EntityToBuild.CostListAdjusted(stuffToUse);

        /// <summary>How many of <paramref name="material"/> this blueprint still needs: all of it, since a
        /// blueprint never holds any (RimWorld: <c>Blueprint.ThingCountNeeded</c>).</summary>
        public int ThingCountNeeded(ThingDef material) => EntityToBuild.CostListCountFor(material, stuffToUse);

        public override void SpawnSetup(Map.Map map, bool respawningAfterLoad)
        {
            if (EntityToBuild.MadeFromStuff) stuffToUse ??= GenStuff.DefaultStuffFor(EntityToBuild);
            base.SpawnSetup(map, respawningAfterLoad);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref stuffToUse, "stuffToUse");
        }

        /// <summary>
        /// Spawns this Blueprint's matching <see cref="Frame"/> at the same position/rotation and destroys
        /// the Blueprint (RimWorld: <c>Blueprint_Build.TryReplaceWithSolidThing</c>, trimmed — no resources
        /// carry over since a Blueprint never holds any; its <see cref="stuffToUse"/> becomes the frame's
        /// <see cref="Things.Thing.Stuff"/>).
        /// </summary>
        public Frame ReplaceWithFrame()
        {
            Map.Map map = Map ?? throw new InvalidOperationException("Blueprint is not spawned.");
            ThingDef? frameDef = GenConstruct.FrameDefFor(EntityToBuild);
            if (frameDef == null)
            {
                throw new InvalidOperationException("No Frame ThingDef is authored for " + EntityToBuild.defName + ".");
            }
            IntVec3 pos = Position;
            Rot4 rot = Rotation;
            Destroy(DestroyMode.Vanish);

            var frame = (Frame)ThingMaker.MakeThing(frameDef, stuffToUse);
            GenSpawn.Spawn(frame, pos, map, rot);
            return frame;
        }
    }
}
