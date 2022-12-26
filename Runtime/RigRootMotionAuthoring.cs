using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace AnimationSystem
{
    public class RigRootMotionAuthoring : MonoBehaviour
    {
        
    }
    
    public struct Rig : IComponentData
    {
        public Entity RootBone;
    }
    
    public struct AnimationRootMotion : IComponentData, IEnableableComponent
    {
        public float3 Delta;
    }
    
    public struct RootBone : IComponentData
    {
        public float3                 Delta;
        public float3                 Position;
        public float3                 PreviousPosition;
        //public bool                   LoopKey;
        //public bool                   LastLoopKey;
        public FixedList512Bytes<bool> KeyLoop; // 0 = false, 1 = true
    }
}