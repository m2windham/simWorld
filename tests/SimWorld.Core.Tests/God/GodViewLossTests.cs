using System;
using System.Linq;
using System.Reflection;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.God;
using SimWorld.God.View;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

using CoreSettlement = SimWorld.World.Settlement;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// Phase 2's measurements — <see cref="DeathLedger"/> and <see cref="ResourceImpactLedger"/>, both hanging
    /// off <see cref="Storyteller"/> — surfaced onto the god view as <see cref="LossSummary"/>. A sibling of
    /// <see cref="GodViewTests"/> rather than an addition to it, per this repo's own convention: an added file
    /// cannot conflict with one already mid-edit.
    /// </summary>
    [Collection("GlobalDefs")]
    public class GodViewLossTests : ContentTestBase
    {
        public GodViewLossTests(CoreContentFixture content) : base(content)
        {
        }

        private static DeathLedger Deaths => Find.Storyteller.deaths;

        private static ResourceImpactLedger ResourceImpact => Find.Storyteller.resourceImpact;

        // ---- the structural premise, scoped to what this lane added ----

        /// <summary>
        /// The narrower cousin of <c>GodViewTests.The_command_surface_is_the_only_way_in</c>, which checks
        /// <c>EdictOption</c> and <see cref="GodViewSnapshot"/>'s own immediate properties but does not walk
        /// into a nested type such as <see cref="LossSummary"/>. Since every new type this lane added is
        /// reachable only through <see cref="GodViewSnapshot.Losses"/>, that gap is exactly where a Def, a
        /// live Pawn or a live Settlement could slip onto the seam unnoticed — so it is asserted here,
        /// directly, the same way <c>MapViewTests</c> walks its whole read model.
        /// </summary>
        [Fact]
        public void The_loss_readouts_hand_out_no_def_and_no_live_object()
        {
            Type[] lossTypes =
            {
                typeof(LossSummary), typeof(DeathCauseLine), typeof(DeathSourceLine),
                typeof(NutritionLossLine), typeof(StructureLossLine),
            };

            foreach (Type type in lossTypes)
            {
                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    string where = type.Name + "." + property.Name;
                    Type t = property.PropertyType;

                    Assert.False(typeof(Def).IsAssignableFrom(t), where + " exposes a Def — the host could reach a worker through it");
                    Assert.False(typeof(GodManager).IsAssignableFrom(t), where + " exposes the manager itself");
                    Assert.False(typeof(Pawn).IsAssignableFrom(t), where + " exposes a live Pawn");
                    Assert.False(typeof(CoreSettlement).IsAssignableFrom(t), where + " exposes a live Settlement");
                }
            }
        }

        // ---- each readout mirrors the ledger it reads ----

        [Fact]
        public void Deaths_by_cause_reflects_the_death_ledger()
        {
            Deaths.Record(DeathCause.Starvation);
            Deaths.Record(DeathCause.Starvation);
            Deaths.Record(DeathCause.Injury);

            LossSummary losses = GodViewSnapshot.Capture().Losses;

            Assert.Equal(3, losses.TotalDeaths);
            Assert.Equal(2, losses.DeathsByCause.Count);
            Assert.Equal(2, losses.DeathsByCause.Single(l => l.Cause == DeathCause.Starvation).Count);
            Assert.Equal(1, losses.DeathsByCause.Single(l => l.Cause == DeathCause.Injury).Count);
            // A cause nobody has died of yet is absent, not a zero row.
            Assert.DoesNotContain(losses.DeathsByCause, l => l.Cause == DeathCause.Disease);
        }

        [Fact]
        public void Deaths_by_source_reflects_what_killed_them()
        {
            Deaths.Record(DeathCause.Injury);
            Deaths.RecordAttributed("ManhunterPack");
            Deaths.Record(DeathCause.Disease);
            Deaths.Record(DeathCause.Disease);
            Deaths.RecordAttributed("Disease_Plague", 2);

            LossSummary losses = GodViewSnapshot.Capture().Losses;

            Assert.Equal(2, losses.DeathsBySource.Count);
            Assert.Equal(1, losses.DeathsBySource.Single(l => l.Source == "ManhunterPack").Count);
            Assert.Equal(2, losses.DeathsBySource.Single(l => l.Source == "Disease_Plague").Count);
            // Ordered by defName, not insertion order, so the view draws the same list on every capture.
            Assert.Equal(losses.DeathsBySource.Select(l => l.Source).OrderBy(s => s, StringComparer.Ordinal),
                losses.DeathsBySource.Select(l => l.Source));
        }

        [Fact]
        public void Losses_reflect_the_resource_impact_ledger_by_source()
        {
            ResourceImpact.RecordNutritionDenied("Drought", 40f);
            ResourceImpact.RecordNutritionDenied("Drought", 2.5f);
            ResourceImpact.RecordStructuresDestroyed("Earthquake", 3);

            LossSummary losses = GodViewSnapshot.Capture().Losses;

            Assert.Equal(42.5f, losses.TotalNutritionDenied);
            Assert.Single(losses.NutritionDeniedBySource);
            Assert.Equal(42.5f, losses.NutritionDeniedBySource.Single(l => l.Source == "Drought").NutritionDenied);

            Assert.Equal(3, losses.TotalStructuresDestroyed);
            Assert.Single(losses.StructuresDestroyedBySource);
            Assert.Equal(3, losses.StructuresDestroyedBySource.Single(l => l.Source == "Earthquake").StructuresDestroyed);

            // A source that only ever destroyed structures never denied any nutrition — absent, not zero.
            Assert.DoesNotContain(losses.NutritionDeniedBySource, l => l.Source == "Earthquake");
            Assert.DoesNotContain(losses.StructuresDestroyedBySource, l => l.Source == "Drought");
        }

        [Fact]
        public void A_civilization_that_has_lost_nothing_produces_empty_readouts_not_rows_of_zeroes()
        {
            LossSummary losses = GodViewSnapshot.Capture().Losses;

            Assert.Equal(0, losses.TotalDeaths);
            Assert.Empty(losses.DeathsByCause);
            Assert.Empty(losses.DeathsBySource);
            Assert.Equal(0f, losses.TotalNutritionDenied);
            Assert.Empty(losses.NutritionDeniedBySource);
            Assert.Equal(0, losses.TotalStructuresDestroyed);
            Assert.Empty(losses.StructuresDestroyedBySource);
        }

        // ---- capturing is read-only ----

        [Fact]
        public void Capturing_the_snapshot_does_not_mutate_either_ledger()
        {
            Deaths.Record(DeathCause.Starvation);
            Deaths.RecordAttributed("Drought");
            ResourceImpact.RecordNutritionDenied("Drought", 10f);
            ResourceImpact.RecordStructuresDestroyed("Earthquake", 1);

            GodViewSnapshot.Capture();
            GodViewSnapshot.Capture();
            LossSummary losses = GodViewSnapshot.Capture().Losses;

            // Three captures of the same recorded state — if Capture ever wrote back through the ledgers this
            // would have tripled by now.
            Assert.Equal(1, Deaths.Total);
            Assert.Equal(1, Deaths.AttributedTo("Drought"));
            Assert.Equal(10f, ResourceImpact.NutritionDeniedBy("Drought"));
            Assert.Equal(1, ResourceImpact.StructuresDestroyedBy("Earthquake"));

            Assert.Equal(1, losses.TotalDeaths);
            Assert.Equal(1, losses.TotalStructuresDestroyed);
        }
    }
}
