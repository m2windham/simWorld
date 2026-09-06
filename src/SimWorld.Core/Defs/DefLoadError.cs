using System;
using System.Collections.Generic;

namespace SimWorld.Defs
{
    /// <summary>One problem found while loading content. Loading continues past errors; see <see cref="DefLoadOptions.ThrowOnError"/>.</summary>
    public sealed class DefLoadError
    {
        public string Message { get; }
        public string? File { get; }
        public string? DefName { get; }

        public DefLoadError(string message, string? file = null, string? defName = null)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            File = file;
            DefName = defName;
        }

        public override string ToString()
        {
            string where = File != null ? " [" + File + (DefName != null ? " :: " + DefName : "") + "]" : (DefName != null ? " [" + DefName + "]" : "");
            return Message + where;
        }
    }

    /// <summary>Thrown by <see cref="DefLoader.Load"/> when <see cref="DefLoadOptions.ThrowOnError"/> is set and errors occurred.</summary>
    public sealed class DefLoadException : Exception
    {
        public IReadOnlyList<DefLoadError> Errors { get; }

        public DefLoadException(IReadOnlyList<DefLoadError> errors)
            : base(BuildMessage(errors))
        {
            Errors = errors;
        }

        private static string BuildMessage(IReadOnlyList<DefLoadError> errors)
        {
            var lines = new List<string> { errors.Count + " error(s) while loading Defs:" };
            foreach (DefLoadError error in errors)
            {
                lines.Add("  - " + error);
            }
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>Outcome of one <see cref="DefLoader.Load"/> call.</summary>
    public sealed class DefLoadResult
    {
        public IReadOnlyList<Def> Defs { get; }
        public IReadOnlyList<DefLoadError> Errors { get; }
        public bool Success => Errors.Count == 0;

        public DefLoadResult(IReadOnlyList<Def> defs, IReadOnlyList<DefLoadError> errors)
        {
            Defs = defs ?? throw new ArgumentNullException(nameof(defs));
            Errors = errors ?? throw new ArgumentNullException(nameof(errors));
        }
    }
}
