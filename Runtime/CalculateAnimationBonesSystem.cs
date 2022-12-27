using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.Profiling;

namespace AnimationSystem
{

    [BurstCompile]
    [UpdateAfter(typeof(PlayAnimationSystem))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct CalculateAnimationBonesSystem : ISystem
    {
        private EntityQuery writeBoneTransformsQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AnimatedRootEntity, AnimatedStreamData, StreamKeyData>()
                .WithAllRW<LocalTransform>();
            writeBoneTransformsQuery = state.GetEntityQuery(builder);
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

            state.Dependency = new ComputeBoneTransforms
            {
                EntityTypeHandle                  = SystemAPI.GetEntityTypeHandle(),
                AnimatedRootEntityTypeHandleRO    = SystemAPI.GetComponentTypeHandle<AnimatedRootEntity>(true),
                AnimatedStreamDataTypeHandleRO    = SystemAPI.GetComponentTypeHandle<AnimatedStreamData>(true),
                AnimatedStreamKeyDataTypeHandleRO = SystemAPI.GetComponentTypeHandle<StreamKeyData>(true),
                LocalTransformTypeHandleRW        = SystemAPI.GetComponentTypeHandle<LocalTransform>(),
                RootBoneTypeHandleRW              = SystemAPI.GetComponentTypeHandle<RootBone>(),
                BlendingLookupRO                  = SystemAPI.GetComponentLookup<AnimationBlending>(true),
                LastSystemVersion                 = state.LastSystemVersion,
            }.ScheduleParallel(writeBoneTransformsQuery, state.Dependency);
            
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
                ref StreamKeyData               streamKeyData
            )
            {
                var animationPlayer   = PlayerLookup[rootEntity.AnimationDataOwner];
                var animationBlending = BlendingLookup[rootEntity.AnimationDataOwner];
                
                if(!animationPlayer.Playing)
                    return;
                
                var     boneIndex     = clipInfo[animationBlending.ClipIndex].BoneIndex;
                var     clipBuffer    = ClipLookup[rootEntity.AnimationDataOwner];
                var     clipData      = clipBuffer[animationBlending.ClipIndex];
                
                // Current animation stream data
                ref var animation = ref clipData.AnimationBlob.Value;
                var     position  = animation.GetPosition(boneIndex, animationPlayer, ref streamKeyData.CurrentKeySample);
                var     rotation  = animation.GetRotation(boneIndex, animationPlayer);
                
                // Previous animation stream data
                ref var previousAnimation = ref clipBuffer[animationBlending.PreviousClipIndex].AnimationBlob.Value;
                var previousPosition  = math.select(position, previousAnimation.GetPosition(boneIndex, animationPlayer, ref streamKeyData.PreviousKeySample), animationBlending.ShouldBlend);
                var previousRotation  = mathex.select(rotation, previousAnimation.GetRotation(boneIndex, animationPlayer), animationBlending.ShouldBlend);
                
                streamData.StreamPosition = position;
                streamData.StreamRotation = rotation;
                streamData.PreviousStreamPosition = previousPosition;
                streamData.PreviousStreamRotation = previousRotation;
                
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
        private unsafe struct ComputeBoneTransforms : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                        EntityTypeHandle;
            [ReadOnly] public ComponentTypeHandle<AnimatedRootEntity> AnimatedRootEntityTypeHandleRO;
            [ReadOnly] public ComponentTypeHandle<AnimatedStreamData> AnimatedStreamDataTypeHandleRO;
            [ReadOnly] public ComponentTypeHandle<StreamKeyData>      AnimatedStreamKeyDataTypeHandleRO;
            public            ComponentTypeHandle<LocalTransform>     LocalTransformTypeHandleRW;
            public            ComponentTypeHandle<RootBone>           RootBoneTypeHandleRW;
            [ReadOnly] public ComponentLookup<AnimationBlending>      BlendingLookupRO;
            public            uint                                    LastSystemVersion;
            
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                AnimatedRootEntity* chunkAnimatedRootEntities = (AnimatedRootEntity*)chunk.GetRequiredComponentDataPtrRO(ref AnimatedRootEntityTypeHandleRO);
                AnimatedStreamData* chunkAnimatedStreamDatas  = (AnimatedStreamData*)chunk.GetRequiredComponentDataPtrRO(ref AnimatedStreamDataTypeHandleRO);
                StreamKeyData*      chunkStreamKeyData        = (StreamKeyData*)chunk.GetRequiredComponentDataPtrRO(ref AnimatedStreamKeyDataTypeHandleRO);
                LocalTransform*     chunkLocalTransforms      = (LocalTransform*)chunk.GetRequiredComponentDataPtrRW(ref LocalTransformTypeHandleRW);
                
                if (Hint.Unlikely(chunk.Has(ref RootBoneTypeHandleRW)))
                {
                    if (chunk.DidChange(ref AnimatedStreamDataTypeHandleRO, LastSystemVersion))
                    {
                        RootBone* chunkRootBones = (RootBone*)chunk.GetRequiredComponentDataPtrRW(ref RootBoneTypeHandleRW);
                        for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; ++i)
                        {
                            var animationBlending = BlendingLookupRO[chunkAnimatedRootEntities[i].AnimationDataOwner];
                            var position          = chunkAnimatedStreamDatas[i].StreamPosition;
                            var rotation          = chunkAnimatedStreamDatas[i].StreamRotation;
                            var previousPosition  = chunkAnimatedStreamDatas[i].PreviousStreamPosition;
                            var previousRotation  = chunkAnimatedStreamDatas[i].PreviousStreamRotation;
                            var pos               = math.select(position, math.lerp(previousPosition, position, animationBlending.Strength), animationBlending.ShouldBlend);
                            var rot               = mathex.select(rotation, math.slerp(previousRotation, rotation, animationBlending.Strength), animationBlending.ShouldBlend);

                            chunkLocalTransforms[i].Position = pos;
                            chunkLocalTransforms[i].Rotation = rot;
                            
                            var streamKeyData = chunkStreamKeyData[i];
                            var rootBone      = chunkRootBones[i];
                            {
                                //rootBone.BlendingKeyframes = animationBlending.IsBlending;
                                rootBone.PreviousPosition  = rootBone.Position;
                                rootBone.Position          = pos;
                                //rootBone.PreviousDelta     = rootBone.Delta;
                        
                                // ignore real delta if we are looping 
                                if (!streamKeyData.CurrentKeySample.KeyLooped || animationBlending.IsBlending)
                                {
                                    rootBone.Delta = rootBone.PreviousPosition - rootBone.Position;
                                }
                            }
                            chunkRootBones[i] = rootBone;
                        }
                    }
                }
                else
                {
                    if (chunk.DidChange(ref AnimatedStreamDataTypeHandleRO, LastSystemVersion))
                    {
                        for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; ++i)
                        {
                            var animationBlending = BlendingLookupRO[chunkAnimatedRootEntities[i].AnimationDataOwner];
                            var position         = chunkAnimatedStreamDatas[i].StreamPosition;
                            var rotation         = chunkAnimatedStreamDatas[i].StreamRotation;
                            var previousPosition = chunkAnimatedStreamDatas[i].PreviousStreamPosition;
                            var previousRotation = chunkAnimatedStreamDatas[i].PreviousStreamRotation;
                            var pos              = math.select(position, math.lerp(previousPosition, position, animationBlending.Strength), animationBlending.ShouldBlend);
                            var rot              = mathex.select(rotation, math.slerp(previousRotation, rotation, animationBlending.Strength), animationBlending.ShouldBlend);

                            chunkLocalTransforms[i].Position = pos;
                            chunkLocalTransforms[i].Rotation = rot;
                        }
                    }
                }
            }
        }
    }
    
}