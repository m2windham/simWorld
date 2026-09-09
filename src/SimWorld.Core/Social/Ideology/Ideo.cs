using System;
using System.Collections.Generic;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Social.Ideology
{
    /// <summary>
    /// A civilization's actual belief system (RimWorld: <c>RimWorld.Ideo</c>) — the runtime object this
    /// module's brief asks for, generated from an authored <see cref="IdeoDef"/> preset the same way a
    /// <c>Pawn</c> is generated from a <c>PawnKindDef</c>. One <c>Ideo</c> stands in for the whole
    /// civilization's belief system in this pass, not one per citizen: RimWorld lets every colonist follow a
    /// different ideoligion (certainty, conversion, an outsider penalty) — the per-citizen membership half of
    /// that is exactly the "belief is a separate, much larger design" the brief scopes out
    /// (<c>docs/spec/simworld-spec.md</c> §8.4), so every Humanlike citizen this pass reaches is simply
    /// assumed to belong to whichever <see cref="Ideo"/> is current (<see cref="PreceptWorker.AppliesTo"/>'s
    /// default). Owned wherever a future civilization-scale manager holds it — see <see cref="IdeoManager"/>'s
    /// own doc for why that is a self-gated stand-in rather than a <c>Find.Ideo</c> slot in this pass.
    /// </summary>
    public sealed class Ideo : IExposable
    {
        public IdeoDef def = null!;

        public string name = "";

        public IdeoRoleTracker Roles { get; private set; } = new IdeoRoleTracker();

        /// <summary>Parameterless for Scribe's <c>Activator.CreateInstance</c> load path — see <see cref="ExposeData"/>.</summary>
        public Ideo()
        {
        }

        public Ideo(IdeoDef def)
        {
            this.def = def ?? throw new ArgumentNullException(nameof(def));
            name = def.LabelCap;
        }

        /// <summary>Every precept this ideoligion holds (RimWorld: <c>Ideo.PreceptsListForReading</c>) — the
        /// union of every carried meme's automatic precepts and this ideoligion's own slot-filling choices.
        /// See <see cref="IdeoDef.AllPrecepts"/>.</summary>
        public IReadOnlyList<PreceptDef> Precepts => def.AllPrecepts;

        public bool HasPrecept(PreceptDef precept)
        {
            if (precept == null) return false;
            IReadOnlyList<PreceptDef> precepts = def.AllPrecepts;
            for (int i = 0; i < precepts.Count; i++)
            {
                if (precepts[i] == precept) return true;
            }
            return false;
        }

        /// <summary>
        /// Grants <paramref name="role"/> to <paramref name="pawn"/>: false (no-op) unless some precept this
        /// ideoligion actually holds names <paramref name="role"/> via <see cref="PreceptDef.grantsRole"/> —
        /// the "the Def says what could apply" gate — after which <see cref="IdeoRoleTracker.TryAssign"/>
        /// enforces the role's own <see cref="IdeoRoleDef.maxHolders"/> cap.
        /// </summary>
        public bool TryAssignRole(Pawn pawn, IdeoRoleDef role)
        {
            if (role == null) return false;
            if (!GrantsRole(role)) return false;
            return Roles.TryAssign(pawn, role);
        }

        /// <summary>Whether some precept this ideoligion holds grants <paramref name="role"/> at all.</summary>
        public bool GrantsRole(IdeoRoleDef role)
        {
            if (role == null) return false;
            IReadOnlyList<PreceptDef> precepts = Precepts;
            for (int i = 0; i < precepts.Count; i++)
            {
                if (precepts[i].grantsRole == role) return true;
            }
            return false;
        }

        public void ExposeData()
        {
            IdeoDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;

            Scribe_Values.Look(ref name, "name", "");

            IdeoRoleTracker? r = Roles;
            Scribe_Deep.Look(ref r, "roles");
            Roles = r ?? new IdeoRoleTracker();
        }
    }
}
