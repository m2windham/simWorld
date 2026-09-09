using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using SimWorld.Content;
using SimWorld.Defs;
using SimWorld.Research;

namespace SimWorld.ReachHarness
{
    /// <summary>
    /// Entry point. See tools/research/README.md; the findings this produces live in
    /// docs/research/tech-reachability.md.
    /// </summary>
    public static class Program
    {
        private static TreeModel tree = null!;

        public static int Main(string[] args)
        {
            var sw = Stopwatch.StartNew();
            LoadContent();
            tree = new TreeModel();

            string command = args.Length > 0 ? args[0] : "all";
            IReadOnlyList<int> seeds = RunConfig.DefaultSeeds;
            List<ResearchPolicy> full = PolicyRegistry.All();
            List<ResearchPolicy> core = PolicyRegistry.Core();

            switch (command)
            {
                case "tree":
                    Reports.TreeStats(tree);
                    break;
                case "baseline":
                    Baseline(full, seeds);
                    break;
                case "eras":
                    EraShape(full, seeds);
                    break;
                case "sweep":
                    Sweep(core, seeds);
                    break;
                case "modifiers":
                    Modifiers(full, seeds);
                    break;
                case "run":
                    Custom(args, full, seeds);
                    break;
                case "all":
                    Reports.TreeStats(tree);
                    Baseline(full, seeds);
                    EraShape(full, seeds);
                    Sweep(core, seeds);
                    Modifiers(full, seeds);
                    break;
                default:
                    Console.Error.WriteLine("commands: tree | baseline | eras | sweep | modifiers | all | run <modifierSpec> [key=value ...]");
                    return 1;
            }

            Console.WriteLine();
            Console.WriteLine($"({sw.Elapsed.TotalSeconds:0.0}s)");
            return 0;
        }

        private static void LoadContent()
        {
            var db = new DefDatabase();
            DefLoadResult result = CoreContent.Load(db, new DefTypeResolver(), new DefLoadOptions { BindDefOfs = true });
            DefDatabase.Global = db;
            if (result.Errors.Count > 0)
            {
                Console.Error.WriteLine($"content loaded with {result.Errors.Count} errors:");
                foreach (DefLoadError e in result.Errors.Take(10)) Console.Error.WriteLine("  " + e);
            }
        }

        /// <summary>
        /// The headline panel. Two baselines because the shipped runtime has two honest readings: the
        /// researcher tech level is set once by ScenPart_StartingEra and nothing ever advances it, so
        /// "as shipped" pays the stale CostFactor and "tech tracks era" is the same tree without it.
        /// </summary>
        /// <summary>
        /// The era-shape report, run over the configuration the game actually ships (tech level tracking the
        /// era, which is how ResearchManager now behaves) and over a range of research throughputs. Throughput
        /// stands in for everything the abstracted clock makes unmeasurable in years: a civilization that
        /// spends more of itself on research reaches the same place sooner, and the question is whether the
        /// *shape* of an era survives that, not how many years it took.
        /// </summary>
        private static void EraShape(IReadOnlyList<ResearchPolicy> policies, IReadOnlyList<int> seeds)
        {
            Reports.Heading("Era shape: how much of an era a civilization sees before leaving it");

            foreach (float throughput in new[] { 0.5f, 1f, 2f, 4f })
            {
                RunConfig c = BaseConfig();
                c.ThroughputMultiplier = throughput;
                PanelResult p = Panel.Run(tree, c, "techtrack", policies, seeds, $"throughput x{throughput:0.#}");
                Console.WriteLine();
                Console.WriteLine($"-- shipped, throughput x{throughput:0.#} --");
                Reports.EraShape(p, tree);
            }

            // The candidate fix, measured rather than assumed: price the leaves — the content nothing else
            // depends on — so sweeping all of them up while working the spine stops being free.
            foreach (float leaf in new[] { 2f, 4f, 8f })
            {
                PanelResult p = Panel.Run(tree, BaseConfig(), $"techtrack,leafcost{leaf:0.#}", policies, seeds, $"leaf cost x{leaf:0.#}");
                Console.WriteLine();
                Console.WriteLine($"-- leaf cost x{leaf:0.#} (spine unchanged) --");
                Reports.EraShape(p, tree);
            }
        }

        private static void Baseline(IReadOnlyList<ResearchPolicy> policies, IReadOnlyList<int> seeds)
        {
            RunConfig config = BaseConfig();

            PanelResult shipped = Panel.Run(tree, config, "none", policies, seeds, "baseline (as shipped)");
            Reports.PanelSummary(shipped);
            Reports.PerPolicy(shipped);
            Reports.EraTable(shipped, tree);
            Reports.DeadContent(shipped, tree, 60);

            PanelResult tracked = Panel.Run(tree, config, "techtrack", policies, seeds, "baseline (tech level tracks era)");
            Reports.PanelSummary(tracked);
            Reports.PerPolicy(tracked);
            Reports.EraTable(tracked, tree);
            Reports.DeadContent(tracked, tree);

            foreach (string start in new[] { "Agrarian", "Industrial" })
            {
                RunConfig c = config.Clone();
                c.StartEra = start;
                PanelResult p = Panel.Run(tree, c, "none", policies, seeds, $"start era {start} (shipped scenario)");
                Reports.PanelSummary(p);
                Reports.EraTable(p, tree);
            }
        }

        /// <summary>Sensitivity to the inputs the baseline had to assume: throughput, horizon, start era, attention.</summary>
        private static void Sweep(IReadOnlyList<ResearchPolicy> policies, IReadOnlyList<int> seeds)
        {
            var panels = new List<PanelResult>();

            foreach (float researchers in new[] { 1f, 2f, 4f, 8f, 16f, 32f })
            {
                RunConfig c = BaseConfig();
                c.Researchers = researchers;
                panels.Add(Panel.Run(tree, c, "none", policies, seeds, $"researchers={researchers:0}"));
            }
            Reports.Compare($"Sweep: research throughput (50y, sticks start, as shipped; {policies.Count}-archetype core panel)", panels);

            panels.Clear();
            foreach (int years in new[] { 10, 25, 50, 100, 200, 500 })
            {
                RunConfig c = BaseConfig();
                c.HorizonYears = years;
                panels.Add(Panel.Run(tree, c, "none", policies, seeds, $"horizon={years}y"));
            }
            Reports.Compare("Sweep: playthrough horizon (2 researchers, as shipped)", panels);

            panels.Clear();
            foreach (EraDef era in tree.Eras)
            {
                RunConfig c = BaseConfig();
                c.StartEra = era.defName;
                panels.Add(Panel.Run(tree, c, "none", policies, seeds, $"start={era.defName}"));
            }
            Reports.Compare("Sweep: starting era (50y, 2 researchers)", panels);

            panels.Clear();
            foreach (float growth in new[] { 0f, 0.03f, 0.06f, 0.12f })
            {
                RunConfig c = BaseConfig();
                c.ResearcherGrowthPerYear = growth;
                c.MaxResearchers = 64f;
                panels.Add(Panel.Run(tree, c, "none", policies, seeds, $"growth={growth:P0}/y cap 64"));
            }
            Reports.Compare("Sweep: a civilization that grows its researcher base (50y)", panels);

            panels.Clear();
            foreach (float attention in new[] { 0.4f, 0.6f, 0.85f, 1.0f })
            {
                RunConfig c = BaseConfig();
                c.Attention = attention;
                panels.Add(Panel.Run(tree, c, "none", policies, seeds, $"attention={attention:0.00}"));
            }
            Reports.Compare("Sweep: attention (fraction of days research runs at all)", panels);

            panels.Clear();
            foreach (int skill in new[] { 4, 8, 12, 16, 20 })
            {
                RunConfig c = BaseConfig();
                c.StartSkill = skill;
                c.SkillGrowth = false;
                panels.Add(Panel.Run(tree, c, "none", policies, seeds, $"skill={skill} (fixed)"));
            }
            Reports.Compare("Sweep: researcher Intellectual skill, no growth", panels);
        }

        /// <summary>Every candidate fix, measured against the same baseline panel.</summary>
        private static void Modifiers(IReadOnlyList<ResearchPolicy> policies, IReadOnlyList<int> seeds)
        {
            RunConfig config = BaseConfig();
            string[] specs =
            {
                "none",
                "techtrack",
                "trim12",
                "throughput4",
                "flatten10",
                "costfreeze4",
                "spine",
                "era60",
                "eragate",
                "eragate,spine",
                "eragate,era60",
                "costscale2",
                "costscale3",
                "costscale5",
                "costscale3,eragate,spine",
                "costscale3,eragate,spine,techtrack",
                "techtrack,throughput4",
            };
            var panels = new List<PanelResult>();
            foreach (string spec in specs) panels.Add(Panel.Run(tree, config, spec, policies, seeds, spec));
            Reports.Compare($"Candidate fixes (50y, sticks start, 2 researchers, {seeds.Count} seeds x {policies.Count} policies)", panels);

            foreach (PanelResult p in panels.Where(p => p.ModifierSpec is "trim12" or "eragate,spine" or "costscale3" or "costscale3,eragate,spine"))
            {
                Reports.PanelSummary(p);
                Reports.PerPolicy(p);
                Reports.EraTable(p, tree);
                Reports.DeadContent(p, tree);
            }
        }

        /// <summary>run &lt;modifierSpec&gt; [horizon=50] [researchers=2] [start=SticksAndStones] [attention=0.85] [skill=8] [growth=0]</summary>
        private static void Custom(string[] args, IReadOnlyList<ResearchPolicy> policies, IReadOnlyList<int> seeds)
        {
            string spec = args.Length > 1 ? args[1] : "none";
            RunConfig c = BaseConfig();
            foreach (string arg in args.Skip(2))
            {
                string[] kv = arg.Split('=', 2);
                if (kv.Length != 2) continue;
                float v;
                switch (kv[0])
                {
                    case "horizon": c.HorizonYears = int.Parse(kv[1], CultureInfo.InvariantCulture); break;
                    case "researchers": c.Researchers = float.Parse(kv[1], CultureInfo.InvariantCulture); break;
                    case "start": c.StartEra = kv[1]; break;
                    case "attention": c.Attention = float.Parse(kv[1], CultureInfo.InvariantCulture); break;
                    case "skill": c.StartSkill = int.Parse(kv[1], CultureInfo.InvariantCulture); break;
                    case "growth": c.ResearcherGrowthPerYear = float.Parse(kv[1], CultureInfo.InvariantCulture); break;
                    case "cap": c.MaxResearchers = float.Parse(kv[1], CultureInfo.InvariantCulture); break;
                    case "jitter": c.ChoiceJitter = float.Parse(kv[1], CultureInfo.InvariantCulture); break;
                    case "ticks": c.WorkTicksPerDay = float.Parse(kv[1], CultureInfo.InvariantCulture); break;
                    case "skillgrowth": c.SkillGrowth = float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v != 0f; break;
                }
            }
            PanelResult panel = Panel.Run(tree, c, spec, policies, seeds, "run " + spec);
            Reports.PanelSummary(panel);
            Reports.PerPolicy(panel);
            Reports.EraTable(panel, tree);
            Reports.DeadContent(panel, tree, 60);
        }

        /// <summary>
        /// The baseline assumptions, all in one place. 50 in-game years, a sticks-and-stones start, two
        /// researcher-equivalents at Intellectual 8 with growth, 15,000 research work-ticks a day, and
        /// research actually happening on 85% of days.
        /// </summary>
        private static RunConfig BaseConfig() => new()
        {
            HorizonYears = 50,
            StartEra = "SticksAndStones",
            Researchers = 2f,
            StartSkill = 8,
            SkillGrowth = true,
            WorkTicksPerDay = 15000f,
            Attention = 0.85f,
            ChoiceJitter = 0.15f,
        };
    }
}
