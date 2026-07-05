using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Puetsua.VRCEasyLoco
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Puetsua Workshop/EasyLoco")]
    public class EasyLoco : MonoBehaviour, IEditorOnly
    {
        public bool useCustomBaseLocomotion;
        public AnimationClip baseStandStill;
        public AnimationClip baseCrouchStill;
        public AnimationClip baseLowCrawlStill;

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
