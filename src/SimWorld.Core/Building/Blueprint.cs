using System;
using SimWorld.Defs;
using SimWorld.Map;
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
        /// Spawns this Blueprint's matching <see cref="Frame"/> at the same position/rotation and destroys
        /// the Blueprint (RimWorld: <c>Blueprint_Build.TryReplaceWithSolidThing</c>, trimmed — no resources
        /// carry over since a Blueprint never holds any).
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

            var frame = (Frame)ThingMaker.MakeThing(frameDef);
            GenSpawn.Spawn(frame, pos, map, rot);
            return frame;
        }
    }
}
