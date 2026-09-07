using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.MindState;
using SimWorld.Sim;
using SimWorld.Work;

namespace SimWorld.Pawns
{
    /// <summary>One degree of a trait: its label and effects (RimWorld: <c>RimWorld.TraitDegreeData</c>).</summary>
    public class TraitDegreeData
    {
        public string? label;
        public string? description;
        public int degree;
        public float commonality = 1f;
        public List<StatModifier>? statOffsets;
        public List<StatModifier>? statFactors;
        public float socialFightChanceFactor = 1f;

        /// <summary>Overrides <see cref="TraitDef.disabledWorkTags"/> for this degree when non-<see cref="WorkTags.None"/>.</summary>
        public WorkTags disabledWorkTags = WorkTags.None;

        /// <summary>Skill points <see cref="Generation.PawnGenerator"/> adds for a pawn with this degree.</summary>
        public List<SkillGain>? skillGains;

        /// <summary>When set, a pawn with this degree can only have these mood breaks.</summary>
        public List<MentalBreakDef>? theOnlyAllowedMentalBreaks;

        /// <summary>Breaks this degree rules out.</summary>
        public List<MentalBreakDef>? disallowedMentalBreaks;

        public string LabelCap => label == null || label.Length == 0 ? "" : char.ToUpperInvariant(label[0]) + label.Substring(1);
    }

    /// <summary>A personality trait with one or more degrees (RimWorld: <c>RimWorld.TraitDef</c>).</summary>
    public class TraitDef : Def
    {
        public List<TraitDegreeData> degreeDatas = new List<TraitDegreeData>();
        public List<TraitDef>? conflictingTraits;
        public List<string>? exclusionTags;
        public float commonality = 1f;
        public float commonalityFemale = -1f;
        public bool allowOnHostileSpawn = true;

        /// <summary>Work tags a pawn with this trait can never do, at every degree unless a degree overrides it.</summary>
        public WorkTags disabledWorkTags = WorkTags.None;

        /// <summary>Work tags the pawn must still be able to do for <see cref="Generation.PawnGenerator"/> to
        /// consider rolling this trait at all (e.g. a bloodthirsty trait requiring <see cref="WorkTags.Violent"/>).</summary>
        public WorkTags requiredWorkTags = WorkTags.None;

        /// <summary>Effective disabled tags for one degree: the degree's own tags if set, else the trait's.</summary>
        public WorkTags DisabledWorkTagsAtDegree(int degree)
        {
            TraitDegreeData data = DataAtDegree(degree);
            return data.disabledWorkTags != WorkTags.None ? data.disabledWorkTags : disabledWorkTags;
        }

        public TraitDegreeData DataAtDegree(int degree)
        {
            for (int i = 0; i < degreeDatas.Count; i++)
            {
                if (degreeDatas[i].degree == degree) return degreeDatas[i];
            }
            throw new ArgumentOutOfRangeException(nameof(degree), "Trait " + defName + " has no degree " + degree + ".");
        }

        public bool HasDegree(int degree)
        {
            for (int i = 0; i < degreeDatas.Count; i++)
            {
                if (degreeDatas[i].degree == degree) return true;
            }
            return false;
        }

        public bool ConflictsWith(TraitDef other)
        {
            if (other == this) return true;
            if (conflictingTraits != null && conflictingTraits.Contains(other)) return true;
            if (other.conflictingTraits != null && other.conflictingTraits.Contains(this)) return true;
            if (exclusionTags != null && other.exclusionTags != null)
            {
                for (int i = 0; i < exclusionTags.Count; i++)
                {
                    if (other.exclusionTags.Contains(exclusionTags[i])) return true;
                }
            }
            return false;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (degreeDatas.Count == 0) yield return "trait has no degreeDatas.";
            var seen = new HashSet<int>();
            foreach (TraitDegreeData data in degreeDatas)
            {
                if (!seen.Add(data.degree)) yield return "duplicate degree " + data.degree + ".";
            }
        }
    }

    /// <summary>A trait instance on a pawn (RimWorld: <c>RimWorld.Trait</c>).</summary>
    public class Trait : IExposable
    {
        public TraitDef def = null!;
        public int degree;
        public bool scenForced;

        public Trait()
        {
        }

        public Trait(TraitDef def, int degree = 0, bool forced = false)
        {
            this.def = def ?? throw new ArgumentNullException(nameof(def));
            this.degree = degree;
            scenForced = forced;
        }

        public TraitDegreeData CurrentData => def.DataAtDegree(degree);

        public string Label => CurrentData.label ?? def.label ?? def.defName;

        public void ExposeData()
        {
            TraitDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
            Scribe_Values.Look(ref degree, "degree");
            Scribe_Values.Look(ref scenForced, "scenForced");
        }

        public override string ToString() => def == null ? "Trait(null)" : def.defName + (degree != 0 ? "(" + degree + ")" : "");
    }

    /// <summary>A pawn's traits (RimWorld: <c>RimWorld.TraitSet</c>).</summary>
    public class TraitSet : IExposable
    {
        private readonly Pawn pawn;
        public List<Trait> allTraits = new List<Trait>();

        public TraitSet(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public void GainTrait(Trait trait)
        {
            if (trait == null) throw new ArgumentNullException(nameof(trait));
            if (HasTrait(trait.def))
            {
                throw new InvalidOperationException(pawn + " already has trait " + trait.def.defName + ".");
            }
            allTraits.Add(trait);
            pawn.Notify_TraitsChanged();
        }

        public bool RemoveTrait(Trait trait)
        {
            bool removed = allTraits.Remove(trait);
            if (removed) pawn.Notify_TraitsChanged();
            return removed;
        }

        public bool HasTrait(TraitDef def)
        {
            for (int i = 0; i < allTraits.Count; i++)
            {
                if (allTraits[i].def == def) return true;
            }
            return false;
        }

        public bool HasTrait(TraitDef def, int degree)
        {
            for (int i = 0; i < allTraits.Count; i++)
            {
                if (allTraits[i].def == def && allTraits[i].degree == degree) return true;
            }
            return false;
        }

        public Trait? GetTrait(TraitDef def)
        {
            for (int i = 0; i < allTraits.Count; i++)
            {
                if (allTraits[i].def == def) return allTraits[i];
            }
            return null;
        }

        public int DegreeOfTrait(TraitDef def) => GetTrait(def)?.degree ?? 0;

        public void ExposeData()
        {
            List<Trait>? list = allTraits;
            Scribe_Collections.Look(ref list, "allTraits", LookMode.Deep);
            allTraits = list ?? new List<Trait>();
        }
    }

    /// <summary>Biography, traits and life history (RimWorld: <c>RimWorld.Pawn_StoryTracker</c>).</summary>
    public class Pawn_StoryTracker : IExposable
    {
        private readonly Pawn pawn;
        public TraitSet traits;

        /// <summary>Backstory for <see cref="BackstorySlot.Childhood"/>; every generated humanlike pawn has one.</summary>
        public BackstoryDef? childhood;

        /// <summary>Backstory for <see cref="BackstorySlot.Adulthood"/>; only pawns generated old enough get one.</summary>
        public BackstoryDef? adulthood;

        /// <summary>Skin tone in [0, 1]; nothing derives a palette from it yet.</summary>
        public float melanin;

        /// <summary>Coarse build ("Male"/"Female"/"Thin"/"Fat"/"Hulk"); appearance only, nothing mechanical yet.</summary>
        public string? bodyType;

        /// <summary>
        /// Life history entries — SimWorld's stand-in for RimWorld's hand-written backstory flavor text: the
        /// sim records its own as things happen (birth today, more later). See <see cref="RecordLifeEvent"/>.
        /// </summary>
        public List<LifeEvent> lifeEvents = new List<LifeEvent>();

        public Pawn_StoryTracker(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
            traits = new TraitSet(pawn);
        }

        public BackstoryDef? GetBackstory(BackstorySlot slot) => slot == BackstorySlot.Childhood ? childhood : adulthood;

        public IEnumerable<BackstoryDef> AllBackstories
        {
            get
            {
                if (childhood != null) yield return childhood;
                if (adulthood != null) yield return adulthood;
            }
        }

        /// <summary>The pawn-info-card readout: adulthood's short title, falling back to childhood's.</summary>
        public string TitleShort => adulthood?.titleShort ?? adulthood?.title ?? childhood?.titleShort ?? childhood?.title ?? "";

        /// <summary>Every work tag barred by the pawn's backstories or any of its traits, OR'ed together.</summary>
        public WorkTags DisabledWorkTagsBackstoryAndTraits
        {
            get
            {
                WorkTags combined = WorkTags.None;
                if (childhood != null) combined |= childhood.workDisables;
                if (adulthood != null) combined |= adulthood.workDisables;
                for (int i = 0; i < traits.allTraits.Count; i++)
                {
                    Trait trait = traits.allTraits[i];
                    combined |= trait.def.DisabledWorkTagsAtDegree(trait.degree);
                }
                return combined;
            }
        }

        /// <summary>Appends a life event stamped with the current tick; pure history, nothing reacts to it.</summary>
        public void RecordLifeEvent(string kind, string text)
        {
            lifeEvents.Add(new LifeEvent(kind, text, Find.TickManager.TicksGame));
        }

        public void ExposeData()
        {
            TraitSet? t = traits;
            Scribe_Deep.Look(ref t, "traits", pawn);
            traits = t ?? new TraitSet(pawn);
            BackstoryDef? c = childhood;
            Scribe_Defs.Look(ref c, "childhood");
            childhood = c;
            BackstoryDef? a = adulthood;
            Scribe_Defs.Look(ref a, "adulthood");
            adulthood = a;
            Scribe_Values.Look(ref melanin, "melanin");
            Scribe_Values.Look(ref bodyType, "bodyType");
            List<LifeEvent>? events = lifeEvents;
            Scribe_Collections.Look(ref events, "lifeEvents", LookMode.Deep);
            lifeEvents = events ?? new List<LifeEvent>();
        }
    }

    /// <summary>One recorded moment in a pawn's history (SimWorld: no separate backstory-text database, so the
    /// sim writes its own life events as things happen — birth today, more later).</summary>
    public class LifeEvent : IExposable
    {
        public string kind = "";
        public string text = "";
        public int tick;

        public LifeEvent()
        {
        }

        public LifeEvent(string kind, string text, int tick)
        {
            this.kind = kind ?? "";
            this.text = text ?? "";
            this.tick = tick;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind", "");
            Scribe_Values.Look(ref text, "text", "");
            Scribe_Values.Look(ref tick, "tick", 0);
        }

        public override string ToString() => kind + " @" + tick.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
