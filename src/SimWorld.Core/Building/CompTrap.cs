using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Building
{
    /// <summary>Data half of <see cref="CompTrap"/> (RimWorld: fields real RimWorld keeps on <c>Verse.BuildingProperties</c> — <c>trapDestroyOnSpring</c> et al — since <c>Building_Trap</c> there is a distinct <c>Thing</c> subclass, not a comp; see that class's own remarks for why this port makes it one).</summary>
    public class CompProperties_Trap : CompProperties
    {
        /// <summary>What the spring deals — required; a trap with no damageDef is a content error.</summary>
        public DamageDef? damageDef;

        /// <summary>
        /// Damage rolled per spring. Unsourced: real RimWorld's spike trap damage is drawn per-material from
        /// a <c>ThingDef</c>-keyed table this sandbox has no decompiled source for (see this module's
        /// report); this port's own flat range instead, pinned by a test on the trend (higher armor takes
        /// less of it through), never on the literal.
        /// </summary>
        public FloatRange damageAmountRange = new FloatRange(8f, 20f);

        public float armorPenetrationBase = 0.2f;

        public CompProperties_Trap()
        {
            compClass = typeof(CompTrap);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (damageDef == null) yield return "CompProperties_Trap has no damageDef.";
        }
    }

    /// <summary>
    /// Armed, triggers once, then spent (RimWorld: <c>Verse.Building_Trap</c>, heavily trimmed — see below).
    /// <b>Deviation:</b> real RimWorld's trigger rule is an elaborate per-pawn "does this pawn know the trap
    /// is here" memory (faction, prior sightings, lords avoiding known traps, a small chance even for a
    /// stranger) with a rearm command once player-owned. None of that — mental state, lords, memory of
    /// terrain — exists in this pass to hang it on, so this comp keeps only the one distinction that
    /// actually matters for "traps hurt enemies, not you": it never springs on a pawn sharing its own
    /// <see cref="Faction"/>. Every other pawn springs it with certainty on first entry, and once sprung it
    /// stays disarmed — re-arming is a Work-system job (RimWorld: <c>JobDriver_RearmTrap</c>) this pass does
    /// not build (see this module's report).
    /// </summary>
    public class CompTrap : Things.ThingComp
    {
        public bool armed = true;

        /// <summary>The faction this trap is defending (never springs on that faction's own pawns); null springs on anyone. See <see cref="CompTurretGun.Faction"/>'s own remarks for why this lives on the comp rather than on the Thing.</summary>
        public Factions.Faction? Faction;

        public CompProperties_Trap Properties => (CompProperties_Trap)props;

        public override void CompTick()
        {
            if (!armed) return;
            Map.Map? map = parent.Map;
            if (map == null) return;

            IReadOnlyList<Things.Thing> here = map.thingGrid.ThingsListAt(parent.Position);
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i] is Pawn pawn && !pawn.Dead)
                {
                    TrySpring(pawn);
                    if (!armed) return;
                }
            }
        }

        private void TrySpring(Pawn p)
        {
            if (Faction != null && ReferenceEquals(p.faction, Faction)) return;

            armed = false;

            // An explosive trap (a sibling CompExplosive on the same Def — an "IED", RimWorld's own pairing
            // of a trigger with a payload) detonates instead of dealing its own direct hit.
            CompExplosive? explosive = parent.GetComp<CompExplosive>();
            if (explosive != null)
            {
                explosive.Detonate(p);
                return;
            }

            DamageDef damageDef = Properties.damageDef!;
            BodyPartRecord? part = p.health.hediffSet.GetRandomNotMissingPart(damageDef, BodyPartHeight.Undefined, BodyPartDepth.Undefined, Rand.Current);
            float amount = Rand.Range(Properties.damageAmountRange);
            var dinfo = new DamageInfo(damageDef, amount, Properties.armorPenetrationBase, parent, part);
            damageDef.Worker.Apply(dinfo, p);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref armed, "armed", true);
            Factions.Faction? f = Faction;
            Scribe_References.Look(ref f, "trapFaction");
            Faction = f;
        }
    }
}
