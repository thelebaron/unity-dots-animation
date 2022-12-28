using System;
using Unity.Entities;
using Unity.Mathematics;

namespace AnimationSystem
{
    public struct AnimationPlayer : IComponentData
    {
        public int CurrentClipIndex;
        public float Duration;
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
        public static float3 GetPosition(ref this BlobAssetReference<AnimationBlob> blob, int boneIndex, AnimationPlayer animationPlayer, out KeySample keySample)
        {
            ref var keys     = ref blob.Value.PositionKeys[boneIndex];
            var     length   = keys.Length;
            var     keyIndex = 0;
            keySample = default;

            if (length <= 0) 
                return float3.zero;
            
            for (int i = 0; i < length; i++)
            {
                if (keys[i].Time > animationPlayer.Elapsed)
                {
                    keyIndex = i;
                    break;
                }
            }

            {
                keySample.Length = length;
                //data.PreviousKeyIndex = data.CurrentKeyIndex;
                keySample.CurrentKeyIndex = keyIndex;
            }

            var prevKeyIndex = (keyIndex == 0) ? length - 1 : keyIndex - 1;
            var prevKey      = keys[prevKeyIndex];
            var nextKey      = keys[keyIndex];
            var timeBetweenKeys = (nextKey.Time > prevKey.Time)
                ? nextKey.Time - prevKey.Time
                : (nextKey.Time + animationPlayer.Duration) - prevKey.Time;

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
                    : (nextKey.Time + animationPlayer.Duration) - prevKey.Time;

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

        // special case for root bone
        public float3 GetPositionRelative(int boneIndex, float elapsed, float duration)
        {
            ref var keys     = ref PositionKeys[boneIndex];
            var     length   = keys.Length;
            var     keyIndex = 0;

            if (length <= 0) 
                return float3.zero;
            
            for (int i = 0; i < length; i++)
            {
                if (keys[i].Time > elapsed)
                {
                    keyIndex = i;
                    break;
                }
            }
            
            var prevKeyIndex = (keyIndex == 0) ? length - 1 : keyIndex - 1;
            var looped = prevKeyIndex == length - 1 && keyIndex == 0;
            var prevKey      = keys[prevKeyIndex];
            var nextKey      = keys[keyIndex];

            if (looped)
            {
                // use same keys as second last frame
                prevKey = keys[length - 2];
                nextKey = keys[length - 1];
            }
            
            var timeBetweenKeys = (nextKey.Time > prevKey.Time) ? nextKey.Time - prevKey.Time
                : (nextKey.Time + duration) - prevKey.Time;
            var t                    = (elapsed - prevKey.Time) / timeBetweenKeys;
            
            var origin               = keys[0].Value;
            var prevPosition         = prevKey.Value;
            var nextPosition         = nextKey.Value;
            var interpolatedPosition = math.lerp(nextPosition, prevPosition, t);

            var relativeToPrevPosition = interpolatedPosition - prevPosition;
            var relativeToOrigin       = interpolatedPosition - origin;
            
            return relativeToPrevPosition;
        }
        
        [Obsolete]
        public float3 GetPosition(int boneIndex, float elapsed, float duration, ref KeySample keySample)
        {
            ref var keys     = ref PositionKeys[boneIndex];
            var     length   = keys.Length;
            var     keyIndex = 0;

            if (length <= 0) 
                return float3.zero;
            
            for (int i = 0; i < length; i++)
            {
                if (keys[i].Time > elapsed)
                {
                    keyIndex = i;
                    break;
                }
            }
            var prevKeyIndex = (keyIndex == 0) ? length - 1 : keyIndex - 1;
            var prevKey      = keys[prevKeyIndex];
            var nextKey      = keys[keyIndex];
            var timeBetweenKeys = (nextKey.Time > prevKey.Time) ? nextKey.Time - prevKey.Time
                : (nextKey.Time + duration) - prevKey.Time;

            var t = (elapsed - prevKey.Time) / timeBetweenKeys;
            var interpolatedPosition = math.lerp(nextKey.Value, prevKey.Value, t);
            
            keySample.Update(length, keyIndex, keys[keyIndex].Value, keys[0].Value);

            var position0 = keys[0].Value;
            // Calculate the relative position of the current item with respect to the previous item
            
            // store the position of the previous item
/*
            for (int i = 0; i < keys.Length; i++)
            {
                var prevPosition = keys[i-1].Value;
                
                // Calculate the relative position of the current item with respect to the previous item
                var relativePosition = keys[i].Value - prevPosition;

                // Calculate the absolute position of the current item
                var absolutePosition = prevPosition + relativePosition;

                // Calculate the relative position of the current item with respect to the starting point
                var relativePositionToLast = absolutePosition - keys[0].Value;

                // Update the position of the previous item
                prevPosition = absolutePosition;
            }
            */
            return interpolatedPosition;
        }
        
        public float3 GetPosition(int boneIndex, float elapsed, float duration)
        {
            ref var keys     = ref PositionKeys[boneIndex];
            var     length   = keys.Length;
            var     keyIndex = 0;

            if (length <= 0) 
                return float3.zero;
            
            for (int i = 0; i < length; i++)
            {
                if (keys[i].Time > elapsed)
                {
                    keyIndex = i;
                    break;
                }
            }
            var prevKeyIndex = (keyIndex == 0) ? length - 1 : keyIndex - 1;
            var prevKey      = keys[prevKeyIndex];
            var nextKey      = keys[keyIndex];
            var timeBetweenKeys = (nextKey.Time > prevKey.Time) ? nextKey.Time - prevKey.Time
                : (nextKey.Time + duration) - prevKey.Time;

            var t = (elapsed - prevKey.Time) / timeBetweenKeys;
            var interpolatedPosition = math.lerp(nextKey.Value, prevKey.Value, t);
            return interpolatedPosition;
        }
        
        public float3 GetPositionAtIndex(int boneIndex, int index)
        {
            ref var keys     = ref PositionKeys[boneIndex];
            var     length   = keys.Length;

            if (length <= 0) 
                return float3.zero;
            
            var key      = keys[index];
            return key.Value;
        }
        
        public quaternion GetRotation(int boneIndex, float elapsed, float duration)
        {
            ref var keys   = ref RotationKeys[boneIndex];
            var     length = keys.Length;
            if (length <= 0) 
                return quaternion.identity;
            
            var nextKeyIndex = 0;
            for (int i = 0; i < length; i++)
            {
                if (keys[i].Time > elapsed)
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
                : (nextKey.Time + duration) - prevKey.Time;

            var t   = (elapsed - prevKey.Time) / timeBetweenKeys;
            var rot = math.slerp(prevKey.Value, nextKey.Value, t);
            return rot;
        }
        
        public quaternion GetRotationAtIndex(int boneIndex, int index)
        {
            ref var keys   = ref RotationKeys[boneIndex];
            var     length = keys.Length;

            if (length <= 0) 
                return quaternion.identity;
            
            var key = keys[index];
            return key.Value;
        }
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
        public float3     StreamPosition;
        public quaternion StreamRotation;
        public float3     PreviousStreamPosition;
        public quaternion PreviousStreamRotation;
    }
    
    internal struct PreviousAnimatedStreamData : IComponentData
    {
        public float3     Position;
        public quaternion Rotation;
    }
    public struct KeySample
    {
        public int    Length;
        public int    CurrentKeyIndex;
        public int    PreviousKeyIndex;
        public bool   KeyLooped;
        public float3 PositionStart;
        public float3 Position;
        public float3 PreviousLocalPosition;

        public void Update(int length, int keyIndex, float3 position, float3 positionStart)
        {
            Length                = length;
            PositionStart         = positionStart;
            PreviousKeyIndex      = CurrentKeyIndex;
            PreviousLocalPosition = Position;
            CurrentKeyIndex       = keyIndex;
            Position              = position;
            KeyLooped = CurrentKeyIndex.Equals(1) && PreviousKeyIndex.Equals(Length - 1) ||
                        CurrentKeyIndex.Equals(1) && PreviousKeyIndex.Equals(1);

        }
    }
    
    internal struct StreamKeyData : IComponentData
    {
        public KeySample CurrentKeySample;
        public KeySample PreviousKeySample;
        
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