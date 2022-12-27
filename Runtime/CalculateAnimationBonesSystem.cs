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
    public partial struct CalculateAnimationBonesSystem : ISystem
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

            state.Dependency = new AnimateBonesJob()
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
        partial struct AnimateBonesJob : IJobEntity
        {
            [ReadOnly]                            public float                              DeltaTime;
            [ReadOnly]                            public ComponentLookup<AnimationPlayer>   PlayerLookup;
            [ReadOnly]                            public BufferLookup<AnimationClipData>    ClipLookup;
            [ReadOnly]                            public ComponentLookup<AnimationBlending> BlendingLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<RootBone>          RootBoneLookup;

            public void Execute(Entity          entity, 
                AnimatedRootEntity              rootEntity, 
                DynamicBuffer<AnimatedBoneInfo> clipInfo, 
                ref AnimatedStreamData          streamData,
                ref StreamKeyData               streamKeyData,
                ref LocalTransform              localTransform
            )
            {
                var animationPlayer   = PlayerLookup[rootEntity.AnimationDataOwner];
                var animationBlending = BlendingLookup[rootEntity.AnimationDataOwner];
                
                if(!animationPlayer.Playing)
                    return;
                
                var     boneIndex     = clipInfo[animationBlending.ClipIndex].BoneIndex;
                var     clipBuffer    = ClipLookup[rootEntity.AnimationDataOwner];
                var     clipData      = clipBuffer[animationBlending.ClipIndex];
                // Current animation clip data
                ref var animation        = ref clipData.AnimationBlob.Value;
                var     position         = animation.GetPosition(boneIndex, animationPlayer, out var keyData);
                var     rotation         = animation.GetRotation(boneIndex, animationPlayer);
                var     previousPosition = float3.zero;
                var     previousRotation = quaternion.identity;
                
                // Previous animation clip data
                ref var previousAnimation = ref clipBuffer[animationBlending.PreviousClipIndex].AnimationBlob.Value;
                previousPosition  = math.select(position, previousAnimation.GetPosition(boneIndex, animationPlayer, out var prevKeyData), animationBlending.ShouldBlend);
                previousRotation  = mathex.select(rotation, previousAnimation.GetRotation(boneIndex, animationPlayer), animationBlending.ShouldBlend);
                
                var pos =  math.select(position, math.lerp(previousPosition, position, animationBlending.Strength), animationBlending.ShouldBlend);
                var rot = mathex.select(rotation, math.slerp(previousRotation, rotation, animationBlending.Strength), animationBlending.ShouldBlend);

                streamData.StreamPosition     = pos;
                streamData.StreamRotation     = rot;
                localTransform.Position = pos;
                localTransform.Rotation = rot;
                

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
            }


            public float3 GetKeyframePosition(bool isRoot, int boneIndex,
                AnimationPlayer                    animationPlayer,
                AnimationClipData                  clip,
                ref KeySample                   keySample)
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
                        keySample.Length = length;
                        keySample.PreviousKeyIndex = keySample.CurrentKeyIndex;
                        keySample.CurrentKeyIndex  = nextKeyIndex;
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
                        keySample.PreviousLocalPosition = keySample.LocalPosition;

                        bool blendConditions = keySample.CurrentKeyIndex.Equals(1) && keySample.PreviousKeyIndex.Equals(length - 1) ||
                                               keySample.CurrentKeyIndex.Equals(1) && keySample.PreviousKeyIndex.Equals(1);
                        if (blendConditions)
                        {
                            keySample.KeyLooped = true;
                            // We have looped around
                            return math.lerp(prevPosition, nextPosition, t);
                        }

                        var position = math.lerp(prevPosition, nextPosition, t);
                        keySample.LocalPosition = position;
                        keySample.KeyLooped     = false;
                        return position;
                    }

                    return math.lerp(prevPosition, nextPosition, t);
                }
                return float3.zero;
            }
            
        }
        
        [BurstCompile]
        internal partial struct UpdateAnimatedTransforms : IJobEntity
        {
            public void Execute(Entity entity, AnimatedStreamData animatedStreamData, ref LocalTransform localTransform)
            {
                localTransform.Position = animatedStreamData.StreamPosition;
                localTransform.Rotation = animatedStreamData.StreamRotation;
            
            }
        
        }
    }
    
}