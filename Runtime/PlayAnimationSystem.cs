using AnimationSystem;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace AnimationSystem
{
    [BurstCompile]
    public partial struct PlayAnimationSystem : ISystem
    {
        private ComponentLookup<AnimationPlayer> playerLookup;
        private BufferLookup<AnimationClipData> clipLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            playerLookup = state.GetComponentLookup<AnimationPlayer>();
            clipLookup = state.GetBufferLookup<AnimationClipData>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            playerLookup.Update(ref state);
            clipLookup.Update(ref state);
            var deltaTime = SystemAPI.Time.DeltaTime;

            /*state.Dependency = new UpdateAnimatedEntitesJob()
            {
                PlayerLookup = playerLookup,
                ClipLookup = clipLookup,
            }.ScheduleParallel(state.Dependency);*/
            
            state.Dependency = new UpdateAnimationPlayerJob()
            {
                DeltaTime = deltaTime,
            }.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    //[WithNone(typeof(AnimatedEntityRootTag))]
    partial struct UpdateAnimatedEntitesJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<AnimationPlayer>             PlayerLookup;
        [ReadOnly] public BufferLookup<AnimationClipData>              ClipLookup;
        //[ReadOnly] public ComponentLookup<AnimationBlendingController> BlendingControllerLookup;

        [BurstCompile]
        public void Execute(
            AnimatedRootEntity info,
            DynamicBuffer<AnimatedBoneInfo> clipInfo,
#if !ENABLE_TRANSFORM_V1
            ref LocalTransform localTransform
#else
            ref Translation translation,
            ref Rotation rotation
#endif
        )
        {
            var animationPlayer = PlayerLookup[info.AnimationDataOwner];
            
            if(!animationPlayer.Playing) 
                return;
            var clipBuffer = ClipLookup[info.AnimationDataOwner];
            var clip = clipBuffer[animationPlayer.CurrentClipIndex];

            ref var animation = ref clip.AnimationBlob.Value;
            var keyFrameArrayIndex = clipInfo[animationPlayer.CurrentClipIndex].BoneIndex;
            // Position
            {
                ref BlobArray<KeyFrameFloat3> keys   = ref animation.PositionKeys[keyFrameArrayIndex];
                var             length = keys.Length;
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
                    var prevKey = keys[prevKeyIndex];
                    var nextKey = keys[nextKeyIndex];
                    var timeBetweenKeys = (nextKey.Time > prevKey.Time)
                        ? nextKey.Time - prevKey.Time
                        : (nextKey.Time + animationPlayer.Duration) - prevKey.Time;

                    var t = (animationPlayer.Elapsed - prevKey.Time) / timeBetweenKeys;
                    var pos = math.lerp(prevKey.Value, nextKey.Value, t);
                    
#if !ENABLE_TRANSFORM_V1
                    localTransform.Position = pos;
#else
                    translation.Value = pos;
#endif
                }
            }

            // Rotation
            {
                ref var keys = ref animation.RotationKeys[keyFrameArrayIndex];
                var length = keys.Length;
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
                    var prevKey = keys[prevKeyIndex];
                    var nextKey = keys[nextKeyIndex];
                    var timeBetweenKeys = (nextKey.Time > prevKey.Time)
                        ? nextKey.Time - prevKey.Time
                        : (nextKey.Time + animationPlayer.Duration) - prevKey.Time;

                    var t = (animationPlayer.Elapsed - prevKey.Time) / timeBetweenKeys;
                    var rot = math.slerp(prevKey.Value, nextKey.Value, t);
                    
#if !ENABLE_TRANSFORM_V1
                    localTransform.Rotation = rot;
#else
                    rotation.Value = rot;
#endif
                }
            }
        }
    }
}


[BurstCompile]
[WithNone(typeof(NeedsBakingTag))]
partial struct UpdateAnimationPlayerJob : IJobEntity
{
    public float DeltaTime;

    [BurstCompile]
    public void Execute(ref AnimationPlayer animationPlayer)
    {
        if(!animationPlayer.Playing) return;
        // Update elapsed time
        animationPlayer.Elapsed += DeltaTime * animationPlayer.Speed;
        if (animationPlayer.Loop)
        {
            animationPlayer.Elapsed %= animationPlayer.Duration;
        }
        else
        {
            animationPlayer.Elapsed = math.min(animationPlayer.Elapsed, animationPlayer.Duration);
        }
    }
}