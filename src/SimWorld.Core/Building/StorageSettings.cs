using System;
using System.Collections.Generic;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>How much a storage place wants what it can take (RimWorld: <c>RimWorld.StoragePriority</c>,
    /// same members, same order). A Thing is moved when somewhere of strictly higher priority will have it.</summary>
    public enum StoragePriority : byte
    {
        Unstored,
        Low,
        Normal,
        Preferred,
        Important,
        Critical,
    }

    /// <summary>
    /// The two special filters RimWorld puts on a storage place's corpse allowances
    /// (<c>SpecialThingFilterDef</c> <c>AllowCorpsesColonist</c> / <c>AllowCorpsesStranger</c>, matched by
    /// <c>SpecialThingFilterWorker_CorpsesColonist</c> / <c>_CorpsesStranger</c>).
    /// <para/>
    /// <b>Translation — the narrowest equivalent.</b> This port's <see cref="ThingFilter"/> has no special
    /// filters at all (<c>disallowedSpecialFilters</c> is left out of it on purpose, and it is shared by every
    /// recipe and bill), and a general special-filter system for the sake of two humanlike-corpse questions is
    /// more than a grave needs. So the two live on <see cref="StorageSettings"/> as an enum, matched by
    /// <see cref="StorageSettingsUtility.Matches"/>, and say exactly what RimWorld's workers say: a
    /// <i>colonist</i> corpse is a humanlike body whose pawn belongs to the player's faction, a <i>stranger</i>
    /// one is a humanlike body whose pawn does not. An animal's body is neither.
    /// </summary>
    public enum SpecialThingFilter : byte
    {
        CorpsesColonist,
        CorpsesStranger,
    }

    /// <summary>What a storage place takes and how much it wants it (RimWorld: <c>RimWorld.StorageSettings</c>).</summary>
    public class StorageSettings : IExposable
    {
        /// <summary>Whoever these settings belong to; asked for its parent (fixed) settings when deciding what
        /// it may take. Not saved — the owner re-attaches itself on load.</summary>
        public IStoreSettingsParent? owner;

        public ThingFilter filter = new ThingFilter();

        /// <summary>Authored as <c>&lt;priority&gt;</c> in content (RimWorld names the backing field
        /// <c>priorityInt</c> and aliases it to that); read and written through <see cref="Priority"/>.</summary>
        public StoragePriority priority = StoragePriority.Normal;

        /// <summary>Corpses this place refuses whatever its filter says — see <see cref="SpecialThingFilter"/>.
        /// A <see cref="List{T}"/> for the XML loader; read as a set.</summary>
        public List<SpecialThingFilter> disallowedSpecialFilters = new List<SpecialThingFilter>();

        public StorageSettings()
        {
        }

        public StorageSettings(IStoreSettingsParent owner)
        {
            this.owner = owner;
            StorageSettings? parent = owner?.GetParentStoreSettings();
            if (parent != null) priority = parent.priority;
        }

        public StoragePriority Priority
        {
            get => priority;
            set => priority = value;
        }

        /// <summary>Replaces this place's rules with the preset's (RimWorld: <c>StorageSettings.SetFromPreset</c>).</summary>
        public void SetFromPreset(StorageSettingsPreset preset)
        {
            filter.SetFromPreset(preset);
            disallowedSpecialFilters.Clear();
        }

        public void CopyFrom(StorageSettings other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            priority = other.priority;
            filter.CopyAllowancesFrom(other.filter);
            disallowedSpecialFilters = new List<SpecialThingFilter>(other.disallowedSpecialFilters);
        }

        /// <summary>RimWorld: <c>StorageSettings.AllowedToAccept(Thing)</c> — this filter, its special filters,
        /// and then the owner's fixed parent settings.</summary>
        public bool AllowedToAccept(Thing t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            if (!filter.Allows(t.def)) return false;
            for (int i = 0; i < disallowedSpecialFilters.Count; i++)
            {
                if (StorageSettingsUtility.Matches(disallowedSpecialFilters[i], t)) return false;
            }
            StorageSettings? parent = owner?.GetParentStoreSettings();
            return parent == null || parent.AllowedToAccept(t);
        }

        public bool AllowedToAccept(ThingDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (!filter.Allows(def)) return false;
            StorageSettings? parent = owner?.GetParentStoreSettings();
            return parent == null || parent.AllowedToAccept(def);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref priority, "priority", StoragePriority.Unstored);
            ThingFilter? f = filter;
            Scribe_Deep.Look(ref f, "filter");
            filter = f ?? new ThingFilter();

            var names = new List<string>();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                for (int i = 0; i < disallowedSpecialFilters.Count; i++) names.Add(disallowedSpecialFilters[i].ToString());
            }
            List<string>? saved = names;
            Scribe_Collections.Look(ref saved, "disallowedSpecialFilters", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                disallowedSpecialFilters = new List<SpecialThingFilter>();
                if (saved != null)
                {
                    foreach (string name in saved)
                    {
                        if (Enum.TryParse(name, out SpecialThingFilter parsed)) disallowedSpecialFilters.Add(parsed);
                    }
                }
            }
        }
    }

    public static class StorageSettingsUtility
    {
        /// <summary>RimWorld: <c>SpecialThingFilterWorker_CorpsesColonist.Matches</c> and
        /// <c>_CorpsesStranger.Matches</c>. Only a humanlike corpse matches either.</summary>
        public static bool Matches(SpecialThingFilter filter, Thing t)
        {
            if (!(t is Corpse corpse) || corpse.InnerPawn == null || !corpse.InnerPawn.RaceProps.Humanlike) return false;
            bool ours = PawnUtility.IsColonist(corpse.InnerPawn);
            return filter == SpecialThingFilter.CorpsesColonist ? ours : !ours;
        }
    }
}
