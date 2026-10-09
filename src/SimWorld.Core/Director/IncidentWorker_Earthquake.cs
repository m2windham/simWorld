using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Letters;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Director
{
    /// <summary>
    /// The ground shakes under a settlement (SimWorld's own defect — RimWorld has no earthquake incident to
    /// port; see <c>docs/design/phase-2-pressure.md</c>'s "Known problems": three defects, an earthquake among
    /// them, were blocked on a settlement having nothing to damage. <see cref="World.Settlement.Structures"/>
    /// unblocks all three; this is the first to spend it).
    ///
    /// <para/><b>Where it lands is the player's decision, exactly as <see cref="IncidentWorker_ManhunterPack"/>
    /// already established.</b> A watched settlement loses real buildings off its own map; an unwatched one
    /// loses the same buildings out of <see cref="World.Settlement.Structures"/> directly — no map needed, no
    /// map generated to stage the loss. The unwatched arm is the one this incident exists for: a threat that
    /// only costs something while the player happens to be looking is free to ignore, and free-to-ignore is
    /// the failure mode <c>docs/design/phase-2-pressure.md</c> is about.
    ///
    /// <para/><b>Watched-map damage is direct <see cref="Thing.TakeDamage"/>, not
    /// <see cref="Building.RoofCollapserImmediate"/>.</b> That class is the obvious first reach — tested,
    /// already crushes whatever a roof falls on — but its <c>DropRoofInCells</c> only acts where
    /// <c>map.roofGrid.Roofed(cell)</c> is true, and in this port that grid is painted once, at map generation,
    /// over mountain and cave cells only (<see cref="MapGen.GenStep_Roofs"/>) — a settlement's own constructed
    /// rooms are never roofed at all (no auto-roof-growth-from-walls exists here). Scattering cells by
    /// "currently roofed" would therefore almost always miss the very walls and beds a settlement actually
    /// built, on any map that is not itself a mountain interior — a poor fit for an incident whose whole point
    /// is to cost a settlement its own buildings. Damaging the buildings directly is the honest answer instead:
    /// it reaches exactly the <see cref="ThingDef"/>s <see cref="World.Settlement.StructureCount"/> already
    /// tracks, watched or not, with no dependency on where the map generator happened to paint rock.
    ///
    /// <para/><b>A pawn standing where a destroyed structure stood is hurt by it, not executed.</b> Not every
    /// collapse finds someone — a <see cref="Building.StoneWallMaterials.AllWallDefs"/> wall and a
    /// <c>StorageHut</c> are both <c>Impassable</c>, so this only ever finds someone in a destroyed <c>Bed</c>
    /// cell, the one known structure def a pawn can actually stand on, which is to say a sleeper. This used to
    /// be certain death (500 Crush aimed at the core, no roll, no region), and on seed <c>roof-a</c> it killed
    /// five of twenty-five founders asleep on day 2.78: a loss with no warning and nothing the player could
    /// have done, which is not a decision, it is an execution.
    /// It is RimWorld's own rule for a roof that is <i>not</i> a mountain now
    /// (<c>Verse.RoofCollapserImmediate.DropRoofInCellPhaseOne</c>'s else branch, the one a constructed or
    /// thin-rock roof takes): <see cref="RoofCollapserImmediate.ThinRoofCrushDamageRange"/> of Crush, rolled per
    /// victim, aimed at <see cref="BodyPartHeight.Top"/>/<see cref="BodyPartDepth.Outside"/> — on a human the
    /// head, neck, eyes, ears, nose and jaw, never an organ. Most people walk away from that, wounded; it is
    /// not gentle (measured on the shipped body, unarmoured, over four hundred firings: about one in nine died, because the
    /// neck has 25 health and the roll reaches 30, and about two in three lost an eye, an ear, the nose or the
    /// jaw, which are 10 to 20 health apiece; the rest were cut or cracked), but it is a roll, and a person
    /// who lives is somebody the settlement can tend.
    /// Certain death stays with the one roof that earns it, an overhead mountain
    /// (<see cref="RoofCollapserImmediate.ThickRoofCrushDamage"/>), and a settlement's own bed is not that.
    /// The range is reused from <see cref="RoofCollapserImmediate"/>, not copied, so the two cannot drift apart.
    /// Two things RimWorld's branch carries are left off, both because this port has no field to carry them:
    /// <c>DamageInfo.SourceCategory.Collapse</c> (the port's <see cref="DamageInfo"/> has no source category;
    /// <c>RoofCollapseDefOf.Crush</c> and a null instigator already read as a falling building) and
    /// <c>roofCollapseDamageMultiplier</c> (a building-only scale; a pawn takes the default 1).
    /// <para/>Being hurt wakes a sleeper: <see cref="DamageWorker.Apply"/> ends in
    /// <c>Pawn_HealthTracker.PostApplyDamage</c>, which is where <c>Pawn_JobTracker.Notify_DamageTaken</c>
    /// clears <c>Pawn.Asleep</c> (and a hit that downs them ends the job outright), so nothing here needs to
    /// wake anyone itself — and a test pins that it does.
    /// <para/>The wounds are stamped with this incident (<see cref="Hediff.sourceIncident"/>), because the
    /// person it hurts may now die of it later — bleeding out, or the infection — long after the dice are
    /// done, and <see cref="Health.WoundProvenance"/> credits that death to whatever stamped the wound. A blow
    /// that kills on the spot is credited through <see cref="DeathLedger.RecordAttributed"/> from the
    /// ledger's own delta, copying <see cref="SettlementRaidResolver.Resolve"/>'s own approach — never from
    /// this worker's own count of who it hit — so attribution can never exceed the deaths the ledger actually
    /// counted: a settlement that belongs to no registered civilization reaches
    /// <c>Pawn_HealthTracker.Kill</c> and is deliberately not counted there, and this worker must not claim it
    /// anyway. (The <i>letter</i>, by contrast, names the player's own people who died, counted by who actually
    /// died and not by what the ledger counted: being told is not the ledger.)
    ///
    /// <para/><b>Not in a settlement's first days</b> (<see cref="IncidentDef.earliestDay"/>, in
    /// <c>Incidents_Earthquake.xml</c>). A founding band has built little worth losing and has not yet met its
    /// first threat; a quake in that window takes a share of a handful of beds off people who have no
    /// buffer. The number is this port's own and the XML says why.
    ///
    /// <para/><b>The player is told what fell, who was hurt, who was caught and walked away, and who died</b>
    /// (<see cref="SendLetter"/>), by name, with a look-target on each.
    ///
    /// <para/><b>Its dice are its own.</b> Every roll comes from <see cref="NamedRand"/>, never the ambient
    /// stream, and the target settlement and the severity are composed <i>before</i> the
    /// <see cref="Ablation.IsDisabled(string?)"/> check — see <see cref="IncidentWorker_ManhunterPack"/>'s own
    /// doc for why that ordering is not optional. Applying damage to a pawn can draw from the ambient stream
    /// internally (<c>Combat.ArmorUtility.ApplyArmor</c>'s deflection roll, for a citizen wearing apparel with
    /// a nonzero armor rating — the bare pawns this port's own tests build never trigger it, which is exactly
    /// why it would be easy to miss), so every such call is shielded with <see cref="Rand.PushState(int)"/> /
    /// <see cref="Rand.PopState"/>, seeded from this incident's own stream, exactly as
    /// <see cref="IncidentWorker_ManhunterPack.Generate"/> shields <c>PawnGenerator</c>. The amount of each
    /// hit is rolled inside that shield too, so the quake draws exactly one number per victim from its own
    /// stream — the same as the certain-death hit did — and the structures it brings down do not depend on
    /// what happens to the people under them.
    /// </summary>
    public sealed class IncidentWorker_Earthquake : IncidentWorker
    {
        /// <summary>The stream every roll in this incident comes from, qualified by the tick so two firings in
        /// one game differ while a replay of the same game does not — the same qualification
        /// <see cref="IncidentWorker_ManhunterPack"/>'s own stream name uses.</summary>
        private const string StreamName = "Earthquake";

        /// <summary>
        /// Fraction of each structure type destroyed. RimWorld has no earthquake to source this from;
        /// unsourced per CLAUDE.md, so the tests pin that a quake actually costs a settlement a measurable
        /// share of what it built, never these literals. Never 0 (an earthquake that touched nothing would not
        /// be one) and never 1 (a single quake razing a settlement outright is a wipe this port does not model).
        /// </summary>
        public static readonly FloatRange DestructionFractionRange = new FloatRange(0.15f, 0.45f);

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (parms == null) throw new ArgumentNullException(nameof(parms));

            var civ = parms.target as CivilizationTarget;
            if (civ == null) return false;

            RandomStream rand = NamedRand.For(
                StreamName + "|" + (Find.TickManager?.TicksGame ?? 0).ToString(CultureInfo.InvariantCulture));

            // Composed before anything below is asked to act: which settlement shakes, and how hard, are
            // decided now — so both arms of one seed draw identically and differ only in what happens next.
            // See Ablation. A civilization with no settlement to shake has nothing for this incident to do,
            // watched or not: Structures lives on Settlement, not on the bare CivilizationTarget.
            SimWorld.World.Settlement? settlement = civ.ChooseTargetSettlement(rand);
            if (settlement == null) return false;

            float severity = rand.Range(DestructionFractionRange);

            // Ablated: everything above has happened — the storyteller picked this incident and spent its
            // refire timer, the target and severity are decided — and nothing below will. Structures stays
            // exactly as it was, and the ledgers below are never written to. See Ablation.
            if (Ablation.IsDisabled(def?.defName))
            {
                return true;
            }

            string? source = def?.defName;
            Map.Map? map = ArrivalMapFor(civ, settlement);

            var toll = new Toll(settlement);
            if (map != null)
            {
                ShakeMap(map, severity, rand, source, toll);
                CreditStructures(source, toll.StructuresDestroyed);
                SendLetter(settlement, toll, watched: true);
                return true;
            }

            ShakeSettlement(settlement, severity, toll);
            CreditStructures(source, toll.StructuresDestroyed);
            SendLetter(settlement, toll, watched: false);
            return true;
        }

        // ---- the unwatched half: the whole reason this incident was unblocked ----

        /// <summary>
        /// Reduces <see cref="World.Settlement.Structures"/> directly, no map generated and none needed — the
        /// abstract twin of <see cref="ShakeMap"/>, the same relationship
        /// <see cref="Building.AbstractSettlementConstruction"/> already has with
        /// <see cref="Building.SettlementConstructionInitiative"/>. No roll: which structures existed and how
        /// many of each is arithmetic over <see cref="World.Settlement.StructureCount"/>, not a draw.
        /// </summary>
        private static void ShakeSettlement(SimWorld.World.Settlement settlement, float severity, Toll toll)
        {
            IReadOnlyList<ThingDef> defs = KnownStructureDefs();
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef structureDef = defs[i];
                int have = settlement.StructureCount(structureDef);
                if (have <= 0) continue;

                int destroy = DestroyCount(have, severity);
                settlement.AddStructure(structureDef, -destroy);
                toll.Fell(structureDef, destroy);
            }
        }

        // ---- the watched half: real Things, on a real map ----

        /// <summary>
        /// Destroys a <paramref name="severity"/> share of each known structure def actually standing on
        /// <paramref name="map"/>, and hurts anyone standing where one of them stood. What fell, who was hurt
        /// and who died are written to <paramref name="toll"/>.
        /// </summary>
        private static void ShakeMap(Map.Map map, float severity, RandomStream rand, string? source, Toll toll)
        {
            IReadOnlyList<ThingDef> defs = KnownStructureDefs();
            for (int d = 0; d < defs.Count; d++)
            {
                // A copy: destroying an entry deregisters it from the very list ThingsOfDef returns (mirrors
                // RoofCollapserImmediate.ThingsUnder's own reasoning).
                var standing = new List<Thing>(map.listerThings.ThingsOfDef(defs[d]));
                if (standing.Count == 0) continue;

                int toDestroy = DestroyCount(standing.Count, severity);
                for (int n = 0; n < toDestroy && standing.Count > 0; n++)
                {
                    int index = rand.Range(0, standing.Count);
                    Thing target = standing[index];
                    standing.RemoveAt(index);
                    if (target.Destroyed) continue;

                    // Captured before TakeDamage can invalidate it (DeSpawn resets Position). The whole
                    // footprint, not Position: a 1x2 bed falls on its foot cell as well as its head.
                    CellRect footprint = target.OccupiedRect();
                    target.TakeDamage(new DamageInfo(RoofCollapseDefOf.Crush, target.MaxHitPoints));

                    // Credited from whether the Thing is actually gone, never assumed from the hit alone: a
                    // future ThingDef with destroyable=false would otherwise let this claim a destruction that
                    // never happened — the same "never over-claim" discipline CrushOnePawn applies to a death.
                    if (!target.Destroyed) continue;
                    toll.Fell(defs[d], 1);

                    foreach (IntVec3 cell in footprint.Cells)
                    {
                        if (GenGrid.InBounds(cell, map)) HurtOccupants(map, cell, source, rand, toll);
                    }
                }
            }
        }

        /// <summary>Hurts every living pawn standing on <paramref name="cell"/> — one cell of a destroyed
        /// structure's own footprint, so this only ever finds someone in a <c>Bed</c> (a wall and a
        /// <c>StorageHut</c> are both <c>Impassable</c>, never standable). See the class doc for what "hurts"
        /// means, and for why a death is credited from <see cref="DeathLedger.Total"/>'s growth and never from
        /// this method's own count of who it hit.</summary>
        private static void HurtOccupants(Map.Map map, IntVec3 cell, string? source, RandomStream rand, Toll toll)
        {
            var occupants = new List<Thing>(map.thingGrid.ThingsListAt(cell));
            for (int i = 0; i < occupants.Count; i++)
            {
                if (!(occupants[i] is Pawn pawn) || pawn.Dead) continue;
                HurtOnePawn(pawn, source, rand, toll);
            }
        }

        private static void HurtOnePawn(Pawn pawn, string? source, RandomStream rand, Toll toll)
        {
            DeathLedger? ledger = Find.Storyteller?.deaths;
            int before = ledger?.Total ?? 0;

            // Whatever the pawn already carried is not this quake's: only wounds that appear below are stamped.
            var carried = new HashSet<Hediff>(pawn.health.hediffSet.hediffs);

            // Shielded: DamageWorker_AddInjury -> Combat.ArmorUtility.ApplyArmor rolls Rand.Value once per
            // armor source with a nonzero rating, which a bare test pawn never has and a clothed citizen does,
            // and a hit that wakes a sleeper re-asks its think tree, which rolls too. Seeded from this
            // incident's own stream so the callee stays deterministic and the ambient stream comes back to
            // exactly where it was — see IncidentWorker_ManhunterPack.Generate.
            //
            // The amount is rolled inside the same shield, the way RoofCollapserImmediate rolls it off
            // Rand.Current, and not from <c>rand</c> itself: this method draws exactly one number from the
            // incident's stream per victim, as the certain-death hit it replaces did, so which structures the
            // quake brings down — drawn from that same stream, between victims — does not move because the
            // people under them now live. The same seed and the same settlement lose the same beds as before
            // this change; only what happens to whoever was in them differs.
            DamageResult result;
            Rand.PushState(rand.Int);
            try
            {
                // RimWorld's ordinary-roof hit: a rolled amount of Crush, aimed at the top of the body from
                // the outside.
                var dinfo = new DamageInfo(RoofCollapseDefOf.Crush, Rand.Range(RoofCollapserImmediate.ThinRoofCrushDamageRange))
                {
                    Height = BodyPartHeight.Top,
                    Depth = BodyPartDepth.Outside,
                };
                result = RoofCollapseDefOf.Crush.Worker.Apply(dinfo, pawn);
            }
            finally
            {
                Rand.PopState();
            }

            if (pawn.Dead)
            {
                if (toll.IsOurs(pawn)) toll.Killed.Add(pawn);
            }
            else if (result.wounded)
            {
                // Hurt means wounded; see the branch below for a hit that was not.
                if (toll.IsOurs(pawn)) toll.Hurt.Add(pawn);
                StampWounds(pawn, carried, source);
            }
            else if (toll.IsOurs(pawn))
            {
                // Caught, woken, and the blow turned away or found nothing to strike: the near miss. Told, because
                // a person who was under a falling bed and is fine is the best thing the letter has to say.
                toll.Spared.Add(pawn);
            }

            if (ledger == null || ledger.Total <= before) return;
            ledger.RecordAttributed(source);
        }

        /// <summary>Marks every hediff the blow added — the injury, and a missing part it took with it — as
        /// this incident's, so a death that follows from them (<see cref="Health.WoundProvenance"/>: blood loss
        /// and infection inherit the stamp of the wound behind them) is credited to it. A wound that already
        /// had a source keeps it.</summary>
        private static void StampWounds(Pawn pawn, HashSet<Hediff> carried, string? source)
        {
            if (source == null) return;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (carried.Contains(hediffs[i])) continue;
                if (hediffs[i].sourceIncident == null) hediffs[i].sourceIncident = source;
            }
        }

        // ---- shared arithmetic ----

        /// <summary>Every structure def this incident (and <see cref="Building.AbstractSettlementConstruction"/>
        /// before it) knows how to count: every wall material, plus the two other completed buildings a
        /// settlement raises. Not read off <see cref="World.Settlement.Structures"/> directly — every consumer
        /// goes through <see cref="World.Settlement.StructureCount"/>, per that method's own doc — so this
        /// names the defs rather than enumerating whatever the ledger happens to hold.</summary>
        private static IReadOnlyList<ThingDef> KnownStructureDefs()
        {
            var defs = new List<ThingDef>(StoneWallMaterials.AllWallDefs)
            {
                ConstructionThingDefOf.Bed,
                ConstructionThingDefOf.StorageHut,
            };
            return defs;
        }

        /// <summary>How many of <paramref name="have"/> to destroy at <paramref name="severity"/>: at least one
        /// whenever there is anything to lose (an earthquake that always spares the last building whatever its
        /// severity would be no earthquake at all), rounded up rather than down for the same reason, and never
        /// more than there actually are.</summary>
        private static int DestroyCount(int have, float severity)
        {
            if (have <= 0) return 0;
            int destroy = (int)Math.Ceiling(have * severity);
            return Math.Clamp(destroy, 1, have);
        }

        /// <summary>Credits the structures actually destroyed to <see cref="ResourceImpactLedger"/>, at the
        /// exact point they were destroyed — never by differencing two runs. See
        /// <see cref="ResourceImpactLedger.RecordStructuresDestroyed"/>.</summary>
        private static void CreditStructures(string? source, int destroyed)
        {
            if (destroyed <= 0) return;
            Find.Storyteller?.resourceImpact.RecordStructuresDestroyed(source, destroyed);
        }

        // ---- shared with ManhunterPack in rule, not in code (CLAUDE.md: add a file, don't share one) ----

        /// <summary>The map this quake physically shakes, or null when it must resolve abstractly — the same
        /// question <see cref="IncidentWorker_ManhunterPack.ArrivalMapFor"/> asks, of the same authority.</summary>
        private static Map.Map? ArrivalMapFor(CivilizationTarget civ, SimWorld.World.Settlement? settlement)
        {
            if (settlement == null) return civ.MapFor(null);
            return IsWatched(settlement) ? civ.MapFor(settlement) : null;
        }

        private static bool IsWatched(SimWorld.World.Settlement settlement) =>
            Find.God.Attention.FocusedTile == settlement.tile;

        /// <summary>What an earthquake did, gathered as it goes so the letter can say it and the arms of the
        /// incident share one shape. Fallen structures are kept in the order they were first lost (which is
        /// <see cref="KnownStructureDefs"/>'s order, so deterministic); pawns in the order they were found.</summary>
        private sealed class Toll
        {
            private readonly List<KeyValuePair<ThingDef, int>> fallen = new List<KeyValuePair<ThingDef, int>>();

            /// <summary>Who was a citizen of the shaken settlement when the ground began to move — taken
            /// first, because a citizen who dies is dropped from the roster. A bed cell can hold a muffalo or
            /// a stranger as well; they are hurt like anyone, and the letter is about the player's people.</summary>
            private readonly HashSet<Pawn> citizens;

            public Toll(SimWorld.World.Settlement settlement)
            {
                citizens = new HashSet<Pawn>(settlement.Citizens);
            }

            /// <summary>Someone the player would call their people: a citizen of the settlement, or a colonist or
            /// prisoner of the player's. Not the player's livestock, which <c>PawnUtility.IsColonist</c> also
            /// answers yes for — a muffalo hurt in a bed cell is hurt, and is not "caught in the collapse and
            /// hurt" in a sentence about people.</summary>
            public bool IsOurs(Pawn pawn) =>
                pawn.RaceProps.Humanlike && (citizens.Contains(pawn) || PawnUtility.ShouldSendNotificationAbout(pawn));

            public int StructuresDestroyed { get; private set; }

            public IReadOnlyList<KeyValuePair<ThingDef, int>> Fallen => fallen;

            /// <summary>Caught in the collapse and alive when it finished, with a wound to show for it — perhaps downed.</summary>
            public readonly List<Pawn> Hurt = new List<Pawn>();

            /// <summary>Caught in the collapse and dead of the blow itself.</summary>
            public readonly List<Pawn> Killed = new List<Pawn>();

            /// <summary>Caught in the collapse, woken by it, and not wounded: armour turned the blow away, or
            /// there was no part left to strike.</summary>
            public readonly List<Pawn> Spared = new List<Pawn>();

            public void Fell(ThingDef def, int count)
            {
                if (count <= 0) return;
                StructuresDestroyed += count;
                for (int i = 0; i < fallen.Count; i++)
                {
                    if (!ReferenceEquals(fallen[i].Key, def)) continue;
                    fallen[i] = new KeyValuePair<ThingDef, int>(def, fallen[i].Value + count);
                    return;
                }
                fallen.Add(new KeyValuePair<ThingDef, int>(def, count));
            }
        }

        /// <summary>
        /// The player is told either way — an earthquake nobody is told about is indistinguishable from
        /// nothing happening, and a defect that costs nothing when unwatched teaches the player nothing about
        /// where to look next time. And told <i>what</i>: which kinds of structure came down and how many of
        /// each, who was caught in it and is hurt, who was caught and came away unhurt, and who is dead — by
        /// name, each a look-target. The
        /// count of the dead is the pawns that actually died, never the ledger's delta: a settlement on no
        /// civilization roster loses people the ledger does not count, and the player is owed the truth either
        /// way.
        /// </summary>
        private static void SendLetter(SimWorld.World.Settlement settlement, Toll toll, bool watched)
        {
            string where = string.IsNullOrEmpty(settlement.name) ? "a settlement" : settlement.name;
            string text;
            if (watched)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("An earthquake tears through ").Append(where).Append(". ");
                sb.Append(toll.StructuresDestroyed > 0
                    ? "It brings down " + DescribeFallen(toll.Fallen) + "."
                    : "Nothing you had built comes down.");
                if (toll.Hurt.Count > 0)
                {
                    sb.Append(' ').Append(NameList(toll.Hurt))
                      .Append(toll.Hurt.Count == 1 ? " was caught in the collapse and is hurt." : " were caught in the collapse and are hurt.");
                }
                if (toll.Spared.Count > 0)
                {
                    sb.Append(' ').Append(NameList(toll.Spared))
                      .Append(toll.Spared.Count == 1 ? " was caught in the collapse and came away unhurt." : " were caught in the collapse and came away unhurt.");
                }
                if (toll.Killed.Count > 0)
                {
                    sb.Append(' ').Append(NameList(toll.Killed))
                      .Append(toll.Killed.Count == 1 ? " was killed in the collapse." : " were killed in the collapse.");
                }
                text = sb.ToString();
            }
            else
            {
                text = toll.StructuresDestroyed > 0
                    ? string.Format(CultureInfo.InvariantCulture,
                        "Word reaches you late: an earthquake struck {0} while your attention was elsewhere, destroying {1}.",
                        where, DescribeFallen(toll.Fallen))
                    : string.Format(CultureInfo.InvariantCulture,
                        "Word reaches you late: an earthquake struck {0} while your attention was elsewhere. Nothing you had built came down.",
                        where);
            }

            List<string>? lookTargets = null;
            if (toll.Hurt.Count + toll.Spared.Count + toll.Killed.Count > 0)
            {
                lookTargets = new List<string>();
                for (int i = 0; i < toll.Hurt.Count; i++) lookTargets.Add(toll.Hurt[i].GetUniqueLoadID());
                for (int i = 0; i < toll.Spared.Count; i++) lookTargets.Add(toll.Spared[i].GetUniqueLoadID());
                for (int i = 0; i < toll.Killed.Count; i++) lookTargets.Add(toll.Killed[i].GetUniqueLoadID());
            }

            Find.LetterStack?.ReceiveLetter("Earthquake: " + where, text, LetterDefOf.NegativeEvent, lookTargets);
        }

        /// <summary>"9 walls, 2 beds and 1 storage hut" — every def's own label, counted, in the order lost.</summary>
        private static string DescribeFallen(IReadOnlyList<KeyValuePair<ThingDef, int>> fallen)
        {
            var parts = new List<string>(fallen.Count);
            for (int i = 0; i < fallen.Count; i++)
            {
                string label = fallen[i].Key.label ?? fallen[i].Key.defName;
                parts.Add(fallen[i].Value.ToString(CultureInfo.InvariantCulture) + " " + label + (fallen[i].Value == 1 ? "" : "s"));
            }
            return JoinWithAnd(parts);
        }

        /// <summary>The most names a letter spells out before it says "and N others"; a bed-full of sleepers is
        /// never more than a handful, but a letter that listed forty names would stop being read.</summary>
        private const int MaxNamesInLetter = 6;

        internal static string NameList(IReadOnlyList<Pawn> pawns)
        {
            var names = new List<string>();
            int shown = Math.Min(pawns.Count, MaxNamesInLetter);
            for (int i = 0; i < shown; i++) names.Add(pawns[i].Label);
            int more = pawns.Count - shown;
            if (more > 0) names.Add(more.ToString(CultureInfo.InvariantCulture) + (more == 1 ? " other" : " others"));
            return JoinWithAnd(names);
        }

        private static string JoinWithAnd(IReadOnlyList<string> parts)
        {
            if (parts.Count == 0) return "";
            if (parts.Count == 1) return parts[0];
            return string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[parts.Count - 1];
        }
    }
}
