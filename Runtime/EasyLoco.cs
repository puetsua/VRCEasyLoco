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

        // Row 0 of each list is the locked Default pose. Additional rows add extra poses that the
        // avatar can switch between from the EasyLoco expression menu at runtime.
        public List<IdlePose> standPoses = new List<IdlePose>();
        public List<IdlePose> crouchPoses = new List<IdlePose>();
        public List<IdlePose> pronePoses = new List<IdlePose>();

        public bool useCustomAction;
        public AnimationClip actionAfk;

        public VRCAvatarDescriptor Avatar
        {
            get
            {
                var descriptors = GetComponentsInParent<VRCAvatarDescriptor>(true);
                return descriptors.Length > 0 ? descriptors[0] : null;
            }
        }
    }
}
