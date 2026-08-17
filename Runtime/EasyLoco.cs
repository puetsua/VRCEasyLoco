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
        /// One idle pose. Row 0 is the locked Default: its clip may change, the row may not be removed.
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

        [Serializable]
        public class AfkSet
        {
            public AnimationClip entering;
            public AnimationClip looping;
            public AnimationClip exiting;
        }

        /// <summary>
        /// Sleep poses by head orientation. <see cref="side"/> is authored lying on the left;
        /// blend trees mirror it for the right.
        /// </summary>
        [Serializable]
        public class SleepSet
        {
            public AnimationClip up;
            public AnimationClip down;
            public AnimationClip side;
        }

        public List<IdlePose> standPoses = new List<IdlePose>();
        public List<IdlePose> crouchPoses = new List<IdlePose>();
        public List<IdlePose> pronePoses = new List<IdlePose>();

        public SleepSet sleep = new SleepSet();

        public AfkSet standAfk = new AfkSet();
        public AfkSet crouchAfk = new AfkSet();
        public AfkSet proneAfk = new AfkSet();

        /// <summary>
        /// Descriptor on this GameObject, or null. Not a parent search: nested avatars would bind
        /// the nearest descriptor above, not the one this component was added to.
        /// </summary>
        public VRCAvatarDescriptor Avatar => GetComponent<VRCAvatarDescriptor>();
    }
}
