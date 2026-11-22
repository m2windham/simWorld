namespace SimWorld.Defs
{
    public partial class ThingDef
    {
        /// <summary>
        /// SimWorld: a wall dug into the ground rather than standing in the air, a pit house's earthen bank. The
        /// room temperature model (<c>Building.RoomTracker</c>) samples the far side of such a wall as the deep
        /// earth, under RimWorld's own deep-equalization rule (toward 15 C, and only while the room is warmer),
        /// instead of as the outdoor air beyond it.
        /// <para/>
        /// <b>Translation.</b> RimWorld's only earth-sheltered room is one carved out of a mountain: thick rock
        /// overhead pulls it toward 15 C, and its rock walls are sampled as half outdoor air because nothing but
        /// more rock lies beyond them. A pit house is dug, so the earth is beside it as well as above it; this
        /// carries RimWorld's deep pull to the sides. Its own file, so <c>ThingDef</c>'s shared parts are not edited.
        /// </summary>
        public bool earthBanked;
    }
}
