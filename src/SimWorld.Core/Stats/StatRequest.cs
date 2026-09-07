using System;
using SimWorld.Defs;
using SimWorld.Things;

namespace SimWorld.Stats
{
    /// <summary>
    /// Addresses one stat lookup: either a live <see cref="Things.Thing"/>, or an abstract
    /// <c>(def, stuff)</c> pair with no Thing at all (RimWorld: <c>RimWorld.StatRequest</c>, which addresses a
    /// <c>BuildableDef</c> — the base of <c>ThingDef</c> and <c>TerrainDef</c>). This port has no
    /// <c>BuildableDef</c> base yet (<c>TerrainDef</c> carries no stats and nothing needs it to), so the
    /// abstract mode narrows to <see cref="Defs.ThingDef"/>; widen this the day a stat-bearing TerrainDef shows up.
    /// </summary>
    public readonly struct StatRequest : IEquatable<StatRequest>
    {
        private readonly Thing? thing;
        private readonly ThingDef? def;
        private readonly ThingDef? stuffDef;

        private StatRequest(Thing? thing, ThingDef? def, ThingDef? stuffDef)
        {
            this.thing = thing;
            this.def = def;
            this.stuffDef = stuffDef;
        }

        /// <summary>A request for a live Thing; its own def answers <see cref="Def"/>. RimWorld's Thing also
        /// carries its own "made from stuff" material — this port's <see cref="Things.Thing"/> does not yet
        /// (no ThingOwner/apparel), so <see cref="StuffDef"/> is always null for a Thing-based request.</summary>
        public static StatRequest For(Thing thing)
        {
            if (thing == null) throw new ArgumentNullException(nameof(thing));
            return new StatRequest(thing, thing.def, null);
        }

        /// <summary>A request for a def with no live Thing — e.g. previewing a stat for a def/stuff combination
        /// nothing has been built from yet (RimWorld: <c>StatRequest.For(BuildableDef, ThingDef stuffDef)</c>).</summary>
        public static StatRequest For(ThingDef def, ThingDef? stuff = null)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            return new StatRequest(null, def, stuff);
        }

        public Thing? Thing => thing;

        public bool HasThing => thing != null;

        /// <summary>The def this request resolves stats against: the Thing's own def, or the abstract def.</summary>
        public ThingDef? Def => def;

        /// <summary>Material this request prices/rates as built from, if any.</summary>
        public ThingDef? StuffDef => stuffDef;

        public bool Equals(StatRequest other) =>
            ReferenceEquals(thing, other.thing) && ReferenceEquals(def, other.def) && ReferenceEquals(stuffDef, other.stuffDef);

        public override bool Equals(object? obj) => obj is StatRequest other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = thing?.GetHashCode() ?? 0;
                hash = (hash * 397) ^ (def?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ (stuffDef?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public override string ToString() => HasThing ? "Thing=" + thing : "Def=" + (def?.defName ?? "null") + ", Stuff=" + (stuffDef?.defName ?? "null");
    }
}
