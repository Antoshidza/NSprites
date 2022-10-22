using NSprites;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

#region RegisterGenericJobType
#region GatherQueryPropertyJob
[assembly: RegisterGenericJobType(typeof(GatherQueryPropertyJob<int>))]
[assembly: RegisterGenericJobType(typeof(GatherQueryPropertyJob<int2>))]
[assembly: RegisterGenericJobType(typeof(GatherQueryPropertyJob<int3>))]
[assembly: RegisterGenericJobType(typeof(GatherQueryPropertyJob<int4>))]
[assembly: RegisterGenericJobType(typeof(GatherQueryPropertyJob<int2x2>))]
[assembly: RegisterGenericJobType(typeof(GatherQueryPropertyJob<int3x3>))]
[assembly: RegisterGenericJobType(typeof(GatherQueryPropertyJob<int4x4>))]
[assembly: RegisterGenericJobType(typeof(GatherQueryPropertyJob<float>))]
[assembly: RegisterGenericJobType(typeof(GatherQueryPropertyJob<float2>))]
[assembly: RegisterGenericJobType(typeof(GatherQueryPropertyJob<float3>))]
[assembly: RegisterGenericJobType(typeof(GatherQueryPropertyJob<float4>))]
[assembly: RegisterGenericJobType(typeof(GatherQueryPropertyJob<float2x2>))]
[assembly: RegisterGenericJobType(typeof(GatherQueryPropertyJob<float3x3>))]
[assembly: RegisterGenericJobType(typeof(GatherQueryPropertyJob<float4x4>))]
#endregion
#region GatherChangedChunksPropertyJob
[assembly: RegisterGenericJobType(typeof(GatherChangedChunksPropertyJob<int>))]
[assembly: RegisterGenericJobType(typeof(GatherChangedChunksPropertyJob<int2>))]
[assembly: RegisterGenericJobType(typeof(GatherChangedChunksPropertyJob<int3>))]
[assembly: RegisterGenericJobType(typeof(GatherChangedChunksPropertyJob<int4>))]
[assembly: RegisterGenericJobType(typeof(GatherChangedChunksPropertyJob<int2x2>))]
[assembly: RegisterGenericJobType(typeof(GatherChangedChunksPropertyJob<int3x3>))]
[assembly: RegisterGenericJobType(typeof(GatherChangedChunksPropertyJob<int4x4>))]
[assembly: RegisterGenericJobType(typeof(GatherChangedChunksPropertyJob<float>))]
[assembly: RegisterGenericJobType(typeof(GatherChangedChunksPropertyJob<float2>))]
[assembly: RegisterGenericJobType(typeof(GatherChangedChunksPropertyJob<float3>))]
[assembly: RegisterGenericJobType(typeof(GatherChangedChunksPropertyJob<float4>))]
[assembly: RegisterGenericJobType(typeof(GatherChangedChunksPropertyJob<float2x2>))]
[assembly: RegisterGenericJobType(typeof(GatherChangedChunksPropertyJob<float3x3>))]
[assembly: RegisterGenericJobType(typeof(GatherChangedChunksPropertyJob<float4x4>))]
#endregion
#region GatherAllChunksPropertyJob
[assembly: RegisterGenericJobType(typeof(GatherAllChunksPropertyJob<int>))]
[assembly: RegisterGenericJobType(typeof(GatherAllChunksPropertyJob<int2>))]
[assembly: RegisterGenericJobType(typeof(GatherAllChunksPropertyJob<int3>))]
[assembly: RegisterGenericJobType(typeof(GatherAllChunksPropertyJob<int4>))]
[assembly: RegisterGenericJobType(typeof(GatherAllChunksPropertyJob<int2x2>))]
[assembly: RegisterGenericJobType(typeof(GatherAllChunksPropertyJob<int3x3>))]
[assembly: RegisterGenericJobType(typeof(GatherAllChunksPropertyJob<int4x4>))]
[assembly: RegisterGenericJobType(typeof(GatherAllChunksPropertyJob<float>))]
[assembly: RegisterGenericJobType(typeof(GatherAllChunksPropertyJob<float2>))]
[assembly: RegisterGenericJobType(typeof(GatherAllChunksPropertyJob<float3>))]
[assembly: RegisterGenericJobType(typeof(GatherAllChunksPropertyJob<float4>))]
[assembly: RegisterGenericJobType(typeof(GatherAllChunksPropertyJob<float2x2>))]
[assembly: RegisterGenericJobType(typeof(GatherAllChunksPropertyJob<float3x3>))]
[assembly: RegisterGenericJobType(typeof(GatherAllChunksPropertyJob<float4x4>))]
#endregion
#endregion

namespace NSprites
{
    internal struct SpriteRenderingSystemState
    {
        public ComponentTypeHandle<PropertyBufferIndexRange> propertyBufferIndexRange_CTH;
        public ComponentTypeHandle<PropertyBufferIndex> propertyBufferIndex_CTH;
        public uint lastSystemVersion;
        public JobHandle inputDeps;
    }

    #region property jobs
    // TPropety supposed to be: int/int2/int3/int4/float/float2/float3/float4
    // TODO: implement int2x2/int3x3/int4x4/float2x2/float3x3/float4x4 because HLSL only supports square matricies
    [BurstCompile]
    internal struct GatherAllChunksPropertyJob<TProperty> : IJobParallelForBatch
        where TProperty : unmanaged
    {
        [ReadOnly] NativeArray<ArchetypeChunk> chunks;
        // this should be filled every frame with GetDynamicComponentTypeHandle
        [ReadOnly] public DynamicComponentTypeHandle componentTypeHandle;
        [ReadOnly] public int typeSize;
        [ReadOnly] public ComponentTypeHandle<PropertyBufferIndexRange> propertyBufferIndexRange_CTH;
        [WriteOnly][NativeDisableParallelForRestriction] public NativeArray<TProperty> outputArray;

        public void Execute(int startIndex, int count)
        {
            var toIndex = startIndex + count;
            for (int chunkIndex = startIndex; chunkIndex < toIndex; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];
                var range = chunk.GetChunkComponentData(propertyBufferIndexRange_CTH);
                NativeArray<TProperty>.Copy(chunk.GetDynamicComponentDataArrayReinterpret<TProperty>(componentTypeHandle, typeSize), 0, outputArray, range.from, range.count);
            }
        }
    }
    [BurstCompile]
    internal struct GatherChangedChunksPropertyJob<TProperty> : IJobParallelForBatch
        where TProperty : unmanaged
    {
        [ReadOnly] NativeArray<ArchetypeChunk> chunks;
        // this should be filled every frame with GetDynamicComponentTypeHandle
        [ReadOnly] public DynamicComponentTypeHandle componentTypeHandle;
        [ReadOnly] public uint lastSystemVersion;
        [ReadOnly] public int typeSize;
        [ReadOnly] public ComponentTypeHandle<PropertyBufferIndexRange> propertyBufferIndexRange_CTH;
        [WriteOnly][NativeDisableParallelForRestriction] public NativeArray<TProperty> outputArray;

        public void Execute(int startIndex, int count)
        {
            var toIndex = startIndex + count;
            for (int chunkIndex = startIndex; chunkIndex < toIndex; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];

                if (!chunk.DidChange(componentTypeHandle, lastSystemVersion))
                    continue;

                var range = chunk.GetChunkComponentData(propertyBufferIndexRange_CTH);
                NativeArray<TProperty>.Copy(chunk.GetDynamicComponentDataArrayReinterpret<TProperty>(componentTypeHandle, typeSize), 0, outputArray, range.from, range.count);
            }
        }
    }
    [BurstCompile]
    internal struct GatherQueryPropertyJob<TProperty> : IJobChunk
            where TProperty : unmanaged
    {
        // this should be filled every frame with GetDynamicComponentTypeHandle
        [ReadOnly] public DynamicComponentTypeHandle componentTypeHandle;
        public int typeSize;
        [WriteOnly][NativeDisableParallelForRestriction] public NativeArray<TProperty> outputArray;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            var data = chunk.GetDynamicComponentDataArrayReinterpret<TProperty>(componentTypeHandle, typeSize);
            NativeArray<TProperty>.Copy(data, 0, outputArray, firstEntityIndex, data.Length);
        }
    }
    #endregion

    #region properties
    internal abstract class InstancedProperty
    {
        /// property id from <see cref="Shader.PropertyToID"> to be able to pass to <see cref="MaterialPropertyBlock">
        protected int _propertyID;
        /// buffer which synced with entities components data
        protected ComputeBuffer _computeBuffer;
        /// cached component type + system to retrieve <see cref="DynamicComponentTypeHandle">
        protected ComponentType _componentType;
        protected SystemBase _system;
        protected JobHandle _lastUpdateHandle;

        public InstancedProperty(in int propertyID, in int count, in int stride, in ComponentType componentType, SystemBase system)
        {
            _propertyID = propertyID;
            _componentType = componentType;
            _system = system;

            _computeBuffer = new ComputeBuffer(count, stride, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
        }

        public abstract void ReloadAllChunkData(in NativeArray<ArchetypeChunk> chunks, in int writeCount, in JobHandle inputDeps);
        public abstract void UpdateOnChangeChunkData(in NativeArray<ArchetypeChunk> chunks, in int writeCount, in JobHandle inputDeps);
        public abstract void LoadAllQueryData(in EntityQuery query, in int writeCount, in JobHandle inputDeps);
        public abstract void Idle();
        public abstract void Complete(in int writeCount);

        public void Reallocate(in int size, MaterialPropertyBlock materialPropertyBlock)
        {
            var stride = _computeBuffer.stride;
            _computeBuffer.Release();
            _computeBuffer = new ComputeBuffer(size, stride, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
            materialPropertyBlock.SetBuffer(_propertyID, _computeBuffer);
        }
    }
    internal class InstancedProperty<T> : InstancedProperty
        where T : unmanaged
    {
        public InstancedProperty(in int propertyID, in int count, in int stride, in ComponentType componentType, SystemBase system)
            : base(propertyID, count, stride, componentType, system) { }
        public override void ReloadAllChunkData(in NativeArray<ArchetypeChunk> chunks, in int writeCount, in JobHandle inputDeps)
        {
            throw new System.NotImplementedException();
        }
        public override void UpdateOnChangeChunkData(in NativeArray<ArchetypeChunk> chunks, in int writeCount, in JobHandle inputDeps)
        {
            throw new System.NotImplementedException();
        }
        public override void LoadAllQueryData(in EntityQuery query, in int writeCount, in JobHandle inputDeps)
        {
            throw new System.NotImplementedException();
        }
        public override void Idle() => _lastUpdateHandle = default;
        public override void Complete(in int writeCount)
        {
            _lastUpdateHandle.Complete();
            _computeBuffer.EndWrite<T>(writeCount);
        }
    }
    #endregion

    // TODO: rewrite this part, explain more
    // assuming there is no batching for different materials/textures/other not-instanced properties we can define some kind of render archetypes
    // it is combination of material + instanced properties set
    internal class RenderArchetypeV2
    {
        #region jobs
        internal struct IndexComparer : IComparer<int>
        {
            public int direction;

            public int Compare([NoAlias] int x, [NoAlias] int y) => x.CompareTo(y) * direction;
            public static IndexComparer GetMaxComparer() => new() { direction = 1 };
            public static IndexComparer GetMinComparer() => new() { direction = -1 };
        }
        [BurstCompile]
        internal struct GatherChunksDataJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunks;
            [WriteOnly][NoAlias] public NativeCounter.ParallelWriter chunkCapacityCounter;
            [WriteOnly][NoAlias] public NativeCounter.ParallelWriter newChunksCapacityCounter;
            [WriteOnly][NoAlias] public NativeCounter.ParallelWriter entityCounter;
            public ComponentTypeHandle<PropertyBufferIndexRange> propertyBufferIndexRange_CTH;
            [WriteOnly] public NativeList<int>.ParallelWriter chunksToBindIndexes;
            [ReadOnly] public uint lastSystemVersion;

            public void Execute([NoAlias] int startIndex, [NoAlias] int count)
            {
                var toIndex = startIndex + count;
                for (int chunkIndex = startIndex; chunkIndex < toIndex; chunkIndex++)
                {
                    var chunk = chunks[chunkIndex];
                    var capacity = chunk.Capacity;
                    chunkCapacityCounter.Add(capacity);
                    entityCounter.Add(chunk.Count);
#if UNITY_EDITOR
                    if (!chunk.HasChunkComponent(propertyBufferIndexRange_CTH))
                        throw new System.Exception($"Render archetype has on change updatable properties, but chunk has no {nameof(PropertyBufferIndexRange)}");
#endif
                    var propertyBufferIndexRange = chunk.GetChunkComponentData(propertyBufferIndexRange_CTH);
                    /// if <see cref="PropertyBufferIndexRange.count"> which is chunk's capacity and if it is 0 it means that chunk is newly created
                    if (propertyBufferIndexRange.count == 0)
                    {
                        chunksToBindIndexes.AddNoResize(chunkIndex);
                        newChunksCapacityCounter.Add(capacity);
                    }
                    // we want to reassign indexes to chunk and chunk's entities if it recieves or loses entities
                    else if(chunk.DidOrderChange(lastSystemVersion))
                        chunksToBindIndexes.AddNoResize(chunkIndex);
                }
            }
        }
        [BurstCompile]
        internal struct FillSelectedChunkIndexesJob : IJob
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunks;
            public ComponentTypeHandle<PropertyBufferIndexRange> propertyBufferIndexRange_CTH;
            [ReadOnly] public int startingFromIndex;
            [ReadOnly] public NativeList<int> chunksIndexes;

            public void Execute()
            {
                for (int i = 0; i < chunksIndexes.Length; i++)
                {
                    var chunk = chunks[chunksIndexes[i]];
                    var capacity = chunk.Capacity;
                    chunk.SetChunkComponentData(propertyBufferIndexRange_CTH, new PropertyBufferIndexRange { from = startingFromIndex, count = capacity });
                    startingFromIndex += capacity;
                }
            }
        }
        [BurstCompile]
        internal struct FillSelectedEntityIndexesJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunks;
            [ReadOnly] public NativeList<int> chunkIndexes;
            public ComponentTypeHandle<PropertyBufferIndex> propertyBufferIndex_CTH;
            [ReadOnly] public ComponentTypeHandle<PropertyBufferIndexRange> propertyBufferIndexRange_CTH;

            public void Execute([NoAlias] int startIndex, [NoAlias] int count)
            {
                var toIndex = startIndex + count;
                for (int i = startIndex; i < toIndex; i++)
                {
                    var chunk = chunks[chunkIndexes[i]];
                    var range = chunk.GetChunkComponentData(propertyBufferIndexRange_CTH);
                    var entityIndexes = chunk.GetNativeArray(propertyBufferIndex_CTH);
                    for (int entityIndex = 0; entityIndex < entityIndexes.Length; entityIndex++)
                        entityIndexes[entityIndex] = new PropertyBufferIndex { value = range.from + entityIndex };
                }
            }
        }
        [BurstCompile]
        internal struct FillChunkIndexesJob : IJob
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunks;
            public ComponentTypeHandle<PropertyBufferIndexRange> propertyBufferIndexRange_CTH;

            public void Execute()
            {
                var index = 0;
                for (int i = 0; i < chunks.Length; i++)
                {
                    var chunk = chunks[i];
                    var capacity = chunk.Capacity;
                    chunk.SetChunkComponentData(propertyBufferIndexRange_CTH, new PropertyBufferIndexRange { from = index, count = capacity });
                    index += capacity;
                }
            }
        }
        [BurstCompile]
        internal struct FillEntityIndexesJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunks;
            public ComponentTypeHandle<PropertyBufferIndex> propertyBufferIndex_CTH;
            [ReadOnly] public ComponentTypeHandle<PropertyBufferIndexRange> propertyBufferIndexRange_CTH;

            public void Execute([NoAlias] int startIndex, [NoAlias] int count)
            {
                var toIndex = startIndex + count;
                for (int chunkIndex = startIndex; chunkIndex < toIndex; chunkIndex++)
                {
                    var chunk = chunks[chunkIndex];
                    var range = chunk.GetChunkComponentData(propertyBufferIndexRange_CTH);
                    var entityIndexes = chunk.GetNativeArray(propertyBufferIndex_CTH);
                    for (int entityIndex = 0; entityIndex < entityIndexes.Length; entityIndex++)
                        entityIndexes[entityIndex] = new PropertyBufferIndex { value = range.from + entityIndex };
                }
            }
        }
        #endregion

        private readonly int _id;
        private readonly EntityQuery _mainQuery;

        private readonly Material _material;
        private readonly MaterialPropertyBlock _materialPropertyBlock;
        private int _drawCount;

        
        // minimum additional capacity we want allocate at a time
        private readonly int _minCapacityStep;

        // properties we want to update every frame. Layouted per-entity
        private readonly InstancedProperty[] _eachUpdateProperties;
        // properties we want to update only of entities created. Layouted per-entity
        private readonly InstancedProperty[] _staticProperties;
        // each-update / static prop's compute buffer's capacity
        private int _nonReactivePropertiesCapacity;

#if !NSPRITES_REACTIVE_PROPERTIES_DISABLE
        private const int MinIndicesPerJobCount = 32;

        // properties we want to update only if data changes / entities created / capacity exceeded. Layouted per-chunk.
        private readonly InstancedProperty[] _reactiveProperties;
        // how much space actually is occupied
        private int _usedCapacity;
        // how much space we currently have
        private int _capacity;

        // how many space we can use to assign chunks without reallocating buffers
        private int UnusedCapacity => _capacity - _usedCapacity;
#endif

        public RenderArchetypeV2(Material material, string[] instancedPropertyNames, in int id, SystemBase system, MaterialPropertyBlock overrideMPB = null, in int preallocatedSpace = 0, in int minCapacityStep = 0)
        {
            _id = id;
            _material = material;
            _materialPropertyBlock = overrideMPB ?? new ();
            _capacity = preallocatedSpace;
            _minCapacityStep = minCapacityStep;

            // TODO: implement update mode option for properties
            /// initialize properties like in <see cref="SpriteRenderingSystem.RegistrateRender">

            // initialize query
        }
        public void ScheduleUpdate(in SpriteRenderingSystemState systemState)
        {
            // we need to use this method every new frame, because query somehow gets invalidated
            _mainQuery.SetSharedComponentFilter(new SpriteRenderID() { id = _id });

            /// in any case there is a need to know entity count to update each-update properties
            /// in case if reactive properties enabled and there are any entity count will be calculated in <see cref="GatherChunksDataJob">
            /// in other case entity count will be calculated through <see cref="EntityQuery.CalculateEntityCount">
            /// each-update properties necessary because there is at least 1 such property to access write instance data in shader through <see cref="PropertyBufferIndex">
            var entityCount = 0;

            #region handle update-on-change properties
#if !NSPRITES_REACTIVE_PROPERTIES_DISABLE
            if (_reactiveProperties.Length != 0)
            {
                #region chunk gather data
                var chunks = _mainQuery.CreateArchetypeChunkArrayAsync(Allocator.TempJob, out var gatherChunksHandle);
                var overallCapacityCounter = new NativeCounter(Allocator.TempJob);
                var newChunksCapacityCounter = new NativeCounter(Allocator.TempJob);
                var entityCounter = new NativeCounter(Allocator.TempJob);
                var chunksToBindIndexes = new NativeList<int>(chunks.Length, Allocator.TempJob);
                var gatherChunksDataHandle = new GatherChunksDataJob
                {
                    chunks = chunks,
                    chunkCapacityCounter = overallCapacityCounter,
                    newChunksCapacityCounter = newChunksCapacityCounter,
                    entityCounter = entityCounter,
                    propertyBufferIndexRange_CTH = systemState.propertyBufferIndexRange_CTH,
                    chunksToBindIndexes = chunksToBindIndexes.AsParallelWriter(),
                    lastSystemVersion = systemState.lastSystemVersion
                }.ScheduleBatch(chunks.Length, MinIndicesPerJobCount, gatherChunksHandle);

                gatherChunksDataHandle.Complete();
                entityCount = entityCounter.Count;
                #endregion

                #region update capacity, sync data
                var neededOverallCapacity = overallCapacityCounter.Count;
                var neededExtraCapacity = newChunksCapacityCounter.Count;
                var unusedCapacity = UnusedCapacity;
                // if overall capacity exceeds current capacity OR
                // there is new chunks and theirs sum capacity exceeds free space we have currently
                // then we need to reallocate all per-property compute buffers and reassign chunks / entities indexes
                if (neededOverallCapacity > _capacity || neededExtraCapacity > UnusedCapacity)
                {
                    // 0. reassign all chunk's / entity's indexes
                    // this job will iterate through chunks one by one and increase theirs indexes
                    // execution can't be parallel because calculation is dependent, so this is weakest part
                    // this part goes before buffer's reallocation because it is just work with indexes we already now
                    var fillChunksIndexesHandle = new FillChunkIndexesJob
                    {
                        chunks = chunks,
                        propertyBufferIndexRange_CTH = systemState.propertyBufferIndexRange_CTH
                    }.Schedule(gatherChunksDataHandle);
                    var fillEntityIndexesHandle = new FillEntityIndexesJob
                    {
                        chunks = chunks,
                        propertyBufferIndexRange_CTH = systemState.propertyBufferIndexRange_CTH,
                        propertyBufferIndex_CTH = systemState.propertyBufferIndex_CTH
                    }.ScheduleBatch(chunks.Length, MinIndicesPerJobCount, fillChunksIndexesHandle);

                    // 1. reallocate compute buffers
                    // here we calculate new capacity no matter what the reason was to reallocate buffers
                    // new capaicty depends on how much new space we need, but this space jump can be lower then min capacity step
                    var newCapacity = math.max(_capacity + _minCapacityStep, neededOverallCapacity - _capacity);
                    _capacity = newCapacity;
                    var preGatherHandle = JobHandle.CombineDependencies(fillEntityIndexesHandle, systemState.inputDeps);
                    // foreach UOC property reallocate compute buffer
                    for (int propertyIndex = 0; propertyIndex < _reactiveProperties.Length; propertyIndex++)
                    {
                        var property = _reactiveProperties[propertyIndex];
                        property.Reallocate(newCapacity, _materialPropertyBlock);
                        // 2. reload all properties to new compute buffers (shot down properties update method, because all data was reloaded)
                        property.ReloadAllChunkData(chunks, neededOverallCapacity, preGatherHandle);
                    }
                }
                // here we can relax because we have enough capacity
                else 
                {
                    var preGatherHandle = systemState.inputDeps;
                    // if we haven't reallocated buffers it means that we have enough space for all chunks we have by now
                    // so we can now assing all indexes to new / changed (reordered) chunks if any
                    if (chunksToBindIndexes.Length != 0)
                    {
                        // 0. assign new chunks indexes starting from previous _count (don't forget to move _count to new value)
                        var fillChunksIndexesHandle = new FillSelectedChunkIndexesJob
                        {
                            chunks = chunks,
                            propertyBufferIndexRange_CTH = systemState.propertyBufferIndexRange_CTH,
                            chunksIndexes = chunksToBindIndexes,
                            startingFromIndex = _usedCapacity
                        }.Schedule(gatherChunksDataHandle);
                        var fillEntityIndexesHandle = new FillSelectedEntityIndexesJob
                        {
                            chunks = chunks,
                            chunkIndexes = chunksToBindIndexes,
                            propertyBufferIndexRange_CTH = systemState.propertyBufferIndexRange_CTH,
                            propertyBufferIndex_CTH = systemState.propertyBufferIndex_CTH
                        }.ScheduleBatch(chunksToBindIndexes.Length, MinIndicesPerJobCount, fillChunksIndexesHandle);
                        preGatherHandle = JobHandle.CombineDependencies(fillEntityIndexesHandle, preGatherHandle);
                    }

                    // finally because there was no need to reallocate buffers we can just update UOC properties
                    // 1. update properties UOC (this will trigger data load including of new chunks)
                    // foreach UOC property reallocate compute buffer
                    for (int propertyIndex = 0; propertyIndex < _reactiveProperties.Length; propertyIndex++)
                        _reactiveProperties[propertyIndex].UpdateOnChangeChunkData(chunks, neededOverallCapacity, preGatherHandle);
                }

                // every frame we calculate overall capacity and cache it
                // we need to cache it because we want to know unused capacity in future and write count of compute buffer
                _usedCapacity = neededOverallCapacity;
                #endregion

                #region dispose containers
                chunks.Dispose();
                overallCapacityCounter.Dispose();
                newChunksCapacityCounter.Dispose();
                entityCounter.Dispose();
                chunksToBindIndexes.Dispose();
                #endregion
            }
            else
                entityCount = _mainQuery.CalculateEntityCount();
#endif
            #endregion

            #region handle every-update / static properties
            // check if we have properties to update
            if (_eachUpdateProperties.Length != 0 || _staticProperties.Length != 0)
            {
#if NSPRITES_REACTIVE_PROPERTIES_DISABLE
                // calculate entity count before any actions if reactive properties code disabled
                entityCount = _mainQuery.CalculateEntityCount();
#endif
                // reallocate buffers if capacity excedeed by min-capacity-step and load all data for each-update and static properties
                if (_nonReactivePropertiesCapacity < entityCount)
                {
                    _nonReactivePropertiesCapacity = (int)math.ceil((float)entityCount / _minCapacityStep);
                    for (int propIndex = 0; propIndex < _eachUpdateProperties.Length; propIndex++)
                    {
                        var property = _eachUpdateProperties[propIndex];
                        property.Reallocate(_nonReactivePropertiesCapacity, _materialPropertyBlock);
                        property.LoadAllQueryData(_mainQuery, entityCount, systemState.inputDeps);
                    }
                    for (int propIndex = 0; propIndex < _staticProperties.Length; propIndex++)
                    {
                        var property = _staticProperties[propIndex];
                        property.Reallocate(_nonReactivePropertiesCapacity, _materialPropertyBlock);
                        property.LoadAllQueryData(_mainQuery, entityCount, systemState.inputDeps);
                    }
                }
                // if there was no exceed just load all each-frame properties data but no static data
                else
                    for (int propIndex = 0; propIndex < _eachUpdateProperties.Length; propIndex++)
                        _eachUpdateProperties[propIndex].LoadAllQueryData(_mainQuery, entityCount, systemState.inputDeps);

            }
            #endregion

            _drawCount = entityCount;
        }
        public void Complete()
        {
#if !NSPRITES_REACTIVE_PROPERTIES_DISABLE
            for (int propIndex = 0; propIndex < _reactiveProperties.Length; propIndex++)
                _reactiveProperties[propIndex].Complete(_usedCapacity);
#endif
            for (int propIndex = 0; propIndex < _eachUpdateProperties.Length; propIndex++)
                _eachUpdateProperties[propIndex].Complete(_drawCount);
            /// TODO: we can't call <see cref="InstancedProperty.Complete"> because we don't know was there any update
            /// remember what static props was updated and complete only them
            for (int propIndex = 0; propIndex < _staticProperties.Length; propIndex++)
                _staticProperties[propIndex].Complete(_drawCount);
        }
    }
}