using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.God;
using SimWorld.Letters;
using SimWorld.Pawns;
using SimWorld.Quests;
using SimWorld.Research;
using SimWorld.Social;

namespace SimWorld.Sim
{
    /// <summary>
    /// The root of a running simulation (RimWorld: <c>Verse.Game</c>): owns the <see cref="World"/>, the
    /// managers every system before this one reached only through <see cref="Find"/>, and the tick order that
    /// drives them. <see cref="Find"/> resolves through whichever <see cref="Game"/> is current
    /// (<see cref="Find.CurrentGame"/>) so every ported call site (<c>Find.TickManager</c>,
    /// <c>Find.Storyteller</c>, ...) keeps working unchanged whether or not a <see cref="Game"/> exists.
    /// <para/>
    /// <b>Why one file, not a folder.</b> RimWorld's own <c>Verse.Game</c> is one class too — the "many
    /// components" shape lives in its <c>GameComponent</c> list, not in a directory of collaborating types.
    /// This port has no <c>GameComponent</c> base (nothing here needs the general "arbitrary plugin with its
    /// own save block" mechanism — every manager it owns already has a purpose-built home), so a single
    /// <c>Game.cs</c> mirroring <c>Find.cs</c>'s own one-file shape stays the honest size for what this class
    /// actually does: hold references, define an order, and orchestrate a start and a save.
    /// </summary>
    public sealed class Game : IExposable
    {
        /// <summary>
        /// Default cadence for <see cref="AutosaveDue"/>, in ticks. RimWorld's own default ("Autosave every
        /// N days", Options menu) could not be verified against decompiled source from this environment; one
        /// in-game quadrum (<see cref="GenDate.TicksPerQuadrum"/>, 15 days) is used as a reasonable
        /// order-of-magnitude default rather than a sourced RimWorld constant — pin behaviour against it
        /// (fires only once the interval has actually elapsed), never the literal, per the repository's own
        /// rule for untraceable numbers.
        /// </summary>
        public const int DefaultAutosaveIntervalTicks = GenDate.TicksPerQuadrum;

        private SimWorld.World.World? world;
        private TickManager? tickManager;
        private ResearchManager? researchManager;
        private Storyteller? storyteller;
        private FactionManager? factionManager;
        private LetterStack? letterStack;
        private QuestManager? questManager;
        private SimWorld.Scenario.Scenario? scenario;
        private FamilyManager? familyManager;
        private SocialInteractionManager? socialInteractionManager;
        private GodManager? godManager;
        private SimWorld.Crafting.GuildManager? guildManager;

        /// <summary>The one <see cref="IIncidentTarget"/> this port has — see that interface's own doc for
        /// why. Registered with <see cref="Storyteller"/> at <see cref="NewGame"/> time and re-registered on
        /// load (<see cref="ExposeData"/>'s <see cref="LoadSaveMode.PostLoadInit"/> branch): it is not part of
        /// <see cref="Storyteller"/>'s own save data (its target list is transient, same as its comps), so a
        /// loaded game must hand it back exactly as a fresh one would have registered it in the first place.</summary>
        private CivilizationTarget? civilizationTarget;

        private int lastAutosaveTick;

        /// <summary>The generated planet, or null before <see cref="NewGame"/> or a load has run.</summary>
        public SimWorld.World.World? World { get => world; set => world = value; }

        /// <summary>The simulation clock and its pre/normal/rare/long/post tick order (RimWorld: <c>Verse.Find.TickManager</c>).</summary>
        public TickManager TickManager
        {
            get => tickManager ??= new TickManager();
            set => tickManager = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Research progress and the current project (RimWorld: <c>Verse.Find.ResearchManager</c>).</summary>
        public ResearchManager ResearchManager
        {
            get => researchManager ??= new ResearchManager();
            set => researchManager = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>The active threat director (RimWorld: <c>Verse.Find.Storyteller</c>).</summary>
        public Storyteller Storyteller
        {
            get => storyteller ??= new Storyteller();
            set => storyteller = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Every civilization and its diplomatic relations (RimWorld: <c>Verse.Find.FactionManager</c>).</summary>
        public FactionManager FactionManager
        {
            get => factionManager ??= new FactionManager();
            set => factionManager = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Every letter waiting for the player (RimWorld: <c>Verse.Find.LetterStack</c>).</summary>
        public LetterStack LetterStack
        {
            get => letterStack ??= new LetterStack();
            set => letterStack = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Every generated quest (RimWorld: <c>RimWorld.Find.QuestManager</c>).</summary>
        public QuestManager QuestManager
        {
            get => questManager ??= new QuestManager();
            set => questManager = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>The scenario this game started from (RimWorld: <c>Verse.Find.Scenario</c>).</summary>
        public SimWorld.Scenario.Scenario Scenario
        {
            get => scenario ??= new SimWorld.Scenario.Scenario();
            set => scenario = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Every household and the marriage/birth/death-from-age sweep (SimWorld's own; see <see cref="Find.FamilyManager"/>).</summary>
        public FamilyManager FamilyManager
        {
            get => familyManager ??= new FamilyManager();
            set => familyManager = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Population-wide social interaction sweep (see <see cref="Find.SocialInteractionManager"/>).
        /// Stateless by that class's own design, so this simply hands back a fresh one each time a game is
        /// loaded, exactly as <see cref="Find"/>'s own lazy default already did before <see cref="Game"/> existed.</summary>
        public SocialInteractionManager SocialInteractionManager
        {
            get => socialInteractionManager ??= new SocialInteractionManager();
            set => socialInteractionManager = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Standing edicts and the god view (see <see cref="Find.God"/>).</summary>
        public GodManager God
        {
            get => godManager ??= new GodManager();
            set => godManager = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Every settlement's standing production queues (<c>crafting.guilds</c>).</summary>
        public SimWorld.Crafting.GuildManager Guilds
        {
            get => guildManager ??= new SimWorld.Crafting.GuildManager();
            set => guildManager = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>See the field's own doc.</summary>
        public CivilizationTarget CivilizationTarget => civilizationTarget ??= new CivilizationTarget();

        /// <summary>
        /// Every settlement's generated interior, live right now (RimWorld: <c>Verse.Game.Maps</c>). This
        /// port generates a settlement's interior lazily, the first time it is entered
        /// (<see cref="SimWorld.World.Settlement.EnterMap"/>), and most settlements in a civilization never
        /// are — so this is not a separately-tracked list, it is exactly the settlements that happen to have
        /// one, read live off <see cref="World"/> each time rather than duplicated bookkeeping that could
        /// drift from it.
        /// </summary>
        public IEnumerable<SimWorld.Map.Map> Maps
        {
            get
            {
                if (world == null) yield break;
                foreach (SimWorld.World.WorldObject obj in world.worldObjects)
                {
                    if (obj is SimWorld.World.Settlement settlement && settlement.InteriorMap != null)
                    {
                        yield return settlement.InteriorMap;
                    }
                }
            }
        }

        /// <summary>
        /// How often <see cref="AutosaveDue"/> fires. A host may change this at any time; it is not itself
        /// Scribed (an options-menu setting, not simulation state — RimWorld's own equivalent lives in
        /// <c>Prefs</c>, not the save).
        /// </summary>
        public int AutosaveIntervalTicks { get; set; } = DefaultAutosaveIntervalTicks;

        /// <summary>
        /// Fires every <see cref="AutosaveIntervalTicks"/> (simcore.autosave: "a cadence and a hook — writing
        /// files is the host's business, not the core's"). The core never touches a filesystem; a host
        /// subscribes and calls whatever <c>Scribe.SaveToString(game, "game")</c> + file-write path it wants.
        /// </summary>
        public event Action<Game>? AutosaveDue;

        /// <summary>For Scribe's deep-load construction, and for building one up manually (<see cref="NewGame"/>).</summary>
        public Game()
        {
        }

        // ---- game start ----

        /// <summary>
        /// Generates a world, runs <paramref name="scenario"/>'s <see cref="SimWorld.Scenario.Scenario.PostWorldGenerate"/>
        /// and <see cref="SimWorld.Scenario.Scenario.PostGameStart"/>, founds the starting settlement
        /// (<see cref="SimWorld.World.SettlementFounder"/>) and returns a <see cref="Game"/> that is already
        /// wired to tick (<see cref="TickManager.PreTickers"/>/<see cref="TickManager.PostTickers"/> populated —
        /// nothing further to call before <see cref="TickManager.DoSingleTick"/>).
        /// <para/>
        /// <b>Founds before running <see cref="SimWorld.Scenario.Scenario.PostGameStart"/>, not after.</b>
        /// <see cref="SimWorld.Scenario.IScenarioContext.StartingPawns"/>'s own doc says it is "populated by
        /// whichever pawn-generation step runs before <c>PostGameStart</c>" — the intended order is generate
        /// pawns, then let scenario parts (forced traits, starting research) act on them. But
        /// <see cref="SimWorld.World.SettlementFounder.Found"/> (this lane may not touch <c>World/**</c>)
        /// always generates its own founding band internally; it has no way to accept a pre-built list. So
        /// this method founds the settlement first — which is the only way to get real <c>Pawn</c> objects at
        /// all — then feeds the resulting citizens into <see cref="SimWorld.Scenario.IScenarioContext.StartingPawns"/>
        /// and runs <c>PostGameStart</c> against them. The observable effect is identical (traits/research/
        /// letters land on the actual founding population); only the call order relative to founding is
        /// reversed from the doc comment's stated ideal. A <c>SettlementFounder.Found</c> overload taking a
        /// pre-generated founder list would let a future pass do this the documented way; flagged here rather
        /// than made quietly, since <c>World/**</c> is another lane's territory this pass.
        /// </summary>
        public static Game NewGame(
            SimWorld.Scenario.Scenario scenario,
            string seedString,
            SimWorld.World.OverallRainfall rainfall = SimWorld.World.OverallRainfall.Normal,
            SimWorld.World.OverallTemperature temperature = SimWorld.World.OverallTemperature.Normal,
            SimWorld.World.OverallPopulation population = SimWorld.World.OverallPopulation.Normal,
            float planetCoverage = 0.3f,
            int? subdivisionOverride = null,
            bool soloStart = false,
            string worldName = "World",
            int? startTile = null,
            int? bandSize = null,
            string? settlementName = null,
            StorytellerDef? storytellerDef = null,
            DifficultyDef? difficultyDef = null)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (seedString == null) throw new ArgumentNullException(nameof(seedString));

            var game = new Game();

            // Find.Reset() is how a game hands over (GodManager's own doc); do that first so nothing from a
            // previous game/test on this thread leaks in, then make this game ambient for everything below.
            Find.Reset();
            Find.CurrentGame = game;
            Pawn.ResetThingIdCounter();

            // PawnGenerator (via SettlementFounder) draws from the ambient Rand.Current, not from any
            // RandomStream this method could pass in explicitly — seed it here so a NewGame is as
            // reproducible from its seed as world generation's own SeededStream calls already are.
            Rand.Current = new RandomStream(SimWorld.World.GenText.StableStringHash(seedString));

            game.Scenario = scenario;
            game.Storyteller = new Storyteller(storytellerDef ?? StorytellerDefOf.Cassandra_Classic, difficultyDef ?? DifficultyDefOf.Medium);
            CivilizationTarget target = game.CivilizationTarget;
            game.Storyteller.RegisterTarget(target);

            SimWorld.Scenario.Scenario.Current = scenario;
            SimWorld.World.World world = SimWorld.World.Gen.WorldGenerator.GenerateWorld(
                seedString, planetCoverage, rainfall, temperature, population, worldName, subdivisionOverride, soloStart);
            scenario.PostWorldGenerate(world);
            game.World = world;

            SimWorld.Factions.Faction playerFaction = ResolvePlayerFaction(scenario, world);

            int tile = startTile ?? PickStartingTile(world.grid);
            int actualBandSize = bandSize ?? Rand.Current.Range(SimWorld.World.SettlementTuning.FoundingBandRange);
            var foundingRand = new RandomStream(SimWorld.World.GenText.StableStringHash(seedString + "|founding"));

            SimWorld.World.Settlement settlement = SimWorld.World.SettlementFounder.Found(
                world, tile, playerFaction, actualBandSize, foundingRand, settlementName);

            var ctx = new SimWorld.Scenario.ScenarioContext();
            ctx.StartingPawns.AddRange(settlement.Citizens);
            ctx.PlayerFactionDef = playerFaction.def;
            scenario.PostGameStart(ctx);

            // Starting items (ScenPart_StartingThing_Defined) have nowhere else to land yet: no map exists
            // until the settlement is entered, and IScenarioContext.AddStartingThing's own doc says exactly
            // this — "for whichever system spawns the actual Thing". Settlement.Stores is that settlement's
            // own "simple def->count ledger" (spec's own words for it); crediting starting items there is the
            // one honest destination this orchestrator can give them without touching World/** or inventing a
            // spawn-on-a-nonexistent-map path.
            foreach (SimWorld.Scenario.StartingThingRecord record in ctx.StartingThings)
            {
                settlement.AddStore(record.thingDef, record.count);
            }

            target.Tile = tile;
            SyncCivilizationTarget(game, target);

            game.WireTickHooks();
            return game;
        }

        /// <summary>Opens (or returns the already-open) interior map for <paramref name="settlement"/> — a
        /// thin, host-facing pass-through to <see cref="SimWorld.World.Settlement.EnterMap"/> so a scope
        /// switch (world view to settlement view) has one call to make through <see cref="Game"/> rather than
        /// needing <see cref="World"/> handed around separately. The returned map is picked up by
        /// <see cref="Maps"/>/the post-tick map sweep automatically on the very next tick — nothing further
        /// to register.</summary>
        public SimWorld.Map.Map EnterSettlement(SimWorld.World.Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (world == null) throw new InvalidOperationException("Game has no World yet.");
            return settlement.EnterMap(world);
        }

        private static SimWorld.Factions.Faction ResolvePlayerFaction(SimWorld.Scenario.Scenario scenario, SimWorld.World.World world)
        {
            SimWorld.Factions.FactionDef? wanted = null;
            foreach (SimWorld.Scenario.ScenPart part in scenario.AllParts)
            {
                if (part is SimWorld.Scenario.ScenPart_PlayerFaction playerFactionPart)
                {
                    wanted = playerFactionPart.factionDef;
                    break;
                }
            }

            if (wanted != null)
            {
                SimWorld.Factions.Faction? named = world.factions.FirstOrDefault(f => f.def == wanted);
                if (named != null) return named;
            }

            SimWorld.Factions.Faction? isPlayer = world.factions.FirstOrDefault(f => f.def.isPlayer);
            if (isPlayer != null) return isPlayer;

            if (world.factions.Count > 0) return world.factions[0];

            throw new InvalidOperationException("World generation produced no factions to found a settlement for.");
        }

        /// <summary>Best-scored non-water tile by the earliest loaded <see cref="SimWorld.World.Siting.SiteWeightDef"/>
        /// (the same convention <c>SettlementTests.BestScoredTile</c>/<c>FullStackTests</c> already use for a
        /// deterministic default founding site).</summary>
        private static int PickStartingTile(SimWorld.World.WorldGrid grid)
        {
            SimWorld.World.Siting.SiteWeightDef weights = DefDatabase<SimWorld.World.Siting.SiteWeightDef>.AllDefsListForReading
                .OrderBy(w => w.era?.order ?? int.MaxValue)
                .First();

            int best = -1;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < grid.TilesCount; i++)
            {
                if (grid.Tiles[i].WaterCovered) continue;
                float score = SimWorld.World.Siting.SiteScorer.Score(grid, i, weights);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            if (best < 0) throw new InvalidOperationException("World generation produced no habitable tile to found a settlement on.");
            return best;
        }

        // ---- tick order (spec §4: pre-tickers -> normal -> rare -> long -> post-tickers) ----

        /// <summary>
        /// (Re)populates <see cref="TickManager.PreTickers"/>/<see cref="TickManager.PostTickers"/> — the
        /// only part of a <see cref="Game"/> that is never Scribed (delegates can't be), so both
        /// <see cref="NewGame"/> and <see cref="ExposeData"/>'s <c>PostLoadInit</c> branch call this to
        /// rebuild them from whatever is actually loaded. Clearing first makes this idempotent: calling it
        /// twice (a test re-wiring, a defensive re-load) never duplicates a hook.
        /// </summary>
        private void WireTickHooks()
        {
            TickManager tm = TickManager;

            tm.PreTickers.Clear();
            tm.PreTickers.Add(_ => world?.WorldTick());

            tm.PostTickers.Clear();
            tm.PostTickers.Add(_ => TickMaps());
            tm.PostTickers.Add(_ => SyncCivilizationTargetIfDue());
            tm.PostTickers.Add(_ => Storyteller.StorytellerTick());
            tm.PostTickers.Add(_ => SocialTick());
            tm.PostTickers.Add(_ => God.GodTick());
            tm.PostTickers.Add(_ => Guilds.GuildManagerTick());
            // building.initiative: settlements decide what they lack and queue the blueprints for it. Self
            // gated on the rare tick like the managers above, so a tick it is not due on costs a modulo.
            tm.PostTickers.Add(_ => SimWorld.Building.SettlementConstructionInitiative.Tick());
            tm.PostTickers.Add(_ => FactionManager.FactionManagerTick());
            tm.PostTickers.Add(_ => LetterStack.LetterStackTick());
            tm.PostTickers.Add(_ => QuestManager.QuestManagerTick());
            tm.PostTickers.Add(_ => AutosaveTick());

            // Starts the cadence from "now" (0 for a new game, whatever TicksGame a load resumed at) rather
            // than from 0 unconditionally — otherwise loading an aged save would autosave on its very first
            // post-load tick, every time.
            lastAutosaveTick = tm.TicksGame;
        }

        private void TickMaps()
        {
            foreach (SimWorld.Map.Map map in Maps) map.MapTick();
        }

        /// <summary>Every Full/Interval citizen on the planet, every civilization's alike. Never includes a
        /// Statistical citizen (<see cref="SimWorld.World.Settlement"/>'s own design: no live <c>Pawn</c>
        /// object exists for one). Used where a whole-world roll really is wanted; the social sweep
        /// deliberately does not use it — see <see cref="SocialTick"/>.</summary>
        private List<Pawn> CollectCitizens()
        {
            var citizens = new List<Pawn>();
            if (world == null) return citizens;
            foreach (SimWorld.World.WorldObject obj in world.worldObjects)
            {
                if (obj is SimWorld.World.Settlement settlement) citizens.AddRange(settlement.Citizens);
            }
            return citizens;
        }

        /// <summary>
        /// Pre-gates on the exact interval <see cref="SocialInteractionManager.SocialInteractionTick"/> itself
        /// gates on, so a tick that would no-op inside that call never first pays to build the population list
        /// — the same reason every other manager here self-gates before doing real work.
        /// </summary>
        private void SocialTick()
        {
            if (world == null) return;
            if (TickManager.TicksGame % SocialTuning.InteractionIntervalTicks != 0) return;

            // One sweep per settlement, not one over the planet. The sweep pairs people up to chat, insult,
            // court and fall out with each other, and two citizens of rival civilizations a continent apart
            // have never met: rolling them against each other manufactures relationships — and, since
            // World.EmergenceManager started founding rivals, most of the candidate pairs in a whole-world
            // sweep are exactly that. A settlement is the smallest unit this port has that means "the people
            // among whom you live", so it is the right scope; when caravans and travel make strangers meet,
            // that is the seam to widen, not this one.
            foreach (SimWorld.World.WorldObject obj in world.worldObjects)
            {
                if (obj is not SimWorld.World.Settlement settlement) continue;
                IReadOnlyList<Pawn> citizens = settlement.Citizens;
                if (citizens.Count > 1) SocialInteractionManager.SocialInteractionTick(citizens);
            }
        }

        /// <summary>
        /// Re-attaches the player civilization's settlements to <see cref="CivilizationTarget"/>, gated to the
        /// same interval <see cref="Storyteller.StorytellerTick"/> actually reads them on
        /// (<see cref="Storyteller.IncidentCycleLengthTicks"/>), so a civilization that grows — or founds a
        /// second town — scales the threat director without paying to rebuild anything every tick.
        ///
        /// <para/>The target derives its roster, seat and wealth from those settlements itself, so nothing is
        /// copied into it here. Two things this fixes by construction: the roster used to be every citizen on
        /// the planet, rival civilizations included, which inflated the threat curve by exactly the rest of
        /// the world once <c>EmergenceManager</c> started founding rivals; and wealth used to be left at its
        /// default because nothing computed it from <see cref="SimWorld.World.Settlement.Stores"/>.
        /// </summary>
        private void SyncCivilizationTargetIfDue()
        {
            if (world == null) return;
            if (TickManager.TicksGame % Storyteller.IncidentCycleLengthTicks != 0) return;
            SyncCivilizationTarget(this, CivilizationTarget);
        }

        private static void SyncCivilizationTarget(Game game, CivilizationTarget target)
        {
            if (game.world == null) return;
            target.SetSettlements(game.PlayerSettlements());
            // The hand-set list is what the target falls back on when it has no settlements; leaving a stale
            // copy of the roster in it would only ever be a second, wrong answer to the same question.
            target.pawns.Clear();
        }

        /// <summary>
        /// The settlements of the player's own civilization — the ones the storyteller is telling a story
        /// about. A world with rivals in it has settlements that are emphatically not the player's, and
        /// <see cref="CollectCitizens"/> deliberately does not make that distinction because its callers
        /// (the social sweep) want everyone.
        /// </summary>
        private List<SimWorld.World.Settlement> PlayerSettlements()
        {
            var found = new List<SimWorld.World.Settlement>();
            if (world == null) return found;
            SimWorld.Factions.Faction? player = world.factions.FirstOrDefault(f => f.def.isPlayer);
            foreach (SimWorld.World.WorldObject obj in world.worldObjects)
            {
                if (obj is SimWorld.World.Settlement settlement
                    && (player == null ? settlement.faction == null : ReferenceEquals(settlement.faction, player)))
                {
                    found.Add(settlement);
                }
            }
            return found;
        }

        private void AutosaveTick()
        {
            int now = TickManager.TicksGame;
            if (now - lastAutosaveTick < AutosaveIntervalTicks) return;
            lastAutosaveTick = now;
            AutosaveDue?.Invoke(this);
        }

        // ---- Scribe ----

        public void ExposeData()
        {
            // Every Find.* access for the rest of this call — including whatever nested nodes below trigger
            // (a settlement's citizens, a spawned Thing's SpawnSetup) — must resolve through this game, not
            // whatever thread-static default Find would otherwise lazily create. Setting this before any
            // child is touched is what makes that true on load as well as at NewGame time.
            Find.CurrentGame = this;

            TickManager? tm = tickManager;
            Scribe_Deep.Look(ref tm, "tickManager");
            tickManager = tm;

            ResearchManager? rm = researchManager;
            Scribe_Deep.Look(ref rm, "researchManager");
            researchManager = rm;

            Storyteller? st = storyteller;
            Scribe_Deep.Look(ref st, "storyteller");
            storyteller = st;

            // FactionManager is deliberately NOT deep-saved here, even though it is not stateless the way
            // SocialInteractionManager is: World.factions already deep-saves every one of these same Faction
            // objects (world generation adds a faction to both World.factions and Find.FactionManager — see
            // WorldGenStep_Factions), and FactionManager.ExposeData (Factions/**, out of this lane's reach)
            // ALSO deep-saves its own "allFactions" independently rather than by reference. Deep-saving it a
            // second time here produced two separate load-id "Faction_1" nodes and two separate Faction
            // objects on load (a genuine, newly-surfaced bug — see this module's report). Rebuilding the
            // index from World.factions after load (below) gets the exact same manager state — every Faction
            // this civilization has, each with its own already-deep-saved relations/goodwill/defeated flag —
            // without saving any of it twice.
            LetterStack? ls = letterStack;
            Scribe_Deep.Look(ref ls, "letterStack");
            letterStack = ls;

            QuestManager? qm = questManager;
            Scribe_Deep.Look(ref qm, "questManager");
            questManager = qm;

            SimWorld.Scenario.Scenario? sc = scenario;
            Scribe_Deep.Look(ref sc, "scenario");
            scenario = sc;

            FamilyManager? fam = familyManager;
            Scribe_Deep.Look(ref fam, "familyManager");
            familyManager = fam;

            GodManager? god = godManager;
            Scribe_Deep.Look(ref god, "godManager");
            godManager = god;

            SimWorld.Crafting.GuildManager? guilds = guildManager;
            Scribe_Deep.Look(ref guilds, "guildManager");
            guildManager = guilds;
            // A guild is attached to its settlement by world tile rather than by reference (WorldObject is not
            // ILoadReferenceable), so the reattachment has to wait until the world itself is back.
            if (Scribe.mode == LoadSaveMode.PostLoadInit) guildManager?.ResolveSettlements(world);

            CivilizationTarget? civ = civilizationTarget;
            Scribe_Deep.Look(ref civ, "civilizationTarget");
            civilizationTarget = civ;

            // SocialInteractionManager is intentionally not Scribed at all — it is stateless by its own
            // design (its class doc: "nothing here needs to survive a save"); it lazily recreates on next
            // access exactly the way Find's own pre-Game default already did.

            SimWorld.World.World? w = world;
            Scribe_Deep.Look(ref w, "world");
            world = w;

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                factionManager = new FactionManager();
                if (world != null)
                {
                    foreach (SimWorld.Factions.Faction faction in world.factions) factionManager.Add(faction);
                }

                if (storyteller != null && civilizationTarget != null) storyteller.RegisterTarget(civilizationTarget);
                WireTickHooks();
            }
        }
    }
}
