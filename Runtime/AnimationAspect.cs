using Unity.Entities;

namespace AnimationSystem
{
    public readonly partial struct AnimationAspect : IAspect
    {
        public readonly Entity                           Self;
        public readonly RefRW<AnimationPlayer>           AnimationPlayer;
        public readonly DynamicBuffer<AnimationClipData> ClipBuffer;
        public readonly ClipBlendingAspect               ClipBlendingAspect;

        public int CurrentClipIndex => AnimationPlayer.ValueRO.CurrentClipIndex;

        public void Play(int clipIndex)
        {
            var clip = ClipBuffer[clipIndex];
            var previousClipIndex = AnimationPlayer.ValueRO.CurrentClipIndex;
            AnimationPlayer.ValueRW.CurrentClipIndex = clipIndex;
            AnimationPlayer.ValueRW.Elapsed = 0;
            AnimationPlayer.ValueRW.CurrentDuration = clip.Duration;
            AnimationPlayer.ValueRW.Speed = clip.Speed;
            AnimationPlayer.ValueRW.Playing = true;
            var blendTime = 0.2f;
            ClipBlendingAspect.StartBlend(previousClipIndex, clipIndex, blendTime);
        }
        
        public void Pause()
        {
            AnimationPlayer.ValueRW.Playing = false;
        }
    }

    public  readonly partial struct ClipBlendingAspect: IAspect
    {
        public readonly Entity                             Self;
        public readonly RefRW<AnimationBlending> AnimationBlendingController;
        public readonly DynamicBuffer<AnimationClipData>   ClipBuffer;

        public void StartBlend(int previousClipIndex, int newClipIndex, float blendTime = 0.1f)
        {
            AnimationBlendingController.ValueRW.ShouldBlend       = true;
            AnimationBlendingController.ValueRW.PreviousClipIndex = previousClipIndex;
            AnimationBlendingController.ValueRW.ClipIndex     = newClipIndex;

        }
    }
        
    public struct AnimationBlending : IComponentData, IEnableableComponent
    {
        public bool ShouldBlend;
        public int  ClipIndex;
        public int  PreviousClipIndex;
        
        public float       CurrentDuration;
        public BlendStatus Status;
        public float       BlendDuration;
        public float       Strength;
        
        public bool IsBlending => Status == BlendStatus.Blend;
    }

    public enum BlendStatus
    {
        Finished,
        Blend
    }

}