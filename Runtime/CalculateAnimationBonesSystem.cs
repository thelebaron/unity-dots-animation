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
                .WithAll<AnimatedRootEntity, AnimatedBoneInfo>()
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

            state.Dependency = new ComputeBoneTransforms
            {
                DeltaTime                         = SystemAPI.Time.DeltaTime,
                EntityTypeHandle                  = SystemAPI.GetEntityTypeHandle(),
                AnimatedRootEntityTypeHandleRO    = SystemAPI.GetComponentTypeHandle<AnimatedRootEntity>(true),
                LocalTransformTypeHandleRW        = SystemAPI.GetComponentTypeHandle<LocalTransform>(),
                RootBoneTypeHandleRW              = SystemAPI.GetComponentTypeHandle<RootBone>(),
                AnimatedBoneInfoTypeHandleRO      = SystemAPI.GetBufferTypeHandle<AnimatedBoneInfo>(true),
                BlendingLookupRO                  = SystemAPI.GetComponentLookup<AnimationBlending>(true),
                PlayerLookupRO                    = SystemAPI.GetComponentLookup<AnimationPlayer>(true),
                ClipLookupRO                      = SystemAPI.GetBufferLookup<AnimationClipData>(true),
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
        private unsafe struct ComputeBoneTransforms : IJobChunk
        {
            public            float                                   DeltaTime;
            [ReadOnly] public EntityTypeHandle                        EntityTypeHandle;
            [ReadOnly] public ComponentTypeHandle<AnimatedRootEntity> AnimatedRootEntityTypeHandleRO;
            public            ComponentTypeHandle<LocalTransform>     LocalTransformTypeHandleRW;
            public            ComponentTypeHandle<RootBone>           RootBoneTypeHandleRW;
            [ReadOnly] public BufferTypeHandle<AnimatedBoneInfo>      AnimatedBoneInfoTypeHandleRO;
            [ReadOnly] public ComponentLookup<AnimationBlending>      BlendingLookupRO;
            [ReadOnly] public ComponentLookup<AnimationPlayer>        PlayerLookupRO;
            [ReadOnly] public BufferLookup<AnimationClipData>         ClipLookupRO;
            public            uint                                    LastSystemVersion;
            
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                AnimatedRootEntity* chunkAnimatedRootEntities = (AnimatedRootEntity*)chunk.GetRequiredComponentDataPtrRO(ref AnimatedRootEntityTypeHandleRO);
                LocalTransform*     chunkLocalTransforms     = (LocalTransform*)chunk.GetRequiredComponentDataPtrRW(ref LocalTransformTypeHandleRW);
                var                 chunkAnimatedBoneInfos   = chunk.GetBufferAccessor(ref AnimatedBoneInfoTypeHandleRO);
                if (Hint.Unlikely(chunk.Has(ref RootBoneTypeHandleRW)))
                {
                    if(chunk.Has<RootBone>())
                    //if (chunk.DidChange(ref AnimatedStreamDataTypeHandleRO, LastSystemVersion))
                    {
                        RootBone* chunkRootBones = (RootBone*)chunk.GetRequiredComponentDataPtrRW(ref RootBoneTypeHandleRW);
                        for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; ++i)
                        {
                            var animationPlayer = PlayerLookupRO[chunkAnimatedRootEntities[i].AnimationDataOwner];                
                            if(!animationPlayer.Playing)
                                continue;
                            
                            var rootBone          = chunkRootBones[i];
                            var animationBlending = BlendingLookupRO[chunkAnimatedRootEntities[i].AnimationDataOwner];
                            var elapsedTime       = animationPlayer.Elapsed;
                            var duration          = animationPlayer.Duration;
                            
                            var boneIndexBuffer = chunkAnimatedBoneInfos[i];
                            var boneIndex       = boneIndexBuffer[animationBlending.ClipIndex].BoneIndex;
                            var clipBuffer      = ClipLookupRO[chunkAnimatedRootEntities[i].AnimationDataOwner];
                            var clipData        = clipBuffer[animationBlending.ClipIndex];
                
                            // Current animation stream data
                            ref var animation = ref clipData.AnimationBlob.Value;
                            var     positionRelative  = animation.GetPositionRelative(boneIndex, elapsedTime, duration);
                            var     rotation  = animation.GetRotation(boneIndex, elapsedTime, duration); // root rotation motion not yet handled
                
                            // Previous animation stream data
                            ref var previousAnimation = ref clipBuffer[animationBlending.PreviousClipIndex].AnimationBlob.Value;
                            var previousPositionRelative = previousAnimation.GetPositionRelative(boneIndex, elapsedTime, duration);
                            
                            var previousPosition  = math.select(positionRelative, previousPositionRelative, animationBlending.ShouldBlend);
                            var previousRotation  = mathex.select(rotation, previousAnimation.GetRotation(boneIndex, elapsedTime, duration), animationBlending.ShouldBlend); // root rotation motion not yet handled
                            var pos              = math.select(positionRelative, math.lerp(previousPosition, positionRelative, animationBlending.Strength), animationBlending.ShouldBlend);
                            var rot              = mathex.select(rotation, math.slerp(previousRotation, rotation, animationBlending.Strength), animationBlending.ShouldBlend);
                             
                            // absolute position
                            var positionAbsolute = animation.GetPosition(boneIndex, elapsedTime, duration);
                            var previousPositionAbsolute  = math.select(positionAbsolute, previousAnimation.GetPosition(boneIndex, elapsedTime, duration), animationBlending.ShouldBlend);
                            var positionAbsoluteInterpolated = math.select(positionAbsolute, math.lerp(previousPositionAbsolute, positionAbsolute, animationBlending.Strength), animationBlending.ShouldBlend);
                            
                            //pos                              *= DeltaTime * 60.0f;
                            chunkLocalTransforms[i].Position += pos;
                            chunkLocalTransforms[i].Position.y = positionAbsoluteInterpolated.y;
                            chunkLocalTransforms[i].Rotation =  rot;
                        }
                    }
                }
                else
                {
                    //if (chunk.DidChange(ref AnimatedStreamDataTypeHandleRO, LastSystemVersion))
                    {
                        for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; ++i)
                        {
                            var animationPlayer = PlayerLookupRO[chunkAnimatedRootEntities[i].AnimationDataOwner];                
                            if(!animationPlayer.Playing)
                                continue;
                            
                            var animationBlending = BlendingLookupRO[chunkAnimatedRootEntities[i].AnimationDataOwner];
                            var elapsedTime       = animationPlayer.Elapsed;
                            var duration          = animationPlayer.Duration;
                            
                            var boneIndexBuffer = chunkAnimatedBoneInfos[i];
                            var boneIndex       = boneIndexBuffer[animationBlending.ClipIndex].BoneIndex;
                            var clipBuffer      = ClipLookupRO[chunkAnimatedRootEntities[i].AnimationDataOwner];
                            var clipData        = clipBuffer[animationBlending.ClipIndex];
                
                            // Current animation stream data
                            ref var animation = ref clipData.AnimationBlob.Value;
                            var     position  = animation.GetPosition(boneIndex, elapsedTime, duration);
                            var     rotation  = animation.GetRotation(boneIndex, elapsedTime, duration); // root rotation motion not yet handled
                
                            // Previous animation stream data
                            ref var previousAnimation = ref clipBuffer[animationBlending.PreviousClipIndex].AnimationBlob.Value;
                            var previousPosition  = math.select(position, previousAnimation.GetPosition(boneIndex, elapsedTime, duration), animationBlending.ShouldBlend);
                            var previousRotation  = mathex.select(rotation, previousAnimation.GetRotation(boneIndex, elapsedTime, duration), animationBlending.ShouldBlend); // root rotation motion not yet handled
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