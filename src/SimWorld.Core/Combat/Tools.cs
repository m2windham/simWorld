using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Health;

namespace SimWorld.Combat
{
    /// <summary>What kind of blow a body part or weapon tool can deal (RimWorld: <c>Verse.ToolCapacityDef</c>): Blunt, Cut, Stab, Poke, Scratch, Bite.</summary>
    public class ToolCapacityDef : Def
    {
    }

    /// <summary>
    /// One way a weapon (or a natural body part) can strike (RimWorld: <c>Verse.Tool</c>). A weapon lists
    /// several — a knife has a Cut edge and a Stab point — and <see cref="ManeuverUtility.FindManeuver"/>
    /// matches each to the <see cref="ManeuverDef"/> that turns it into an attack.
    /// </summary>
    public class Tool
    {
        public string? label;
        public List<ToolCapacityDef>? capacities;
        public float power;
        public float cooldownTime = 2f;

        /// <summary>Explicit per-tool value; RimWorld's power-derived fallback is skipped, so content always sets this.</summary>
        public float armorPenetration;

        public float chanceFactor = 1f;
        public BodyPartGroupDef? linkedBodyPartsGroup;

        public bool HasCapacity(ToolCapacityDef capacity) => capacity != null && capacities != null && capacities.Contains(capacity);

        public IEnumerable<string> ConfigErrors()
        {
            if (capacities == null || capacities.Count == 0) yield return "tool '" + (label ?? "?") + "' has no capacities.";
            if (power <= 0f) yield return "tool '" + (label ?? "?") + "' power must be positive.";
        }
    }

    /// <summary>
    /// Links a <see cref="ToolCapacityDef"/> to the attack it produces (RimWorld: <c>Verse.ManeuverDef</c>).
    /// <see cref="verb"/> is a template: <see cref="MeleeVerbUtility.MakeVerb"/> clones it per tool, filling in
    /// that tool's power, penetration and cooldown.
    /// </summary>
    public class ManeuverDef : Def
    {
        public ToolCapacityDef requiredCapacity = null!;
        public VerbProperties verb = null!;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (requiredCapacity == null) yield return "maneuver has no requiredCapacity.";
            if (verb == null) yield return "maneuver has no verb.";
            else if (verb.meleeDamageDef == null) yield return "maneuver verb has no meleeDamageDef.";
        }
    }

    /// <summary>Matches a <see cref="Tool"/> to the <see cref="ManeuverDef"/> that knows how to swing it (RimWorld: <c>Verse.VerbUtility</c> + <c>Pawn_MeleeVerbs</c>, simplified to one maneuver per tool).</summary>
    public static class ManeuverUtility
    {
        public static ManeuverDef? FindManeuver(Tool tool)
        {
            if (tool?.capacities == null) return null;
            IReadOnlyList<ManeuverDef> maneuvers = DefDatabase<ManeuverDef>.AllDefsListForReading;
            for (int i = 0; i < maneuvers.Count; i++)
            {
                if (tool.capacities.Contains(maneuvers[i].requiredCapacity)) return maneuvers[i];
            }
            return null;
        }
    }

    /// <summary>Builds the <see cref="Verb_MeleeAttack"/> a pawn uses to swing one of its weapon's <see cref="Tool"/>s.</summary>
    public static class MeleeVerbUtility
    {
        public static Verb_MeleeAttack? MakeVerb(Pawns.Pawn caster, Tool tool)
        {
            if (caster == null) throw new ArgumentNullException(nameof(caster));
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            ManeuverDef? maneuver = ManeuverUtility.FindManeuver(tool);
            if (maneuver == null) return null;

            var props = new VerbProperties
            {
                verbClass = typeof(Verb_MeleeAttack),
                label = maneuver.verb.label,
                meleeDamageDef = maneuver.verb.meleeDamageDef,
                meleeDamageBaseAmount = tool.power,
                meleeArmorPenetrationBase = tool.armorPenetration,
                warmupTime = 0f,
                defaultCooldownTime = tool.cooldownTime,
            };
            return new Verb_MeleeAttack(caster, props, tool);
        }
    }
}
