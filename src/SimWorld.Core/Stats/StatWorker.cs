using System.Collections.Generic;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Pawns;

namespace SimWorld.Stats
{
    /// <summary>
    /// Computes one <see cref="StatDef"/>'s value for a <see cref="StatRequest"/> (RimWorld: <c>RimWorld.StatWorker</c>).
    /// <see cref="GetValueUnfinalized"/>'s order mirrors RimWorld's real method: base value → pawn offsets
    /// (trait, then hediff-stage, then gene — pawngen.genes) → pawn factors (trait, then hediff-stage, then
    /// gene) → stuff factor/offset → capacity factors. Trimmed to what this port's modules actually read: RimWorld also folds in skill-need
    /// offsets/factors, capacity *offsets*, apparel/equipment gear, life-stage factors, facility bonuses,
    /// Inspired bonuses and a scenario factor — none of those have a live counterpart in this port yet (no
    /// ThingOwner/apparel, no per-stat skill gating, no Inspiration system), so they are left out rather than
    /// faked. <see cref="FinalizeValue"/> then runs stat parts, the post-process curve, and the min/max clamp.
    /// </summary>
    public class StatWorker
    {
        public StatDef stat = null!;

        public float GetValue(StatRequest req, bool applyPostProcess = true)
        {
            float val = GetValueUnfinalized(req, applyPostProcess);
            FinalizeValue(req, ref val, applyPostProcess);
            return val;
        }

        public virtual float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            float num = GetBaseValueFor(req.Def);

            Pawn? pawn = req.Thing as Pawn;
            if (pawn != null)
            {
                TraitSet? traits = pawn.story?.traits;
                List<Hediff> hediffs = pawn.health.hediffSet.hediffs;

                // ---- offsets: trait degree, then hediff stage, then genes (pawngen.genes) ----
                if (traits != null)
                {
                    for (int i = 0; i < traits.allTraits.Count; i++)
                    {
                        num += traits.allTraits[i].CurrentData.statOffsets.GetStatOffsetFromList(stat);
                    }
                }
                for (int i = 0; i < hediffs.Count; i++)
                {
                    num += hediffs[i].CurStage?.statOffsets.GetStatOffsetFromList(stat) ?? 0f;
                }
                num += pawn.genes?.StatOffsetTotal(stat) ?? 0f;

                // ---- factors: trait degree, then hediff stage, then genes (pawngen.genes) ----
                if (traits != null)
                {
                    for (int i = 0; i < traits.allTraits.Count; i++)
                    {
                        num *= traits.allTraits[i].CurrentData.statFactors.GetStatFactorFromList(stat);
                    }
                }
                for (int i = 0; i < hediffs.Count; i++)
                {
                    num *= hediffs[i].CurStage?.statFactors.GetStatFactorFromList(stat) ?? 1f;
                }
                num *= pawn.genes?.StatFactorTotal(stat) ?? 1f;
            }

            // ---- stuff factor/offset: applies to any def, pawn or not ----
            StuffProperties? stuffProps = req.StuffDef?.stuffProps;
            if (stuffProps != null)
            {
                if (num > 0f)
                {
                    num *= stuffProps.statFactors.GetStatFactorFromList(stat);
                }
                num += stuffProps.statOffsets.GetStatOffsetFromList(stat);
            }

            // ---- capacity factors: last, so a wounded pawn's stat bends off everything above ----
            if (pawn != null && stat.capacityFactors != null)
            {
                PawnCapacitiesHandler capacities = pawn.health.capacities;
                for (int i = 0; i < stat.capacityFactors.Count; i++)
                {
                    PawnCapacityFactor cf = stat.capacityFactors[i];
                    float factor = cf.GetFactor(capacities.GetLevel(cf.capacity));
                    num = GenMath.Lerp(num, num * factor, cf.weight);
                }
            }

            return num;
        }

        public virtual void FinalizeValue(StatRequest req, ref float val, bool applyPostProcess)
        {
            if (stat.parts != null)
            {
                for (int i = 0; i < stat.parts.Count; i++)
                {
                    stat.parts[i].TransformValue(req, ref val);
                }
            }
            if (applyPostProcess && stat.postProcessCurve != null)
            {
                val = stat.postProcessCurve.Evaluate(val);
            }
            val = GenMath.Clamp(val, stat.minValue, stat.maxValue);
        }

        /// <summary><paramref name="def"/>'s <c>statBases</c> entry, else this stat's own default (RimWorld:
        /// <c>StatWorker.GetBaseValueFor</c>).</summary>
        public virtual float GetBaseValueFor(ThingDef? def) => def != null ? def.GetStatValueAbstract(stat) : stat.defaultBaseValue;

        /// <summary>Whether a (future) stats UI should list this stat for the request (RimWorld: <c>StatWorker.ShouldShowFor</c>,
        /// trimmed to the one check every RimWorld category shares — the rest of RimWorld's branching needs UI
        /// concepts, such as apparel/weapon categories, this port does not have).</summary>
        public virtual bool ShouldShowFor(StatRequest req)
        {
            if (stat.showIfUndefined) return true;
            return req.Def != null && req.Def.StatBaseDefined(stat);
        }
    }
}
