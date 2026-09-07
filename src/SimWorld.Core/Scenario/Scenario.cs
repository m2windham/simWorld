using System.Collections.Generic;
using System.Text;
using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Scenario
{
    /// <summary>
    /// A configured game start (RimWorld: <c>Verse.Scenario</c>): a name/summary/description plus the
    /// <see cref="ScenPart"/>s that shape it. RimWorld saves the chosen scenario deep inside the savegame
    /// rather than referencing it by def — so does this port (see the "Scribe round trip" test); a
    /// <see cref="ScenarioDef"/> only exists to ship the built-in ones as content.
    /// </summary>
    public sealed class Scenario : IExposable
    {
        /// <summary>
        /// SimWorld hook: the scenario a running game is using, for systems (world generation, so far) that
        /// read scenario data without going through the full <see cref="IScenarioContext"/> pipeline. Test-only —
        /// <see cref="PostGameStart"/> itself never reads this.
        /// </summary>
        public static Scenario? Current { get; set; }

        public string name = "";
        public string summary = "";
        public string description = "";
        public List<ScenPart> parts = new List<ScenPart>();

        public IReadOnlyList<ScenPart> AllParts => parts;

        /// <summary>How many rival civilizations this scenario asks for, via its <see cref="ScenPart_RivalCivilizations"/>
        /// part; zero when it has none.</summary>
        public IntRange RivalCivilizationCount
        {
            get
            {
                foreach (ScenPart part in parts)
                {
                    if (part is ScenPart_RivalCivilizations rival) return rival.count;
                }
                return IntRange.Zero;
            }
        }

        /// <summary>Name, description, and every part's non-empty <see cref="ScenPart.Summary"/>, highest
        /// <see cref="ScenPartDef.summaryPriority"/> first.</summary>
        public string GetFullInformationText()
        {
            var ordered = new List<ScenPart>(parts);
            ordered.Sort((a, b) => (b.def?.summaryPriority ?? 0).CompareTo(a.def?.summaryPriority ?? 0));

            var sb = new StringBuilder();
            sb.Append(name);
            if (description.Length > 0)
            {
                sb.Append('\n').Append(description);
            }
            foreach (ScenPart part in ordered)
            {
                string line = part.Summary(this);
                if (line.Length > 0) sb.Append('\n').Append(line);
            }
            return sb.ToString();
        }

        public void PostWorldGenerate(SimWorld.World.World? world)
        {
            foreach (ScenPart part in parts) part.PostWorldGenerate(world);
        }

        public void PostGameStart(IScenarioContext ctx)
        {
            foreach (ScenPart part in parts) part.PostGameStart(ctx);
        }

        public IEnumerable<string> ConfigErrors()
        {
            foreach (ScenPart part in parts)
            {
                foreach (string error in part.ConfigErrors())
                {
                    yield return part.GetType().Name + ": " + error;
                }
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name", "");
            Scribe_Values.Look(ref summary, "summary", "");
            Scribe_Values.Look(ref description, "description", "");
            List<ScenPart>? list = parts;
            Scribe_Collections.Look(ref list, "parts", LookMode.Deep);
            parts = list ?? new List<ScenPart>();
        }
    }

    /// <summary>A named, shippable <see cref="Scenario"/> (RimWorld: <c>Verse.ScenarioDef</c>).</summary>
    public class ScenarioDef : Def
    {
        public Scenario scenario = null!;

        public bool isScenarioDefault;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (scenario == null)
            {
                yield return "scenario is required.";
            }
            else
            {
                foreach (string error in scenario.ConfigErrors()) yield return error;
            }
        }
    }

    [DefOf]
    public static class ScenarioDefOf
    {
        public static ScenarioDef TribalStart = null!;
    }
}
