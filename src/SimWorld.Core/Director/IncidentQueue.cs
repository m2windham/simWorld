using System.Collections.Generic;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>One incident scheduled to fire at a future tick (RimWorld: <c>Verse.QueuedIncident</c>).</summary>
    public sealed class QueuedIncident : IExposable
    {
        public IncidentDef def = null!;
        public float points;
        public bool forced;
        public IIncidentTarget? target;
        public int fireTick;

        public QueuedIncident()
        {
        }

        public QueuedIncident(FiringIncident firingIncident, int fireTick)
        {
            def = firingIncident.def;
            points = firingIncident.parms.points;
            forced = firingIncident.parms.forced;
            target = firingIncident.parms.target;
            this.fireTick = fireTick;
        }

        public FiringIncident ToFiringIncident() =>
            new FiringIncident(def, null, new IncidentParms { target = target!, points = points, forced = forced });

        public void ExposeData()
        {
            IncidentDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
            Scribe_Values.Look(ref points, "points");
            Scribe_Values.Look(ref forced, "forced");
            Scribe_References.Look(ref target, "target");
            Scribe_Values.Look(ref fireTick, "fireTick");
        }
    }

    /// <summary>
    /// Incidents selected to fire later rather than immediately (RimWorld: <c>Verse.IncidentQueue</c>) — a
    /// storyteller comp can schedule one ahead instead of firing it the moment it's picked.
    /// </summary>
    public sealed class IncidentQueue : IExposable
    {
        private List<QueuedIncident> queued = new List<QueuedIncident>();

        public IReadOnlyList<QueuedIncident> Queued => queued;

        public void Add(FiringIncident firingIncident, int fireTick)
        {
            queued.Add(new QueuedIncident(firingIncident, fireTick));
        }

        /// <summary>Fires every entry whose <see cref="QueuedIncident.fireTick"/> has arrived, through <see cref="Storyteller.TryFire"/>.</summary>
        public void IncidentQueueTick()
        {
            int now = Find.TickManager.TicksGame;
            for (int i = queued.Count - 1; i >= 0; i--)
            {
                if (queued[i].fireTick <= now)
                {
                    QueuedIncident q = queued[i];
                    queued.RemoveAt(i);
                    Find.Storyteller.TryFire(q.ToFiringIncident());
                }
            }
        }

        public void ExposeData()
        {
            List<QueuedIncident>? list = queued;
            Scribe_Collections.Look(ref list, "queued", LookMode.Deep);
            queued = list ?? new List<QueuedIncident>();
        }
    }
}
