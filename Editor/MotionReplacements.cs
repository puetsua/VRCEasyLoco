using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Puetsua.VRCEasyLoco.Editor
{
    /// <summary>
    /// Name → motion map plus a ledger of which names a template walk actually found.
    /// Take replacements out through <see cref="TryGet"/> so unmatched keys fail the build.
    /// </summary>
    internal sealed class MotionReplacements
    {
        private readonly Dictionary<string, Motion> byName;
        private readonly HashSet<string> matched = new HashSet<string>();

        public MotionReplacements(IReadOnlyDictionary<string, Motion> replacements)
        {
            byName = replacements == null
                ? new Dictionary<string, Motion>()
                : replacements.ToDictionary(entry => entry.Key, entry => entry.Value);
        }

        public bool IsEmpty => byName.Count == 0;

        public bool TryGet(string name, out Motion replacement)
        {
            if (!byName.TryGetValue(name, out replacement))
            {
                return false;
            }

            matched.Add(name);
            return true;
        }

        /// <summary>True if <paramref name="name"/> is a key. Does not count as a match.</summary>
        public bool IsReplacementTarget(string name)
        {
            return byName.ContainsKey(name);
        }

        /// <summary>Slot this motion was registered under. Does not count as a match.</summary>
        public bool TryGetKey(Motion motion, out string key)
        {
            foreach (var entry in byName)
            {
                if (entry.Value == motion)
                {
                    key = entry.Key;
                    return true;
                }
            }

            key = null;
            return false;
        }

        public IReadOnlyList<string> Unmatched()
        {
            return byName.Keys.Where(name => !matched.Contains(name)).OrderBy(name => name).ToList();
        }

        public void ThrowIfUnmatched(string kind, string where)
        {
            var unmatched = Unmatched();
            if (unmatched.Count == 0)
            {
                return;
            }

            var names = string.Join(", ", unmatched.Select(name => "\"" + name + "\""));
            throw new System.InvalidOperationException(
                $"{where} has no {kind} named {names}. The {kind}s this package writes into are matched by name, "
                + "so a template that renamed them would have dropped the animations set for them in silence.");
        }
    }
}
