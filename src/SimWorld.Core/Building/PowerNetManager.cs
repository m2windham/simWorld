using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// Builds and maintains every <see cref="PowerNet"/> on one map as a graph of cardinally-adjacent
    /// <see cref="CompPower"/> connectors (RimWorld: <c>RimWorld.PowerNetManager</c>). <b>Incremental by
    /// construction, not by later optimisation</b>: a spawn only ever inspects that one Thing's immediate
    /// neighbours (see <see cref="Notify_CompSpawned"/>); a despawn only re-floods the one net that connector
    /// belonged to (see <see cref="Notify_CompDeSpawned"/>), never the whole map. See this module's report
    /// for the measured cost of both.
    /// </summary>
    public sealed class PowerNetManager
    {
        private readonly Map.Map map;
        private readonly List<PowerNet> nets = new List<PowerNet>();

        public IReadOnlyList<PowerNet> Nets => nets;

        public PowerNetManager(Map.Map map)
        {
            this.map = map;
        }

        public void PowerNetManagerTick()
        {
            for (int i = 0; i < nets.Count; i++) nets[i].PowerNetTick();
        }

        /// <summary>
        /// Wires a newly-spawned comp into the net graph: joins a single neighbouring net outright, merges
        /// every distinct net found adjacent (a comp bridging two previously-separate nets), or starts a new
        /// one-comp net if it has no power-comp neighbour at all. RimWorld's real conduits connect only
        /// cardinally, not diagonally; kept here (see <see cref="GenAdj.CellsAdjacentCardinal"/>).
        /// </summary>
        public void Notify_CompSpawned(CompPower comp)
        {
            var neighborNets = new List<PowerNet>();
            foreach (CompPower neighbor in AdjacentPowerComps(comp))
            {
                if (neighbor.powerNet != null && !neighborNets.Contains(neighbor.powerNet))
                {
                    neighborNets.Add(neighbor.powerNet);
                }
            }

            if (neighborNets.Count == 0)
            {
                var net = new PowerNet();
                net.connectors.Add(comp);
                comp.powerNet = net;
                nets.Add(net);
                return;
            }

            PowerNet target = neighborNets[0];
            for (int i = 1; i < neighborNets.Count; i++)
            {
                PowerNet absorbed = neighborNets[i];
                for (int c = 0; c < absorbed.connectors.Count; c++)
                {
                    absorbed.connectors[c].powerNet = target;
                    target.connectors.Add(absorbed.connectors[c]);
                }
                nets.Remove(absorbed);
            }
            target.connectors.Add(comp);
            comp.powerNet = target;
        }

        /// <summary>
        /// Drops a despawning comp from its net. If that leaves the net empty it is discarded outright;
        /// otherwise the remaining members are re-flood-filled — proportional to that one net's size, never
        /// the whole map — since removing a bridging comp can split one net into several.
        /// </summary>
        public void Notify_CompDeSpawned(CompPower comp)
        {
            PowerNet? net = comp.powerNet;
            if (net == null) return;
            net.connectors.Remove(comp);
            comp.powerNet = null;
            nets.Remove(net);

            if (net.connectors.Count == 0) return;

            var remaining = new HashSet<CompPower>(net.connectors);
            while (remaining.Count > 0)
            {
                CompPower seed = First(remaining);
                List<CompPower> component = FloodFillWithin(seed, remaining);
                var newNet = new PowerNet();
                for (int i = 0; i < component.Count; i++)
                {
                    component[i].powerNet = newNet;
                    newNet.connectors.Add(component[i]);
                    remaining.Remove(component[i]);
                }
                nets.Add(newNet);
            }
        }

        private List<CompPower> FloodFillWithin(CompPower seed, HashSet<CompPower> universe)
        {
            var visited = new List<CompPower>();
            var seen = new HashSet<CompPower> { seed };
            var queue = new Queue<CompPower>();
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                CompPower cur = queue.Dequeue();
                visited.Add(cur);
                foreach (CompPower neighbor in AdjacentPowerComps(cur))
                {
                    if (universe.Contains(neighbor) && seen.Add(neighbor)) queue.Enqueue(neighbor);
                }
            }
            return visited;
        }

        private IEnumerable<CompPower> AdjacentPowerComps(CompPower comp)
        {
            foreach (IntVec3 c in GenAdj.CellsAdjacentCardinal(comp.parent.Position))
            {
                if (!GenGrid.InBounds(c, map)) continue;
                IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(c);
                for (int i = 0; i < here.Count; i++)
                {
                    if (here[i] is ThingWithComps twc)
                    {
                        CompPower? other = twc.GetComp<CompPower>();
                        if (other != null) yield return other;
                    }
                }
            }
        }

        private static CompPower First(HashSet<CompPower> set)
        {
            foreach (CompPower c in set) return c;
            throw new System.InvalidOperationException("Empty set.");
        }
    }
}
