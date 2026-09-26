using SimWorld.Factions;
using SimWorld.Sim;

namespace SimWorld.AI.Group
{
    /// <summary>
    /// What a <see cref="Lord"/> is on this map to do, as a graph of toils (RimWorld:
    /// <c>Verse.AI.Group.LordJob</c>). Saved with the lord; the graph it builds is not, and is rebuilt on load.
    /// </summary>
    public abstract class LordJob : IExposable
    {
        public Lord? lord;

        /// <summary>Whether <see cref="Lord.SetJob"/> may add the panic-flee toil to this job's graph.
        /// RimWorld's default, and true for every job this port has.</summary>
        public virtual bool AddFleeToil => true;

        /// <summary>Builds the graph. Runs inside <see cref="Rand.PushState(int)"/> seeded from the lord, as
        /// RimWorld's runs inside <c>Rand.Seed = loadID * 193</c>, so any roll made here is the same on every
        /// load and moves nobody else's dice.</summary>
        public abstract StateGraph CreateGraph();

        public virtual void ExposeData()
        {
        }
    }

    /// <summary>
    /// A raid (RimWorld: <c>RimWorld.LordJob_AssaultColony</c>). Read from two decompiles: RimWorld 1.0
    /// (josh-m/RW-Decompile, <c>RimWorld/LordJob_AssaultColony.cs</c>) and 1.6
    /// (32bitx64bit/RW-Decompiled-1.6, same path). The part ported is identical in both:
    /// <list type="bullet">
    /// <item>Toils: <see cref="LordToil_AssaultColony"/> (the start) and <see cref="LordToil_ExitMap"/>.</item>
    /// <item>For a <see cref="FactionDef.humanlikeFaction"/> with <see cref="canTimeoutOrFlee"/>:
    /// assault → exit on <see cref="Trigger_TicksPassed"/> after <see cref="AssaultTimeBeforeGiveUp"/>, with
    /// the message <c>MessageRaidersGivenUpLeaving</c>, "{0} from {1} have given up and are leaving."</item>
    /// <item>The flee — any toil → <see cref="LordToil_PanicFlee"/> on <see cref="Trigger_FractionPawnsLost"/>
    /// — is not in this graph in RimWorld either; <see cref="Lord.SetJob"/> adds it to every lord job.</item>
    /// </list>
    /// <b>Not ported, and why.</b> The second timeout-or-flee transition, assault → exit on
    /// <c>Trigger_FractionColonyDamageTaken(0.25~0.35, 900)</c> ("satisfied with the damage done"), measures
    /// the colony's buildings' hit points against their total when the raid arrived; this port has no
    /// per-faction building ownership to total (see <c>Duties_Assault.xml</c> on why a raid cannot trash the
    /// colony either), so there is nothing for it to read. The kidnap and steal subgraphs need carrying a pawn
    /// or an item off the map, which nothing here does. The sapper toil and its timeout
    /// (<c>SapTimeBeforeGiveUp</c>) need digging. <c>Trigger_BecameNonHostileToPlayer</c> needs faction
    /// relation changes to signal lords, and nothing does yet; a raid whose faction makes peace mid-fight
    /// still leaves when the timeout comes.
    /// </summary>
    public sealed class LordJob_AssaultColony : LordJob
    {
        /// <summary>RimWorld's <c>AssaultTimeBeforeGiveUp</c>, the same in 1.0 and 1.6: how long a raid
        /// assaults before it gives up and goes home, in ticks (about 10 to 15 in-game hours).</summary>
        public static readonly IntRange AssaultTimeBeforeGiveUp = new IntRange(26000, 38000);

        private Faction? assaulterFaction;

        /// <summary>RimWorld's flag of the same name. Every raid strategy RimWorld makes a
        /// <c>LordJob_AssaultColony</c> for passes true.</summary>
        public bool canTimeoutOrFlee = true;

        /// <summary>For Scribe.</summary>
        public LordJob_AssaultColony()
        {
        }

        public LordJob_AssaultColony(Faction assaulterFaction, bool canTimeoutOrFlee = true)
        {
            this.assaulterFaction = assaulterFaction;
            this.canTimeoutOrFlee = canTimeoutOrFlee;
        }

        public Faction? AssaulterFaction => assaulterFaction;

        public override StateGraph CreateGraph()
        {
            var graph = new StateGraph();
            var assault = new LordToil_AssaultColony();
            graph.AddToil(assault);
            var exit = new LordToil_ExitMap();
            graph.AddToil(exit);

            if (assaulterFaction != null && assaulterFaction.def.humanlikeFaction && canTimeoutOrFlee)
            {
                var giveUp = new Transition(assault, exit);
                giveUp.AddTrigger(new Trigger_TicksPassed(Rand.Range(AssaultTimeBeforeGiveUp)));
                giveUp.AddPreAction(new TransitionAction_Message(
                    "Raiders leaving",
                    TransitionAction_Message.PawnsPluralCap(assaulterFaction) + " from " + assaulterFaction.name
                    + " have given up and are leaving."));
                graph.AddTransition(giveUp);
            }
            return graph;
        }

        public override void ExposeData()
        {
            Scribe_References.Look(ref assaulterFaction, "assaulterFaction");
            Scribe_Values.Look(ref canTimeoutOrFlee, "canTimeoutOrFlee", true);
        }
    }
}
