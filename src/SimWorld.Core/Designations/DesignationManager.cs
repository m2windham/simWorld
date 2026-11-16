using System;
using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Designations
{
    /// <summary>
    /// Every designation on one map (RimWorld: <c>Verse.DesignationManager</c>), kept in a list per def as
    /// RimWorld 1.4 keeps them, so a giver that wants the Mine designations reads exactly those and nothing else:
    /// <see cref="AI.WorkGiver_Miner"/> scans this list, not the mountain. In RimWorld the player places them;
    /// here a settlement does (<see cref="AI.MiningInitiative"/>).
    /// </summary>
    public sealed class DesignationManager
    {
        private List<Designation> allDesignations = new List<Designation>();
        private readonly Dictionary<DesignationDef, List<Designation>> byDef = new Dictionary<DesignationDef, List<Designation>>();

        private static readonly IReadOnlyList<Designation> None = Array.Empty<Designation>();

        public IReadOnlyList<Designation> AllDesignations => allDesignations;

        public void AddDesignation(Designation newDes)
        {
            if (newDes == null) throw new ArgumentNullException(nameof(newDes));
            if (newDes.def.targetType == TargetType.Cell && DesignationAt(newDes.target.Cell, newDes.def) != null)
            {
                throw new InvalidOperationException("Tried to double-add designation at the same cell: " + newDes);
            }
            if (newDes.def.targetType == TargetType.Thing && newDes.target.Thing != null && DesignationOn(newDes.target.Thing, newDes.def) != null)
            {
                throw new InvalidOperationException("Tried to double-add designation on the same thing: " + newDes);
            }
            newDes.designationManager = this;
            allDesignations.Add(newDes);
            ListFor(newDes.def).Add(newDes);
        }

        public void RemoveDesignation(Designation des)
        {
            if (des == null) throw new ArgumentNullException(nameof(des));
            allDesignations.Remove(des);
            if (byDef.TryGetValue(des.def, out List<Designation>? list)) list.Remove(des);
        }

        public void TryRemoveDesignation(IntVec3 c, DesignationDef def)
        {
            Designation? des = DesignationAt(c, def);
            if (des != null) RemoveDesignation(des);
        }

        public Designation? DesignationAt(IntVec3 c, DesignationDef def)
        {
            IReadOnlyList<Designation> list = SpawnedDesignationsOfDef(def);
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].target.HasThing && list[i].target.Cell == c) return list[i];
            }
            return null;
        }

        public Designation? DesignationOn(Thing t, DesignationDef def)
        {
            IReadOnlyList<Designation> list = SpawnedDesignationsOfDef(def);
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i].target.Thing, t)) return list[i];
            }
            return null;
        }

        /// <summary>In the order they were added.</summary>
        public IReadOnlyList<Designation> SpawnedDesignationsOfDef(DesignationDef def) =>
            byDef.TryGetValue(def, out List<Designation>? list) ? list : None;

        public bool AnySpawnedDesignationOfDef(DesignationDef def) => SpawnedDesignationsOfDef(def).Count > 0;

        public void RemoveAllDesignationsOfDef(DesignationDef def)
        {
            if (!byDef.TryGetValue(def, out List<Designation>? list)) return;
            for (int i = list.Count - 1; i >= 0; i--) allDesignations.Remove(list[i]);
            list.Clear();
        }

        /// <summary>A building left these cells. Every cell designation whose def goes with its building goes with
        /// it (RimWorld: <c>Notify_BuildingDespawned</c>, called from <c>Building.DeSpawn</c>), and so does every
        /// designation aimed at the building itself.</summary>
        public void Notify_BuildingDespawned(Thing b, CellRect occupied)
        {
            for (int i = allDesignations.Count - 1; i >= 0; i--)
            {
                Designation des = allDesignations[i];
                bool goes = des.target.HasThing
                    ? ReferenceEquals(des.target.Thing, b)
                    : des.def.removeIfBuildingDespawned && occupied.Contains(des.target.Cell);
                if (goes) RemoveDesignation(des);
            }
        }

        public void ExposeData()
        {
            List<Designation>? list = allDesignations;
            Scribe_Collections.Look(ref list, "allDesignations", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars) allDesignations = list ?? new List<Designation>();

            // Indexed once every target has resolved, dropping any whose def or target did not survive the load.
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                var loaded = allDesignations;
                allDesignations = new List<Designation>();
                byDef.Clear();
                foreach (Designation des in loaded)
                {
                    if (des?.def != null && des.target.IsValid) AddDesignation(des);
                }
            }
        }

        private List<Designation> ListFor(DesignationDef def)
        {
            if (!byDef.TryGetValue(def, out List<Designation>? list))
            {
                list = new List<Designation>();
                byDef[def] = list;
            }
            return list;
        }
    }
}
