using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Puetsua.VRCEasyLoco
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Puetsua Workshop/EasyLoco")]
    public class EasyLoco : MonoBehaviour, IEditorOnly
    {
        /// <summary>
        /// A single selectable idle pose. The first entry of each stance list is the "Default"
        /// pose: its <see cref="clip"/> may be overridden, but the entry itself cannot be removed.
        /// </summary>
        [Serializable]
        public class IdlePose
        {
            public string menuName;
            public AnimationClip clip;

            public IdlePose()
            {
            }

            public IdlePose(string menuName, AnimationClip clip)
            {
                this.menuName = menuName;
                this.clip = clip;
            }
        }

        /// <summary>
        /// The three-stage AFK animation for one stance branch. A null clip keeps the built-in
        /// Afk*Default clip for that stage.
        /// </summary>
        [Serializable]
        public class AfkSet
        {
            public AnimationClip entering;
            public AnimationClip looping;
            public AnimationClip exiting;
        }

        /// <summary>
        /// One on-side sleep pose. <see cref="normal"/> plays with the feet free; the feet-locked
        /// branch swaps in its own variant per facing, because holding both feet on the floor needs
        /// a differently shaped pose than the free one.
        /// </summary>
        [Serializable]
        public class SleepSideSet
        {
            public AnimationClip normal;
            public AnimationClip feetLockUp;
            public AnimationClip feetLockDown;
        }

        /// <summary>
        /// The sleep poses blended by head orientation while sleep mode is on. A null clip keeps the
        /// built-in Sleep* clip for that facing. Facing up and down are shared between the free and
        /// feet-locked branches; only the on-side poses differ.
        /// </summary>
        [Serializable]
        public class SleepSet
        {
            public AnimationClip up;
            public AnimationClip down;
            public SleepSideSet left = new SleepSideSet();
            public SleepSideSet right = new SleepSideSet();
        }

        // Row 0 of each list is the locked Default pose. Additional rows add extra poses that the
        // avatar can switch between from the EasyLoco expression menu at runtime.
        public List<IdlePose> standPoses = new List<IdlePose>();
        public List<IdlePose> crouchPoses = new List<IdlePose>();
        public List<IdlePose> pronePoses = new List<IdlePose>();

        // Head orientation picks between these while sleeping; contacts on the sleep sensor rig
        // drive the blend.
        public SleepSet sleep = new SleepSet();

        // AFK is branched by posture at runtime; each stance plays its own entering/looping/exiting.
        public AfkSet standAfk = new AfkSet();
        public AfkSet crouchAfk = new AfkSet();
        public AfkSet proneAfk = new AfkSet();

        /// <summary>
        /// The avatar this component configures, or null when it is not on an avatar. Deliberately
        /// the same GameObject only - not a search up the hierarchy: the "Add EasyLoco Component"
        /// menu only ever puts it on the descriptor, and with nested avatars a parent search would
        /// silently bind to whichever descriptor happened to be closest above it.
        /// </summary>
        public VRCAvatarDescriptor Avatar => GetComponent<VRCAvatarDescriptor>();
    }
}
