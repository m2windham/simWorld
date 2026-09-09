using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>Data half of <see cref="CompExplosive"/> (RimWorld: <c>RimWorld.CompProperties_Explosive</c>, trimmed to the fields this pass's <see cref="Combat.GenExplosion"/> actually reads — no wick countdown, no gas/fire/spawn-on-detonate, none of which exist in this pass; see that class's own remarks).</summary>
    public class CompProperties_Explosive : CompProperties
    {
        public DamageDef? explosiveDamageType;

        public float explosiveRadius = 3.5f;

        public float damageAmountBase;

        /// <summary>-1 (the default) derives penetration from <see cref="damageAmountBase"/> (RimWorld: <c>GenExplosion.DoExplosion</c>'s own fallback); set explicitly to override it.</summary>
        public float armorPenetrationBase = -1f;

        public bool damageFalloff;

        /// <summary>Detonates when destroyed by lethal damage (RimWorld: <c>CompProperties_Explosive.explodeOnKilled</c>).</summary>
        public bool explodeOnKilled = true;

        public CompProperties_Explosive()
        {
            compClass = typeof(CompExplosive);
        }

        public override System.Collections.Generic.IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (explosiveDamageType == null) yield return "CompProperties_Explosive has no explosiveDamageType.";
            if (explosiveRadius <= 0f) yield return "CompProperties_Explosive.explosiveRadius must be positive.";
        }
    }

    /// <summary>
    /// Detonates once, radially, through <see cref="Combat.GenExplosion"/> (RimWorld: <c>RimWorld.CompExplosive</c>,
    /// trimmed to its detonation core — no wick/countdown delay, no gas/fire/spawn-on-detonate: this pass has
    /// no fuse timer, gas, or fire system to hang those on). Triggered either directly (a <see cref="CompTrap"/>
    /// sharing this Def calls <see cref="Detonate"/> when it springs — RimWorld's own IED pairing of a
    /// trigger with a payload) or by dying (<see cref="PostDestroy"/>, when <see cref="CompProperties_Explosive.explodeOnKilled"/>).
    /// </summary>
    public class CompExplosive : ThingComp
    {
        public bool detonated;

        /// <summary>
        /// Snapshotted every <see cref="PostDeSpawn"/> (i.e. kept current for as long as the parent is
        /// spawned), because <see cref="PostDestroy"/> — the hook that knows <em>why</em> a Thing died —
        /// runs after <c>Thing.DeSpawn</c> has already reset the parent's own <c>Position</c>/<c>Map</c> to
        /// nothing (RimWorld: real <c>CompExplosive.Detonate</c> is a plain instance method called before
        /// any of that unwinds, so it never needs to remember its own position — this comp's auto-detonate
        /// path is the one place that ordering bites).
        /// </summary>
        private Map.Map? lastMap;

        private Map.IntVec3 lastPosition;

        public CompProperties_Explosive Properties => (CompProperties_Explosive)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            lastMap = parent.Map;
            lastPosition = parent.Position;
        }

        public override void PostDeSpawn(Map.Map map)
        {
            lastMap = map;
            lastPosition = parent.Position;
            base.PostDeSpawn(map);
        }

        /// <summary>Blows up at the parent's last known position. A no-op once already detonated, or off any map.</summary>
        public void Detonate(Thing? instigator = null)
        {
            if (detonated) return;
            detonated = true;

            if (parent.Spawned)
            {
                lastMap = parent.Map;
                lastPosition = parent.Position;
            }
            if (lastMap != null)
            {
                Combat.GenExplosion.DoExplosion(
                    lastPosition, lastMap, Properties.explosiveRadius, Properties.explosiveDamageType!,
                    instigator ?? parent, Properties.damageAmountBase, Properties.armorPenetrationBase, Properties.damageFalloff);
            }
            if (parent.Spawned && !parent.Destroyed) parent.Destroy(DestroyMode.KillFinalize);
        }

        public override void PostDestroy(DestroyMode mode, Map.Map? previousMap)
        {
            if (!detonated && mode == DestroyMode.KillFinalize && Properties.explodeOnKilled)
            {
                detonated = true;
                if (lastMap != null)
                {
                    Combat.GenExplosion.DoExplosion(
                        lastPosition, lastMap, Properties.explosiveRadius, Properties.explosiveDamageType!,
                        parent, Properties.damageAmountBase, Properties.armorPenetrationBase, Properties.damageFalloff);
                }
            }
            base.PostDestroy(mode, previousMap);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref detonated, "detonated", false);
        }
    }
}
