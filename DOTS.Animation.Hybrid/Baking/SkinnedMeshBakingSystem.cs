using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace AnimationSystem.Hybrid
{
    
    internal class SkinnedMeshBaker : Baker<SkinnedMeshAuthoring>
    {
        public override void Bake(SkinnedMeshAuthoring authoring)
        {
            var skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>(authoring);
            if (skinnedMeshRenderer == null)
                return;
            AddComponent<SkinnedMeshTag>();

            // Only execute this if we have a valid skinning setup
            DependsOn(skinnedMeshRenderer.sharedMesh);
            var hasSkinning = skinnedMeshRenderer.bones.Length > 0 &&
                              skinnedMeshRenderer.sharedMesh.bindposes.Length > 0;
            if (hasSkinning)
            {
                // Setup reference to the root bone
                var rootTransform = skinnedMeshRenderer.rootBone
                    ? skinnedMeshRenderer.rootBone
                    : skinnedMeshRenderer.transform;
                var rootEntity = GetEntity(rootTransform);
                AddComponent(new RootEntity { Value = rootEntity });

                // Setup reference to the other bones
                var boneEntityArray = AddBuffer<BoneEntity>();
                boneEntityArray.ResizeUninitialized(skinnedMeshRenderer.bones.Length);

                for (int boneIndex = 0; boneIndex < skinnedMeshRenderer.bones.Length; ++boneIndex)
                {
                    var bone = skinnedMeshRenderer.bones[boneIndex];
                    var boneEntity = GetEntity(bone);
                    boneEntityArray[boneIndex] = new BoneEntity { Value = boneEntity };
                }

                // Store the bindpose for each bone
                var bindPoseArray = AddBuffer<BindPose>();
                bindPoseArray.ResizeUninitialized(skinnedMeshRenderer.bones.Length);

                for (int boneIndex = 0; boneIndex != skinnedMeshRenderer.bones.Length; ++boneIndex)
                {
                    var bindPose = skinnedMeshRenderer.sharedMesh.bindposes[boneIndex];
                    bindPoseArray[boneIndex] = new BindPose { Value = bindPose };
                }
            }
        }
    }

    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    public partial struct ComputeSkinMatricesBakingSystem : ISystem
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
            
            foreach (var (root, bones) in SystemAPI.Query<RefRO<RootEntity>,DynamicBuffer<BoneEntity>>()
                         .WithAll<SkinnedMeshTag>()
                         .WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab))
            {
                var rootEntity = root.ValueRO.Value;
                    
#if !ENABLE_TRANSFORM_V1
                ecb.AddComponent<LocalToWorld>(rootEntity); // this is possibly redundant
#else
                ecb.AddComponent<WorldToLocal>(chunkIndex, rootEntity.Value);
#endif
                ecb.AddComponent<RootEntity>(rootEntity);

                // Add tags to the bones so we can find them later
                // when computing the SkinMatrices
                for (int boneIndex = 0; boneIndex < bones.Length; ++boneIndex)
                {
                    var boneEntity = bones[boneIndex].Value;
                    ecb.AddComponent(boneEntity, new BoneTag());
                }
            }
            
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }


    }
    
}