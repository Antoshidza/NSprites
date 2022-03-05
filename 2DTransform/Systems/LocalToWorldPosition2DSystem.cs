using Unity.Entities;
using Unity.Transforms;

namespace NSprites
{
    [UpdateInGroup(typeof(TransformSystemGroup))]
    public class LocalToWorldPosition2DSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var translation2D_CDFE = GetComponentDataFromEntity<WorldPosition2D>(true);

            Entities
                .WithNativeDisableContainerSafetyRestriction(translation2D_CDFE)
                .WithReadOnly(translation2D_CDFE)
                .ForEach((ref WorldPosition2D worldPosition, in Parent parent, in LocalPosition2D localPosition) =>
                {
                    worldPosition.value = localPosition.value + translation2D_CDFE[parent.value].value;
                }).ScheduleParallel();
        }
    }
}
