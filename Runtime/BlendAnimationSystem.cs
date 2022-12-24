using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace AnimationSystem
{
    [BurstCompile]
    //[UpdateAfter(PlayAnimationSystem)]
    public partial struct BlendAnimationSystem : ISystem
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
            var deltaTime = SystemAPI.Time.DeltaTime;
            
            foreach (var blendAspect in SystemAPI.Query<ClipBlendingAspect>())
            {
                if (blendAspect.AnimationBlendingController.ValueRW.ShouldBlend && blendAspect.AnimationBlendingController.ValueRW.Status == BlendStatus.Finished)
                {
                    blendAspect.AnimationBlendingController.ValueRW.CurrentDuration = 0.0f;
                    blendAspect.AnimationBlendingController.ValueRW.BlendDuration   = 0.35f;
                    blendAspect.AnimationBlendingController.ValueRW.Status          = BlendStatus.Blending;
                }
                
                if (blendAspect.AnimationBlendingController.ValueRW.Status == BlendStatus.Blending)
                {
                    blendAspect.AnimationBlendingController.ValueRW.CurrentDuration += deltaTime;
                    
                    var blendDuration                  = blendAspect.AnimationBlendingController.ValueRW.BlendDuration;
                    var blendTime                      = blendAspect.AnimationBlendingController.ValueRW.CurrentDuration;
                    var blendTimeLeft                  = math.abs(blendTime-blendDuration);
                    var blendTimeLeftNormalized        = blendTimeLeft / blendDuration;
                    var blendStrength                  = 1.0f - blendTimeLeftNormalized;
                    blendAspect.AnimationBlendingController.ValueRW.Strength = blendStrength;
                    
                    if (blendAspect.AnimationBlendingController.ValueRW.CurrentDuration >= blendAspect.AnimationBlendingController.ValueRW.BlendDuration)
                    {
                        blendAspect.AnimationBlendingController.ValueRW.Status = BlendStatus.Finished;
                        blendAspect.AnimationBlendingController.ValueRW.ShouldBlend = false;
                    }
                }
            }
            
            state.Dependency = new UpdateAnimatedEntitesJob()
            {
                PlayerLookup = SystemAPI.GetComponentLookup<AnimationPlayer>(true),
                ClipLookup   = SystemAPI.GetBufferLookup<AnimationClipData>(true),
                BlendingLookup = SystemAPI.GetComponentLookup<AnimationBlending>(true),
            }.ScheduleParallel(state.Dependency);

        }
        
        [BurstCompile]
        //[WithNone(typeof(AnimatedEntityRootTag))]
        partial struct UpdateAnimatedEntitesJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<AnimationPlayer>             PlayerLookup;
            [ReadOnly] public BufferLookup<AnimationClipData>              ClipLookup;
            [ReadOnly] public ComponentLookup<AnimationBlending> BlendingLookup;

            [BurstCompile]
            public void Execute(
                AnimatedEntityDataInfo info,
                DynamicBuffer<AnimatedEntityClipInfo> clipInfo,
    #if !ENABLE_TRANSFORM_V1
                ref LocalTransform localTransform
    #else
                ref Translation translation,
                ref Rotation rotation
    #endif
            )
            {
                var animationPlayer = PlayerLookup[info.AnimationDataOwner];
                var animationBlending = BlendingLookup[info.AnimationDataOwner];
                
                if(!animationPlayer.Playing) 
                    return;
                
                var clipBuffer = ClipLookup[info.AnimationDataOwner];
                var clip = clipBuffer[animationPlayer.CurrentClipIndex];
                
                ref AnimationBlob animation          = ref clip.AnimationBlob.Value;
                int     keyFrameArrayIndex = clipInfo[animationPlayer.CurrentClipIndex].IndexInKeyframeArray;
                
                var     prevClip               = clipBuffer[animationBlending.PreviousClipIndex];
                var     nextClip               = clipBuffer[animationBlending.NextClipIndex];
                ref var prevAnimation          = ref prevClip.AnimationBlob.Value;
                ref var nextAnimation          = ref nextClip.AnimationBlob.Value;
                var     prevKeyFrameArrayIndex = clipInfo[animationBlending.PreviousClipIndex].IndexInKeyframeArray;
                var     nextKeyFrameArrayIndex = clipInfo[animationBlending.NextClipIndex].IndexInKeyframeArray;
                
                // Position
                var previousPosition = GetKeyframePosition(prevKeyFrameArrayIndex, animationPlayer, prevClip, ref prevAnimation);
                var previousRotation = GetKeyframeRotation(prevKeyFrameArrayIndex, animationPlayer, prevClip, ref prevAnimation);
                
                var nextPosition = GetKeyframePosition(nextKeyFrameArrayIndex, animationPlayer, nextClip, ref nextAnimation);
                var nextRotation = GetKeyframeRotation(nextKeyFrameArrayIndex, animationPlayer, nextClip, ref nextAnimation);
                
#if !ENABLE_TRANSFORM_V1
                if (animationBlending.Status == BlendStatus.Blending)
                {
                    localTransform.Position = math.lerp(previousPosition, nextPosition, animationBlending.Strength);
                    localTransform.Rotation = math.slerp(previousRotation, nextRotation, animationBlending.Strength);
                }
                else
                {
                    localTransform.Position = nextPosition;
                    localTransform.Rotation = nextRotation;
                }
#else
                if (animationBlending.Status == BlendStatus.Blending)
                {
                    
                    translation.Value = math.lerp(previousPosition, nextPosition, animationBlending.Strength);
                    rotation.Value = math.slerp(previousRotation, nextRotation, animationBlending.Strength);
                }
                else
                {
                    translation.Value = nextPosition;
                    rotation.Value = nextRotation;
                }=
#endif
            }
            
            public float3 GetKeyframePosition(int keyFrameArrayIndex, AnimationPlayer animationPlayer, AnimationClipData clip, ref AnimationBlob animation)
            {
                ref var keys   = ref animation.PositionKeys[keyFrameArrayIndex];
                var     length = keys.Length;
                if (length > 0)
                {
                    var nextKeyIndex = 0;
                    for (int i = 0; i < length; i++)
                    {
                        if (keys[i].Time > animationPlayer.Elapsed)
                        {
                            nextKeyIndex = i;
                            break;
                        }
                    }

                    var prevKeyIndex = (nextKeyIndex == 0) ? length - 1 : nextKeyIndex - 1;
                    var prevKey      = keys[prevKeyIndex];
                    var nextKey      = keys[nextKeyIndex];
                    var timeBetweenKeys = (nextKey.Time > prevKey.Time)
                        ? nextKey.Time - prevKey.Time
                        : (nextKey.Time + animationPlayer.CurrentDuration) - prevKey.Time;

                    var t   = (animationPlayer.Elapsed - prevKey.Time) / timeBetweenKeys;
                    var pos = math.lerp(prevKey.Value, nextKey.Value, t);
                    return pos;
                }
                return float3.zero;
            }
            
            public quaternion GetKeyframeRotation(int keyFrameArrayIndex, AnimationPlayer animationPlayer, AnimationClipData clip, ref AnimationBlob animation)
            {
                ref var keys   = ref animation.RotationKeys[keyFrameArrayIndex];
                var     length = keys.Length;
                if (length > 0)
                {
                    var nextKeyIndex = 0;
                    for (int i = 0; i < length; i++)
                    {
                        if (keys[i].Time > animationPlayer.Elapsed)
                        {
                            nextKeyIndex = i;
                            break;
                        }
                    }

                    var prevKeyIndex = (nextKeyIndex == 0) ? length - 1 : nextKeyIndex - 1;
                    var prevKey      = keys[prevKeyIndex];
                    var nextKey      = keys[nextKeyIndex];
                    var timeBetweenKeys = (nextKey.Time > prevKey.Time)
                        ? nextKey.Time - prevKey.Time
                        : (nextKey.Time + animationPlayer.CurrentDuration) - prevKey.Time;

                    var t   = (animationPlayer.Elapsed - prevKey.Time) / timeBetweenKeys;
                    var rot = math.slerp(prevKey.Value, nextKey.Value, t);
                    return rot;
                }
                return quaternion.identity;
            }
        }
    }
    
}