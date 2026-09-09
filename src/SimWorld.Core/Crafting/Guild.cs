using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Work;
using SimWorld.World;

namespace SimWorld.Crafting
{
    /// <summary>Numbers this module owns. None of them are RimWorld's — RimWorld has no guilds — so every one
    /// is pinned by a trend test rather than trusted as a literal.</summary>
    public static class GuildTuning
    {
        /// <summary>How often a guild converts labour into goods. The long tick, like every other
        /// civilization-scale process here (<c>Storyteller.StorytellerTick</c>, <c>FamilyManager.DemographyTick</c>,
        /// <c>GodManager.GodTick</c>) — production is a trend, not an event.</summary>
        public const int GuildIntervalTicks = GenTicks.TickLongInterval;

        /// <summary>
        /// Work one member contributes per interval before their skill is taken into account, in the same
        /// units as <see cref="RecipeDef.workAmount"/> — where RimWorld's own content prices a batch of
        /// sandstone blocks at 1,600. A quarter of the interval's 2,000 ticks spent at the bench, which is
        /// what a citizen who also eats, sleeps and hauls has left for their trade. Unsourced like everything
        /// else here; what the tests pin is that a batch takes a few intervals rather than one, and that more
        /// and better members make it faster.
        /// </summary>
        public const float WorkPerMemberPerInterval = 500f;

        /// <summary>Skill scaling: an unskilled member works at this share of the base, a level-20 master at
        /// <see cref="SkillFactorAtMaxLevel"/>. Linear between, deliberately — a curve here would be inventing
        /// precision this model has no source for.</summary>
        public const float SkillFactorAtZero = 0.5f;

        public const float SkillFactorAtMaxLevel = 1.5f;

        /// <summary>The skill band a member of the Statistical cohort is sampled from. The cohort has no
        /// <see cref="SkillRecord"/> per person — that is the point of the tier — so one deterministic draw
        /// stands for all of them, exactly as <c>GodRollup</c> already does for mood and health.</summary>
        public const int StatisticalSkillSampleMin = 3;

        public const int StatisticalSkillSampleMax = 12;
    }

    /// <summary>
    /// A guild: the civilization-scale translation of RimWorld's workbench bill queue
    /// (<c>docs/status.json</c>, <c>crafting.guilds</c>).
    ///
    /// <para/><b>What changes and what does not.</b> RimWorld's production is a bill stack on a workbench,
    /// worked by a colonist who walks there, hauls ingredients and completes one iteration at a time. The
    /// bills are the good part and they survive unchanged — <see cref="BillStack"/>, <see cref="Bill_Production"/>
    /// and its repeat modes are used exactly as the Crafting module already defines them, including
    /// <see cref="BillRepeatMode"/>'s target-count hysteresis, which at this scale reads as "keep two hundred
    /// units in the granary" rather than "keep two hundred meals on the shelf". What changes is who holds the
    /// stack and where the goods come from and go: a guild belongs to a <see cref="Settlement"/>, draws its
    /// ingredients from that settlement's <see cref="Settlement.Stores"/> ledger and puts its products back
    /// there, and is staffed by everyone in the settlement carrying the guild's <see cref="GuildDef.role"/>.
    ///
    /// <para/><b>Deliberately not modelled</b>, and each for the same reason — it belongs to the settlement
    /// interior, not to the civilization: no workbench Thing, no hauling, no job driver, no per-iteration
    /// worker. A guild converts labour into goods on the long tick. When a settlement is opened and its
    /// citizens are walking around a real map, RimWorld's own bill/job path is the one that should run there;
    /// this is what happens to the other ninety-nine towns.
    ///
    /// <para/><b>Ingredients must be fixed.</b> A bill whose recipe wants "any meat" cannot be filled from a
    /// def-count ledger without inventing a choice the settlement has no stockpile to make; such a bill is
    /// skipped rather than guessed at, and <see cref="GuildDef.ConfigErrors"/> refuses to ship one.
    /// </summary>
    public sealed class Guild : IBillGiver, IProductCounter, IExposable
    {
        private GuildDef def = null!;
        private int tile = -1;
        private int statisticalMembers;
        private float workAccumulated;
        private BillStack bills = null!;

        /// <summary>Resolved after load from <see cref="tile"/>; see <see cref="ResolveSettlement"/>.</summary>
        private Settlement? settlement;

        /// <summary>Parameterless for the save system.</summary>
        public Guild()
        {
            bills = new BillStack(this);
        }

        public Guild(GuildDef def, Settlement settlement)
        {
            this.def = def ?? throw new ArgumentNullException(nameof(def));
            this.settlement = settlement ?? throw new ArgumentNullException(nameof(settlement));
            tile = settlement.tile;
            bills = new BillStack(this);
        }

        public GuildDef Def => def;

        public Settlement? Settlement => settlement;

        public BillStack Bills => bills;

        /// <summary>
        /// How many of the settlement's Statistical cohort work this guild. Set by whoever runs the
        /// civilization; bounded on read by what the settlement actually has, so shrinking a town cannot leave
        /// a guild staffed by people who are no longer there.
        /// </summary>
        public int StatisticalMembers
        {
            get => settlement == null ? statisticalMembers : Math.Min(statisticalMembers, settlement.StatisticalPopulation);
            set => statisticalMembers = Math.Max(0, value);
        }

        public IProductCounter? ProductCounter => this;

        public string LabelCap => def?.LabelCap ?? nameof(Guild);

        /// <summary>
        /// A bill's target count is measured against the settlement's own ledger — the guild has no shelf of
        /// its own, and "how many do we have" is a question about the town.
        /// </summary>
        public int CountProducts(Bill_Production bill)
        {
            if (bill?.recipe?.products == null || settlement == null) return 0;
            int total = 0;
            for (int i = 0; i < bill.recipe.products.Count; i++)
            {
                total += settlement.StoreCountOf(bill.recipe.products[i].thingDef);
            }
            return total;
        }

        /// <summary>Citizens of this guild's settlement carrying its role. Statistical members are counted
        /// separately (<see cref="StatisticalMembers"/>) because they have no <see cref="Pawn"/> at all.</summary>
        public IEnumerable<Pawn> Members
        {
            get
            {
                if (settlement == null || def?.role == null) yield break;
                IReadOnlyList<Pawn> citizens = settlement.Citizens;
                for (int i = 0; i < citizens.Count; i++)
                {
                    if (ReferenceEquals(citizens[i].workSettings?.Role, def.role)) yield return citizens[i];
                }
            }
        }

        /// <summary>
        /// Labour available this interval, in the same units as <see cref="RecipeDef.workAmount"/>. Real
        /// members contribute against their own skill; the Statistical cohort contributes one deterministic
        /// sampled skill shared by all of them, weighted by count — <c>GodRollup</c>'s own idiom, and for the
        /// same reason: drawing once per person is exactly the enumeration that tier exists to avoid.
        /// </summary>
        public float WorkThisInterval(int ticksGame)
        {
            if (settlement == null || def == null) return 0f;

            float work = 0f;
            foreach (Pawn member in Members)
            {
                int level = def.skill == null ? 0 : member.skills?.GetSkill(def.skill)?.Level ?? 0;
                work += GuildTuning.WorkPerMemberPerInterval * SkillFactor(level);
            }

            int cohort = StatisticalMembers;
            if (cohort > 0)
            {
                int seed = MurmurHash.Combine(
                    GenText.StableStringHash(settlement.name),
                    settlement.tile,
                    MurmurHash.Combine(GenText.StableStringHash(def.defName), ticksGame));
                float sampled = RandomStream.RangeSeeded(
                    GuildTuning.StatisticalSkillSampleMin, GuildTuning.StatisticalSkillSampleMax, seed);
                work += cohort * GuildTuning.WorkPerMemberPerInterval * SkillFactor(sampled);
            }

            return work;
        }

        private static float SkillFactor(float level)
        {
            float t = GenMath.Clamp01(level / SkillRecord.MaxLevel);
            return GuildTuning.SkillFactorAtZero + t * (GuildTuning.SkillFactorAtMaxLevel - GuildTuning.SkillFactorAtZero);
        }

        /// <summary>
        /// Work already done toward the batch in progress. A batch of blocks costs more than one interval's
        /// labour, so without this a guild whose members cannot finish one in a single interval would finish
        /// one never — the granularity of the tick would decide what a civilization can make. Saved, because
        /// half a batch of work is real.
        /// </summary>
        public float WorkAccumulated => workAccumulated;

        /// <summary>
        /// Banks this interval's labour and spends it down the bill stack, in order, returning how many
        /// iterations completed. A guild with nothing it can work on — no runnable bill, no materials in the
        /// settlement's stores, or a recipe the civilization has not researched — banks nothing and makes
        /// nothing: idle craftsmen do not stockpile labour, and a supply shortage shows up as an industry
        /// standing still rather than as free goods later.
        /// </summary>
        public int GuildTick(int ticksGame)
        {
            if (settlement == null || def == null) return 0;
            if (NextRunnable() == null) return 0;

            workAccumulated += WorkThisInterval(ticksGame);

            int iterations = 0;
            float cap = MinWorkAmount;
            while (true)
            {
                Bill_Production? bill = NextRunnable();
                if (bill == null) break;

                float cost = Math.Max(bill.recipe.workAmount, MinWorkAmount);
                cap = cost;
                if (cost > workAccumulated) break;

                ConsumeIngredients(bill);
                ProduceOutputs(bill);
                bill.Notify_IterationCompleted(null!, new List<ItemStack>());
                workAccumulated -= cost;
                iterations++;
            }

            // At most the batch in progress is ever banked. A guild that ran out of work does not carry a
            // century of idle labour into the day its stores are refilled.
            workAccumulated = Math.Min(workAccumulated, cap);
            return iterations;
        }

        private Bill_Production? NextRunnable()
        {
            IReadOnlyList<Bill> list = bills.Bills;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is Bill_Production bill && bill.ShouldDoNow() && CanRunNow(bill)) return bill;
            }
            return null;
        }

        /// <summary>A recipe priced at nothing would let one interval run forever; charge it a floor.</summary>
        private const float MinWorkAmount = 1f;

        private bool CanRunNow(Bill_Production bill)
        {
            RecipeDef recipe = bill.recipe;
            if (recipe == null || recipe.products == null || recipe.products.Count == 0) return false;
            if (!recipe.AvailableNow) return false;
            if (recipe.ingredients == null) return true;

            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                IngredientCount ingredient = recipe.ingredients[i];
                ThingDef? fixedDef = FixedDefOf(ingredient);
                if (fixedDef == null) return false;
                if (settlement!.StoreCountOf(fixedDef) < (int)Math.Ceiling(ingredient.GetBaseCount())) return false;
            }
            return true;
        }

        private void ConsumeIngredients(Bill_Production bill)
        {
            if (bill.recipe.ingredients == null) return;
            for (int i = 0; i < bill.recipe.ingredients.Count; i++)
            {
                IngredientCount ingredient = bill.recipe.ingredients[i];
                ThingDef? fixedDef = FixedDefOf(ingredient);
                if (fixedDef == null) continue;
                settlement!.AddStore(fixedDef, -(int)Math.Ceiling(ingredient.GetBaseCount()));
            }
        }

        private void ProduceOutputs(Bill_Production bill)
        {
            List<ThingDefCountClass> products = bill.recipe.products!;
            for (int i = 0; i < products.Count; i++)
            {
                settlement!.AddStore(products[i].thingDef, products[i].count);
            }
        }

        /// <summary>The one def a fixed ingredient names, or null when the recipe leaves the choice open —
        /// which a settlement ledger cannot make. See this class's own remarks.</summary>
        internal static ThingDef? FixedDefOf(IngredientCount ingredient)
        {
            if (ingredient == null || ingredient.filter == null || !ingredient.IsFixedIngredient) return null;
            foreach (ThingDef d in ingredient.filter.AllowedThingDefs) return d;
            return null;
        }

        /// <summary>Re-attaches a loaded guild to the settlement it belongs to, matched by world tile — the
        /// one identity a <see cref="Settlement"/> has that is both saved and unique, since
        /// <c>WorldObject</c> is not <see cref="ILoadReferenceable"/> and cannot be saved by reference.</summary>
        public void ResolveSettlement(World.World? world)
        {
            if (world == null) return;
            foreach (WorldObject obj in world.worldObjects)
            {
                if (obj is Settlement s && s.tile == tile)
                {
                    settlement = s;
                    return;
                }
            }
        }

        public void ExposeData()
        {
            GuildDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
            Scribe_Values.Look(ref tile, "tile", -1);
            Scribe_Values.Look(ref statisticalMembers, "statisticalMembers");
            Scribe_Values.Look(ref workAccumulated, "workAccumulated");
            BillStack? stack = bills;
            Scribe_Deep.Look(ref stack, "bills", this);
            bills = stack ?? new BillStack(this);
        }
    }

    /// <summary>
    /// Every guild in the game, ticked together on <see cref="GuildTuning.GuildIntervalTicks"/> and saved as
    /// one unit. Owned by <see cref="Sim.Game"/> the way the other civilization-scale managers are; guilds
    /// live here rather than on <see cref="Settlement"/> so the World layer keeps knowing nothing about
    /// production, which is the same separation <c>Economy.CoalSupply</c> already reads settlements from the
    /// outside for.
    /// </summary>
    public sealed class GuildManager : IExposable
    {
        private List<Guild> guilds = new List<Guild>();

        public IReadOnlyList<Guild> Guilds => guilds;

        public Guild Establish(GuildDef def, Settlement settlement)
        {
            var guild = new Guild(def, settlement);
            guilds.Add(guild);
            return guild;
        }

        public void Add(Guild guild)
        {
            if (guild == null) throw new ArgumentNullException(nameof(guild));
            guilds.Add(guild);
        }

        public void Remove(Guild guild) => guilds.Remove(guild);

        public IEnumerable<Guild> GuildsIn(Settlement settlement)
        {
            for (int i = 0; i < guilds.Count; i++)
            {
                if (ReferenceEquals(guilds[i].Settlement, settlement)) yield return guilds[i];
            }
        }

        /// <summary>Self-gating like every other manager's tick, so a tick that would do nothing costs a
        /// modulo rather than a walk of every guild in the world.</summary>
        public void GuildManagerTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % GuildTuning.GuildIntervalTicks != 0) return;
            for (int i = 0; i < guilds.Count; i++) guilds[i].GuildTick(now);
        }

        public void ExposeData()
        {
            List<Guild>? list = guilds;
            Scribe_Collections.Look(ref list, "guilds", LookMode.Deep);
            guilds = list ?? new List<Guild>();
        }

        /// <summary>Called once after a load, when the world exists again, to re-attach guilds to settlements.</summary>
        public void ResolveSettlements(World.World? world)
        {
            for (int i = 0; i < guilds.Count; i++) guilds[i].ResolveSettlement(world);
        }
    }
}
