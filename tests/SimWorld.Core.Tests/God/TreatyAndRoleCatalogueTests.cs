using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Offices;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Work;
using SimWorld.World;

using Xunit;

using CoreWorld = SimWorld.World.World;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// <see cref="TreatyCatalogue"/>, <see cref="RoleCatalogue"/>, the per-civilization
    /// <see cref="FactionRelationView.CanSignTreaty"/>/<see cref="FactionRelationView.TreatyReason"/>, and
    /// <see cref="WorkPriorityRow.RoleDefName"/> — the defNames <see cref="GodCommands.SignTreaty"/>,
    /// <see cref="GodCommands.ApplyRoleToSettlement"/> and <see cref="GodCommands.ApplyRoleToCivilization"/>
    /// take, which the host may not read off <c>DefDatabase</c> itself.
    ///
    /// <para/>The load-bearing tests are the agreement ones: the view must never offer what the simulation would
    /// refuse, nor hide what it would accept — the property <c>GodViewTests</c> already pins for edicts.
    /// </summary>
    public class TreatyAndRoleCatalogueTests : ContentTestBase
    {
        public TreatyAndRoleCatalogueTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static FactionDef FactionDefNamed(string defName) => DefDatabase<FactionDef>.GetNamed(defName);

        private static Faction NewFaction(FactionDef def, string name) => new Faction(def, name, "F_" + name);

        private static CoreWorld BareWorld()
        {
            var world = new CoreWorld();
            Find.World = world;
            return world;
        }

        private static Settlement NewSettlement(CoreWorld world, int tile, string name)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, tile, null, name, Find.TickManager.TicksGame);
            world.worldObjects.Add(settlement);
            return settlement;
        }

        // ---- treaties ----

        [Fact]
        public void The_treaty_catalogue_lists_every_treaty_in_content_once_in_defName_order()
        {
            IReadOnlyList<TreatyOption> options = TreatyCatalogue.Treaties();

            List<string> expected = DefDatabase<TreatyDef>.AllDefsListForReading
                .Select(d => d.defName)
                .OrderBy(n => n, System.StringComparer.Ordinal)
                .ToList();
            Assert.NotEmpty(expected);
            Assert.Equal(expected, options.Select(o => o.DefName).ToList());
        }

        [Fact]
        public void Each_treaty_option_reads_its_terms_off_the_def()
        {
            foreach (TreatyOption option in TreatyCatalogue.Treaties())
            {
                TreatyDef def = DefDatabase<TreatyDef>.GetNamed(option.DefName);
                Assert.Equal(def.LabelCap, option.Label);
                Assert.Equal(def.description ?? "", option.Description);
                Assert.Equal(def.nonAggression, option.NonAggression);
                Assert.Equal(def.tradeAccess, option.TradeAccess);
                Assert.Equal(def.durationDays > 0 ? def.durationDays : 0, option.DurationDays);
                Assert.True(option.DurationDays >= 0);
            }

            // Content ships an open-ended treaty, so the 0-for-open-ended reading is exercised, not vacuous.
            Assert.Contains(TreatyCatalogue.Treaties(), o => o.DurationDays == 0);
            Assert.Contains(TreatyCatalogue.Treaties(), o => o.DurationDays > 0);
        }

        /// <summary>
        /// The view never offers a treaty the command refuses, nor withholds one it would sign: for every other
        /// civilization (a permanent enemy among them) and every treaty in the catalogue, the snapshot's
        /// <see cref="FactionRelationView.CanSignTreaty"/> is exactly whether <see cref="GodCommands.SignTreaty"/>
        /// then signs, and a refusal carries the very sentence the view showed. Re-captured before each signing,
        /// because signing changes the relation (a pact ends a war).
        /// </summary>
        [Fact]
        public void The_treaty_view_agrees_with_SignTreaty_for_every_civilization_and_treaty()
        {
            Faction player = NewFaction(FactionDefNamed("PlayerCivilization"), "AgreePlayer");
            Find.FactionManager.Add(player);
            var others = new List<Faction>();
            foreach (string defName in new[] { "TribalCivilization", "OutlanderCivilization", "RoughOutlanders" })
            {
                Faction other = NewFaction(FactionDefNamed(defName), "Agree" + defName);
                if (!other.def.permanentEnemy) player.SetRelationDirect(other, FactionRelationKind.Neutral, 0);
                Find.FactionManager.Add(other);
                others.Add(other);
            }
            Find.TickManager.DebugSetTicksGame(0);

            bool sawRefusal = false, sawSigning = false;
            foreach (Faction other in others)
            {
                foreach (TreatyOption treaty in TreatyCatalogue.Treaties())
                {
                    FactionRelationView view = GodViewSnapshot.Capture().Civilizations
                        .Single(c => c.CivilizationId == other.loadID);
                    Assert.False(string.IsNullOrEmpty(view.TreatyReason));

                    GodCommandResult result = GodCommands.SignTreaty(other.loadID, treaty.DefName);

                    Assert.Equal(view.CanSignTreaty, result.Outcome == GodCommandOutcome.Done);
                    if (view.CanSignTreaty)
                    {
                        sawSigning = true;
                    }
                    else
                    {
                        sawRefusal = true;
                        Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
                        Assert.Equal(view.TreatyReason, result.Reason);
                    }
                }
            }

            // Both branches exercised, so neither half of the agreement holds vacuously.
            Assert.True(sawSigning);
            Assert.True(sawRefusal);
        }

        /// <summary>
        /// The command's own refusal check (<c>GodCommands.TreatyRefusal</c>, which the view reads too) mirrors
        /// <see cref="Faction.SignTreaty"/> itself for every pairing of shipped civilization kinds, a permanent
        /// enemy on either side included. If <see cref="Faction.SignTreaty"/> gains a rule, this fails rather
        /// than the Diplomacy screen offering a button the simulation then refuses.
        /// </summary>
        [Fact]
        public void The_shared_treaty_refusal_matches_Faction_SignTreaty_for_every_pairing()
        {
            TreatyDef treaty = DefDatabase<TreatyDef>.AllDefsListForReading[0];
            IReadOnlyList<FactionDef> kinds = DefDatabase<FactionDef>.AllDefsListForReading;
            Find.TickManager.DebugSetTicksGame(0);

            int pairs = 0;
            foreach (FactionDef a in kinds)
            {
                foreach (FactionDef b in kinds)
                {
                    Faction actor = NewFaction(a, "Mirror" + pairs + "A");
                    Faction target = NewFaction(b, "Mirror" + pairs + "B");
                    Find.FactionManager.Add(actor);
                    Find.FactionManager.Add(target);

                    bool viewSaysSignable = GodCommands.TreatyRefusal(actor, target) == null;
                    Assert.Equal(viewSaysSignable, actor.SignTreaty(target, treaty));
                    pairs++;
                }
            }

            Assert.Contains(kinds, k => k.permanentEnemy);
            Assert.Contains(kinds, k => !k.permanentEnemy);
            Assert.False(GodCommands.TreatyRefusal(Find.FactionManager.AllFactionsListForReading[0],
                Find.FactionManager.AllFactionsListForReading[0]) == null);
        }

        // ---- roles ----

        [Fact]
        public void The_role_catalogue_lists_every_role_once_ordinary_roles_before_office_roles()
        {
            IReadOnlyList<RoleOption> options = RoleCatalogue.Roles();

            Assert.Equal(DefDatabase<RoleDef>.AllDefsListForReading.Count, options.Count);
            Assert.Equal(options.Count, options.Select(o => o.DefName).Distinct().Count());

            int firstOffice = options.ToList().FindIndex(o => o.OfficeDefName != null);
            Assert.True(firstOffice > 0, "content ships both ordinary and office roles");
            Assert.All(options.Skip(firstOffice), o => Assert.NotNull(o.OfficeDefName));

            List<RoleOption> ordinary = options.Take(firstOffice).ToList();
            List<RoleOption> office = options.Skip(firstOffice).ToList();
            Assert.Equal(ordinary.Select(o => o.DefName).OrderBy(n => n, System.StringComparer.Ordinal), ordinary.Select(o => o.DefName));
            Assert.Equal(office.Select(o => o.DefName).OrderBy(n => n, System.StringComparer.Ordinal), office.Select(o => o.DefName));
        }

        [Fact]
        public void Each_role_option_reads_its_work_types_and_office_off_content()
        {
            foreach (RoleOption option in RoleCatalogue.Roles())
            {
                RoleDef def = DefDatabase<RoleDef>.GetNamed(option.DefName);
                Assert.Equal(def.LabelCap, option.Label);
                Assert.Equal(def.description ?? "", option.Description);
                Assert.Equal(def.emphasizedWorkTypes.Select(w => w.defName), option.EmphasizedWorkTypeDefNames);
                Assert.Equal(def.emphasizedWorkTypes.Select(w => w.LabelCap), option.EmphasizedWorkTypeLabels);

                OfficeDef? office = DefDatabase<OfficeDef>.AllDefsListForReading.FirstOrDefault(o => o.role == def);
                Assert.Equal(office?.defName, option.OfficeDefName);
            }
        }

        /// <summary>
        /// Every role the catalogue offers, both commands accept — and once applied, the view reports it as the
        /// role in force on both the roster (<see cref="CitizenLine.RoleDefName"/>) and the work tab
        /// (<see cref="WorkPriorityRow.RoleDefName"/>).
        /// </summary>
        [Fact]
        public void Every_catalogued_role_is_accepted_by_both_commands_and_read_back_as_in_force()
        {
            CoreWorld world = BareWorld();
            Settlement settlement = NewSettlement(world, 11, "RoleTown");
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            settlement.AddCitizen(a);
            settlement.AddCitizen(b);

            IReadOnlyList<RoleOption> roles = RoleCatalogue.Roles();
            Assert.NotEmpty(roles);
            foreach (RoleOption role in roles)
            {
                Assert.Equal(GodCommandOutcome.Done, GodCommands.ApplyRoleToCivilization(role.DefName).Outcome);
                AssertInForce(settlement.tile, role.DefName);

                Assert.Equal(GodCommandOutcome.Done, GodCommands.ApplyRoleToSettlement(settlement.tile, null).Outcome);
                AssertInForce(settlement.tile, null);

                Assert.Equal(GodCommandOutcome.Done, GodCommands.ApplyRoleToSettlement(settlement.tile, role.DefName).Outcome);
                AssertInForce(settlement.tile, role.DefName);
            }
        }

        private static void AssertInForce(int tile, string? roleDefName)
        {
            SettlementRoster roster = GodViewSnapshot.CitizensOf(tile)!;
            Assert.NotEmpty(roster.Citizens);
            Assert.All(roster.Citizens, c => Assert.Equal(roleDefName, c.RoleDefName));

            WorkPriorityTable table = GodViewSnapshot.WorkPrioritiesOf(tile)!;
            Assert.NotEmpty(table.Rows);
            Assert.All(table.Rows, r => Assert.Equal(roleDefName, r.RoleDefName));
        }

        [Fact]
        public void Both_catalogues_are_safe_to_read_without_a_game()
        {
            Find.World = null;

            Assert.NotEmpty(TreatyCatalogue.Treaties());
            Assert.NotEmpty(RoleCatalogue.Roles());
        }
    }
}
