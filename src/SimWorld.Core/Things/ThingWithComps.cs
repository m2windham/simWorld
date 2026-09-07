using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Things
{
    /// <summary>
    /// A Thing whose behaviour is composed from <see cref="ThingComp"/> instances (RimWorld:
    /// <c>Verse.ThingWithComps</c>). One comp per <c>def.comps</c> entry; every lifecycle hook below forwards
    /// to each comp after doing its own base work. <see cref="SimWorld.Pawns.Pawn"/> derives from this even
    /// though most pawn Defs currently list no comps, so it is ready the moment one does.
    /// </summary>
    public class ThingWithComps : Thing
    {
        private List<ThingComp>? comps;

        /// <summary>Empty (not null) until <see cref="InitializeComps"/> runs.</summary>
        public IReadOnlyList<ThingComp> AllComps => (IReadOnlyList<ThingComp>?)comps ?? Array.Empty<ThingComp>();

        public override void PostMake()
        {
            base.PostMake();
            InitializeComps();
        }

        /// <summary>Builds <see cref="AllComps"/> fresh from <c>def.comps</c>; safe to call again (Scribe re-creates comps on load).</summary>
        public void InitializeComps()
        {
            comps = new List<ThingComp>();
            if (def?.comps == null) return;
            foreach (CompProperties compProps in def.comps)
            {
                if (compProps.compClass == null) continue;
                var comp = (ThingComp)Activator.CreateInstance(compProps.compClass)!;
                comp.parent = this;
                comp.Initialize(compProps);
                comps.Add(comp);
            }
        }

        public T? GetComp<T>() where T : ThingComp
        {
            if (comps != null)
            {
                for (int i = 0; i < comps.Count; i++)
                {
                    if (comps[i] is T typed) return typed;
                }
            }
            return null;
        }

        public T? TryGetComp<T>() where T : ThingComp => GetComp<T>();

        public override void SpawnSetup(Map.Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            for (int i = 0; comps != null && i < comps.Count; i++)
            {
                comps[i].PostSpawnSetup(respawningAfterLoad);
            }
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Map.Map? map = Map;
            if (map != null)
            {
                for (int i = 0; comps != null && i < comps.Count; i++)
                {
                    comps[i].PostDeSpawn(map);
                }
            }
            base.DeSpawn(mode);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            Map.Map? previousMap = Map;
            base.Destroy(mode);
            for (int i = 0; comps != null && i < comps.Count; i++)
            {
                comps[i].PostDestroy(mode, previousMap);
            }
        }

        public override void Tick()
        {
            for (int i = 0; comps != null && i < comps.Count; i++)
            {
                comps[i].CompTick();
            }
        }

        public override void TickRare()
        {
            for (int i = 0; comps != null && i < comps.Count; i++)
            {
                comps[i].CompTickRare();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                InitializeComps();
            }
            for (int i = 0; comps != null && i < comps.Count; i++)
            {
                comps[i].PostExposeData();
            }
        }
    }
}
