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
    /// <para/><b>A pawn standing where a destroyed structure stood is crushed with it.</b> Not every collapse
    /// finds someone — a <see cref="Building.StoneWallMaterials.AllWallDefs"/> wall and a <c>StorageHut</c> are
    /// both <c>Impassable</c>, so this only ever finds someone in a destroyed <c>Bed</c> cell, the one known
    /// structure def a pawn can actually stand on — but when it does, the hit is certain death
    /// (<see cref="CollapseCrushDamage"/>, aimed at the core body part): a building actually
    /// falling directly onto somebody is not a coin flip, the same judgement
    /// <see cref="Building.RoofCollapserImmediate.MakeSure"/> already makes for a mountain roof. Those deaths
    /// are credited through <see cref="DeathLedger.RecordAttributed"/> from the ledger's own delta, copying
    /// <see cref="SettlementRaidResolver.Resolve"/>'s own approach — never from this worker's own count of
    /// who it hit — so attribution can never exceed the deaths the ledger actually counted: a settlement that
    /// belongs to no registered civilization reaches <c>Pawn_HealthTracker.Kill</c> and is deliberately not
    /// counted there, and this worker must not claim it anyway.
    ///
    /// <para/><b>Its dice are its own.</b> Every roll comes from <see cref="NamedRand"/>, never the ambient
    /// stream, and the target settlement and the severity are composed <i>before</i> the
    /// <see cref="Ablation.IsDisabled(string?)"/> check — see <see cref="IncidentWorker_ManhunterPack"/>'s own
    /// doc for why that ordering is not optional. Applying damage to a pawn can draw from the ambient stream
    /// internally (<c>Combat.ArmorUtility.ApplyArmor</c>'s deflection roll, for a citizen wearing apparel with
    /// a nonzero armor rating — the bare pawns this port's own tests build never trigger it, which is exactly
    /// why it would be easy to miss), so every such call is shielded with <see cref="Rand.PushState(int)"/> /
    /// <see cref="Rand.PopState"/>, seeded from this incident's own stream, exactly as
    /// <see cref="IncidentWorker_ManhunterPack.Generate"/> shields <c>PawnGenerator</c>.
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

        /// <summary>
        /// Damage dealt to whoever is standing where a destroyed structure stood, aimed at the core body part
        /// rather than rolled for coverage — comfortably past <c>Health.HealthTuning.LethalDamageThreshold</c>
        /// (150) regardless of which non-missing part a coverage roll would otherwise have picked, the same
        /// margin <c>Health.Tests.DeathAttributionTests.KilledBy</c> uses to guarantee a kill. A structure
        /// actually falling directly onto somebody is not a near-miss; see
        /// <see cref="Building.RoofCollapserImmediate.MakeSure"/> for the same judgement about a mountain roof.
        /// </summary>
        private const float CollapseCrushDamage = 500f;

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

            if (map != null)
            {
                (int destroyed, int killed) = ShakeMap(map, severity, rand, source);
                CreditStructures(source, destroyed);
                SendLetter(settlement, destroyed, killed, watched: true);
                return true;
            }

            int lost = ShakeSettlement(settlement, severity);
            CreditStructures(source, lost);
            SendLetter(settlement, lost, 0, watched: false);
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
        private static int ShakeSettlement(SimWorld.World.Settlement settlement, float severity)
        {
            int total = 0;
            IReadOnlyList<ThingDef> defs = KnownStructureDefs();
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef structureDef = defs[i];
                int have = settlement.StructureCount(structureDef);
                if (have <= 0) continue;

                int destroy = DestroyCount(have, severity);
                settlement.AddStructure(structureDef, -destroy);
                total += destroy;
            }
            return total;
        }

        // ---- the watched half: real Things, on a real map ----

        /// <summary>
        /// Destroys a <paramref name="severity"/> share of each known structure def actually standing on
        /// <paramref name="map"/>, and crushes anyone standing where one of them stood. Returns how many
        /// structures were destroyed and how many people were killed doing it.
        /// </summary>
        private static (int Destroyed, int Killed) ShakeMap(Map.Map map, float severity, RandomStream rand, string? source)
        {
            int destroyedTotal = 0;
            int killedTotal = 0;
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

                    IntVec3 cell = target.Position; // captured before TakeDamage can invalidate it (DeSpawn)
                    target.TakeDamage(new DamageInfo(RoofCollapseDefOf.Crush, target.MaxHitPoints));

                    // Credited from whether the Thing is actually gone, never assumed from the hit alone: a
                    // future ThingDef with destroyable=false would otherwise let this claim a destruction that
                    // never happened — the same "never over-claim" discipline CrushOnePawn applies to a death.
                    if (!target.Destroyed) continue;
                    destroyedTotal++;

                    killedTotal += CrushOccupants(map, cell, source, rand);
                }
            }
            return (destroyedTotal, killedTotal);
        }

        /// <summary>Crushes every living pawn standing on <paramref name="cell"/> — a destroyed structure's own
        /// footprint, so this only ever finds someone in a <c>Bed</c> (a wall and a <c>StorageHut</c> are both
        /// <c>Impassable</c>, never standable). Credits a death exactly when <see cref="DeathLedger.Total"/>
        /// actually grew from it, never from this method's own count of who it hit — see the class doc for why.</summary>
        private static int CrushOccupants(Map.Map map, IntVec3 cell, string? source, RandomStream rand)
        {
            var occupants = new List<Thing>(map.thingGrid.ThingsListAt(cell));
            int killed = 0;
            for (int i = 0; i < occupants.Count; i++)
            {
                if (!(occupants[i] is Pawn pawn) || pawn.Dead) continue;
                killed += CrushOnePawn(pawn, source, rand);
            }
            return killed;
        }

        private static int CrushOnePawn(Pawn pawn, string? source, RandomStream rand)
        {
            DeathLedger? ledger = Find.Storyteller?.deaths;
            int before = ledger?.Total ?? 0;

            // Shielded: DamageWorker_AddInjury -> Combat.ArmorUtility.ApplyArmor rolls Rand.Value once per
            // armor source with a nonzero rating, which a bare test pawn never has and a clothed citizen does.
            // Seeded from this incident's own stream so the callee stays deterministic and the ambient stream
            // comes back to exactly where it was — see IncidentWorker_ManhunterPack.Generate.
            Rand.PushState(rand.Int);
            try
            {
                BodyPartRecord? core = pawn.health.hediffSet.GetNotMissingParts().FirstOrDefault(p => p.IsCorePart);
                var dinfo = new DamageInfo(RoofCollapseDefOf.Crush, CollapseCrushDamage,
                    RoofCollapserImmediate.ThickRoofArmorPenetration, instigator: null, hitPart: core);
                RoofCollapseDefOf.Crush.Worker.Apply(dinfo, pawn);

                // Defensive, exactly as RoofCollapserImmediate.MakeSure is for a mountain collapse: certain
                // death must not quietly become "usually" because coverage found no valid part.
                if (!pawn.Dead) pawn.health.Kill(dinfo, null);
            }
            finally
            {
                Rand.PopState();
            }

            if (ledger == null || ledger.Total <= before) return 0;
            ledger.RecordAttributed(source);
            return 1;
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

        /// <summary>The player is told either way — an earthquake nobody is told about is indistinguishable
        /// from nothing happening, and a defect that costs nothing when unwatched teaches the player nothing
        /// about where to look next time.</summary>
        private static void SendLetter(SimWorld.World.Settlement settlement, int structuresLost, int killed, bool watched)
        {
            string where = string.IsNullOrEmpty(settlement.name) ? "a settlement" : settlement.name;
            string text;
            if (watched)
            {
                text = killed > 0
                    ? string.Format(CultureInfo.InvariantCulture,
                        "An earthquake tears through {0}. {1} structures come down, and {2} of your people are lost in the collapse.",
                        where, structuresLost, killed)
                    : string.Format(CultureInfo.InvariantCulture,
                        "An earthquake tears through {0}. {1} structures come down.", where, structuresLost);
            }
            else
            {
                text = string.Format(CultureInfo.InvariantCulture,
                    "Word reaches you late: an earthquake struck {0} while your attention was elsewhere, destroying {1} structures.",
                    where, structuresLost);
            }

            Find.LetterStack?.ReceiveLetter("Earthquake: " + where, text, LetterDefOf.NegativeEvent);
        }
    }
}
