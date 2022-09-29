using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Burst;

namespace NSprites
{
    [UpdateInGroup(typeof(TransformSystemGroup))]
    public partial class LocalToParent2DSystem : SystemBase
    {
        [BurstCompile]
        private struct UpdateHierarchy : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<WorldPosition2D> worldPosition_CTH;
            [NativeDisableContainerSafetyRestriction] public ComponentDataFromEntity<WorldPosition2D> worldPosition_CDFE;
            [ReadOnly] public ComponentDataFromEntity<LocalPosition2D> localPosition_CDFE;
            [ReadOnly] public BufferTypeHandle<Child2D> child_BTH;
            [ReadOnly] public BufferFromEntity<Child2D> child_BFE;
            public uint lastSystemVersion;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                //if position or child set was changed then we need update children hierarchically
                var needUpdate = batchInChunk.DidChange(worldPosition_CTH, lastSystemVersion) && batchInChunk.DidChange(child_BTH, lastSystemVersion);

                var chunkWorldPosition = batchInChunk.GetNativeArray(worldPosition_CTH);
                var chunkChild = batchInChunk.GetBufferAccessor(child_BTH);

                for (int entityIndex = 0; entityIndex < batchInChunk.Count; entityIndex++)
                {
                    var worldPosition = chunkWorldPosition[entityIndex];
                    var children = chunkChild[entityIndex];

                    for (int childIndex = 0; childIndex < children.Length; childIndex++)
                        UpdateChild(worldPosition.value, children[childIndex].value, needUpdate);
                }
            }

            private void UpdateChild(in float2 parentPosition, in Entity childEntity, bool needUpdate)
            {
                var position = parentPosition + localPosition_CDFE[childEntity].value;
                worldPosition_CDFE[childEntity] = new WorldPosition2D { value = position };

                //if this child also is a parent update its children
                if (!child_BFE.HasComponent(childEntity))
                    return;

                needUpdate = needUpdate || localPosition_CDFE.DidChange(childEntity, lastSystemVersion) || child_BFE.DidChange(childEntity, lastSystemVersion);
                var children = child_BFE[childEntity];

                for (int childIndex = 0; childIndex < children.Length; childIndex++)
                    UpdateChild(position, children[childIndex].value, needUpdate);
            }
        }

        private EntityQuery _rootQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            _rootQuery = GetEntityQuery
            (
                new EntityQueryDesc
                {
                    All = new ComponentType[]
                    {
                        typeof(WorldPosition2D),
                        typeof(Child2D)
                    },
                    None = new ComponentType[]
                    {
                        typeof(Parent2D)
                    }
                }
            );
        }
        protected override void OnUpdate()
        {
            Dependency = new UpdateHierarchy
            {
                worldPosition_CDFE = GetComponentDataFromEntity<WorldPosition2D>(false),
                localPosition_CDFE = GetComponentDataFromEntity<LocalPosition2D>(true),
                child_BFE = GetBufferFromEntity<Child2D>(true),
                child_BTH = GetBufferTypeHandle<Child2D>(true),
                worldPosition_CTH = GetComponentTypeHandle<WorldPosition2D>(true),
                lastSystemVersion = LastSystemVersion
            }.ScheduleParallel(_rootQuery, Dependency);
        }
    }
}
