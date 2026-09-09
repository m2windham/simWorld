using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Combat
{
    /// <summary>
    /// Radial explosion damage (RimWorld: <c>Verse.GenExplosion</c> + <c>Verse.Explosion</c> +
    /// <c>RimWorld.DamageWorker_AddInjury.ExplosionCellsToHit/ExplosionAffectCell</c>). Ported down to its
    /// mechanical core: which cells the blast actually reaches, and how hard each one gets hit — every
    /// cosmetic RimWorld layers on top (fire, gas, spawned filth/rubble, screen shake, sound) is out of
    /// scope, since none of those systems exist in this pass either.
    /// </summary>
    public static class GenExplosion
    {
        /// <summary>
        /// Damage at the blast radius as a fraction of damage at the epicenter, when <c>damageFalloff</c> is
        /// set (RimWorld: <c>Verse.Explosion.DamageFactorAtEdge</c>).
        /// </summary>
        public const float DamageFactorAtEdge = 0.2f;

        /// <summary>
        /// Armor penetration derived from damage amount when none is given explicitly (RimWorld:
        /// <c>Verse.GenExplosion.DoExplosion</c>'s own <c>armorPenetration = damAmount * 0.015f</c> fallback).
        /// </summary>
        public const float ArmorPenetrationFromDamageAmountFactor = 0.015f;

        /// <summary>
        /// Detonates <paramref name="damageDef"/> at <paramref name="center"/>: every cell <see cref="ExplosionCellsToHit"/>
        /// reaches takes one hit, falling off with distance when <paramref name="damageFalloff"/> is set
        /// (RimWorld: <c>Explosion.GetDamageAmountAt</c>/<c>GetArmorPenetrationAt</c>, lerping linearly from
        /// the full amount at the center to <see cref="DamageFactorAtEdge"/> of it at the edge). A pawn hit
        /// goes through the ordinary <c>DamageDef.Worker</c> pipeline; anything else takes the generic
        /// hit-points path (RimWorld: the same Pawn/Thing split <see cref="Building.RoofCollapseUtility"/>
        /// already uses for collapse damage).
        /// </summary>
        public static void DoExplosion(Map.IntVec3 center, Map.Map map, float radius, DamageDef damageDef, object? instigator, float damageAmountBase, float armorPenetrationBase = -1f, bool damageFalloff = false)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (damageDef == null) throw new ArgumentNullException(nameof(damageDef));
            if (radius <= 0f) return;

            float armorPenetration = armorPenetrationBase >= 0f ? armorPenetrationBase : damageAmountBase * ArmorPenetrationFromDamageAmountFactor;
            List<Map.IntVec3> cells = ExplosionCellsToHit(center, map, radius);

            for (int i = 0; i < cells.Count; i++)
            {
                Map.IntVec3 c = cells[i];
                float amount = damageFalloff ? FalloffAt(damageAmountBase, center, c, radius) : damageAmountBase;
                float pen = damageFalloff ? FalloffAt(armorPenetration, center, c, radius) : armorPenetration;
                var dinfo = new DamageInfo(damageDef, amount, pen, instigator);

                // Copy first: applying damage can destroy/despawn a Thing, mutating the very list ThingsListAt returns.
                var here = new List<Thing>(map.thingGrid.ThingsListAt(c));
                for (int t = 0; t < here.Count; t++)
                {
                    ApplyTo(here[t], dinfo, damageDef);
                }
            }
        }

        private static void ApplyTo(Thing thing, DamageInfo dinfo, DamageDef damageDef)
        {
            if (thing.def.category == ThingCategory.Mote || thing.def.category == ThingCategory.Ethereal) return;
            if (thing is Pawn pawn)
            {
                if (pawn.Dead) return;
                damageDef.Worker.Apply(dinfo, pawn);
            }
            else
            {
                thing.TakeDamage(dinfo);
            }
        }

        /// <summary>Linear falloff from <paramref name="baseAmount"/> at the center to <see cref="DamageFactorAtEdge"/> of it at the edge, floored at 1 (RimWorld: <c>Explosion.GetDamageAmountAt</c>).</summary>
        private static float FalloffAt(float baseAmount, Map.IntVec3 center, Map.IntVec3 c, float radius)
        {
            float t = c.DistanceTo(center) / radius;
            float lerped = GenMath.Lerp(baseAmount, baseAmount * DamageFactorAtEdge, t);
            return Math.Max(GenMath.RoundRandom(lerped, Rand.Current), 1);
        }

        /// <summary>
        /// Every cell the blast reaches (RimWorld: <c>DamageWorker_AddInjury.ExplosionCellsToHit</c>,
        /// <c>affectedAngle</c>/<c>needLOSToCell*</c> narrowing dropped — nothing in this pass throws a
        /// directional or two-source explosion). A cell counts only with <see cref="Map.GenSight.LineOfSight"/>
        /// from <paramref name="center"/> — the epicenter's own cell is never checked against itself
        /// (<c>skipFirstCell: true</c>), so a wall standing between the blast and a cell shields it
        /// entirely, never merely dampened. A wall that itself faces the blast is still added once, from
        /// its walkable open-cell neighbour, so it takes the hit that stops the blast rather than escaping
        /// damage altogether (RimWorld: the <c>adjWallCells</c> pass).
        /// </summary>
        public static List<Map.IntVec3> ExplosionCellsToHit(Map.IntVec3 center, Map.Map map, float radius)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            var open = new List<Map.IntVec3>();
            var openSet = new HashSet<Map.IntVec3>();
            int count = Map.GenRadial.NumCellsInRadius(radius);
            IReadOnlyList<Map.IntVec3> pattern = Map.GenRadial.RadialPattern;
            for (int i = 0; i < count; i++)
            {
                Map.IntVec3 c = center + pattern[i];
                if (!Map.GenGrid.InBounds(c, map)) continue;
                if (!Map.GenSight.LineOfSight(center, c, map, skipFirstCell: true)) continue;
                if (openSet.Add(c)) open.Add(c);
            }

            var wallCells = new List<Map.IntVec3>();
            var wallSet = new HashSet<Map.IntVec3>();
            for (int i = 0; i < open.Count; i++)
            {
                Map.IntVec3 oc = open[i];
                if (!Map.GenGrid.Walkable(oc, map)) continue; // a wall reached by LOS doesn't itself propagate further.

                for (int k = 0; k < Map.GenAdj.CardinalDirections.Length; k++)
                {
                    Map.IntVec3 nb = oc + Map.GenAdj.CardinalDirections[k];
                    if (!nb.InHorDistOf(center, radius)) continue;
                    if (!Map.GenGrid.InBounds(nb, map)) continue;
                    if (Map.GenGrid.Standable(nb, map)) continue; // only a blocking neighbour (a wall) needs adding this way.
                    if (Map.GenGrid.GetEdifice(nb, map) == null) continue;
                    if (openSet.Contains(nb) || wallSet.Contains(nb)) continue;
                    wallCells.Add(nb);
                    wallSet.Add(nb);
                }
            }

            open.AddRange(wallCells);
            return open;
        }
    }
}
