using System.Collections.Generic;
using System.Globalization;

namespace SimWorld.Pawns
{
    /// <summary>
    /// Load-time validation of the forced/disallowed trait pair that <see cref="PawnKindDef"/> and
    /// <see cref="BackstoryDef"/> both carry.
    ///
    /// <para/>Both halves are honoured by <see cref="Generation.PawnGenerator.GeneratePawn"/>, and the
    /// generator resolves every contradiction between them by staying silent: a forced trait that is also
    /// disallowed is skipped, a forced degree the trait does not declare falls back to the trait's first
    /// degree, and a pair of forced traits that conflict leaves whichever lost the race off the pawn. None of
    /// that is visible from the XML, and all of it produces a pawn that quietly disagrees with the content
    /// that asked for it. So the contradiction is a config error instead — content is wrong at load, not at
    /// the two-hundredth generated pawn.
    /// </summary>
    internal static class BackstoryTraitValidation
    {
        /// <summary>Every contradiction in one def's forced/disallowed trait pair, worded for a config error.</summary>
        internal static IEnumerable<string> Errors(List<BackstoryTrait>? forcedTraits, List<TraitDef>? disallowedTraits)
        {
            if (disallowedTraits != null)
            {
                for (int i = 0; i < disallowedTraits.Count; i++)
                {
                    if (disallowedTraits[i] == null) yield return "disallowedTraits contains a null entry.";
                }
            }

            if (forcedTraits == null) yield break;

            var seen = new List<TraitDef>();
            for (int i = 0; i < forcedTraits.Count; i++)
            {
                BackstoryTrait forced = forcedTraits[i];
                if (forced == null || forced.def == null)
                {
                    yield return "forcedTraits contains an entry with no def.";
                    continue;
                }

                TraitDef def = forced.def;

                if (disallowedTraits != null && disallowedTraits.Contains(def))
                {
                    yield return "forces trait " + def.defName + " and disallows it at the same time.";
                }

                if (!def.HasDegree(forced.degree))
                {
                    yield return "forces trait " + def.defName + " at degree "
                        + forced.degree.ToString(CultureInfo.InvariantCulture) + ", which that trait does not declare.";
                }

                for (int j = 0; j < seen.Count; j++)
                {
                    if (seen[j] == def)
                    {
                        yield return "forces trait " + def.defName + " more than once.";
                    }
                    else if (seen[j].ConflictsWith(def))
                    {
                        yield return "forces conflicting traits " + seen[j].defName + " and " + def.defName + ".";
                    }
                }

                seen.Add(def);
            }
        }
    }
}
