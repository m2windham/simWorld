using System.Collections.Generic;

using SimWorld.Research;
using SimWorld.Sim;

namespace SimWorld.God.View
{
    /// <summary>
    /// The direct-pick half of the research lever — see <c>GodViewSnapshot.Research.cs</c> for the read half
    /// a host needs before it can offer this at all.
    ///
    /// <para/><b>The gap this closes.</b> <see cref="ResearchManager.CurrentProj"/> is a plain settable
    /// property and <see cref="GodManager.Activate"/> already assigns it for an edict's
    /// <see cref="EdictDef.researchFocus"/> — but nothing in <c>src/</c> ever let a player choose a project
    /// directly. <see cref="ResearchAgenda"/> fills the gap when nobody has, cheapest-first; this is the other
    /// half, the player naming exactly what the civilization studies next.
    ///
    /// <para/><b>Never clobbered by the agenda.</b> <see cref="ResearchAgenda.EnsureProject"/> only ever fills
    /// a null <see cref="ResearchManager.CurrentProj"/> — see that method's own doc — so a project set here
    /// stands until it finishes on its own, exactly like an edict's <c>researchFocus</c>. Nothing extra is
    /// needed on this side to protect the player's pick; it is protected by the agenda already refusing to
    /// overwrite a non-null value, which
    /// <c>GodCommandsResearchTests.A_players_pick_survives_while_in_progress_and_the_agenda_only_resumes_once_it_finishes</c>
    /// exercises through the real tick loop rather than trusting this comment.
    ///
    /// <para/><b>What this refuses, and what it does not (<c>docs/design/player-first.md</c> §5).</b> An
    /// unknown project and a project <see cref="ResearchProjectDef.CanStartNow"/> genuinely disagrees with —
    /// already finished, or a prerequisite still outstanding — are the only refusals.
    /// <see cref="ResearchProjectDef.CanStartNow"/> decides, not this method: nothing here re-derives
    /// prerequisite logic of its own. A <em>bad</em> pick — a project that does nothing for the civilization's
    /// present problem, or abandoning a near-finished one for a cheap distraction — is never refused; its
    /// progress is preserved exactly where <see cref="ResearchManager"/> left it and resumes if the player
    /// ever comes back to it, since <see cref="ResearchManager"/> tracks progress per project, not only for
    /// whichever is current.
    /// </summary>
    public static partial class GodCommands
    {
        /// <summary>
        /// Makes <paramref name="defName"/> the civilization's current research project, replacing whatever
        /// was current — the player's explicit pick always takes precedence over the agenda's own cheapest-
        /// first choice or an edict's <c>researchFocus</c>, the same way an explicit order pre-empts a standing
        /// rule elsewhere in this codebase (<c>docs/design/player-first.md</c> §5).
        /// </summary>
        public static GodCommandResult SetResearchProject(string defName)
        {
            if (string.IsNullOrEmpty(defName))
            {
                return GodCommandResult.Refused("No research project id given.");
            }

            ResearchManager manager = Find.ResearchManager;
            ResearchProjectDef? project = manager.GetProject(defName);
            if (project == null)
            {
                return GodCommandResult.Refused("No research project named '" + defName + "'.");
            }

            if (manager.CurrentProj == project)
            {
                return GodCommandResult.NoChange(project.LabelCap + " is already the current research project.");
            }

            if (project.IsFinished)
            {
                return GodCommandResult.Refused(project.LabelCap + " has already been researched.");
            }

            if (!project.CanStartNow)
            {
                return GodCommandResult.Refused(
                    project.LabelCap + " is locked behind " + UnmetPrerequisitesOf(project) + ".");
            }

            manager.CurrentProj = project;
            return GodCommandResult.Done(project.LabelCap + " is now the civilization's research focus.");
        }

        /// <summary>Every visible prerequisite of <paramref name="project"/> that is not yet finished, by
        /// label — hidden prerequisites are never named, matching <see cref="ResearchProjectDef.prerequisites"/>'s
        /// own "drawn as edges on the tree" contract: a hidden one gates silently in the simulation, and
        /// naming it here would out it to a host that is only supposed to see the visible tree.</summary>
        private static string UnmetPrerequisitesOf(ResearchProjectDef project)
        {
            var missing = new List<string>();
            if (project.prerequisites != null)
            {
                for (int i = 0; i < project.prerequisites.Count; i++)
                {
                    ResearchProjectDef? p = project.prerequisites[i];
                    if (p != null && !p.IsFinished) missing.Add(p.LabelCap);
                }
            }
            return missing.Count > 0 ? string.Join(", ", missing) : "prerequisites not yet met";
        }
    }
}
