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

        /// <summary>The designation kinds whose settlement initiative the player has switched off (see
        /// <see cref="SettlementInitiativeOn"/>). Empty by default: every initiative runs.</summary>
        private List<DesignationDef> initiativeOff = new List<DesignationDef>();

        /// <summary>How many designations aim at a Thing rather than a cell, so a despawn on a map with none —
        /// every map a player has not touched — costs one comparison (<see cref="Notify_ThingDespawned"/>).</summary>
        private int thingDesignationCount;

        public IReadOnlyList<Designation> AllDesignations => allDesignations;

        /// <summary>Bumped on every change a reader could see — a mark added, removed or claimed, an initiative
        /// switched. Not saved: the read model pairs it with the map view's session id, which a load renews.</summary>
        public int Version { get; private set; }

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
            if (newDes.target.HasThing) thingDesignationCount++;
            Version++;
        }

        public void RemoveDesignation(Designation des)
        {
            if (des == null) throw new ArgumentNullException(nameof(des));
            if (!allDesignations.Remove(des)) return;
            if (byDef.TryGetValue(des.def, out List<Designation>? list)) list.Remove(des);
            if (des.target.HasThing) thingDesignationCount--;
            Version++;
        }

        /// <summary>The player takes over a mark a settlement placed: it is now worked first and outlives the
        /// settlement's initiative being switched off. Not in RimWorld, which has no second author.</summary>
        public void ClaimForPlayer(Designation des)
        {
            if (des == null) throw new ArgumentNullException(nameof(des));
            if (des.source == DesignationSource.Player) return;
            des.source = DesignationSource.Player;
            Version++;
        }

        /// <summary>Every designation aimed at <paramref name="t"/>, of any def (RimWorld:
        /// <c>DesignationManager.RemoveAllDesignationsOn</c>).</summary>
        public void RemoveAllDesignationsOn(Thing t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            if (thingDesignationCount == 0) return;
            for (int i = allDesignations.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(allDesignations[i].target.Thing, t)) RemoveDesignation(allDesignations[i]);
            }
        }

        /// <summary>A non-building Thing left the map — a plant cut, an animal killed (its body is a corpse
        /// now), an item picked up — and every mark aimed at it goes with it (RimWorld: <c>Thing.DeSpawn</c>
        /// calls <c>RemoveAllDesignationsOn</c>). Buildings take <see cref="Notify_BuildingDespawned"/>.</summary>
        public void Notify_ThingDespawned(Thing t) => RemoveAllDesignationsOn(t);

        /// <summary>Whether the player has marked <paramref name="t"/> with <paramref name="def"/>.</summary>
        public bool HasPlayerDesignationOn(Thing t, DesignationDef def) =>
            DesignationOn(t, def)?.source == DesignationSource.Player;

        /// <summary>Whether any <paramref name="def"/> mark on this map was placed by the player — the cheap
        /// refusal every <see cref="AI.WorkGiver_PlayerDesignated"/> makes before scanning.</summary>
        public bool AnyPlayerDesignationOfDef(DesignationDef def)
        {
            IReadOnlyList<Designation> list = SpawnedDesignationsOfDef(def);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].source == DesignationSource.Player) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether this settlement's own initiative for <paramref name="def"/>'s kind of work runs — the
        /// player's standing rule "only do what I mark". On by default. Off, the initiative-driven fallback
        /// for that kind stops (<see cref="AI.MiningInitiative"/> places no Mine marks and withdraws its own;
        /// <see cref="AI.WorkGiver_PlantsCut"/>, <see cref="AI.WorkGiver_Hunt"/> and
        /// <see cref="AI.WorkGiver_TameAnimals"/> skip), and the player's own marks are still worked. Not in
        /// RimWorld, where nothing but the player ever designates.
        /// </summary>
        public bool SettlementInitiativeOn(DesignationDef def) => !initiativeOff.Contains(def);

        public void SetSettlementInitiative(DesignationDef def, bool on)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (SettlementInitiativeOn(def) == on) return;
            if (on) initiativeOff.Remove(def);
            else initiativeOff.Add(def);
            Version++;
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
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].target.HasThing) thingDesignationCount--;
                allDesignations.Remove(list[i]);
            }
            if (list.Count > 0) Version++;
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

            // Written only when the player has switched something off, so a save of a map nobody touched is
            // what it was before the switch existed.
            List<DesignationDef>? off = initiativeOff.Count > 0 || Scribe.mode != LoadSaveMode.Saving ? initiativeOff : null;
            if (off != null) Scribe_Collections.Look(ref off, "settlementInitiativeOff", LookMode.Def);
            if (Scribe.mode == LoadSaveMode.LoadingVars) initiativeOff = off ?? new List<DesignationDef>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) initiativeOff.RemoveAll(d => d == null);

            // Indexed once every target has resolved, dropping any whose def or target did not survive the load.
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                var loaded = allDesignations;
                allDesignations = new List<Designation>();
                byDef.Clear();
                thingDesignationCount = 0;
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
