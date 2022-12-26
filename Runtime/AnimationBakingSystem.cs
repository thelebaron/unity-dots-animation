using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace AnimationSystem
{
    public class AnimationBaker : Baker<AnimationsAuthoring>
    {
        public override void Bake(AnimationsAuthoring authoring)
        {
            var clipBuffer = AddBuffer<AnimationClipData>();
            clipBuffer.ResizeUninitialized(authoring.Clips.Count);
            var clipIndex = 0;
            var entityBuffer = AddBuffer<AnimatedEntityBakingInfo>();
            foreach (var clipAuthoring in authoring.Clips)
            {
                var clip = clipAuthoring.clip;

                if (clip == null)
                {
                    Debug.LogError("Clip is null");
                    continue;
                }
                
                var curveBindings = AnimationUtility.GetCurveBindings(clip);
                var animationBlobBuilder = new BlobBuilder(Allocator.Temp);
                ref AnimationBlob animationBlob = ref animationBlobBuilder.ConstructRoot<AnimationBlob>();

                var curvesByEntity = curveBindings.GroupBy(curve => curve.path).ToArray();

                var entityCount = curvesByEntity.Length;
                var positionsArrayBuilder = animationBlobBuilder.Allocate(ref animationBlob.PositionKeys, entityCount);
                var rotationsArrayBuilder = animationBlobBuilder.Allocate(ref animationBlob.RotationKeys, entityCount);
                var scalesArrayBuilder    = animationBlobBuilder.Allocate(ref animationBlob.ScaleKeys, entityCount);

                var entityArrayIdx = 0;
                foreach (var entityCurves in curvesByEntity)
                {
                    if (entityCurves.Key.Equals(""))
                    {
                        Debug.Log("Humanoid rigs are not supported");
                        continue;
                    }
                    
                    var boneTransform = authoring.transform.Find(entityCurves.Key);
                    
                    if (boneTransform == null)
                    {
                        Debug.LogWarning("Bone transform for " + entityCurves.Key + " is null");
                        continue;
                    }
                    var boneEntity = GetEntity(boneTransform);
                    if (boneEntity == Entity.Null)
                    {
                        Debug.LogWarning("GetEntity for " + boneTransform + " is Entity.Null");
                        continue;
                    }
                    
                    entityBuffer.Add(new AnimatedEntityBakingInfo()
                    {
                        ClipIndex = clipIndex,
                        Entity = boneEntity,
                        IndexInKeyframeArray = entityArrayIdx,
                    });


                    var curveDict = entityCurves.ToDictionary(curve => curve.propertyName, curve => curve);
                    var posX = AnimationUtility.GetEditorCurve(clip, curveDict.GetValueOrDefault("m_LocalPosition.x"));
                    var posY = AnimationUtility.GetEditorCurve(clip, curveDict.GetValueOrDefault("m_LocalPosition.y"));
                    var posZ = AnimationUtility.GetEditorCurve(clip, curveDict.GetValueOrDefault("m_LocalPosition.z"));

                    if (posX.length != posY.length || posX.length != posZ.length)
                    {
                        throw new Exception("Position curves are not the same length");
                    }

                    var rotX = AnimationUtility.GetEditorCurve(clip, curveDict.GetValueOrDefault("m_LocalRotation.x"));
                    var rotY = AnimationUtility.GetEditorCurve(clip, curveDict.GetValueOrDefault("m_LocalRotation.y"));
                    var rotZ = AnimationUtility.GetEditorCurve(clip, curveDict.GetValueOrDefault("m_LocalRotation.z"));
                    var rotW = AnimationUtility.GetEditorCurve(clip, curveDict.GetValueOrDefault("m_LocalRotation.w"));

                    if (rotX.length != rotY.length || rotX.length != rotZ.length || rotW.length != rotZ.length)
                    {
                        throw new Exception("Rotation curves are not the same length");
                    }

                    BlobBuilderArray<KeyFrameFloat3> positionArrayBuilder = animationBlobBuilder.Allocate(
                        ref positionsArrayBuilder[entityArrayIdx],
                        posX.length
                    );
                    BlobBuilderArray<KeyFrameFloat4> rotationArrayBuilder = animationBlobBuilder.Allocate(
                        ref rotationsArrayBuilder[entityArrayIdx],
                        rotX.length
                    );
                    BlobBuilderArray<KeyFrameFloat3> scaleArrayBuilder = animationBlobBuilder.Allocate(
                        ref scalesArrayBuilder[entityArrayIdx],
                        0
                    );

                    // Postion
                    for (int i = 0; i < posX.length; i++)
                    {
                        var key = new KeyFrameFloat3
                        {
                            Time = posX.keys[i].time,
                            Value = new float3(posX.keys[i].value, posY.keys[i].value, posZ.keys[i].value)
                        };
                        positionArrayBuilder[i] = key;
                    }

                    // Rotation
                    for (int i = 0; i < (rotX.length); i++)
                    {
                        var key = new KeyFrameFloat4
                        {
                            Time = rotX.keys[i].time,
                            Value = new float4(rotX.keys[i].value, rotY.keys[i].value, rotZ.keys[i].value,
                                rotW.keys[i].value)
                        };
                        rotationArrayBuilder[i] = key;
                    }

                    entityArrayIdx++;
                }
                
                var animationClipData = new AnimationClipData()
                {
                    Duration = clip.length,
                    Speed = clipAuthoring.defaultSpeed,
                    AnimationBlob = animationBlobBuilder.CreateBlobAssetReference<AnimationBlob>(Allocator.Persistent)
                };
                clipBuffer[clipIndex++] = animationClipData;
            }

            AddComponent(new AnimationPlayer()
            {
                CurrentClipIndex = 0,
                CurrentDuration = clipBuffer[0].Duration,
                Elapsed = 0,
                Speed = clipBuffer[0].Speed,
                Loop = true,
                Playing = true,
            });

            AddComponent<AnimationRootMotion>();
            AddComponent<AnimationBlending>();
            AddComponent(new NeedsBakingTag());
                
            //Error: entity doesnt belong to authoring gameobject
             var skinnedMeshRenderers = authoring.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
            {
                var e = GetEntity(skinnedMeshRenderer.rootBone.gameObject);
                // For rootmotion setup, we need to know the root bone's parent for the rig entity
                AddComponent(GetEntity(), new TemporaryRootBoneEntity
                {
                    RootBoneEntity = e
                });
            }
        }
    }

    [TemporaryBakingType]
    public struct TemporaryRootBoneEntity : IComponentData
    {
        public Entity RootBoneEntity;
    }
    
    // [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [RequireMatchingQueriesForUpdate]
    public partial class AnimationBakingSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Allocator.TempJob);

            Entities
                .WithAll<AnimationClipData, NeedsBakingTag>()
                .ForEach((Entity rootEntity, in DynamicBuffer<AnimatedEntityBakingInfo> entities) =>
                {
                    for (int entityIndex = 0; entityIndex < entities.Length; entityIndex++)
                    {
                        var bakingInfo = entities[entityIndex];
                        var e = bakingInfo.Entity;
                        if (entityIndex == 0)
                        {
                            ecb.AddComponent(e, new AnimatedEntityRootTag());
                        }
                        if (bakingInfo.ClipIndex == 0)
                        {
                            ecb.AddComponent(e, new AnimatedEntityDataInfo()
                            {
                                AnimationDataOwner = rootEntity,
                            });
                            ecb.AddBuffer<AnimatedEntityClipInfo>(e);
                            
                            //ecb.AddComponent(e, new KeyframeData());
                            ecb.AddComponent(e, new ClipKeyData());
                        }

                        ecb.AppendToBuffer(e, new AnimatedEntityClipInfo()
                        {
                            IndexInKeyframeArray = bakingInfo.IndexInKeyframeArray,
                        });
                    }

                    ecb.RemoveComponent<NeedsBakingTag>(rootEntity);
                    ecb.RemoveComponent<AnimatedEntityBakingInfo>(rootEntity);
                }).WithEntityQueryOptions(EntityQueryOptions.IncludeDisabledEntities).WithoutBurst()
                .WithStructuralChanges().Run();

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }
    }
}