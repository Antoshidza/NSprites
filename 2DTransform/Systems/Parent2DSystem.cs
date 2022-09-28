using Unity.Entities;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;

namespace NSprites
{
    [UpdateBefore(typeof(LocalToParent2DSystem))]
    [UpdateInGroup(typeof(Unity.Transforms.TransformSystemGroup))]
    public partial class Parent2DSystem : SystemBase
    {
        private EntityQuery _missingChildren;
        private EntityQuery _lastParentLessChildren;
        private EntityQuery _reparentedChildren;
        private EntityQuery _missingParents;
        private EntityQuery _lastParentWithoutParent;
        private EntityQuery _staticRelationshipsAlone;
        private EntityQuery _childBufferAlone;

        private ComponentType _lastParent2DComp = typeof(LastParent2D);
        private ComponentTypes _componentsToRemoveFromUnparentedChildren = new ComponentTypes
        (
            ComponentType.ReadOnly<Parent2D>(),
            ComponentType.ReadOnly<LastParent2D>(),
            ComponentType.ReadOnly<LocalPosition2D>()
        );

        [BurstCompile]
        private struct GatherReparentedChildrenDataJob : IJobEntityBatch
        {
            public NativeParallelMultiHashMap<Entity, Entity>.ParallelWriter parentChildToAdd;
            public NativeParallelMultiHashMap<Entity, Entity>.ParallelWriter parentChildToRemove;
            public NativeParallelHashSet<Entity>.ParallelWriter uniqueAffectedParents;
            [ReadOnly] public ComponentTypeHandle<Parent2D> parent_CTH;
            public ComponentTypeHandle<LastParent2D> lastParent_CTH;
            [ReadOnly] public EntityTypeHandle entityTypeHandle;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var chunkParents = batchInChunk.GetNativeArray(parent_CTH);
                var chunkLastParents = batchInChunk.GetNativeArray(lastParent_CTH);
                var entities = batchInChunk.GetNativeArray(entityTypeHandle);

                for (int entityIndex = 0; entityIndex < batchInChunk.Count; entityIndex++)
                {
                    var parentEntity = chunkParents[entityIndex].value;
                    var lastParentEntity = chunkLastParents[entityIndex].value;

                    //means there is real parent changing
                    if (parentEntity != lastParentEntity)
                    {
                        var entity = entities[entityIndex];
                        parentChildToAdd.Add(parentEntity, entity);
                        uniqueAffectedParents.Add(parentEntity);

                        if (lastParentEntity != Entity.Null)
                        {
                            parentChildToRemove.Add(lastParentEntity, entity);
                            uniqueAffectedParents.Add(lastParentEntity);
                        }
                    }

                    chunkLastParents[entityIndex] = new LastParent2D { value = parentEntity };
                }
            }
        }
        [BurstCompile]
        private struct GatherMissingChildrenDataJob : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<LastParent2D> lastParent_CTH;
            [ReadOnly] public EntityTypeHandle entityTypeHandle;
            public NativeParallelMultiHashMap<Entity, Entity>.ParallelWriter parentChildToRemove;
            public NativeParallelHashSet<Entity>.ParallelWriter uniqueAffectedParents;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var chunkEntities = batchInChunk.GetNativeArray(entityTypeHandle);
                var chunkLastParents = batchInChunk.GetNativeArray(lastParent_CTH);

                for (int entityIndex = 0; entityIndex < batchInChunk.Count; entityIndex++)
                {
                    var childEntity = chunkEntities[entityIndex];
                    var lastParent = chunkLastParents[entityIndex].value;
                    if (lastParent != Entity.Null)
                    {
                        parentChildToRemove.Add(lastParent, childEntity);
                        uniqueAffectedParents.Add(lastParent);
                    }
                }
            }
        }
        [BurstCompile]
        private struct FixupRelationsJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<Entity> uniqueAffectedParents;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, Entity> parentChildToAdd;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, Entity> parentChildToRemove;
            public EntityCommandBuffer.ParallelWriter ecb;
            [NativeDisableParallelForRestriction] public BufferFromEntity<Child2D> child_BFE;

            public void Execute(int startIndex, int count)
            {
                var forCount = startIndex + count;
                var childList = new NativeList<Entity>(Allocator.Temp);
                for (int parentIndex = startIndex; parentIndex < forCount; parentIndex++)
                {
                    var inJobIndex = parentIndex + startIndex;
                    var parentEntity = uniqueAffectedParents[parentIndex];
                    DynamicBuffer<Child2D> childBuffer = default;

                    var parentHasNewChildren = parentChildToAdd.TryGetFirstValue(parentEntity, out var addChildEntity, out var addIterator);
                    var parentHasRemovedChildren = parentChildToRemove.TryGetFirstValue(parentEntity, out var removeChildEntity, out var removeIterator);

                    if (!parentHasNewChildren && !parentHasRemovedChildren)
                        return;

                    var parentHasChildBuffer = child_BFE.HasComponent(parentEntity);

                    //if parent has removed children and existing child buffer then we want to clear it
                    if (parentHasRemovedChildren && parentHasChildBuffer)
                    {
                        childBuffer = child_BFE[parentEntity];
                        do
                            childList.Add(removeChildEntity);
                        while (parentChildToRemove.TryGetNextValue(out removeChildEntity, ref removeIterator));
                        //if remove list is the same length and there will be no new children then we can safely remove buffer
                        if (childList.Length == childBuffer.Length && !parentHasNewChildren)
                            ecb.RemoveComponent<Child2D>(inJobIndex, parentEntity);
                        //otherwise we want to carefully extract all remove children
                        else
                        {
                            for (int i = 0; i < childList.Length; i++)
                            {
                                var childInBufferIndex = GetChildIndex(childBuffer, childList[i]);
                                if (childInBufferIndex != -1)
                                    childBuffer.RemoveAtSwapBack(childInBufferIndex);
                            }
                        }
                        childList.Clear();
                    }

                    //if parent has new children then we want to allocate/access new buffer if needed and fill it with new children
                    if (parentHasNewChildren)
                    {
                        //if parent has no child buffer at all then in every case we just want to allocate new one
                        if (!parentHasChildBuffer)
                            childBuffer = ecb.AddBuffer<Child2D>(inJobIndex, parentEntity);
                        //if there is buffer and we not cache it in "Remove" section, then cache it here
                        else if (!parentHasRemovedChildren)
                            childBuffer = child_BFE[parentEntity];

                        do
                        {
                            if (!Contains(childBuffer, addChildEntity))
                                childList.Add(addChildEntity);
                        }
                        while (parentChildToAdd.TryGetNextValue(out addChildEntity, ref addIterator));

                        childBuffer.AddRange(childList.AsArray().Reinterpret<Child2D>());
                        childList.Clear();
                    }
                }
            }
            private int GetChildIndex(in DynamicBuffer<Child2D> children, in Entity childEntity)
            {
                for (int childIndex = 0; childIndex < children.Length; childIndex++)
                    if (children[childIndex].value == childEntity)
                        return childIndex;
                return -1;
            }
            private bool Contains(in DynamicBuffer<Child2D> children, in Entity childEntity)
            {
                for (int childIndex = 0; childIndex < children.Length; childIndex++)
                    if (children[childIndex].value == childEntity)
                        return true;
                return false;
            }
        }
        [BurstCompile]
        private struct UnparentChildrenJob : IJobEntityBatch
        {
            [ReadOnly] public EntityTypeHandle entityTypeHandle;
            [ReadOnly] public BufferTypeHandle<Child2D> child_BTH;
            [ReadOnly] public ComponentDataFromEntity<Parent2D> parent_CDFE;
            public EntityCommandBuffer.ParallelWriter ecb;
            public ComponentTypes componentsToRemoveFromChildren;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var chunkEntities = batchInChunk.GetNativeArray(entityTypeHandle);
                var chunkChildren = batchInChunk.GetBufferAccessor(child_BTH);

                for (int entityIndex = 0; entityIndex < batchInChunk.Count; entityIndex++)
                {
                    var parentEntity = chunkEntities[entityIndex];
                    var childBuffer = chunkChildren[entityIndex];

                    for (int childIndex = 0; childIndex < childBuffer.Length; childIndex++)
                    {
                        var childEntity = childBuffer[childIndex].value;
                        if (!parent_CDFE.HasComponent(childEntity) || parent_CDFE[childEntity].value == parentEntity)
                            ecb.RemoveComponent(batchIndex, childEntity, componentsToRemoveFromChildren);
                    }
                }
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            _missingChildren = GetEntityQuery
            (
                ComponentType.ReadOnly<LastParent2D>(),
                ComponentType.Exclude<Parent2D>(),
                ComponentType.Exclude<StaticRelationshipsTag>()
            );
            _lastParentLessChildren = GetEntityQuery
            (
                ComponentType.ReadOnly<Parent2D>(),
                ComponentType.Exclude<LastParent2D>(),
                //no need to attach LastParent2D to entities which won't be handled by that system
                ComponentType.Exclude<StaticRelationshipsTag>()
            );
            _reparentedChildren = GetEntityQuery
            (
                ComponentType.ReadOnly<Parent2D>(),
                ComponentType.ReadWrite<LastParent2D>(),
                ComponentType.Exclude<StaticRelationshipsTag>(),

                //we want to be sure we deal with transform entities, because LocalToParent2DSystem will access this components
                ComponentType.ReadOnly<LocalPosition2D>(),
                ComponentType.ReadOnly<WorldPosition2D>()
            );
            _reparentedChildren.SetChangedVersionFilter(typeof(Parent2D));
            _missingParents = GetEntityQuery
            (
                ComponentType.ReadOnly<Child2D>(),
                ComponentType.Exclude<WorldPosition2D>(),
                ComponentType.Exclude<StaticRelationshipsTag>()
            );
            _lastParentWithoutParent = GetEntityQuery
            (
                ComponentType.ReadOnly<LastParent2D>(),
                ComponentType.Exclude<Parent2D>()
            );
            _staticRelationshipsAlone = GetEntityQuery
            (
                ComponentType.ReadOnly<StaticRelationshipsTag>(),
                ComponentType.Exclude<Child2D>(),
                ComponentType.Exclude<Parent2D>()
            );
            _childBufferAlone = GetEntityQuery
            (
                ComponentType.ReadOnly<Child2D>(),
                ComponentType.Exclude<WorldPosition2D>()
            );
        }
        protected override void OnUpdate()
        {
            //children without LastParent2D must have one
            if (!_lastParentLessChildren.IsEmptyIgnoreFilter)
                EntityManager.AddComponent(_lastParentLessChildren, _lastParent2DComp);

            if (!_missingParents.IsEmptyIgnoreFilter)
            {
                var unparentECB = new EntityCommandBuffer(Allocator.TempJob);
                Dependency = new UnparentChildrenJob
                {
                    entityTypeHandle = GetEntityTypeHandle(),
                    child_BTH = GetBufferTypeHandle<Child2D>(true),
                    parent_CDFE = GetComponentDataFromEntity<Parent2D>(true),
                    ecb = unparentECB.AsParallelWriter(),
                    componentsToRemoveFromChildren = _componentsToRemoveFromUnparentedChildren
                }.ScheduleParallel(_missingParents, Dependency);
                Dependency.Complete();
                unparentECB.Playback(EntityManager);
                EntityManager.RemoveComponent<Child2D>(_missingParents);
                unparentECB.Dispose();
            }

            var missingChildrenIsEmpty = _missingChildren.IsEmptyIgnoreFilter;
            var reparentedChildrenIsEmpty = _reparentedChildren.IsEmpty;

            if (!missingChildrenIsEmpty || !reparentedChildrenIsEmpty)
            {
                var potentialRemoveCount = 0;
                var potentialAddCount = 0;
                if (!missingChildrenIsEmpty)
                    potentialRemoveCount += _missingChildren.CalculateEntityCount();
                if (!reparentedChildrenIsEmpty)
                {
                    var reparentedCount = _reparentedChildren.CalculateEntityCount();
                    potentialAddCount += reparentedCount;
                    potentialRemoveCount += reparentedCount;
                }
                //remove count is always bigger or equal to add count, so we can use it like max potential size. * 2 because there can be N parent + N last parent and all unique
                var uniqueParents = new NativeParallelHashSet<Entity>(potentialRemoveCount * 2, Allocator.TempJob);
                var parentChildToRemove = new NativeParallelMultiHashMap<Entity, Entity>(potentialRemoveCount, Allocator.TempJob);
                var parentChildToAdd = new NativeParallelMultiHashMap<Entity, Entity>(potentialAddCount, Allocator.TempJob);

                var uniqueParents_PW = uniqueParents.AsParallelWriter();
                var parentChildToRemove_PW = parentChildToRemove.AsParallelWriter();

                Dependency = new GatherMissingChildrenDataJob
                {
                    entityTypeHandle = GetEntityTypeHandle(),
                    lastParent_CTH = GetComponentTypeHandle<LastParent2D>(true),
                    uniqueAffectedParents = uniqueParents_PW,
                    parentChildToRemove = parentChildToRemove_PW
                }.ScheduleParallel(_missingChildren, Dependency);

                Dependency = new GatherReparentedChildrenDataJob
                {
                    parentChildToAdd = parentChildToAdd.AsParallelWriter(),
                    parentChildToRemove = parentChildToRemove_PW,
                    uniqueAffectedParents = uniqueParents_PW,
                    entityTypeHandle = GetEntityTypeHandle(),
                    parent_CTH = GetComponentTypeHandle<Parent2D>(true),
                    lastParent_CTH = GetComponentTypeHandle<LastParent2D>(false)
                }.ScheduleParallel(_reparentedChildren, Dependency);

                Dependency.Complete();

                var ecb = new EntityCommandBuffer(Allocator.TempJob);
                var uniqueParentArray = uniqueParents.ToNativeArray(Allocator.TempJob);
                Dependency = new FixupRelationsJob
                {
                    parentChildToAdd = parentChildToAdd,
                    parentChildToRemove = parentChildToRemove,
                    uniqueAffectedParents = uniqueParentArray,
                    child_BFE = GetBufferFromEntity<Child2D>(false),
                    ecb = ecb.AsParallelWriter(),
                }.ScheduleBatch(uniqueParentArray.Length, 32, default);

                uniqueParents.Dispose();

                Dependency.Complete();
                ecb.Playback(EntityManager);

                ecb.Dispose();
                uniqueParentArray.Dispose();
                parentChildToAdd.Dispose();
                parentChildToRemove.Dispose();
            }

            if (!_lastParentWithoutParent.IsEmptyIgnoreFilter)
                EntityManager.RemoveComponent<LastParent2D>(_lastParentWithoutParent);

            if (!_staticRelationshipsAlone.IsEmptyIgnoreFilter)
                EntityManager.RemoveComponent<StaticRelationshipsTag>(_staticRelationshipsAlone);

            if (!_childBufferAlone.IsEmptyIgnoreFilter)
                EntityManager.RemoveComponent<Child2D>(_childBufferAlone);
        }
    }
}