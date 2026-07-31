using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Puetsua.VRCEasyLoco.Editor
{
    /// <summary>
    /// The name -> motion map a build hands to a template walk, and the ledger of which of those
    /// names the walk actually found.
    ///
    /// Every key is a claim about the template: this motion, or this state, is in there and is the
    /// thing the user's clip belongs on. Nothing used to check the claim. A renamed clip in a
    /// Default* tree, a renamed AFK state, and the key simply matched nothing - the build wrote a
    /// controller that looked right, and the user's animation was quietly not in it. That is the
    /// same shape of failure as the idle overrides leaking into the VR branch: correct-looking
    /// output, wrong content, found only in-game.
    ///
    /// So the lookup is the ledger. <see cref="TryGet"/> is the only way out of here, which is what
    /// keeps the record honest - a caller cannot swap a motion in without it being counted, and any
    /// walk added later is covered without being told to be.
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

        /// <summary>The replacement for <paramref name="name"/>, counting it as found.</summary>
        public bool TryGet(string name, out Motion replacement)
        {
            if (!byName.TryGetValue(name, out replacement))
            {
                return false;
            }

            matched.Add(name);
            return true;
        }

        /// <summary>
        /// Whether this name is one of the keys, without counting it as found. Only for deciding
        /// whether a subtree is worth cloning: the swap inside the clone is what does the counting,
        /// so a tree that gets looked at and skipped never reads as a match.
        /// </summary>
        public bool Contains(string name)
        {
            return byName.ContainsKey(name);
        }

        /// <summary>
        /// The keys no walk ever found, sorted so the build's error message reads the same twice.
        /// Empty is the expected answer - anything else means the template stopped carrying
        /// something this package names.
        /// </summary>
        public IReadOnlyList<string> Unmatched()
        {
            return byName.Keys.Where(name => !matched.Contains(name)).OrderBy(name => name).ToList();
        }

        /// <summary>
        /// Ends a walk: every key still unaccounted for fails the build, naming them.
        /// <paramref name="kind"/> is what the names are ("motion", "state"), <paramref name="where"/>
        /// what was searched.
        ///
        /// The throwing lives here rather than at each call site on purpose. Taking replacements out
        /// through <see cref="TryGet"/> earns the recording for free, but a walk that never asks for
        /// the verdict records honestly and reports nothing - which is the original bug wearing a
        /// ledger. One method to end with is a rule that can be followed.
        /// </summary>
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
