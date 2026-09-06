using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;

namespace SimWorld.Health
{
    /// <summary>What a part is for: capacities are computed from parts carrying a tag (RimWorld: <c>Verse.BodyPartTagDef</c>).</summary>
    public class BodyPartTagDef : Def
    {
    }

    /// <summary>Groups parts for armor coverage and targeting (RimWorld: <c>Verse.BodyPartGroupDef</c>).</summary>
    public class BodyPartGroupDef : Def
    {
        public string? labelShort;
        public int listOrder;
    }

    public enum BodyPartHeight
    {
        Undefined,
        Bottom,
        Middle,
        Top,
    }

    public enum BodyPartDepth
    {
        Undefined,
        Outside,
        Inside,
    }

    /// <summary>A kind of body part (RimWorld: <c>Verse.BodyPartDef</c>); records in a body reference these.</summary>
    public class BodyPartDef : Def
    {
        public int hitPoints = 10;
        public float permanentInjuryChanceFactor = 1f;
        public float bleedRate = 1f;
        public float frostbiteVulnerability;
        public bool skinCovered;
        public bool solid;
        public bool alive = true;
        public bool delicate;
        public bool beautyRelated;
        public bool conceptual;
        public bool socketed;
        public bool canSuggestAmputation = true;
        public bool destroyableByDamage = true;
        public List<BodyPartTagDef>? tags;

        public bool IsDelicate => delicate;

        public bool HasTag(BodyPartTagDef tag) => tags != null && tags.Contains(tag);

        public float GetMaxHealth(Pawn pawn) => (float)Math.Ceiling(hitPoints * pawn.HealthScale);

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (hitPoints <= 0 && !conceptual) yield return "hitPoints must be positive.";
        }
    }

    /// <summary>
    /// One part in a body tree (RimWorld: <c>Verse.BodyPartRecord</c>). <see cref="coverage"/> is the share of
    /// the parent this part occupies; <see cref="coverageAbs"/> is its own absolute share of the whole body
    /// (excluding children) and is what hit selection weighs.
    /// </summary>
    public class BodyPartRecord
    {
        public BodyPartDef def = null!;
        public string? customLabel;
        public List<BodyPartRecord> parts = new List<BodyPartRecord>();
        public BodyPartHeight height = BodyPartHeight.Undefined;
        public BodyPartDepth depth = BodyPartDepth.Undefined;
        public float coverage = 1f;
        public List<BodyPartGroupDef>? groups;

        [Unsaved] public BodyDef body = null!;
        [Unsaved] public BodyPartRecord? parent;
        [Unsaved] public float coverageAbs;
        [Unsaved] public float coverageAbsWithChildren;
        [Unsaved] public int Index = -1;

        public string Label => customLabel ?? def.label ?? def.defName;

        public string LabelCap => Label.Length == 0 ? "" : char.ToUpperInvariant(Label[0]) + Label.Substring(1);

        public bool IsCorePart => parent == null;

        public bool IsInGroup(BodyPartGroupDef group) => groups != null && groups.Contains(group);

        /// <summary>Every descendant, depth-first, excluding this part.</summary>
        public IEnumerable<BodyPartRecord> GetChildParts()
        {
            for (int i = 0; i < parts.Count; i++)
            {
                yield return parts[i];
                foreach (BodyPartRecord grandchild in parts[i].GetChildParts())
                {
                    yield return grandchild;
                }
            }
        }

        public IEnumerable<BodyPartRecord> GetDirectChildParts() => parts;

        public bool IsAncestorOf(BodyPartRecord other)
        {
            for (BodyPartRecord? p = other.parent; p != null; p = p.parent)
            {
                if (ReferenceEquals(p, this)) return true;
            }
            return false;
        }

        public bool HasTag(BodyPartTagDef tag) => def.HasTag(tag);

        public override string ToString() => Label;
    }

    /// <summary>
    /// A species' body: a tree of parts rooted at <see cref="corePart"/> (RimWorld: <c>Verse.BodyDef</c>).
    /// Absolute coverages are cached after cross-references resolve.
    /// </summary>
    public class BodyDef : Def
    {
        public BodyPartRecord? corePart;

        private readonly List<BodyPartRecord> allParts = new List<BodyPartRecord>();
        private readonly Dictionary<BodyPartTagDef, List<BodyPartRecord>> partsByTag = new Dictionary<BodyPartTagDef, List<BodyPartRecord>>();

        public IReadOnlyList<BodyPartRecord> AllParts => allParts;

        public IReadOnlyList<BodyPartRecord> GetPartsWithTag(BodyPartTagDef tag)
        {
            return partsByTag.TryGetValue(tag, out List<BodyPartRecord> list) ? list : (IReadOnlyList<BodyPartRecord>)Array.Empty<BodyPartRecord>();
        }

        public bool HasPartWithTag(BodyPartTagDef tag) => partsByTag.ContainsKey(tag);

        public IEnumerable<BodyPartRecord> GetPartsWithDef(BodyPartDef def)
        {
            for (int i = 0; i < allParts.Count; i++)
            {
                if (allParts[i].def == def) yield return allParts[i];
            }
        }

        public BodyPartRecord? GetPartByLabel(string label)
        {
            for (int i = 0; i < allParts.Count; i++)
            {
                if (string.Equals(allParts[i].Label, label, StringComparison.OrdinalIgnoreCase)) return allParts[i];
            }
            return null;
        }

        public override void ResolveReferences()
        {
            base.ResolveReferences();
            allParts.Clear();
            partsByTag.Clear();
            if (corePart != null)
            {
                CacheDataRecursive(corePart, null);
            }
        }

        private void CacheDataRecursive(BodyPartRecord part, BodyPartRecord? parent)
        {
            part.parent = parent;
            part.body = this;
            part.Index = allParts.Count;
            allParts.Add(part);
            part.coverageAbsWithChildren = parent == null ? 1f : parent.coverageAbsWithChildren * part.coverage;
            float children = 0f;
            for (int i = 0; i < part.parts.Count; i++)
            {
                CacheDataRecursive(part.parts[i], part);
                children += part.parts[i].coverageAbsWithChildren;
            }
            part.coverageAbs = Math.Max(0f, part.coverageAbsWithChildren - children);
            if (part.def.tags != null)
            {
                for (int i = 0; i < part.def.tags.Count; i++)
                {
                    if (!partsByTag.TryGetValue(part.def.tags[i], out List<BodyPartRecord> list))
                    {
                        list = new List<BodyPartRecord>();
                        partsByTag[part.def.tags[i]] = list;
                    }
                    list.Add(part);
                }
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (corePart == null)
            {
                yield return "body has no corePart.";
                yield break;
            }
            foreach (BodyPartRecord part in allParts)
            {
                if (part.def == null) yield return "part without def.";
                else if (part.coverage <= 0f || part.coverage > 1f) yield return "part " + part.Label + " coverage must be in (0, 1].";
                if (part.coverageAbsWithChildren > 0f && part.coverageAbs <= 0f && part.parts.Count > 0)
                {
                    yield return "part " + part.Label + " children cover more than the part itself.";
                }
            }
        }
    }
}
