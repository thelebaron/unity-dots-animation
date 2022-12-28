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
        public float3         DeltaUnused;
    }
}