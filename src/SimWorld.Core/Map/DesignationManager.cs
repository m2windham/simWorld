using System;
using System.Collections.Generic;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Map
{
    /// <summary>
    /// Every <see cref="Designation"/> on one map (RimWorld: <c>Verse.DesignationManager</c>). Constructed
    /// with the map's other managers (<c>Map.InitializeAIManagers</c>) and saved by <c>Map.ExposeData</c> the
    /// same way <see cref="Building.ZoneManager"/> is: the manager always exists already, so it cannot go
    /// through Scribe's parameterless-constructor path and its <see cref="ExposeData"/> is called directly.
    ///
    /// <para/><b>What this adds to the game, in one line:</b> a place to write down "dig that" so that a
    /// citizen's work search reads intent instead of inventing it. Before it, <c>AI.WorkGiver_Miner</c> mined
    /// every mineable edifice on the map, which on a seed-777 settlement took 12,985 limestone cells to 242
    /// in six days and left no mountain standing anywhere.
    ///
    /// <para/><b>Faithful to RimWorld's shape, with one difference in cost.</b> RimWorld finds a cell mark
    /// (<see cref="DesignationAt"/>) by scanning every designation on the map. That is fine for a player
    /// who draws tens of cells and a trap for one who drags a rectangle over a mountain — every candidate
    /// the work scan considers asks it, so a scan of N marks is N² — so cell-targeted marks are also kept in
    /// a dictionary keyed by (def, cell). The answers are identical; only the lookup is cheaper. The index is
    /// rebuilt from the list on load rather than saved, so there is nothing for it to disagree with.
    ///
    /// <para/><b>Not ported:</b> <c>DrawDesignations</c> (the host draws from a <c>defName</c>), the
    /// <c>SetForbidden(false)</c> RimWorld applies to a Thing a mark is added to (this port has no forbidden
    /// flag), and the mote it throws when a mark is added.
    /// </summary>
    public sealed class DesignationManager
    {
        private readonly Map map;
        private List<Designation> allDesignations = new List<Designation>();
        private readonly Dictionary<(DesignationDef def, int cellIndex), Designation> byCell =
            new Dictionary<(DesignationDef def, int cellIndex), Designation>();

        public DesignationManager(Map map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
        }

        /// <summary>Every designation on this map, in the order they were added (RimWorld:
        /// <c>DesignationManager.allDesignations</c>, read-only here — adding and removing go through
        /// the methods below so the cell index cannot drift from the list).</summary>
        public IReadOnlyList<Designation> AllDesignations => allDesignations;

        // ---- adding ----

        /// <summary>
        /// Puts a mark on the map (RimWorld: <c>DesignationManager.AddDesignation</c>). A second mark of the
        /// same def on the same cell (or the same Thing) is refused and nothing changes — RimWorld logs an
        /// error and returns; this returns false instead, because a caller drawing a rectangle over cells
        /// that are already marked is doing something ordinary, not something wrong.
        /// </summary>
        public bool AddDesignation(Designation newDes)
        {
            if (newDes == null) throw new ArgumentNullException(nameof(newDes));
            if (newDes.def == null) throw new ArgumentException("A designation needs a def.", nameof(newDes));

            if (newDes.def.targetType == TargetType.Cell)
            {
                IntVec3 cell = newDes.target.Cell;
                if (!GenGrid.InBounds(cell, map)) return false;
                if (DesignationAt(cell, newDes.def) != null) return false;
                byCell[(newDes.def, map.cellIndices.CellToIndex(cell))] = newDes;
            }
            else
            {
                if (!newDes.target.HasThing) return false;
                if (DesignationOn(newDes.target.Thing!, newDes.def) != null) return false;
            }

            allDesignations.Add(newDes);
            newDes.designationManager = this;
            return true;
        }

        // ---- asking ----

        /// <summary>The first designation of any kind on <paramref name="t"/>, or null.</summary>
        public Designation? DesignationOn(Thing t)
        {
            for (int i = 0; i < allDesignations.Count; i++)
            {
                if (ReferenceEquals(allDesignations[i].target.Thing, t)) return allDesignations[i];
            }
            return null;
        }

        /// <summary>The <paramref name="def"/> designation on <paramref name="t"/>, or null. A cell-indexed
        /// def has no Thing to look up; RimWorld logs an error, this simply finds nothing.</summary>
        public Designation? DesignationOn(Thing t, DesignationDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (def.targetType == TargetType.Cell) return null;
            for (int i = 0; i < allDesignations.Count; i++)
            {
                Designation d = allDesignations[i];
                if (ReferenceEquals(d.target.Thing, t) && d.def == def) return d;
            }
            return null;
        }

        /// <summary>The <paramref name="def"/> designation on <paramref name="c"/>, or null. A Thing-indexed
        /// def has no cell to look up; RimWorld logs an error, this simply finds nothing.</summary>
        public Designation? DesignationAt(IntVec3 c, DesignationDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (def.targetType == TargetType.Thing) return null;
            if (!GenGrid.InBounds(c, map)) return null;
            return byCell.TryGetValue((def, map.cellIndices.CellToIndex(c)), out Designation? found) ? found : null;
        }

        /// <summary>Every designation on <paramref name="t"/>.</summary>
        public IEnumerable<Designation> AllDesignationsOn(Thing t)
        {
            int count = allDesignations.Count;
            for (int i = 0; i < count && i < allDesignations.Count; i++)
            {
                if (ReferenceEquals(allDesignations[i].target.Thing, t)) yield return allDesignations[i];
            }
        }

        /// <summary>Every designation on <paramref name="c"/>, whatever its kind.</summary>
        public IEnumerable<Designation> AllDesignationsAt(IntVec3 c)
        {
            int count = allDesignations.Count;
            for (int i = 0; i < count && i < allDesignations.Count; i++)
            {
                Designation des = allDesignations[i];
                if (OnThisMap(des) && des.target.Cell == c) yield return des;
            }
        }

        /// <summary>Whether a mark with no Thing under it sits on <paramref name="c"/>.</summary>
        public bool HasMapDesignationAt(IntVec3 c)
        {
            for (int i = 0; i < allDesignations.Count; i++)
            {
                Designation d = allDesignations[i];
                if (!d.target.HasThing && d.target.Cell == c) return true;
            }
            return false;
        }

        /// <summary>
        /// Every <paramref name="def"/> designation whose target is on this map (RimWorld:
        /// <c>SpawnedDesignationsOfDef</c>) — the one question <c>AI.WorkGiver_Miner</c> asks. Safe to
        /// remove marks while iterating: the walk stops at the count it started with and re-checks the list.
        /// </summary>
        public IEnumerable<Designation> SpawnedDesignationsOfDef(DesignationDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            int count = allDesignations.Count;
            for (int i = 0; i < count && i < allDesignations.Count; i++)
            {
                Designation des = allDesignations[i];
                if (des.def == def && OnThisMap(des)) yield return des;
            }
        }

        // ---- removing ----

        public void RemoveDesignation(Designation des)
        {
            if (des == null) throw new ArgumentNullException(nameof(des));
            if (!allDesignations.Remove(des)) return;
            if (des.def.targetType == TargetType.Cell && des.target.Cell.IsValid && GenGrid.InBounds(des.target.Cell, map))
            {
                var key = (des.def, map.cellIndices.CellToIndex(des.target.Cell));
                if (byCell.TryGetValue(key, out Designation? indexed) && ReferenceEquals(indexed, des)) byCell.Remove(key);
            }
        }

        public void TryRemoveDesignation(IntVec3 c, DesignationDef def)
        {
            Designation? designation = DesignationAt(c, def);
            if (designation != null) RemoveDesignation(designation);
        }

        public void TryRemoveDesignationOn(Thing t, DesignationDef def)
        {
            Designation? designation = DesignationOn(t, def);
            if (designation != null) RemoveDesignation(designation);
        }

        /// <summary>Removes every designation on <paramref name="t"/>. With <paramref name="standardCanceling"/>
        /// only those whose def says <see cref="DesignationDef.designateCancelable"/>.</summary>
        public void RemoveAllDesignationsOn(Thing t, bool standardCanceling = false)
        {
            for (int i = allDesignations.Count - 1; i >= 0; i--)
            {
                Designation d = allDesignations[i];
                if (standardCanceling && !d.def.designateCancelable) continue;
                if (ReferenceEquals(d.target.Thing, t)) RemoveDesignation(d);
            }
        }

        public void RemoveAllDesignationsOfDef(DesignationDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            for (int i = allDesignations.Count - 1; i >= 0; i--)
            {
                if (allDesignations[i].def == def) RemoveDesignation(allDesignations[i]);
            }
        }

        /// <summary>
        /// A building left the map: takes away every mark in its footprint whose def asks to go with it
        /// (<see cref="DesignationDef.removeIfBuildingDespawned"/>). This is how a <c>Mine</c> mark ends —
        /// RimWorld calls it from the end of <c>Building.DeSpawn</c>; here
        /// <see cref="Mineable.DeSpawn"/> does, with the rect it read <i>before</i> it left, because a
        /// de-spawned Thing no longer knows where it was.
        /// </summary>
        public void Notify_BuildingDespawned(CellRect footprint)
        {
            for (int i = allDesignations.Count - 1; i >= 0; i--)
            {
                Designation d = allDesignations[i];
                if (d.def.removeIfBuildingDespawned && footprint.Contains(d.target.Cell)) RemoveDesignation(d);
            }
        }

        /// <summary>Same, for a building that has not yet left — its footprint is read from where it stands.</summary>
        public void Notify_BuildingDespawned(Thing b)
        {
            if (b == null) throw new ArgumentNullException(nameof(b));
            Notify_BuildingDespawned(b.OccupiedRect());
        }

        private bool OnThisMap(Designation des) =>
            !des.target.HasThing || ReferenceEquals(des.target.Thing!.Map, map);

        // ---- Scribe ----

        /// <summary>
        /// Saves and loads every designation, deep (RimWorld: <c>DesignationManager.ExposeData</c>). Called
        /// from <c>Map.ExposeData</c> on every Scribe phase. A mark with no def, or whose target does not
        /// match its def's <see cref="DesignationDef.targetType"/> (a cell-indexed mark with no valid cell, a
        /// Thing-indexed one with no Thing), cannot be acted on and is dropped on load with an error rather
        /// than carried — RimWorld's own post-load checks.
        /// </summary>
        public void ExposeData()
        {
            List<Designation>? saved = Scribe.mode == LoadSaveMode.Saving ? new List<Designation>(allDesignations) : null;
            Scribe_Collections.Look(ref saved, "allDesignations", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                allDesignations = saved ?? new List<Designation>();
                if (allDesignations.RemoveAll(x => x == null) != 0)
                {
                    Scribe.loader.Error("Some designations were null after loading.");
                }
                if (allDesignations.RemoveAll(x => x.def == null) != 0)
                {
                    Scribe.loader.Error("Some designations had a null def after loading.");
                }
                for (int i = 0; i < allDesignations.Count; i++) allDesignations[i].designationManager = this;
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                for (int j = allDesignations.Count - 1; j >= 0; j--)
                {
                    Designation d = allDesignations[j];
                    if (d.def.targetType == TargetType.Cell)
                    {
                        if (d.target.HasThing || !d.target.Cell.IsValid || !GenGrid.InBounds(d.target.Cell, map))
                        {
                            Scribe.loader.Error("Cell-needing designation " + d + " had no cell target. Removing.");
                            allDesignations.RemoveAt(j);
                        }
                    }
                    else if (!d.target.HasThing)
                    {
                        Scribe.loader.Error("Thing-needing designation " + d + " had no thing target. Removing.");
                        allDesignations.RemoveAt(j);
                    }
                }

                byCell.Clear();
                for (int i = 0; i < allDesignations.Count; i++)
                {
                    Designation d = allDesignations[i];
                    if (d.def.targetType != TargetType.Cell) continue;
                    byCell[(d.def, map.cellIndices.CellToIndex(d.target.Cell))] = d;
                }
            }
        }
    }
}
