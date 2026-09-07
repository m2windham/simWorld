using System;
using System.Collections.Generic;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>What a <see cref="ThinkNode"/> came back with (RimWorld: <c>Verse.AI.ThinkResult</c>).</summary>
    public readonly struct ThinkResult
    {
        public Job? Job { get; }
        public ThinkNode? SourceNode { get; }

        public ThinkResult(Job job, ThinkNode sourceNode)
        {
            Job = job ?? throw new ArgumentNullException(nameof(job));
            SourceNode = sourceNode;
        }

        public static readonly ThinkResult NoJob = default;

        public bool IsValid => Job != null;
    }

    /// <summary>
    /// One node of a pawn's priority think tree (RimWorld: <c>Verse.AI.ThinkNode</c>). Content builds a tree
    /// of these under a <see cref="ThinkTreeDef"/> with <c>Class=</c> polymorphism; <see cref="Pawn_JobTracker"/>
    /// evaluates it top-down every time the pawn needs a new job.
    /// </summary>
    public abstract class ThinkNode
    {
        public abstract ThinkResult TryIssueJobPackage(Pawn pawn);
    }

    /// <summary>Tries each child in list order; the first one to return a job wins (RimWorld: <c>Verse.AI.ThinkNode_Priority</c>).</summary>
    public class ThinkNode_Priority : ThinkNode
    {
        public List<ThinkNode> subNodes = new List<ThinkNode>();

        public override ThinkResult TryIssueJobPackage(Pawn pawn)
        {
            for (int i = 0; i < subNodes.Count; i++)
            {
                ThinkResult result = subNodes[i].TryIssueJobPackage(pawn);
                if (result.IsValid) return result;
            }
            return ThinkResult.NoJob;
        }
    }
}
