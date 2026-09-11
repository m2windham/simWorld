using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Work;
using SimWorld.World;

namespace SimWorld.Crafting
{
    /// <summary>Numbers this initiative owns. Its own class in its own file rather than a field appended to
    /// <see cref="GuildTuning"/>, per CLAUDE.md: a new file cannot conflict with another lane's edit.</summary>
    public static class GuildInitiativeTuning
    {
        /// <summary>
        /// Self-gate cadence: the same <see cref="GuildTuning.GuildIntervalTicks"/> a guild's own production
        /// runs on. Deciding whether a settlement has an industry and running that industry are one
        /// civilization-scale process seen twice, and putting them on different clocks would only make which
        /// noticed a change first depend on the tick number — the reasoning
        /// <c>Building.WorksInitiativeTuning.IntervalTicks</c> writes down for its own siblings.
        /// </summary>
        public const int IntervalTicks = GuildTuning.GuildIntervalTicks;

        /// <summary>
        /// Craftsmen a settlement seats in one guild. <b>Flat, not per capita</b> — and the flatness is the
        /// sourced part, in the same sense <c>Building.WorksInitiativeTuning.ResearchBenchesWanted</c> is:
        /// scaling an industry with population is the obvious civilization-scale instinct and the arithmetic
        /// this codebase already did says it is wrong.
        ///
        /// <para/>Derived from <see cref="StonecuttingTuning.TablesWanted"/>, which is the same question asked
        /// at map scale — how many people should a settlement have working stone at once — and which
        /// <c>StonecuttingTuning</c>'s own remarks answer with a full derivation: one mason turns RimWorld's
        /// 1,600-work batch into twenty blocks in about a fifteenth of a day, so "the binding constraint at any
        /// settlement size is chunk supply, never bench throughput". A guild is that same mason moved up a
        /// level of detail, so it seats the same number for the same reason. It also lands exactly where
        /// <see cref="GuildTuning.WorkPerMemberPerInterval"/>'s own doc says a guild should sit — "a batch takes
        /// a few intervals rather than one" — which is the independent check that the derivation transfers:
        /// one member contributes 250–750 work per interval against a 1,600-work batch.
        ///
        /// <para/>Pinned by <c>SettlementStockTests</c> as a band (a settlement's guild finishes a batch in
        /// more than one interval and fewer than a season) rather than as this literal.
        /// </summary>
        public const int CraftsmenPerGuild = StonecuttingTuning.TablesWanted;
    }

    /// <summary>
    /// The settlement decides for itself to have an industry: it establishes the guilds its civilization knows
    /// the trades for, staffs them, and keeps the standing bills on them (system: <c>crafting.guilds</c>).
    ///
    /// <para/><b>Why this class exists.</b> <see cref="GuildManager.Establish"/> had no caller anywhere in
    /// <c>src/</c> — every <see cref="Guild"/> in the repository was made by a test — so
    /// <see cref="GuildManager.GuildManagerTick"/> ran every long tick over an empty list for the life of
    /// every game. That is the same shape as "nothing in <c>src/</c> ever created a bill"
    /// (<see cref="StonecutterInitiative"/>) and "nothing in <c>src/</c> ever created a stockpile"
    /// (<c>Economy.SettlementStockInitiative</c>): a complete, tested, content-backed module that no game
    /// could reach. It is also the last link in the chain that lane opened — a guild with materials but no
    /// existence is no better off than a guild with an existence and no materials.
    ///
    /// <para/><b>The labour invariant, which is the tiering one.</b> <i>A citizen either works jobs on a map or
    /// works in a guild, never both.</i> Spec §11.3 makes Full tier the tier that has jobs at all ("no jobs, no
    /// mind state, no skills" for everything below), and <c>Settlement.SyncCitizenSpawns</c> puts exactly the
    /// Full-tier citizens on the interior. So this class only ever seats a citizen <i>below</i> Full, and
    /// releases one the moment they are promoted into Full. Without that, a settlement would get its
    /// stonecutting twice out of the same people: once at the bench through
    /// <c>WorkGiver_DoBill</c> and once in the guild. The rule lives here rather than inside
    /// <see cref="Guild.Members"/> deliberately — a guild should answer "who carries my role", not "who is
    /// allowed to", and a host or a scenario that seats somebody by hand is not this initiative's business to
    /// overrule.
    ///
    /// <para/><b>Two kinds of seat, because there are two kinds of citizen below Full.</b> An Interval citizen
    /// has a real <c>Pawn</c> with real skills, so it takes the guild's <see cref="GuildDef.role"/> and
    /// <see cref="Guild.Members"/> finds it with no change. A Statistical citizen is a seat in
    /// <see cref="Settlement.StatisticalPopulation"/>'s count with no <c>Pawn</c> behind it (spec §11.3), so it
    /// is counted through <see cref="Guild.StatisticalMembers"/> — a field nothing in <c>src/</c> had ever
    /// written either. Live members are seated first because their skill is real rather than sampled.
    ///
    /// <para/><b>Roles this never touches.</b> Only a citizen whose role is null is ever given one, and only a
    /// citizen carrying <i>this guild's</i> role is ever released — so an office holder (whose role is its
    /// <c>OfficeDef.role</c>, and which <c>Offices.OfficeManager</c> owns) is invisible to this pass in both
    /// directions, and a citizen the god has directed with
    /// <c>Work.WorkPolicyUtility.ApplyRoleToPopulation</c> keeps that direction.
    ///
    /// <para/><b>Tiering.</b> Nothing here reads or generates an interior map at all: a guild is the
    /// civilization-scale half of production and works for a settlement whether or not anyone has ever entered
    /// it — which is the case <see cref="Guild"/> was written for ("this is what happens to the other
    /// ninety-nine towns"). A settlement with a live map reaches the same guild through the citizens its
    /// attention budget could not seat at Full.
    ///
    /// <para/><b>No state of its own</b>, so nothing to Scribe: guilds save through
    /// <see cref="GuildManager.ExposeData"/>, roles save with the pawn that carries them, and every decision
    /// here is re-derived from those two on the next pass.
    /// </summary>
    public static class GuildInitiative
    {
        /// <summary>Civilization-wide entry point, called once per tick from <c>Sim.Game.WireTickHooks</c> and
        /// short-circuited by the gate below on every tick but the one it fires on. A silent no-op with no
        /// world or no game running.</summary>
        public static void Tick()
        {
            SimWorld.World.World? world = Find.World;
            Game? game = Find.CurrentGame;
            if (world == null || game == null) return;
            if (Find.TickManager.TicksGame % GuildInitiativeTuning.IntervalTicks != 0) return;
            foreach (Settlement settlement in world.Settlements) Run(settlement, game.Guilds);
        }

        /// <summary>The ungated pass for one settlement. Public so a test (or a future caller entering a
        /// settlement scope) can drive it without arranging for the tick number to land on the
        /// interval.</summary>
        public static void Run(Settlement settlement, GuildManager guilds)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (guilds == null) throw new ArgumentNullException(nameof(guilds));

            foreach (GuildDef def in EstablishableDefs())
            {
                Guild? guild = GuildOf(guilds, settlement, def);
                if (guild == null)
                {
                    if (!CanSupport(settlement, def)) continue;
                    guild = guilds.Establish(def, settlement);
                }

                EnsureBills(guild);
                Staff(settlement, guild);
            }
        }

        /// <summary>Every shipped <see cref="GuildDef"/>, in defName order, so two runs of the same state
        /// establish the same guilds in the same order — determinism is a feature, and Def load order is the
        /// one thing here that could otherwise decide it.</summary>
        private static List<GuildDef> EstablishableDefs()
        {
            var defs = new List<GuildDef>(DefDatabase<GuildDef>.AllDefsListForReading);
            defs.Sort((a, b) => string.CompareOrdinal(a.defName, b.defName));
            return defs;
        }

        private static Guild? GuildOf(GuildManager guilds, Settlement settlement, GuildDef def)
        {
            foreach (Guild guild in guilds.GuildsIn(settlement))
            {
                if (ReferenceEquals(guild.Def, def)) return guild;
            }
            return null;
        }

        /// <summary>
        /// Whether <paramref name="settlement"/> should have a guild of this kind yet: the civilization has to
        /// know at least one of its trades (<see cref="RecipeDef.AvailableNow"/> — research, the same gate
        /// <see cref="Guild.GuildTick"/> applies at run time) and the settlement has to have somebody who could
        /// staff it. A guild established before either is an industry that exists and can never work, which is
        /// the exact shape of defect this file was opened to close.
        /// </summary>
        public static bool CanSupport(Settlement settlement, GuildDef def)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (PotentialMembers(settlement) <= 0) return false;

            for (int i = 0; i < def.recipes.Count; i++)
            {
                if (def.recipes[i] != null && def.recipes[i].AvailableNow) return true;
            }
            return false;
        }

        /// <summary>Citizens this settlement could seat in a guild: everyone below Full tier. See the class doc
        /// for why Full is excluded rather than preferred.</summary>
        public static int PotentialMembers(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));

            int seatable = settlement.StatisticalPopulation;
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                if (CanBeSeated(citizens[i])) seatable++;
            }
            return seatable;
        }

        /// <summary>
        /// Keeps one standing bill per trade the guild may work and the civilization knows, reusing
        /// <see cref="StonecutterInitiative.StandingBillFor"/> rather than restating what a settlement wants:
        /// a guild asks for exactly what a bench asks for, at the same target and with RimWorld's same
        /// target-count hysteresis, which at this scale reads as "keep this many blocks in the granary". A
        /// zero-target bill is never queued — nothing in content is built out of that product yet, and a bill
        /// that would pause the instant it was read looks exactly like one that should be running.
        /// </summary>
        private static void EnsureBills(Guild guild)
        {
            List<RecipeDef> recipes = guild.Def.recipes;
            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDef recipe = recipes[i];
                if (recipe == null || !recipe.AvailableNow || HasBillFor(guild, recipe)) continue;

                Bill_Production bill = StonecutterInitiative.StandingBillFor(recipe);
                if (bill.targetCount <= 0) continue;
                guild.Bills.AddBill(bill);
            }
        }

        private static bool HasBillFor(Guild guild, RecipeDef recipe)
        {
            IReadOnlyList<Bill> bills = guild.Bills.Bills;
            for (int i = 0; i < bills.Count; i++)
            {
                if (ReferenceEquals(bills[i].recipe, recipe)) return true;
            }
            return false;
        }

        /// <summary>
        /// Seats <see cref="GuildInitiativeTuning.CraftsmenPerGuild"/> craftsmen and releases anybody the
        /// settlement can no longer honestly say is one — a citizen promoted into Full tier (who now works on
        /// the map instead, see the class doc), a citizen seated past the count, and a citizen who has died or
        /// can no longer work at all. Live citizens are taken in roster order, which is creation order and so
        /// reads as seniority — the same tie-break <c>God.AttentionBudget</c> uses, and stable across a save.
        /// </summary>
        private static void Staff(Settlement settlement, Guild guild)
        {
            RoleDef? role = guild.Def.role;
            if (role == null) return;

            int seats = GuildInitiativeTuning.CraftsmenPerGuild;
            int seated = 0;

            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn pawn = citizens[i];
                bool carries = ReferenceEquals(pawn.workSettings?.Role, role);

                if (seated < seats && CanBeSeated(pawn) && (carries || pawn.workSettings!.Role == null))
                {
                    if (!carries) pawn.workSettings!.SetRole(role);
                    seated++;
                    continue;
                }

                // Only ever releases this guild's own role: an office holder or a citizen under a standing
                // work policy carries somebody else's, and is left exactly as it was found.
                if (carries) pawn.workSettings!.SetRole(null);
            }

            guild.StatisticalMembers = Math.Max(0, seats - seated);
        }

        /// <summary>A citizen a guild may seat: alive, humanlike, able to work at all
        /// (<c>Work.WorkPolicyUtility.ApplyRoleToPopulation</c>'s own predicate — a civilization's industry is
        /// staffed by its people, not its livestock or its dead) and below Full tier.</summary>
        private static bool CanBeSeated(Pawn? pawn) =>
            pawn != null
            && !pawn.Dead
            && pawn.RaceProps.Humanlike
            && pawn.workSettings != null
            && pawn.workSettings.EverWork
            && pawn.tier.Tier != PawnTier.Full;
    }
}
