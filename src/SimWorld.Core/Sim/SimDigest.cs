using System;
using System.Globalization;
using SimWorld.Things;

namespace SimWorld.Sim
{
    /// <summary>
    /// A one-line fingerprint of a running simulation, for finding the first tick at which two runs that should
    /// agree stop agreeing: the bench and the Unity host on the same seed, say, which share a core commit but not
    /// a .NET runtime. Same commit, same seed and same inputs give the same line on every tick, so the first line
    /// that differs bounds the divergence to the ticks since the last one that matched.
    /// <para/>
    /// It reads and never writes: no draw from <see cref="Rand"/>, no cache filled, nothing a later tick could see.
    /// The fields are the ambient stream's draw count (any branch taken differently shows here first), and a hash
    /// over every thing on the map in its lister order: def, position, rotation, stack count and hit points.
    /// Strings are hashed by their characters, never by <see cref="string.GetHashCode()"/>, which differs between
    /// processes and runtimes.
    /// </summary>
    public static class SimDigest
    {
        public static string Line(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            ulong hash = 14695981039346656037UL;
            var things = map.listerThings.AllThings;
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                hash = Mix(hash, t.def.defName);
                hash = Mix(hash, t.Position.x);
                hash = Mix(hash, t.Position.z);
                hash = Mix(hash, t.Rotation.AsInt);
                hash = Mix(hash, t.stackCount);
                hash = Mix(hash, t.HitPoints);
            }
            return "tick=" + Find.TickManager.TicksGame.ToString(CultureInfo.InvariantCulture)
                + " rand=" + Rand.Current.Iterations.ToString(CultureInfo.InvariantCulture)
                + " things=" + things.Count.ToString(CultureInfo.InvariantCulture)
                + " hash=" + hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        private static ulong Mix(ulong hash, int value)
        {
            unchecked
            {
                for (int b = 0; b < 4; b++)
                {
                    hash ^= (byte)(value >> (8 * b));
                    hash *= 1099511628211UL;
                }
                return hash;
            }
        }

        private static ulong Mix(ulong hash, string value)
        {
            for (int i = 0; i < value.Length; i++) hash = Mix(hash, value[i]);
            return Mix(hash, value.Length);
        }
    }
}
