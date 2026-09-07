using System;
using System.Collections.Generic;

namespace SimWorld.Quests
{
    /// <summary>
    /// Named scratch values threaded through one quest generation pass (RimWorld: <c>RimWorld.QuestGen.Slate</c>).
    /// <see cref="QuestNode"/>s write and read it instead of passing values as method arguments; a pushed
    /// <see cref="PushPrefix"/> namespaces every name written until it is popped, so a node that runs a
    /// sub-tree more than once (not used by SimWorld's compact node set yet, but kept for parity) doesn't
    /// collide with itself.
    /// </summary>
    public sealed class Slate
    {
        private readonly Dictionary<string, object?> vars = new Dictionary<string, object?>(StringComparer.Ordinal);
        private readonly List<string> prefixStack = new List<string>();

        public void Set(string name, object? value)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            vars[Prefixed(name)] = value;
        }

        public T? Get<T>(string name, T? defaultValue = default)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            return vars.TryGetValue(Prefixed(name), out object? value) && value is T typed ? typed : defaultValue;
        }

        public bool TryGet<T>(string name, out T? value)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (vars.TryGetValue(Prefixed(name), out object? raw) && raw is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public bool Exists(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            return vars.ContainsKey(Prefixed(name));
        }

        public void PushPrefix(string prefix)
        {
            if (prefix == null) throw new ArgumentNullException(nameof(prefix));
            prefixStack.Add(prefix);
        }

        public void PopPrefix()
        {
            if (prefixStack.Count == 0) throw new InvalidOperationException("Slate prefix stack is empty.");
            prefixStack.RemoveAt(prefixStack.Count - 1);
        }

        private string Prefixed(string name) =>
            prefixStack.Count == 0 ? name : string.Join(".", prefixStack) + "." + name;
    }
}
