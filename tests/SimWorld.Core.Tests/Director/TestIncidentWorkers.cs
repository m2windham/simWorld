using SimWorld.Director;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// A worker that fires and does nothing, for storyteller tests that care about selection, gating, refire
    /// spacing or points jitter and not at all about what an incident does when it lands.
    ///
    /// <para/>This used to be <c>IncidentWorker_ThreatEvent</c> in production code — a real class, shipped,
    /// that <c>ManhunterPack</c> pointed at, whose whole body was <c>=&gt; parms.points &gt; 0f</c>. Once
    /// ManhunterPack got a worker that actually does something, what remained was a convincing-looking threat
    /// worker with no users but these tests. Leaving it in <c>src/</c> would be leaving a trap: the next
    /// person to need "a threat worker" would find it, wire content to it, and ship another incident that
    /// reports success and does nothing. A test double belongs in the test project, where its name says what
    /// it is.
    /// </summary>
    public sealed class IncidentWorker_TestThreat : IncidentWorker
    {
        protected override bool TryExecuteWorker(IncidentParms parms) => parms.points > 0f;
    }
}
