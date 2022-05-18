using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Unity.Collections;

namespace NSprites
{
    public class Transform2DConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            Entities
                .ForEach((Entity entity, Transform2DAuthoring transform2DAuthoring) =>
                {
                    ecb.AddComponent(entity, new Transform2D() { gameObject = transform2DAuthoring.gameObject });
                });
            ecb.Playback(EntityManager);

            Entities
                .WithNone<ExcludeFrom2DConversion>()
                .ForEach((Transform2D transform2D) =>
                {
                    void Convert(Transform transform, in Entity entity, in float2 parentWorldPosition, in Entity parentEntity)
                    {
                        var worldPosition = new float2(transform.position.x, transform.position.y);
                        DstEntityManager.AddComponentData(entity, new WorldPosition2D { value = worldPosition });

                        if(parentEntity != Entity.Null)
                        {
                            DstEntityManager.AddComponentData(entity, new LocalPosition2D { value = worldPosition - parentWorldPosition });
                            DstEntityManager.AddComponentData(entity, new Parent2D { value = parentEntity });
                            DynamicBuffer<Child2D> GetChildBuffer(in Entity parentEntity)
                            {
                                if(DstEntityManager.HasComponent<Child2D>(parentEntity))
                                    return DstEntityManager.GetBuffer<Child2D>(parentEntity);
                                return DstEntityManager.AddBuffer<Child2D>(parentEntity);
                            }
                            GetChildBuffer(parentEntity).Add(new Child2D { value = entity });
                        }

                        for(int i = 0; i < transform.childCount; i++)
                        {
                            var child = transform.GetChild(i);
                            var childEntity = TryGetPrimaryEntity(child);
                            if(childEntity == Entity.Null || child.TryGetComponent<ExcludeFrom2DConversion>(out _))
                                continue;

                            Convert(child, childEntity, worldPosition, entity);
                        }
                    }
                    bool NestedEntity(Transform parentTransform)
                    {
                        //there is no parent at all, so entity isn't nested
                        if(parentTransform == null)
                            return false;
                        var parentEntity = TryGetPrimaryEntity(parentTransform);
                        //there is no conversion for parent, so entity isn't nested
                        if(parentEntity == Entity.Null)
                            return false;
                        //parent has 2D transform which means this entity for sure is nested
                        if(EntityManager.HasComponent<Transform2D>(parentEntity))
                            return true;
                        //parent has no 2D transform, but it can have grandparents with 2D transform still, so check recursively
                        return NestedEntity(parentTransform.parent);
                    }
                    var transform = transform2D.gameObject.transform;
                    if(!NestedEntity(transform.parent))
                        Convert(transform, GetPrimaryEntity(transform), default, Entity.Null);
                });
        }
    }
}
