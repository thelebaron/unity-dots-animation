﻿using Unity.Entities;
using Unity.Burst;
using Unity.Collections;
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
    [UpdateAfter(typeof(BlendAnimationSystem))]
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
            state.Dependency = new RootBoneKeyFrameJob().Schedule(state.Dependency);
            
            state.Dependency = new OverwriteRootPosition
            {
                AnimationRootMotionLookup = SystemAPI.GetComponentLookup<AnimationRootMotion>(),
            }.Schedule(state.Dependency);
            state.Dependency = new AnimatedRootMotionJob{ }.Schedule(state.Dependency);

        }
        
        [BurstCompile]
        partial struct OverwriteRootPosition  : IJobEntity
        {
            public ComponentLookup<AnimationRootMotion> AnimationRootMotionLookup;
            public void Execute(Entity entity, RootBone rootBone, AnimatedEntityDataInfo info, ref LocalTransform localTransform)
            {
                localTransform.Position.x = 0;
                localTransform.Position.z = 0;
                var animationRootMotion = AnimationRootMotionLookup[info.AnimationDataOwner];
                animationRootMotion.Delta = rootBone.Delta;
                AnimationRootMotionLookup[info.AnimationDataOwner] = animationRootMotion;
            }
        }
        
        [BurstCompile]
        partial struct AnimatedRootMotionJob  : IJobEntity
        {
            public void Execute(Entity entity, ref AnimationRootMotion animationRootMotion, ref LocalTransform localTransform)
            {
                localTransform.Position.x += animationRootMotion.Delta.x;
                localTransform.Position.z += animationRootMotion.Delta.z;
            }
        }

        partial struct RootBoneKeyFrameJob : IJobEntity
        {
            public void Execute(ref KeyframeData keyframeData, ref RootBone rootBone)
            {
                //animatedKeyframe.PreviousIndex = animatedKeyframe.Index;

                if (keyframeData.CurrentKeyIndex.Equals(1) && keyframeData.PreviousKeyIndex > 1)
                {
                    rootBone.Delta = 0;
                }
                if(rootBone.BlendingKeyframes)
                    rootBone.Delta = 0;
            }
        }
    }
    
}