using System;
using System.Collections.Generic;
using SimWorld.Combat;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Things;

namespace SimWorld.Pawns
{
    /// <summary>
    /// One worn apparel piece, as an armor source (RimWorld: apparel's contribution to
    /// <c>Verse.ArmorUtility</c>'s loop). <see cref="Combat.Armor"/>'s own doc comments built
    /// <see cref="IArmorSource"/>/<see cref="PawnArmor"/> as the hook for exactly this; wearing an item
    /// registers one of these, removing it unregisters — nothing in Combat itself changes.
    /// </summary>
    public sealed class ApparelArmorSource : IArmorSource
    {
        private readonly ThingWithComps apparel;

        public ApparelArmorSource(ThingWithComps apparel)
        {
            this.apparel = apparel ?? throw new ArgumentNullException(nameof(apparel));
        }

        public ThingWithComps Apparel => apparel;

        /// <summary>Routed through the stat pipeline exactly like <see cref="NaturalArmor"/>, so this item's
        /// own hediff-equivalent — its quality (<see cref="Stats.StatPart_Quality"/>) and stuff
        /// (<see cref="Things.Thing.Stuff"/>) — already bend the number before it gets here.</summary>
        public float ArmorRating(StatDef armorStat, BodyPartRecord part) => apparel.GetStatValue(armorStat);

        public bool Covers(BodyPartRecord part) => apparel.def.apparel?.CoversBodyPart(part) ?? false;
    }

    /// <summary>
    /// Apparel a pawn is wearing (RimWorld: <c>Verse.Pawn_ApparelTracker</c>). Trimmed to what the rest of the
    /// codebase currently reaches for: a worn list, wear/remove, and armor registration —
    /// <see cref="Generation.PawnApparelGenerator"/> is the only thing populating it today, ahead of a real
    /// job driver for players/AI to dress a pawn by hand.
    /// </summary>
    public sealed class Pawn_ApparelTracker : IExposable
    {
        private readonly Pawn pawn;
        private List<ThingWithComps> worn = new List<ThingWithComps>();
        private readonly Dictionary<ThingWithComps, ApparelArmorSource> armorSources = new Dictionary<ThingWithComps, ApparelArmorSource>();

        /// <summary>Scribe reconstructs this by passing the owning pawn as a ctor arg (see <see cref="Pawn.ExposeData"/>), the same way <see cref="Pawn_EquipmentTracker"/> does.</summary>
        public Pawn_ApparelTracker(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public Pawn Pawn => pawn;

        public IReadOnlyList<ThingWithComps> WornApparel => worn;

        public bool IsWorn(ThingWithComps apparel) => apparel != null && worn.Contains(apparel);

        /// <summary>Whether wearing <paramref name="apparelDef"/> would conflict (same layer, overlapping body
        /// part group) with something already worn (RimWorld: <c>ApparelUtility.CanWearTogether</c> asked of
        /// the whole worn set).</summary>
        public bool WouldConflictWithWorn(ThingDef apparelDef)
        {
            ApparelProperties? candidate = apparelDef?.apparel;
            if (candidate == null) return false;
            for (int i = 0; i < worn.Count; i++)
            {
                ApparelProperties? wornProps = worn[i].def.apparel;
                if (wornProps != null && wornProps.ConflictsWith(candidate)) return true;
            }
            return false;
        }

        /// <summary>Puts <paramref name="apparel"/> on and registers it as an armor source. Does not check
        /// <see cref="WouldConflictWithWorn"/> itself — callers (map ports of the job driver, generation) pick
        /// where a conflict means "unequip the other piece first" versus "don't offer this one."</summary>
        public void Wear(ThingWithComps apparel)
        {
            if (apparel == null) throw new ArgumentNullException(nameof(apparel));
            if (apparel.def.apparel == null) throw new ArgumentException(apparel.def.defName + " has no ApparelProperties.", nameof(apparel));
            worn.Add(apparel);
            RegisterArmor(apparel);
        }

        /// <summary>Takes <paramref name="apparel"/> off and unregisters its armor contribution. Returns false when it wasn't worn.</summary>
        public bool RemoveApparel(ThingWithComps apparel)
        {
            if (apparel == null || !worn.Remove(apparel)) return false;
            UnregisterArmor(apparel);
            return true;
        }

        private void RegisterArmor(ThingWithComps apparel)
        {
            var source = new ApparelArmorSource(apparel);
            armorSources[apparel] = source;
            PawnArmor.Register(pawn, source);
        }

        private void UnregisterArmor(ThingWithComps apparel)
        {
            if (armorSources.TryGetValue(apparel, out ApparelArmorSource? source))
            {
                PawnArmor.Unregister(pawn, source);
                armorSources.Remove(apparel);
            }
        }

        public void ExposeData()
        {
            List<ThingWithComps>? list = worn;
            Scribe_Collections.Look(ref list, "worn", LookMode.Deep);
            worn = list ?? new List<ThingWithComps>();

            // Armor registrations are runtime-only (ConditionalWeakTable-backed in Combat.PawnArmor): rebuild
            // them from the loaded worn list rather than saving/loading anything about them directly.
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                armorSources.Clear();
                for (int i = 0; i < worn.Count; i++)
                {
                    RegisterArmor(worn[i]);
                }
            }
        }
    }
}
