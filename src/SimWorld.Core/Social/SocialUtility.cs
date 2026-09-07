using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Thoughts;

namespace SimWorld.Social
{
    /// <summary>
    /// The opinion formula and the small mechanics around it (RimWorld: parts of <c>Pawn_RelationsTracker</c>
    /// and <c>PawnUtility</c>). <see cref="Pawn_RelationsTracker.OpinionOf"/> is the public entry point; this
    /// class holds the composition so that tracker file stays focused on the state it owns.
    /// </summary>
    public static class SocialUtility
    {
        /// <summary>
        /// <paramref name="pawn"/>'s opinion of <paramref name="other"/>: personality traits (the *other*
        /// pawn's, read from its own traits — how it comes across regardless of who is looking), relation type
        /// (family, derived from demography's own ids, plus any stored <see cref="DirectPawnRelation"/>),
        /// social memories specifically about <paramref name="other"/>, and a stable per-pair compatibility
        /// factor (RimWorld's own mechanic: two given people just naturally get on or don't, from a hash of
        /// the pair — see <see cref="CompatibilityFactor"/>). Pure and side-effect free: calling it twice with
        /// unchanged state always returns the same number, so it never has to draw from <see cref="Rand"/>.
        /// </summary>
        public static int OpinionOf(Pawn pawn, Pawn other)
        {
            float opinion = 0f;
            opinion += other.story.traits.ConstantOpinionOffset();
            opinion += RelationOpinionOffset(pawn, other);
            opinion += SocialMemoryOpinionOffset(pawn, other);
            opinion += CompatibilityFactor(pawn.thingIDNumber, other.thingIDNumber);
            return (int)GenMath.Clamp(System.MathF.Round(opinion), SocialTuning.MinOpinion, SocialTuning.MaxOpinion);
        }

        /// <summary>Sum of every <see cref="PawnRelationDef.opinionOffset"/> whose relation currently holds between the two pawns.</summary>
        public static float RelationOpinionOffset(Pawn pawn, Pawn other)
        {
            float total = 0f;
            IReadOnlyList<PawnRelationDef> defs = DefDatabase<PawnRelationDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                PawnRelationDef def = defs[i];
                if (def.Worker.InRelation(pawn, other)) total += def.opinionOffset;
            }
            return total;
        }

        /// <summary>
        /// Sum of <paramref name="pawn"/>'s memories about <paramref name="other"/> specifically, grouped and
        /// stacked the same way <see cref="ThoughtHandler.TotalMoodOffset"/> stacks mood: repeats of the same
        /// memory about the same pawn average together and decay geometrically by
        /// <see cref="ThoughtDef.stackedEffectMultiplier"/>, rather than summing flat (three insults should
        /// weigh more than one, not three times as much).
        /// </summary>
        public static float SocialMemoryOpinionOffset(Pawn pawn, Pawn other)
        {
            IReadOnlyList<Thought_Memory> memories = pawn.needs.mood?.thoughts.memories.Memories ?? System.Array.Empty<Thought_Memory>();
            var byDef = new Dictionary<ThoughtDef, List<Thought_Memory>>();
            for (int i = 0; i < memories.Count; i++)
            {
                Thought_Memory m = memories[i];
                if (!ReferenceEquals(m.otherPawn, other)) continue;
                if (m.OpinionOffset() == 0f) continue;
                if (!byDef.TryGetValue(m.def, out List<Thought_Memory>? group))
                {
                    group = new List<Thought_Memory>();
                    byDef[m.def] = group;
                }
                group.Add(m);
            }

            float total = 0f;
            foreach (KeyValuePair<ThoughtDef, List<Thought_Memory>> kv in byDef)
            {
                List<Thought_Memory> group = kv.Value;
                float sum = 0f, weight = 1f, weightSum = 0f;
                for (int i = 0; i < group.Count; i++)
                {
                    sum += group[i].OpinionOffset();
                    weightSum += weight;
                    weight *= kv.Key.stackedEffectMultiplier;
                }
                total += (sum / group.Count) * weightSum;
            }
            return total;
        }

        /// <summary>
        /// A value stable for a given unordered pair of pawn ids forever (RimWorld: <c>Pawn_RelationsTracker
        /// .ConstantPerPawnsPairCompatibilityOffset</c>) — "two given people just naturally get on or don't."
        /// Canonicalizes order (min, max) before hashing so <c>CompatibilityFactor(a, b) == CompatibilityFactor(b, a)</c>;
        /// uses the seeded (non-stream-advancing) <see cref="RandomStream.RangeSeeded(int, int, int)"/> so
        /// querying it never perturbs <see cref="Rand.Current"/>.
        /// </summary>
        public static int CompatibilityFactor(int idA, int idB)
        {
            int lo = idA < idB ? idA : idB;
            int hi = idA < idB ? idB : idA;
            int seed = MurmurHash.Combine(lo, hi);
            int range = (int)SocialTuning.CompatibilityRange;
            return RandomStream.RangeSeeded(-range, range + 1, seed);
        }

        /// <summary>
        /// Adds a mutual <see cref="DirectPawnRelation"/> (friend, rival, lover, ex-spouse…) to both sides at
        /// once — the same "set both sides together" shape <see cref="FamilyManager.FoundHousehold"/> already
        /// uses for <c>spouseId</c>, so a mutual relation can never exist on only one side.
        /// </summary>
        public static void AddMutualRelation(Pawn a, Pawn b, PawnRelationDef def)
        {
            a.relations.AddDirectRelation(def, b.thingIDNumber);
            b.relations.AddDirectRelation(def, a.thingIDNumber);
        }

        public static void RemoveMutualRelation(Pawn a, Pawn b, PawnRelationDef def)
        {
            a.relations.RemoveDirectRelation(def, b.thingIDNumber);
            b.relations.RemoveDirectRelation(def, a.thingIDNumber);
        }
    }
}
