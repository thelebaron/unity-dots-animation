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
            
            float3 nextPosition;
            float3 prevPosition;
            var    prevKeyIndex = (keyIndex == 0) ? length - 1 : keyIndex - 1;
            var    looped       = prevKeyIndex == length - 1 && keyIndex == 0;
            var    prevKey      = keys[prevKeyIndex];
            var    nextKey      = keys[keyIndex];

            var origin = keys[0].Value;
            prevPosition = prevKey.Value;
            nextPosition = nextKey.Value;
            
            if (looped)
            {
                // old code: might interfere with timing?
                // use same keys as second last frame
                prevKey = keys[length - 2];
                nextKey = keys[length - 1];
                // new code: just use position from keys and average the last and first key positions
                //prevPosition = (prevKey.Value + keys[length - 1].Value) * 0.5f;
            }
            
            var timeBetweenKeys = (nextKey.Time > prevKey.Time) 
                ? nextKey.Time - prevKey.Time
                : (nextKey.Time + duration) - prevKey.Time;
            var t                    = (elapsed - prevKey.Time) / timeBetweenKeys;
            
            var interpolatedPosition = math.lerp(nextPosition, prevPosition, t);

            //var relativeToPrevPosition = interpolatedPosition - prevPosition;            
            var relativeToPrevPosition = prevPosition -interpolatedPosition; // apparently wrong but character moves backwards if using above

            if (looped)
            {
                relativeToPrevPosition = 0f;
            }
            var relativeToOrigin       = interpolatedPosition - origin;
            
            return relativeToPrevPosition;
        }
 
        public quaternion GetRotationRelative(int boneIndex, float elapsed, float duration)
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

            var t                      = (elapsed - prevKey.Time) / timeBetweenKeys;
            var prevRotation           = prevKey.Value;
            var nextRotation           = nextKey.Value;
            var rotationInterpolated   = math.slerp(prevRotation, nextRotation, t);
            var relativeToPrevRotation = math.mul(rotationInterpolated, math.inverse(prevRotation));
            return relativeToPrevRotation;
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