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

        public InstancedProperty(in int propertyID, in int count, in int stride, in ComponentType componentType, SystemBase system)
        {
            _propertyID = propertyID;
            _componentType = componentType;
            _system = system;

            _computeBuffer = new ComputeBuffer(count, stride, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
        }

        public abstract JobHandle ReloadAllData(in NativeArray<ArchetypeChunk> chunks, in int writeCount, in JobHandle inputDeps);
        public abstract JobHandle UpdateOnChangeData(in NativeArray<ArchetypeChunk> chunks, in int writeCount, in JobHandle inputDeps);
        public abstract void EndWrite(in int count);

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
        public override JobHandle ReloadAllData(in NativeArray<ArchetypeChunk> chunks, in int writeCount, in JobHandle inputDeps)
        {
            throw new System.NotImplementedException();
        }
        public override JobHandle UpdateOnChangeData(in NativeArray<ArchetypeChunk> chunks, in int writeCount, in JobHandle inputDeps)
        {
            throw new System.NotImplementedException();
        }
        public override void EndWrite(in int count) => _computeBuffer.EndWrite<T>(count);
    }
    #endregion

    // TODO: rewrite this part, explain more
    // assuming there is no batching for different materials/textures/other not-instanced properties we can define some kind of render archetypes
    // it is combination of material + instanced properties set
    // UOC - Update On Change
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
        private readonly Material _material;
        private readonly MaterialPropertyBlock _materialPropertyBlock;
        private readonly EntityQuery _mainQuery;
        
        // properties we want to update every frame. Layouted per-entity
        private readonly InstancedProperty[] _eachFrameProperties;
        // properties we want to update only of entities created. Layouted per-entity
        private readonly InstancedProperty[] _staticProperties;
        
        private const int MinIndicesPerJobCount = 32;   // for UOC

#if !NSPRITES_REACTIVE_PROPERTIES_DISABLE
        // properties we want to update only if data changes / entities created / capacity exceeded. Layouted per-chunk.
        private readonly InstancedProperty[] _onChageProperties;
        // minimum additional capacity we want allocate at a time
        private readonly int _minCapacityStep;
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
            _materialPropertyBlock = overrideMPB ?? new MaterialPropertyBlock();
            _capacity = preallocatedSpace;
            _minCapacityStep = minCapacityStep;

            // TODO: implement update mode option for properties
            /// initialize properties like in <see cref="SpriteRenderingSystem.RegistrateRender">

            // initialize query
        }
        public JobHandle ScheduleUpdate(in SpriteRenderingSystemState systemState)
        {
            _mainQuery.SetSharedComponentFilter(new SpriteRenderID() { id = _id });

            #region handle UOC properties
#if !NSPRITES_REACTIVE_PROPERTIES_DISABLE
            #region chunk gather data
            var chunks = _mainQuery.CreateArchetypeChunkArrayAsync(Allocator.TempJob, out var gatherChunksHandle);
            var overallCapacityCounter = new NativeCounter(Allocator.TempJob);
            var newChunksCapacityCounter = new NativeCounter(Allocator.TempJob);
            var chunksToBindIndexes = new NativeList<int>(chunks.Length, Allocator.TempJob);
            var gatherChunksDataHandle = new GatherChunksDataJob
            {
                chunks = chunks,
                chunkCapacityCounter = overallCapacityCounter,
                newChunksCapacityCounter = newChunksCapacityCounter,
                propertyBufferIndexRange_CTH = systemState.propertyBufferIndexRange_CTH,
                chunksToBindIndexes = chunksToBindIndexes.AsParallelWriter(),
                lastSystemVersion = systemState.lastSystemVersion
            }.ScheduleBatch(chunks.Length, MinIndicesPerJobCount, gatherChunksHandle);

            gatherChunksDataHandle.Complete();
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
                for (int propertyIndex = 0; propertyIndex < _onChageProperties.Length; propertyIndex++)
                {
                    var property = _onChageProperties[propertyIndex];
                    property.Reallocate(newCapacity, _materialPropertyBlock);
                    // 2. reload all properties to new compute buffers (shot down properties update method, because all data was reloaded)
                    property.ReloadAllData(chunks, neededOverallCapacity, preGatherHandle);
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
                for (int propertyIndex = 0; propertyIndex < _onChageProperties.Length; propertyIndex++)
                    _onChageProperties[propertyIndex].UpdateOnChangeData(chunks, neededOverallCapacity, preGatherHandle);
            }

            // every frame we calculate overall capacity and cache it
            _usedCapacity = neededOverallCapacity;
            #endregion

            #region dispose containers
            chunks.Dispose();
            overallCapacityCounter.Dispose();
            newChunksCapacityCounter.Dispose();
            chunksToBindIndexes.Dispose();
            #endregion
#endif
            #endregion
            return systemState.inputDeps;
        }
        public void Complete()
        {
        }
    }
}