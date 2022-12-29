using Unity.Entities;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Transforms;

namespace AnimationSystem
{
    /*
     * To determine the motion of a character each frame, we calculate the range
     * on the animation time line that this frame update covered and return a
     * delta value between position of the root bone at the start and at the
     * end of the time covered on the animation timeline.
     *
     * [0] [1] [2] [3] [4] [5]
     * |   |   |   |   |   |
     * 1   2   3   4   5   6
     * | 1 | 2 | 3 | 4 | 5 |
     *
     * When looping frame 5 to 0, the delta is -5
     * but to correct for this the delta should be 1
     * so the root bone is moved 1 unit forward.
     *
     * divide the -position delta by the total frames in the animation?
     *
     * This system is responsible for calculating the delta
     * and applying it to the root bone.
     * 
     */
    
    [BurstCompile]
    [UpdateAfter(typeof(CalculateAnimationBonesSystem))]
    public partial struct RootMotionSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new OverwriteRootPosition
            {
                DeltaTime                 = SystemAPI.Time.DeltaTime,
                AnimationRootMotionLookup = SystemAPI.GetComponentLookup<AnimationRootMotion>(),
            }.Schedule(state.Dependency);
            state.Dependency = new AnimatedRootMotionJob{ DeltaTime = SystemAPI.Time.DeltaTime,}.Schedule(state.Dependency);

        }
        
        [BurstCompile]
        partial struct OverwriteRootPosition  : IJobEntity
        {
            public float                                DeltaTime;
            public ComponentLookup<AnimationRootMotion> AnimationRootMotionLookup;
            public void Execute(Entity entity, RootBone rootBone, AnimatedRootEntity info, ref LocalTransform localTransform)
            {
                var animationRootMotion = AnimationRootMotionLookup[info.AnimationDataOwner];
                var delta               = localTransform.Position;
                var prevDelta           = animationRootMotion.Delta; 
                delta                                              *= DeltaTime * 40;
                animationRootMotion.Delta                          =  delta;
                AnimationRootMotionLookup[info.AnimationDataOwner] =  animationRootMotion;
                localTransform.Position.x                          =  0;
                localTransform.Position.z                          =  0;
            }
        }
        
        [BurstCompile]
        partial struct AnimatedRootMotionJob  : IJobEntity
        {
            public float DeltaTime;
            public void Execute(Entity entity, LocalToWorld localToWorld,ref AnimationRootMotion rootMotion, ref LocalTransform localTransform)
            {
                // multiply rootmotion.Delta by current rotation
                //var delta = math.mul(localToWorld.Value, new float4(rootMotion.Delta, 0));
                var delta = math.mul(localToWorld.Rotation, rootMotion.Delta);
                delta.y = 0;
                // interpolate using delta
                //var interpolated = math.lerp(localTransform.Position, localTransform.Position + delta, 90 * DeltaTime);
                localTransform.Position += delta;
            }
        }
    }
    
    
    [BurstCompile]
    [UpdateInGroup(typeof(TransformSystemGroup), OrderFirst = true)]
    public partial struct RotationEulerSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new RotationEulerJob().Schedule(state.Dependency);
        }
        
        [BurstCompile]
        public partial struct RotationEulerJob  : IJobEntity
        {
            public void Execute(RotationEulerXYZ rootMotion, ref LocalTransform localTransform)
            {
                localTransform.Rotation   =  quaternion.EulerXYZ(math.radians(rootMotion.Value));
            }
        }
    }

    public struct RotationEulerXYZ : IComponentData
    {
        public float3 Value;
    }
    
}