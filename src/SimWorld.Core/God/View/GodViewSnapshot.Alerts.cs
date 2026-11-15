using System.Collections.Generic;

namespace SimWorld.God.View
{
    public sealed partial class GodViewSnapshot
    {
        private IReadOnlyList<AlertView>? alerts;

        /// <summary>
        /// Every alert up right now on every settlement that has an interior map, highest priority first
        /// within each settlement (<see cref="Alerts.AlertsReadout"/>). Empty with no game running, and empty
        /// for a settlement nobody has opened: alerts read bodies on a map, and a settlement with no interior
        /// has none to read.
        ///
        /// <para/>Built the first time it is read and cached on this snapshot, the same way
        /// <see cref="PendingLetters"/> is and for the same reason (see that property): read it on the tick you
        /// captured if you need it to agree with the rest of the snapshot.
        /// </summary>
        public IReadOnlyList<AlertView> Alerts => alerts ??= AlertView.ActiveNow();
    }
}
