using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Combat
{
    /// <summary>
    /// Something that can soak up damage aimed at a pawn: a worn apparel layer once apparel exists, or the
    /// pawn's own hide (RimWorld: apparel's <c>ArmorUtility</c> loop plus the race's natural armor stat).
    /// </summary>
    public interface IArmorSource
    {
        /// <summary>Base armor rating against <paramref name="armorStat"/> (an <c>ArmorRating_*</c> StatDef) for a hit on <paramref name="part"/>.</summary>
        float ArmorRating(StatDef armorStat, BodyPartRecord part);

        /// <summary>Whether this source protects <paramref name="part"/> at all.</summary>
        bool Covers(BodyPartRecord part);
    }

    /// <summary>
    /// A pawn's own hide, read straight off its ThingDef's <c>statBases</c> (RimWorld: the race's
    /// <c>ArmorRating_*</c> stat, before any apparel). Covers every part; reports 0 when the ThingDef sets
    /// nothing, which is every pawn shipped so far.
    /// </summary>
    public sealed class NaturalArmor : IArmorSource
    {
        private readonly Pawn pawn;

        public NaturalArmor(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public float ArmorRating(StatDef armorStat, BodyPartRecord part) => pawn.def.GetStatValueAbstract(armorStat);

        public bool Covers(BodyPartRecord part) => true;
    }

    /// <summary>
    /// Per-pawn armor sources beyond natural hide (RimWorld: <c>Pawn_ApparelTracker.WornApparel</c>).
    /// Keyed by a <see cref="ConditionalWeakTable{Pawn,List}"/> so <c>Pawn</c> itself carries nothing extra
    /// and entries vanish with their pawn. Apparel doesn't exist yet, so this is mainly a hook for tests and
    /// later modules; <see cref="ArmorUtility"/> always appends <see cref="NaturalArmor"/> after whatever is
    /// registered here.
    /// </summary>
    public static class PawnArmor
    {
        private static readonly ConditionalWeakTable<Pawn, List<IArmorSource>> registrations = new ConditionalWeakTable<Pawn, List<IArmorSource>>();

        public static void Register(Pawn pawn, IArmorSource source)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (source == null) throw new ArgumentNullException(nameof(source));
            registrations.GetValue(pawn, _ => new List<IArmorSource>()).Add(source);
        }

        public static bool Unregister(Pawn pawn, IArmorSource source)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            return registrations.TryGetValue(pawn, out List<IArmorSource> list) && list.Remove(source);
        }

        public static void Clear(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (registrations.TryGetValue(pawn, out List<IArmorSource> list)) list.Clear();
        }

        /// <summary>Registered sources outermost-first, with natural armor appended last (innermost).</summary>
        public static IReadOnlyList<IArmorSource> GetSources(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            var result = new List<IArmorSource>();
            if (registrations.TryGetValue(pawn, out List<IArmorSource> list)) result.AddRange(list);
            result.Add(new NaturalArmor(pawn));
            return result;
        }
    }

    /// <summary>
    /// Rolls damage against a pawn's armor (RimWorld: <c>RimWorld.ArmorUtility</c>). Every layer that covers
    /// the hit part gets one roll: below half its (rating - penetration) the hit is deflected outright;
    /// below the full value it is halved and, if it was Sharp, converted to Blunt; otherwise it passes
    /// through unchanged. A pawn with no armor sources at all (rating 0 everywhere) never rolls and the
    /// damage passes through byte-for-byte, so <see cref="HealthTests"/>'s no-armor hits are unaffected.
    /// </summary>
    public static class ArmorUtility
    {
        /// <summary>
        /// Applies every armor source covering <paramref name="part"/>, outermost first, mutating
        /// <paramref name="damageDef"/> in place if a Sharp hit is diminished to Blunt.
        /// </summary>
        public static float GetPostArmorDamage(Pawn pawn, float amount, float armorPenetration, BodyPartRecord part, ref DamageDef damageDef, out bool deflected, out bool diminished)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (part == null) throw new ArgumentNullException(nameof(part));
            if (damageDef == null) throw new ArgumentNullException(nameof(damageDef));

            deflected = false;
            diminished = false;
            if (damageDef.armorCategory == null) return amount;
            StatDef armorStat = damageDef.armorCategory.armorRatingStat;

            IReadOnlyList<IArmorSource> sources = PawnArmor.GetSources(pawn);
            for (int i = 0; i < sources.Count; i++)
            {
                IArmorSource source = sources[i];
                if (!source.Covers(part)) continue;
                float rating = source.ArmorRating(armorStat, part);
                if (rating <= 0f) continue; // no roll: keeps a no-armor pawn's Rand sequence untouched

                DamageDef before = damageDef;
                ApplyArmor(ref amount, armorPenetration, rating, null, ref damageDef, pawn, out _);
                if (!ReferenceEquals(damageDef, before)) diminished = true;
                if (amount <= 0f)
                {
                    deflected = true;
                    return 0f;
                }
            }
            return amount;
        }

        /// <summary>
        /// One armor roll (RimWorld: <c>ArmorUtility.ApplyArmor</c>): <c>num = max(rating - penetration, 0)</c>;
        /// below <c>num * 0.5</c> the hit is deflected; below <c>num</c> it is halved (rounded randomly) and a
        /// Sharp hit becomes Blunt; otherwise nothing changes. <paramref name="armorThing"/> stands in for the
        /// worn apparel item (durability damage skipped: no Thing/apparel module yet, so it is always null).
        /// </summary>
        public static void ApplyArmor(ref float damAmount, float armorPenetration, float armorRating, object? armorThing, ref DamageDef damageDef, Pawn pawn, out bool metalArmor)
        {
            if (damageDef == null) throw new ArgumentNullException(nameof(damageDef));
            metalArmor = false;

            float num = Math.Max(armorRating - armorPenetration, 0f);
            float roll = Rand.Value;
            if (roll < num * 0.5f)
            {
                damAmount = 0f;
            }
            else if (roll < num)
            {
                damAmount = GenMath.RoundRandom(damAmount / 2f, Rand.Current);
                if (damageDef.armorCategory == DamageArmorCategoryDefOf.Sharp)
                {
                    damageDef = DamageDefOf.Blunt;
                }
            }
        }
    }
}
