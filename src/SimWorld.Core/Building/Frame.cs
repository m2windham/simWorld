using System;
using System.Collections.Generic;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Building
{
    /// <summary>
    /// Materials delivered, work being applied — the stage between <see cref="Blueprint"/> and a finished
    /// <see cref="Building"/> (RimWorld: <c>Verse.Frame</c>). Occupies the edifice/path grid exactly as its
    /// finished building would (a half-built wall blocks movement too) even though its own <c>def.category</c>
    /// is <see cref="ThingCategory.Frame"/>, not <c>Building</c> — see <see cref="SpawnSetup"/>.
    /// </summary>
    public class Frame : Thing
    {
        /// <summary>RimWorld's real fraction of delivered materials refunded on a failed construction is not
        /// sourced here; this port picks a flat half, documented rather than hidden, and pinned by a test
        /// (CLAUDE.md: "pin the behaviour with a test rather than the literal value").</summary>
        public const float FailRefundFraction = 0.5f;

        /// <summary>Modest, unsourced xp — pinned by a test that just checks it is positive and skill-scaled
        /// like every other <see cref="SkillRecord.Learn"/> call in this codebase, not by its literal value.</summary>
        public const float CompletionXp = 200f;

        public ThingDef EntityToBuild => def.entityToBuild!;

        public float workDone;

        private readonly Dictionary<ThingDef, int> resourceContainer = new Dictionary<ThingDef, int>();

        /// <summary>Total work-units this Frame's construction needs (RimWorld: <c>Frame.WorkToBuild</c>).</summary>
        public float WorkToBuild => EntityToBuild.GetStatValueAbstract(StatDefOf.WorkToBuild);

        public int MaterialDelivered(ThingDef material) => resourceContainer.TryGetValue(material, out int n) ? n : 0;

        /// <summary>How many more of <paramref name="material"/> this Frame still needs, 0 if none or already met.</summary>
        public int MaterialStillNeeded(ThingDef material)
        {
            int required = EntityToBuild.CostListCountFor(material);
            int have = MaterialDelivered(material);
            return Math.Max(0, required - have);
        }

        /// <summary>True once every <see cref="Defs.ThingDef.costList"/> entry is fully delivered.</summary>
        public bool MaterialsFullySatisfied()
        {
            if (EntityToBuild.costList == null) return true;
            for (int i = 0; i < EntityToBuild.costList.Count; i++)
            {
                ThingDefCountClass entry = EntityToBuild.costList[i];
                if (MaterialDelivered(entry.thingDef) < entry.count) return false;
            }
            return true;
        }

        /// <summary>Records a delivered stack (RimWorld: <c>Frame.resourceContainer</c>/<c>Notify_ThingAdded</c>,
        /// trimmed to a plain per-Def tally — no per-item hit-point/quality averaging exists to track yet).</summary>
        public void AddMaterial(ThingDef material, int count)
        {
            if (count <= 0) return;
            resourceContainer[material] = MaterialDelivered(material) + count;
        }

        public override void SpawnSetup(Map.Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            // Frame.def.category is ThingCategory.Frame, not Building, so Thing.SpawnSetup's own
            // def.IsEdifice check (which only ever looks at ThingCategory.Building) never fires for it;
            // register manually against what the *finished* building would occupy instead.
            if (EntityToBuild.IsEdifice) map.edificeGrid.Register(this);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Map.Map? map = Map;
            if (map != null && EntityToBuild.IsEdifice) map.edificeGrid.DeRegister(this);
            base.DeSpawn(mode);
        }

        /// <summary>Finishes construction: destroys this Frame and spawns the real Building in its place
        /// (RimWorld: <c>Frame.CompleteConstruction</c>).</summary>
        public Things.Thing CompleteConstruction(Pawn worker)
        {
            Map.Map map = Map ?? throw new InvalidOperationException("Frame is not spawned.");
            IntVec3 pos = Position;
            Rot4 rot = Rotation;
            ThingDef built = EntityToBuild;
            Destroy(DestroyMode.Vanish);

            Things.Thing building = ThingMaker.MakeThing(built);
            GenSpawn.Spawn(building, pos, map, rot);
            worker.skills?.GetSkill(SkillDefOf.Construction)?.Learn(CompletionXp);
            return building;
        }

        /// <summary>
        /// Fails construction: destroys this Frame, refunds <see cref="FailRefundFraction"/> of whatever
        /// materials were delivered as loose stacks, and — matching RimWorld's real behaviour — respawns a
        /// fresh <see cref="Blueprint"/> in its place rather than erasing the player's intent
        /// (RimWorld: <c>Frame.FailConstruction</c>).
        /// </summary>
        public void FailConstruction(Pawn worker)
        {
            Map.Map map = Map ?? throw new InvalidOperationException("Frame is not spawned.");
            IntVec3 pos = Position;
            Rot4 rot = Rotation;
            ThingDef built = EntityToBuild;

            var refunds = new List<(ThingDef material, int count)>();
            foreach (KeyValuePair<ThingDef, int> kv in resourceContainer)
            {
                int refund = (int)(kv.Value * FailRefundFraction);
                if (refund > 0) refunds.Add((kv.Key, refund));
            }

            Destroy(DestroyMode.FailConstruction);

            ThingDef? blueprintDef = GenConstruct.BlueprintDefFor(built);
            if (blueprintDef != null)
            {
                Things.Thing blueprint = ThingMaker.MakeThing(blueprintDef);
                GenSpawn.Spawn(blueprint, pos, map, rot);
            }

            foreach ((ThingDef material, int count) in refunds)
            {
                Things.Thing stack = ThingMaker.MakeThing(material);
                stack.stackCount = count;
                GenSpawn.Spawn(stack, pos, map);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref workDone, "workDone");

            Dictionary<ThingDef, int>? dict = Scribe.mode == LoadSaveMode.Saving ? resourceContainer : null;
            Scribe_Collections.Look(ref dict, "resourceContainer", LookMode.Def, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                resourceContainer.Clear();
                if (dict != null)
                {
                    foreach (KeyValuePair<ThingDef, int> kv in dict) resourceContainer[kv.Key] = kv.Value;
                }
            }
        }
    }
}
