using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace AnimationSystem
{
    public struct AnimationPlayer : IComponentData
    {
        public int CurrentClipIndex;
        public float CurrentDuration;
        public float Elapsed;
        public float Speed;
        public bool Loop;
        public bool Playing;
    }

    public struct AnimationClipData : IBufferElementData
    {
        public float Duration;
        public float Speed;
        public BlobAssetReference<AnimationBlob> AnimationBlob;
    }

    public static class AnimationBlobExtensions
    {
        public static float3 GetPosition(ref this BlobAssetReference<AnimationBlob> blob, int boneIndex, AnimationPlayer animationPlayer)
        {
            ref var keys   = ref blob.Value.PositionKeys[boneIndex];
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

                {
                    //keyframeData.Length = length;
                    //keyframeData.PreviousKeyIndex = keyframeData.CurrentKeyIndex;
                    //keyframeData.CurrentKeyIndex  = nextKeyIndex;
                }

                var prevKeyIndex = (nextKeyIndex == 0) ? length - 1 : nextKeyIndex - 1;
                var prevKey      = keys[prevKeyIndex];
                var nextKey      = keys[nextKeyIndex];
                var timeBetweenKeys = (nextKey.Time > prevKey.Time)
                    ? nextKey.Time - prevKey.Time
                    : (nextKey.Time + animationPlayer.CurrentDuration) - prevKey.Time;

                var t            = (animationPlayer.Elapsed - prevKey.Time) / timeBetweenKeys;
                var nextPosition = nextKey.Value;
                var prevPosition = prevKey.Value;

                /*if (isRoot)
                {
                    keyframeData.PreviousLocalPosition = keyframeData.LocalPosition;

                    bool blendConditions = keyframeData.CurrentKeyIndex.Equals(1) && keyframeData.PreviousKeyIndex.Equals(length - 1) ||
                                           keyframeData.CurrentKeyIndex.Equals(1) && keyframeData.PreviousKeyIndex.Equals(1);
                    if (blendConditions)
                    {
                        keyframeData.KeyLooped = true;
                        // We have looped around
                        return math.lerp(prevPosition, nextPosition, t);
                    }

                    var position = math.lerp(prevPosition, nextPosition, t);
                    keyframeData.LocalPosition = position;
                    keyframeData.KeyLooped     = false;
                    return position;
                }*/

                return math.lerp(prevPosition, nextPosition, t);
            }
            return float3.zero;
        }
        
        public static quaternion GetRotation(ref this BlobAssetReference<AnimationBlob> blob, int boneIndex, AnimationPlayer animationPlayer)
        {
            ref var keys      = ref blob.Value.RotationKeys[boneIndex];
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

    public struct AnimationBlob
    {
        public BlobArray<BlobArray<KeyFrameFloat3>> PositionKeys;
        public BlobArray<BlobArray<KeyFrameFloat4>> RotationKeys;
        public BlobArray<BlobArray<KeyFrameFloat3>> ScaleKeys;

        
    }

    public struct KeyFrameFloat3
    {
        public float Time;
        public float3 Value;
    }

    public struct KeyFrameFloat4
    {
        public float Time;
        public float4 Value;
    }
    
    internal struct AnimatedRootEntity : IComponentData
    {
        public Entity AnimationDataOwner;
    }

    internal struct AnimatedStreamData : IComponentData
    {
        public float3 Position;
        public quaternion Rotation;
    }
    
    internal struct PreviousAnimatedStreamData : IComponentData
    {
        public float3     Position;
        public quaternion Rotation;
    }
    internal struct KeyframeData
    {
        public int    Length;
        public int    CurrentKeyIndex;
        public int    PreviousKeyIndex;
        public bool   KeyLooped;
        public float3 LocalPosition;
        public float3 PreviousLocalPosition;
    }
    
    internal struct ClipKeyData : IComponentData
    {
        public KeyframeData KeyframeData;
        public KeyframeData PreviousKeyframeData;
        public bool         BlendKeys;
        
    }

    internal struct AnimatedBoneInfo : IBufferElementData
    {
        public int BoneIndex;
    }
    
    internal struct AnimatedEntityRootTag: IComponentData
    {
    }

    
    // Baking related
    internal struct AnimatedEntityBakingInfo : IBufferElementData
    {
        public int ClipIndex;
        public Entity Entity;
        public int IndexInKeyframeArray;
    }

    internal struct NeedsBakingTag : IComponentData
    {
    }
}