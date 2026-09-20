using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using SimWorld.Defs;
using SimWorld.God;
using SimWorld.God.View;
using SimWorld.Pawns;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

using CoreMap = SimWorld.Map.Map;
using CoreSettlement = SimWorld.World.Settlement;
using CoreWorld = SimWorld.World.World;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// Every public type in <c>God/View</c> hands out values and names, never a live object.
    ///
    /// <para/><b>Why this exists when a structural test already did.</b>
    /// <c>GodViewTests.The_command_surface_is_the_only_way_in</c> checks two things: that
    /// <see cref="EdictOption"/>'s own properties expose no <see cref="Def"/>, and that
    /// <see cref="GodViewSnapshot"/>'s <i>immediate</i> properties expose no <see cref="GodManager"/>. That is
    /// narrower than what CLAUDE.md and the spec both claim of this seam — "the snapshot is values and every
    /// handle is a defName, so the host cannot hold a Def and through it reach a worker. A test enforces that
    /// structurally." It does not recurse into the nested summaries, and it never looks for a live
    /// <c>Pawn</c>, <c>Thing</c>, <c>Map</c>, <c>Settlement</c> or <c>World</c> anywhere. A Def sitting on
    /// <see cref="SettlementSummary"/> or <see cref="ConditionLine"/> passes it untouched.
    ///
    /// <para/><b>The mirror was better than the original.</b> <c>Map/View</c>'s equivalent —
    /// <c>MapViewTests.The_read_model_hands_out_no_def_no_live_object_and_no_engine_type</c> — walks its whole
    /// namespace, checks generic arguments, rejects mutable collections, and guards itself with a minimum type
    /// count. Its own doc says it "mirrors God/View's GodViewTests one scale down, including the structural
    /// test", which is how the gap survived: the copy was thorough, the original was assumed to be, and the
    /// doc that described both described the copy.
    ///
    /// <para/>This is the same shape as every other defect found this phase — a guarantee that was written
    /// down, believed, and narrower in the code than in the sentence describing it. So the rule the
    /// documentation already claims is enforced here, over the whole namespace, rather than left as a
    /// sentence. The older test stays exactly as it is: it asserts something true, and nothing here replaces
    /// it.
    /// </summary>
    public class GodViewSeamIntegrityTests : ContentTestBase
    {
        public GodViewSeamIntegrityTests(CoreContentFixture content) : base(content)
        {
        }

        /// <summary>Handing any of these to the host would give it a way back into the simulation: a
        /// <see cref="Def"/> carries its worker, and the rest are the live objects themselves.</summary>
        private static readonly (Type Type, string Why)[] Forbidden =
        {
            (typeof(Def), "a Def — the host could reach its worker through it"),
            (typeof(GodManager), "the manager itself"),
            (typeof(Thing), "a live Thing — that is a write surface into the simulation"),
            (typeof(Pawn), "a live Pawn"),
            (typeof(CoreMap), "the Map itself"),
            (typeof(CoreSettlement), "a live Settlement"),
            (typeof(CoreWorld), "the World itself"),
        };

        [Fact]
        public void The_whole_read_model_hands_out_no_def_and_no_live_object()
        {
            List<Type> viewTypes = typeof(GodViewSnapshot).Assembly
                .GetTypes()
                .Where(t => t.IsPublic && t.Namespace == "SimWorld.God.View")
                .ToList();

            int checkedTypes = 0;
            foreach (Type type in viewTypes)
            {
                if (type.IsEnum) continue;   // a closed set of names carries nothing to reach through
                checkedTypes++;

                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    string where = type.Name + "." + property.Name;
                    AssertHandsOutNothingLive(property.PropertyType, where);

                    foreach (Type argument in property.PropertyType.IsGenericType
                        ? property.PropertyType.GetGenericArguments()
                        : Array.Empty<Type>())
                    {
                        AssertHandsOutNothingLive(argument, where + " (collection element)");
                    }
                }

                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    AssertHandsOutNothingLive(field.FieldType, type.Name + "." + field.Name);
                }
            }

            // Guards the guard: a namespace typo here would pass vacuously for ever, which is precisely how
            // the narrower test above went unnoticed.
            Assert.True(
                checkedTypes >= 10,
                $"expected the whole God/View read model, found {checkedTypes} types");
        }

        /// <summary>
        /// A mutable collection is a write surface even when every element is a value: the host could clear a
        /// list the simulation still holds. The read model hands out read-only views or arrays it has already
        /// copied.
        /// </summary>
        [Fact]
        public void The_whole_read_model_hands_out_no_mutable_collection()
        {
            IEnumerable<Type> viewTypes = typeof(GodViewSnapshot).Assembly
                .GetTypes()
                .Where(t => t.IsPublic && !t.IsEnum && t.Namespace == "SimWorld.God.View");

            foreach (Type type in viewTypes)
            {
                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    Type t = property.PropertyType;
                    if (!t.IsGenericType) continue;

                    Type definition = t.GetGenericTypeDefinition();
                    Assert.False(
                        definition == typeof(List<>) || definition == typeof(Dictionary<,>)
                            || definition == typeof(HashSet<>) || definition == typeof(ICollection<>)
                            || definition == typeof(IList<>) || definition == typeof(IDictionary<,>),
                        $"{type.Name}.{property.Name} exposes a mutable collection the host could write through");
                }
            }
        }

        private static void AssertHandsOutNothingLive(Type type, string where)
        {
            foreach ((Type forbidden, string why) in Forbidden)
            {
                Assert.False(
                    forbidden.IsAssignableFrom(type),
                    where + " exposes " + why);
            }
        }
    }
}
