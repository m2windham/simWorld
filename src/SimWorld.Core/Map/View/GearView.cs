using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Things;

namespace SimWorld.Map.View
{
    /// <summary>Where a piece of gear is carried.</summary>
    public enum GearSlot
    {
        /// <summary>The weapon in hand.</summary>
        Weapon,

        /// <summary>Something worn.</summary>
        Apparel,
    }

    /// <summary>
    /// One thing a person carries or wears, as a citizen panel lists it (RimWorld: the Gear tab,
    /// <c>ITab_Pawn_Gear</c>, which lists the equipment and the apparel with each one's label and hit points).
    /// The fourth playtest could not see what anybody carried, so it could not tell whether the bronze it had
    /// just learned had reached anyone's hands: the gear has to say what it is made of and which age it
    /// belongs to, not only its defName. Values and defNames only, like the rest of the view.
    /// </summary>
    public sealed class GearView
    {
        internal GearView(string defName, string label, string? stuffDefName, GearSlot slot, float hitPointsFraction, string? eraDefName, string? eraLabel)
        {
            DefName = defName;
            Label = label;
            StuffDefName = stuffDefName;
            Slot = slot;
            HitPointsFraction = hitPointsFraction;
            EraDefName = eraDefName;
            EraLabel = eraLabel;
        }

        /// <summary>The thing's defName ("MeleeWeapon_Spear").</summary>
        public string DefName { get; }

        /// <summary>In words, with what it is made of where that is chosen: "bronze spear", "leather tunic".</summary>
        public string Label { get; }

        /// <summary>The stuff it was made from, or null for a thing that is not made from stuff.</summary>
        public string? StuffDefName { get; }

        public GearSlot Slot { get; }

        /// <summary>Hit points left, 0-1.</summary>
        public float HitPointsFraction { get; }

        /// <summary>The era it first becomes makeable in (<see cref="EraAvailability.EraOf"/>), or null when
        /// nothing about it is gated by an era (hide, wood, a club).</summary>
        public string? EraDefName { get; }

        public string? EraLabel { get; }

        /// <summary>The weapon first, then what is worn in the order it was put on. Empty for a pawn with
        /// neither tracker (an animal).</summary>
        internal static IReadOnlyList<GearView> Of(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            var gear = new List<GearView>();
            IReadOnlyList<ThingWithComps>? held = pawn.equipment?.AllEquipmentListForReading;
            if (held != null)
            {
                for (int i = 0; i < held.Count; i++) gear.Add(ViewOf(held[i], GearSlot.Weapon));
            }
            IReadOnlyList<ThingWithComps>? worn = pawn.apparel?.WornApparel;
            if (worn != null)
            {
                for (int i = 0; i < worn.Count; i++) gear.Add(ViewOf(worn[i], GearSlot.Apparel));
            }
            return gear;
        }

        private static GearView ViewOf(Thing thing, GearSlot slot)
        {
            string label = thing.def.label ?? thing.def.defName;
            ThingDef? stuff = thing.Stuff;
            if (stuff != null) label = (stuff.label ?? stuff.defName) + " " + label;
            int max = thing.MaxHitPoints;
            float hp = max > 0 ? Math.Clamp(thing.HitPoints / (float)max, 0f, 1f) : 1f;
            EraDef? era = EraAvailability.EraOf(thing.def);
            return new GearView(thing.def.defName, label, stuff?.defName, slot, hp, era?.defName, era?.LabelCap);
        }
    }
}
