using Unity.Entities;
using UnityEngine;
using Unity.Collections;

namespace NSprites
{
    [UpdateInGroup(typeof(GameObjectBeforeConversionGroup), OrderFirst = true)]
    public class ExcludeTransformsConversionSystem : GameObjectConversionSystem
    {
        private EntityQuery _transformQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            _transformQuery = GetEntityQuery
            (
                ComponentType.ReadOnly<ExcludeTransformFromConversion>(),
                ComponentType.ReadOnly<Transform>()
            );
        }
        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            Entities
                .WithAll<ExcludeTransformFromConversion>()
                .ForEach((Entity entity, Transform transform) =>
                {
                    ecb.AddComponent(entity, new ConvertPointer() { transform = transform });
                });
            EntityManager.RemoveComponent<Transform>(_transformQuery);
            ecb.Playback(EntityManager);
        }
    }
}
