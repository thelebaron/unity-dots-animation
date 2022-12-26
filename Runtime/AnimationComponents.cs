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

    public struct AnimationBlob
    {
        // todo add
        // public bool RootMotion;
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
    
    internal struct AnimatedEntityDataInfo : IComponentData
    {
        public Entity AnimationDataOwner;
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

    internal struct AnimatedEntityClipInfo : IBufferElementData
    {
        public int IndexInKeyframeArray;
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