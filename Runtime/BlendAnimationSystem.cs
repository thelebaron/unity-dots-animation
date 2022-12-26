﻿using System;
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
            
            /*foreach (var blendAspect in SystemAPI.Query<ClipBlendingAspect>())
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
            }*/
            state.Dependency = new BlendAspectJob
            {
                DeltaTime = deltaTime,
            }.ScheduleParallel(state.Dependency);

            state.Dependency = new BlendAnimatedBonesJob()
            {
                PlayerLookup = SystemAPI.GetComponentLookup<AnimationPlayer>(true),
                ClipLookup   = SystemAPI.GetBufferLookup<AnimationClipData>(true),
                BlendingLookup = SystemAPI.GetComponentLookup<AnimationBlending>(true),
                RootBoneLookup = SystemAPI.GetComponentLookup<RootBone>(),
            }.ScheduleParallel(state.Dependency);

        }

        [BurstCompile]
        partial struct BlendAspectJob : IJobEntity
        {
            [ReadOnly] public float DeltaTime;

            public void Execute(Entity entity, ref ClipBlendingAspect blendAspect)
            {
                if (blendAspect.AnimationBlendingController.ValueRW.ShouldBlend && blendAspect.AnimationBlendingController.ValueRW.Status == BlendStatus.Finished)
                {
                    blendAspect.AnimationBlendingController.ValueRW.CurrentDuration = 0.0f;
                    blendAspect.AnimationBlendingController.ValueRW.BlendDuration   = 0.35f;
                    blendAspect.AnimationBlendingController.ValueRW.Status          = BlendStatus.Blending;
                }
                
                if (blendAspect.AnimationBlendingController.ValueRW.Status == BlendStatus.Blending)
                {
                    blendAspect.AnimationBlendingController.ValueRW.CurrentDuration += DeltaTime;
                    
                    var blendDuration           = blendAspect.AnimationBlendingController.ValueRW.BlendDuration;
                    var blendTime               = blendAspect.AnimationBlendingController.ValueRW.CurrentDuration;
                    var blendTimeLeft           = math.abs(blendTime-blendDuration);
                    var blendTimeLeftNormalized = blendTimeLeft / blendDuration;
                    var blendStrength           = 1.0f - blendTimeLeftNormalized;
                    blendAspect.AnimationBlendingController.ValueRW.Strength = blendStrength;
                    
                    if (blendAspect.AnimationBlendingController.ValueRW.CurrentDuration >= blendAspect.AnimationBlendingController.ValueRW.BlendDuration)
                    {
                        blendAspect.AnimationBlendingController.ValueRW.Status      = BlendStatus.Finished;
                        blendAspect.AnimationBlendingController.ValueRW.ShouldBlend = false;
                    }
                }
            }
        }

        [BurstCompile]
        //[WithNone(typeof(AnimatedEntityRootTag))]
        partial struct BlendAnimatedBonesJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<AnimationPlayer>   PlayerLookup;
            [ReadOnly] public BufferLookup<AnimationClipData>    ClipLookup;
            [ReadOnly] public ComponentLookup<AnimationBlending> BlendingLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<RootBone>          RootBoneLookup;

            public void Execute(Entity entity, 
                AnimatedEntityDataInfo info, 
                DynamicBuffer<AnimatedEntityClipInfo> clipInfo, 
                ref KeyframeData keyframeData,
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
                
                var     clipBuffer             = ClipLookup[info.AnimationDataOwner];
                var     nextClip               = clipBuffer[animationBlending.NextClipIndex];
                var     nextKeyFrameArrayIndex = clipInfo[animationBlending.NextClipIndex].IndexInKeyframeArray;
                
                var  nextPosition = GetKeyframePosition(nextKeyFrameArrayIndex, animationPlayer, nextClip, ref keyframeData);
                var  nextRotation = GetKeyframeRotation(nextKeyFrameArrayIndex, animationPlayer, nextClip);
                bool isRootBone   = RootBoneLookup.HasComponent(entity);
                bool isBlendingKeyframes   = false;
#if !ENABLE_TRANSFORM_V1
                if (animationBlending.Status == BlendStatus.Blending)
                {
                    isBlendingKeyframes = true;
                    var     prevClip               = clipBuffer[animationBlending.PreviousClipIndex];
                    var     prevKeyFrameArrayIndex = clipInfo[animationBlending.PreviousClipIndex].IndexInKeyframeArray;
                    
                    var previousPosition = GetKeyframePosition(prevKeyFrameArrayIndex, animationPlayer, prevClip, ref keyframeData);
                    var previousRotation = GetKeyframeRotation(prevKeyFrameArrayIndex, animationPlayer, prevClip);
                    
                    localTransform.Position = math.lerp(previousPosition, nextPosition, animationBlending.Strength);
                    localTransform.Rotation = math.slerp(previousRotation, nextRotation, animationBlending.Strength);
                    
                }
                else
                {
                    localTransform.Position = nextPosition;
                    localTransform.Rotation = nextRotation;
                }

                // Rootmotion calculation
                if (isRootBone)
                {
                    var rootBone = RootBoneLookup[entity];
                    {
                        rootBone.BlendingKeyframes = isBlendingKeyframes;
                        rootBone.PreviousPosition = rootBone.Position;
                        rootBone.Position         = localTransform.Position;
                        var delta = rootBone.PreviousPosition - rootBone.Position;
                        rootBone.Delta = delta;
                    }
                    RootBoneLookup[entity] = rootBone;
                }
#else
                if (animationBlending.Status == BlendStatus.Blending)
                {
                    var     prevClip               = clipBuffer[animationBlending.PreviousClipIndex];
                    ref var prevAnimation          = ref prevClip.AnimationBlob.Value;
                    var     prevKeyFrameArrayIndex = clipInfo[animationBlending.PreviousClipIndex].IndexInKeyframeArray;
                    // Position
                    var previousPosition = GetKeyframePosition(prevKeyFrameArrayIndex, animationPlayer, prevClip, ref prevAnimation);
                    var previousRotation = GetKeyframeRotation(prevKeyFrameArrayIndex, animationPlayer, prevClip, ref prevAnimation);

                    translation.Value = math.lerp(previousPosition, nextPosition, animationBlending.Strength);
                    rotation.Value = math.slerp(previousRotation, nextRotation, animationBlending.Strength);
                }
                else
                {
                    translation.Value = nextPosition;
                    rotation.Value = nextRotation;
                }
#endif

            }


            public float3 GetKeyframePosition(          
                int keyFrameArrayIndex, 
                AnimationPlayer animationPlayer, 
                AnimationClipData clip,
                ref KeyframeData keyframeData)
            {
                ref var animation = ref clip.AnimationBlob.Value;
                ref var keys      = ref animation.PositionKeys[keyFrameArrayIndex];
                var     length    = keys.Length;
                
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

                    {
                        keyframeData.PreviousKeyIndex = keyframeData.CurrentKeyIndex;
                        keyframeData.CurrentKeyIndex         = nextKeyIndex;
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
            
            public quaternion GetKeyframeRotation( 
                int keyFrameArrayIndex, 
                AnimationPlayer animationPlayer, 
                AnimationClipData clip)
            {
                ref var animation = ref clip.AnimationBlob.Value;
                ref var keys      = ref animation.RotationKeys[keyFrameArrayIndex];
                var     length    = keys.Length;
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