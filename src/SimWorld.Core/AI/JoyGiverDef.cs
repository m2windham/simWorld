using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Needs;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// One way of taking recreation (RimWorld: <c>RimWorld.JoyGiverDef</c>). A <see cref="JoyGiver"/> worker
    /// turns it into a <see cref="Job"/> for a particular pawn on a particular map; <see cref="JobGiver_GetJoy"/>
    /// picks between the defs by weight.
    ///
    /// <para/><b>Why this Def carries the joy numbers and RimWorld's does not.</b> In RimWorld
    /// <c>joyKind</c>, <c>joyGainRate</c> and <c>joyDuration</c> live on the <c>JobDef</c>, and
    /// <c>JoyUtility.JoyTickCheckEnd</c> reads them off <c>pawn.CurJob.def</c>. Here they live on the
    /// <see cref="JoyGiverDef"/> and <see cref="JoyUtility.GiverForJob"/> maps a running job back to the
    /// giver that authored it. The reason is this repository's own merge rule (CLAUDE.md, "Add a file rather
    /// than edit a shared one"): putting them on <see cref="JobDef"/> means editing <c>AI/JobDef.cs</c>, and
    /// a new Def type in a new file cannot conflict with anyone. The numbers are still authored in content,
    /// exactly as RimWorld authors them, and <see cref="ConfigErrors"/> keeps the <c>jobDef</c> ↔ giver
    /// relation one-to-one so the reverse lookup is total and unambiguous.
    /// </summary>
    public class JoyGiverDef : Def
    {
        /// <summary>The <see cref="JoyGiver"/> subclass that turns this def into a job.</summary>
        public Type giverClass = null!;

        /// <summary>Selection weight before tolerance damping (RimWorld: <c>JoyGiverDef.baseChance</c>).</summary>
        public float baseChance = 1f;

        /// <summary>Which flavour of recreation this is, and so which tolerance it builds.</summary>
        public JoyKindDef joyKind = null!;

        /// <summary>The job this giver issues. One giver per job def — see the class remarks.</summary>
        public JobDef jobDef = null!;

        /// <summary>
        /// Joy per tick, in RimWorld's own units: <see cref="JoyUtility.JoyGainPerTickAtRate1"/> multiplies
        /// it (RimWorld: <c>JobDef.joyGainRate</c>, read by <c>JoyUtility.JoyTickCheckEnd</c>).
        /// </summary>
        public float joyGainRate = 1f;

        /// <summary>How long a full session lasts, in ticks (RimWorld: <c>JobDef.joyDuration</c>). A session
        /// also ends early the moment the need is full.</summary>
        public int joyDuration = 4000;

        /// <summary>Only usable under open sky (RimWorld: <c>JoyGiverDef.unroofedOnly</c>), read by
        /// <see cref="JoyGiver_Skygaze"/>.</summary>
        public bool unroofedOnly;

        /// <summary>
        /// Whether this recreation needs a map at all. Not RimWorld's — every RimWorld pawn is spawned, so
        /// the question never arises there; the nearest thing it has is <c>JoyKindDef.needsThing</c>. It is
        /// what <see cref="Needs.AbstractRecreation"/> reads to decide which of these givers a citizen with
        /// no map can still take, exactly as <c>Economy.SettlementLarder</c> is the map-free half of eating.
        /// True for the givers whose only requirement is the sky, the ground or other people.
        /// </summary>
        public bool canDoWithoutMap;

        /// <summary>Capacities a pawn must still have to do this (RimWorld: <c>JoyGiverDef.requiredCapacities</c>).</summary>
        public List<PawnCapacityDef> requiredCapacities = new List<PawnCapacityDef>();

        // Two of RimWorld's fields are deliberately not here, rather than here and unread — this repo's
        // wiring audit treats a field no shipped Def sets, or that no line of src/ reads, as a dormant seam
        // to be explained rather than left lying about:
        //
        //   thingDefs      - the things a building-based giver searches for. Nothing in this port's content
        //                    is a recreation building (no chess table, no horseshoes pin, no gather spot),
        //                    so the field would have no reader and no writer. It comes back with the first
        //                    giver that needs it.
        //   pctPawnsEverDo - the fraction of the population who ever do a given activity at all. RimWorld
        //                    uses it to stop every colonist doing the same niche thing; with only three
        //                    givers here, excluding a pawn from one of them can leave that pawn a single
        //                    joy kind and so a guaranteed slide into boredom. It comes back with the fourth.

        private JoyGiver? workerInt;

        public JoyGiver Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (JoyGiver)Activator.CreateInstance(giverClass)!;
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (giverClass == null || !typeof(JoyGiver).IsAssignableFrom(giverClass))
            {
                yield return "giverClass must derive from JoyGiver.";
            }
            if (joyKind == null) yield return "joy giver has no joyKind.";
            if (jobDef == null) yield return "joy giver has no jobDef.";
            if (joyGainRate <= 0f) yield return "joy giver has a non-positive joyGainRate.";
            if (joyDuration <= 0) yield return "joy giver has a non-positive joyDuration.";
        }
    }

    /// <summary>
    /// Turns a <see cref="JoyGiverDef"/> into a job for one pawn (RimWorld: <c>RimWorld.JoyGiver</c>).
    /// Returning null means "not right now" — no map, nowhere to do it, weather against it — and
    /// <see cref="JobGiver_GetJoy"/> simply tries the next giver.
    /// </summary>
    public abstract class JoyGiver
    {
        public JoyGiverDef def = null!;

        /// <summary>Selection weight for this pawn right now, before tolerance damping (RimWorld:
        /// <c>JoyGiver.GetChance</c>).</summary>
        public virtual float GetChance(Pawn pawn) => def.baseChance;

        public abstract Job? TryGiveJob(Pawn pawn);

        /// <summary>Whether this pawn is physically able to do this at all (RimWorld:
        /// <c>JoyGiver.CanBeGivenTo</c>, minus the gene and outdoor-preference clauses this port has no data
        /// for).</summary>
        public virtual bool CanBeGivenTo(Pawn pawn) => MissingRequiredCapacity(pawn) == null;

        public PawnCapacityDef? MissingRequiredCapacity(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            for (int i = 0; i < def.requiredCapacities.Count; i++)
            {
                if (!pawn.health.capacities.CapableOf(def.requiredCapacities[i])) return def.requiredCapacities[i];
            }
            return null;
        }
    }
}
