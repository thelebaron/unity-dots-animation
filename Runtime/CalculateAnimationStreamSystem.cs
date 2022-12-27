using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace AnimationSystem
{

    [BurstCompile]
    [UpdateAfter(typeof(PlayAnimationSystem))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct CalculateAnimationStreamSystem : ISystem
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
            
            state.Dependency = new BlendAspectJob
            {
                DeltaTime = deltaTime,
            }.ScheduleParallel(state.Dependency);

            state.Dependency = new BlendAnimatedBonesJob()
            {
                DeltaTime      = deltaTime,
                PlayerLookup   = SystemAPI.GetComponentLookup<AnimationPlayer>(true),
                ClipLookup     = SystemAPI.GetBufferLookup<AnimationClipData>(true),
                BlendingLookup = SystemAPI.GetComponentLookup<AnimationBlending>(true),
                RootBoneLookup = SystemAPI.GetComponentLookup<RootBone>(),
            }.ScheduleParallel(state.Dependency);
            
            state.Dependency = new UpdateAnimatedTransforms().ScheduleParallel(state.Dependency);
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
                    blendAspect.AnimationBlendingController.ValueRW.BlendDuration   = 0.2f;
                    blendAspect.AnimationBlendingController.ValueRW.Status          = BlendStatus.Blend;
                }
                
                if (blendAspect.AnimationBlendingController.ValueRW.Status == BlendStatus.Blend)
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
            [ReadOnly]                            public float                              DeltaTime;
            [ReadOnly]                            public ComponentLookup<AnimationPlayer>   PlayerLookup;
            [ReadOnly]                            public BufferLookup<AnimationClipData>    ClipLookup;
            [ReadOnly]                            public ComponentLookup<AnimationBlending> BlendingLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<RootBone>          RootBoneLookup;

            public void Execute(Entity                entity, 
                AnimatedRootEntity                    rootEntity, 
                ref AnimatedStreamData                streamData,
                ref PreviousAnimatedStreamData        previousStreamData,
                DynamicBuffer<AnimatedBoneInfo> clipInfo, 
                ref ClipKeyData                       clipKeyData,
    #if !ENABLE_TRANSFORM_V1
                ref LocalTransform localTransform
    #else
                ref Translation translation,
                ref Rotation rotation
    #endif
            )
            {
                var animationPlayer   = PlayerLookup[rootEntity.AnimationDataOwner];
                var animationBlending = BlendingLookup[rootEntity.AnimationDataOwner];
                previousStreamData.Position = streamData.Position;
                previousStreamData.Rotation = streamData.Rotation;
                
                if(!animationPlayer.Playing) 
                    return;
                var     boneIndex     = clipInfo[animationBlending.ClipIndex].BoneIndex;
                var     clipBuffer    = ClipLookup[rootEntity.AnimationDataOwner];
                var     clipData      = clipBuffer[animationBlending.ClipIndex];
                
                ref var animation = ref clipData.AnimationBlob.Value;
                var     position      = animation.GetPosition(boneIndex, animationPlayer, out var keyData);
                var     rotation      = animation.GetRotation(boneIndex, animationPlayer);
                
                streamData.Position = position;
                streamData.Rotation = rotation;
                
                ref var previousAnimation = ref clipBuffer[animationBlending.PreviousClipIndex].AnimationBlob.Value;
                var     previousPosition  = math.select(position, previousAnimation.GetPosition(boneIndex, animationPlayer, out var prevKeyData), animationBlending.ShouldBlend);
                var     previousRotation  = mathex.select(rotation, previousAnimation.GetRotation(boneIndex, animationPlayer), animationBlending.ShouldBlend);
                
                //var previousPosition = previousAnimation.GetPosition(boneIndex, animationPlayer, out var prevKeyData);
                //var previousRotation = previousAnimation.GetRotation(boneIndex, animationPlayer);
                localTransform.Position = math.select(position, math.lerp(previousPosition, position, animationBlending.Strength), animationBlending.ShouldBlend);
                localTransform.Rotation = mathex.select(rotation, math.slerp(previousRotation, rotation, animationBlending.Strength), animationBlending.ShouldBlend);
                

                var isRootBone = RootBoneLookup.HasComponent(entity);
                // Rootmotion calculation
                /*if (isRootBone)
                {
                    var rootBone = RootBoneLookup[entity];
                    {
                        rootBone.BlendingKeyframes = animationBlending.IsBlending;
                        rootBone.PreviousPosition  = rootBone.Position;
                        rootBone.Position          = localTransform.Position;
                        rootBone.PreviousDelta     = rootBone.Delta;
                        
                        // ignore real delta if we are looping 
                        if (!clipKeyData.KeySampleData.KeyLooped)
                        {
                            rootBone.Delta = rootBone.PreviousPosition - rootBone.Position;
                        }
                    }
                    RootBoneLookup[entity] = rootBone;
                }*/
#if !ENABLE_TRANSFORM_V1
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


            public float3 GetKeyframePosition(bool isRoot, int boneIndex,
                AnimationPlayer                    animationPlayer,
                AnimationClipData                  clip,
                ref KeySampleData                   keySampleData)
            {
                ref var animation = ref clip.AnimationBlob.Value;
                ref var keys      = ref animation.PositionKeys[boneIndex];
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
                        keySampleData.Length = length;
                        keySampleData.PreviousKeyIndex = keySampleData.CurrentKeyIndex;
                        keySampleData.CurrentKeyIndex  = nextKeyIndex;
                    }
                    
                    var prevKeyIndex = (nextKeyIndex == 0) ? length - 1 : nextKeyIndex - 1;
                    var prevKey      = keys[prevKeyIndex];
                    var nextKey      = keys[nextKeyIndex];
                    var timeBetweenKeys = (nextKey.Time > prevKey.Time)
                        ? nextKey.Time - prevKey.Time
                        : (nextKey.Time + animationPlayer.CurrentDuration) - prevKey.Time;

                    var t   = (animationPlayer.Elapsed - prevKey.Time) / timeBetweenKeys;
                    var nextPosition = nextKey.Value;
                    var prevPosition = prevKey.Value;
                    
                    if (isRoot)
                    {
                        keySampleData.PreviousLocalPosition = keySampleData.LocalPosition;

                        bool blendConditions = keySampleData.CurrentKeyIndex.Equals(1) && keySampleData.PreviousKeyIndex.Equals(length - 1) ||
                                               keySampleData.CurrentKeyIndex.Equals(1) && keySampleData.PreviousKeyIndex.Equals(1);
                        if (blendConditions)
                        {
                            keySampleData.KeyLooped = true;
                            // We have looped around
                            return math.lerp(prevPosition, nextPosition, t);
                        }

                        var position = math.lerp(prevPosition, nextPosition, t);
                        keySampleData.LocalPosition = position;
                        keySampleData.KeyLooped     = false;
                        return position;
                    }

                    return math.lerp(prevPosition, nextPosition, t);
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
        
        [BurstCompile]
        internal partial struct UpdateAnimatedTransforms : IJobEntity
        {
            public void Execute(Entity entity, AnimatedStreamData animatedStreamData, PreviousAnimatedStreamData previousAnimatedStreamData, ref LocalTransform localTransform)
            {
                //localTransform.Position = animatedStreamData.Position;
            
            }
        
        }
    }
    
}