using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace AnimationSystem
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    public partial struct RigBakingSystem : ISystem
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
            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            foreach (var (rootBone, entity) in SystemAPI.Query<RefRO<TemporaryRootBoneEntity>>().WithEntityAccess().WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities))
            {
                var rootBoneEntity = rootBone.ValueRO.RootBoneEntity;
                ecb.AddComponent<RootBone>(rootBoneEntity);
                ecb.AddComponent<ParentTransform>(rootBoneEntity);
                
                var parent = SystemAPI.GetComponent<Parent>(rootBoneEntity);
                ecb.AddComponent<Rig>(parent.Value, new Rig{RootBone = rootBoneEntity});
                //ecb.AddComponent<AnimationRootMotion>(parent.Value);
                ecb.AddComponent<ParentTransform>(parent.Value);
            }
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}