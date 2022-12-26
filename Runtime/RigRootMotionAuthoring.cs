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
        public float3         Velocity;
        public float3         Delta;
        public float3         PreviousDelta;
        public float3         Position;
        public float3         PreviousPosition;
        public bool           BlendingKeyframes;
        public RigidTransform PreviousStream;
        public RigidTransform DeltaX;
    }
}