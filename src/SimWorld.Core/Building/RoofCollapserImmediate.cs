using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// Drops a roof into the cells it was holding up and hurts whatever was under it (RimWorld:
    /// <c>Verse.RoofCollapserImmediate</c>). Split out of <see cref="RoofCollapseUtility"/>, which in RimWorld
    /// answers only "is this cell held up" — the three concerns (support query, which cells fall, what
    /// falling does) are three types there and are three files here.
    ///
    /// <para/><b>Two collapses, not one, and the difference is the whole of it.</b> RimWorld keys the branch
    /// on <c>roofDef.collapseLeavingThingDef != null &amp;&amp; collapseLeavingThingDef.passability ==
    /// Impassable</c> — a roof that refills the cell it fell into with solid rock:
    /// <list type="number">
    /// <item><b>Overhead mountain</b> (the only shipped roof that does) is <i>certain death</i>: 99999 Crush
    /// at 999 armour penetration, aimed at the brain for a pawn, applied twice, followed by an outright kill
    /// for anything still standing. Then <c>CollapsedRocks</c> fills the cell and the roof <i>stays</i>
    /// (<c>RoofDef.VanishOnCollapse</c> is <c>!isThickRoof</c>), so the new rock holds that roof — and its
    /// neighbours' — up again, and the collapse cannot walk outward across a mountain.</item>
    /// <item><b>Every other roof</b> — constructed, and thin rock — is a knock on the head:
    /// <see cref="ThinRoofCrushDamageRange"/> (15–30) of Crush aimed at
    /// <see cref="BodyPartHeight.Top"/>/<see cref="BodyPartDepth.Outside"/>, and the roof vanishes.
    /// A healthy colonist walks away from it.</item>
    /// </list>
    ///
    /// <para/><b>What this port did before, and what each divergence cost.</b> One branch, 50 Blunt (100 under
    /// thick rock), no body region, no leavings, and the roof removed either way:
    /// <list type="bullet">
    /// <item><b>The DamageDef was wrong</b> — <c>Blunt</c>, whose <c>deathMessage</c> is "{0} has been beaten
    /// to death", so a settlement crushed by its own ceiling read as a settlement murdering itself. That
    /// misreading is written down twice in <c>docs/WORK-REGISTER.md</c> (§9a's premise, corrected in §10).</item>
    /// <item><b>No body region was set</b>, so <c>DamageWorker_AddInjury</c> drew a random part by coverage
    /// over the <i>whole</i> body — inside parts included. A 50-severity hit that lands on a heart destroys
    /// it. That is §10's reported signature exactly ("a destroyed heart, brain or liver on a corpse whose
    /// total injury severity is near zero"): not a big injury, one unlucky part. RimWorld's thin-roof hit
    /// <i>cannot</i> reach an organ — Top/Outside is scalp and shoulders.</item>
    /// <item><b>The thin-roof number was 50 against RimWorld's 15–30</b>, and flat rather than rolled.</item>
    /// <item><b>Nothing was left behind and the thick roof was removed</b>, so an overhead-mountain collapse
    /// permanently enlarged the unsupported area instead of shoring it up — every later collapse started from
    /// a wider hole than the last.</item>
    /// </list>
    ///
    /// <para/><b>Deliberate deviation, and where it should go instead.</b> RimWorld carries
    /// <c>RoofDef.collapseLeavingThingDef</c> and <c>RoofDef.VanishOnCollapse</c>; this port's
    /// <c>Map.RoofDef</c> carries neither, and <c>Map/</c> belongs to another lane in this batch, so both are
    /// expressed here off <see cref="RoofDef.isThickRoof"/> — which is exactly what RimWorld's own
    /// <c>VanishOnCollapse =&gt; !isThickRoof</c> does, and which picks out the same single shipped roof
    /// (<c>RoofRockThick</c>) that names <c>CollapsedRocks</c> there. Behaviour is 1:1; the two fields should
    /// move onto <c>RoofDef</c> and into <c>Roofs.xml</c> when that path is free.
    /// </summary>
    public static class RoofCollapserImmediate
    {
        /// <summary>
        /// Crush damage an ordinary (non-mountain) roof does to each thing it lands on — RimWorld's own
        /// <c>RoofCollapserImmediate.ThinRoofCrushDamageRange</c>, sourced from decompiled 1.6 source.
        /// Rolled per thing, not per collapse.
        /// </summary>
        public static readonly IntRange ThinRoofCrushDamageRange = new IntRange(15, 30);

        /// <summary>Damage a mountain uses to make sure (RimWorld's literal, with its 999 armour penetration).
        /// It is not a number to be survived and is not treated as one — see <see cref="MakeSure"/>.</summary>
        public const float ThickRoofCrushDamage = 99999f;

        /// <summary>Armour penetration on the mountain's hit; nothing in this port's armour model resists it.</summary>
        public const float ThickRoofArmorPenetration = 999f;

        /// <summary>Drops the roof in one cell, if it has one.</summary>
        public static void DropRoofInCells(IntVec3 c, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            DropRoofInCells(new List<IntVec3> { c }, map);
        }

        /// <summary>
        /// Drops the roof in every cell in <paramref name="cells"/>, in RimWorld's own two passes: every cell
        /// is damaged first, then every cell's roof is resolved. The order matters — phase two can spawn an
        /// edifice (<c>CollapsedRocks</c>) into a cell, and doing that before the neighbouring cells have
        /// dealt their damage would have the new rock absorb a hit meant for what was standing there.
        /// </summary>
        public static void DropRoofInCells(List<IntVec3> cells, Map.Map map)
        {
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (cells.Count == 0) return;

            for (int i = 0; i < cells.Count; i++)
            {
                if (GenGrid.InBounds(cells[i], map) && map.roofGrid.Roofed(cells[i])) DropRoofInCellPhaseOne(cells[i], map);
            }
            for (int i = 0; i < cells.Count; i++)
            {
                if (GenGrid.InBounds(cells[i], map) && map.roofGrid.Roofed(cells[i])) DropRoofInCellPhaseTwo(cells[i], map);
            }

            // Room's cached anyCellUnroofed/temperature tracking is only invalidated by an edifice spawning or
            // despawning (see RoomTracker's own remarks) — a roof-only change like this one needs the same
            // nudge, or a room that just lost its roof would keep equalising as if it still had one.
            map.roomTracker.Notify_Dirty();
        }

        /// <summary>Everything in the cell takes the hit (RimWorld: <c>DropRoofInCellPhaseOne</c>).</summary>
        private static void DropRoofInCellPhaseOne(IntVec3 c, Map.Map map)
        {
            RoofDef? roof = map.roofGrid.RoofAt(c);
            if (roof == null) return;

            if (LeavesImpassableRock(roof))
            {
                // RimWorld runs this loop twice, and the second pass is not belt-and-braces: killing a pawn
                // puts a Corpse in the cell that was not in the list the first pass took, and a mountain
                // leaves no body to find either. The list is re-read each pass for exactly that reason.
                for (int pass = 0; pass < 2; pass++)
                {
                    foreach (Thing thing in ThingsUnder(c, map)) CrushUnderMountain(thing);
                }
            }
            else
            {
                foreach (Thing thing in ThingsUnder(c, map)) CrushUnderRoof(thing);
            }
        }

        /// <summary>
        /// The roof itself resolves (RimWorld: <c>DropRoofInCellPhaseTwo</c>). An ordinary roof vanishes; a
        /// mountain's does not, and instead the cell fills with rock that holds it.
        /// </summary>
        private static void DropRoofInCellPhaseTwo(IntVec3 c, Map.Map map)
        {
            RoofDef? roof = map.roofGrid.RoofAt(c);
            if (roof == null) return;

            if (!LeavesImpassableRock(roof))
            {
                map.roofGrid.SetRoof(c, null);
                return;
            }

            // The roof stays (RoofDef.VanishOnCollapse is !isThickRoof) and the cell becomes solid again —
            // unless something is already occupying the edifice slot, which phase one has just done its best
            // to clear but cannot promise for an indestructible one.
            if (map.edificeGrid[c] != null) return;
            Thing rocks = ThingMaker.MakeThing(RoofCollapseDefOf.CollapsedRocks);
            GenSpawn.Spawn(rocks, c, map);
        }

        /// <summary>
        /// RimWorld's branch, expressed off the one field this port's <see cref="RoofDef"/> has:
        /// <c>collapseLeavingThingDef != null &amp;&amp; passability == Impassable</c> is true for exactly the
        /// thick rock roof in shipped content, and <c>RoofDef.VanishOnCollapse</c> is <c>!isThickRoof</c>
        /// outright. See this class's own remarks for why the two fields are not on the Def here.
        /// </summary>
        private static bool LeavesImpassableRock(RoofDef roof) => roof.isThickRoof;

        /// <summary>A copy, because damage can destroy a Thing, which deregisters it from the very list
        /// <c>ThingsListAt</c> returns.</summary>
        private static List<Thing> ThingsUnder(IntVec3 c, Map.Map map) => new List<Thing>(map.thingGrid.ThingsListAt(c));

        /// <summary>
        /// A mountain lands on it. RimWorld aims a pawn's hit at the brain, which is not a damage figure at
        /// all but a statement that this kills — see <see cref="MakeSure"/>, RimWorld's own follow-up
        /// <c>Kill</c> for anything the damage pipeline leaves standing.
        /// </summary>
        private static void CrushUnderMountain(Thing thing)
        {
            if (thing.Destroyed) return;
            if (thing is Pawn pawn)
            {
                if (pawn.Dead) return;
                BodyPartRecord? brain = pawn.health.hediffSet.GetBrain();
                var dinfo = new DamageInfo(RoofCollapseDefOf.Crush, ThickRoofCrushDamage, ThickRoofArmorPenetration, instigator: null, hitPart: brain);
                if (brain == null)
                {
                    // A body with no brain part still dies under a mountain; aim where RimWorld aims a
                    // non-pawn instead of letting the part roll pick an arm.
                    dinfo.Height = BodyPartHeight.Top;
                    dinfo.Depth = BodyPartDepth.Outside;
                }
                // Pawns take damage through the Health module's own DamageWorker pipeline, not
                // Thing.TakeDamage (which only ever runs the generic hit-points path) — matching how
                // Combat.Verb_MeleeAttack already applies damage to a pawn target.
                RoofCollapseDefOf.Crush.Worker.Apply(dinfo, pawn);
                MakeSure(pawn);
                return;
            }

            var thingInfo = new DamageInfo(RoofCollapseDefOf.Crush, ThickRoofCrushDamage, ThickRoofArmorPenetration)
            {
                Height = BodyPartHeight.Top,
                Depth = BodyPartDepth.Outside,
            };
            thing.TakeDamage(thingInfo);
            if (!thing.Destroyed && thing.def.destroyable) thing.Destroy(DestroyMode.KillFinalize);
        }

        /// <summary>
        /// RimWorld's <c>if (!thing.Destroyed &amp;&amp; thing.def.destroyable) thing.Kill(...)</c>, for a
        /// pawn. Nothing walks out from under a mountain, and a body that happens to have no vital part the
        /// aimed hit could destroy must not be the exception.
        /// </summary>
        private static void MakeSure(Pawn pawn)
        {
            if (pawn.Dead || pawn.Destroyed) return;
            pawn.health.Kill(new DamageInfo(RoofCollapseDefOf.Crush, ThickRoofCrushDamage, ThickRoofArmorPenetration), null);
        }

        /// <summary>
        /// An ordinary roof lands on it: one roll of <see cref="ThinRoofCrushDamageRange"/>, aimed at the top
        /// of the body from the outside. RimWorld also scales a building's share by
        /// <c>BuildingProperties.roofCollapseDamageMultiplier</c>, a field this port's
        /// <c>BuildingProperties</c> does not carry; every shipped building would take the default 1 anyway.
        /// </summary>
        private static void CrushUnderRoof(Thing thing)
        {
            if (thing.Destroyed) return;

            ThingCategory category = thing.def.category;
            if (category != ThingCategory.Item && category != ThingCategory.Plant
                && category != ThingCategory.Building && category != ThingCategory.Pawn) return;

            float amount = Rand.Current.Range(ThinRoofCrushDamageRange);
            var dinfo = new DamageInfo(RoofCollapseDefOf.Crush, amount)
            {
                Height = BodyPartHeight.Top,
                Depth = BodyPartDepth.Outside,
            };

            if (thing is Pawn pawn)
            {
                if (pawn.Dead) return;
                RoofCollapseDefOf.Crush.Worker.Apply(dinfo, pawn);
                return;
            }
            thing.TakeDamage(dinfo);
        }
    }
}
