using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace NSprites
{
    #region data structs
    /// <summary>Holds info about: what shader's property id / update mode property uses</summary>
    public struct PropertyData
    {
        internal readonly int propertyID;
        internal readonly PropertyUpdateMode updateMode;

        public PropertyData(int propertyID, in PropertyUpdateMode updateMode = default)
        {
            this.propertyID = propertyID;
            this.updateMode = NSpritesUtils.GetActualMode(updateMode);
        }

        public PropertyData(in string propertyName, in PropertyUpdateMode updateMode = default)
            : this(Shader.PropertyToID(propertyName), updateMode) { }

        public static implicit operator PropertyData(in string propertyName) => new(propertyName);
    }
    internal struct SystemData
    {
        public EntityQuery query;
        public JobHandle inputDeps;
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
        public ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH_RW;
        public ComponentTypeHandle<PropertyPointer> propertyPointer_CTH_RW;
        public ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH_RO;
        public uint lastSystemVersion;
#endif
    }
    internal struct PropertySpaceCounter
    {
        // how much space property has
        public int capacity;
        // how much space property use
        public int count;
        // how much more space property can use
        public int UnusedCapacity => capacity - count;
    }
    internal struct PropertyHandleCollector : IDisposable
    {
        private NativeArray<JobHandle> _handles;
        private readonly int _conditionalOffset;
        public bool includeConditional;
        public readonly int propertyPointerIndex;

        public JobHandle this[int i] => _handles[i];

        public PropertyHandleCollector(in int capacity, in int conditionalCount, in Allocator allocator)
        {
            _handles = new NativeArray<JobHandle>(capacity, allocator);
            _conditionalOffset = capacity - conditionalCount;
            includeConditional = false;
            propertyPointerIndex = capacity - 1;
        }

        public void Add(in JobHandle handle, in int index) => _handles[index] = handle;
        public JobHandle GetCombined() => JobHandle.CombineDependencies(new NativeSlice<JobHandle>(_handles, 0, includeConditional ? _handles.Length : _conditionalOffset));

        public void Reset() => includeConditional = false;

        public void Dispose() => _handles.Dispose();
    }
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
    internal struct ReusableNativeList<T> : IDisposable
        where T : unmanaged
    {
        private NativeList<T> _list;

        public ReusableNativeList(in int initialCapacity, in Allocator allocator)
        {
            _list = new NativeList<T>(initialCapacity, allocator);
        }

        public void Dispose()
        {
            if(_list.IsCreated)
                _list.Dispose();
        }

        public NativeList<T> GetList(in int capacity)
        {
            if (_list.Capacity < capacity)
                _list.SetCapacity(capacity);
            _list.Clear();
            return _list;
        }
    }
#endif
    #endregion

    #region property jobs
    // TProperty supposed to be: int/int2/int3/int4/int2x2/int3x3/int4x4/float/float2/float3/float4/float2x2/float3x3/float4x4
    // matrices types are only square because HLSL supports only such, so there is no need to support any other NxM types
    #region reactive / static properties jobs
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
    [BurstCompile]
    /// job will take all chunks, read theirs <see cref="PropertyPointerChunk"/> and copy component data to compute buffer starting from chunk's range from value
    internal struct SyncPropertyByChunkJob : IJobParallelForBatch
    {
        [ReadOnly] public NativeArray<ArchetypeChunk> chunks;
        // this should be filled every frame with GetDynamicComponentTypeHandle
        [ReadOnly] public DynamicComponentTypeHandle componentTypeHandle;
        [ReadOnly] public int typeSize;
        [ReadOnly] public ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH;
        [WriteOnly][NativeDisableParallelForRestriction] public NativeArray<byte> outputArray;

        public void Execute(int startIndex, int count)
        {
            var toIndex = startIndex + count;
            for (int chunkIndex = startIndex; chunkIndex < toIndex; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];
                SyncPropertyByQueryJob.WriteData(chunk, ref componentTypeHandle, chunk.GetChunkComponentData(ref propertyPointerChunk_CTH).from, outputArray, typeSize);
            }
        }
    }
    [BurstCompile]
    /// job will take chunks with <see cref="ArchetypeChunk.DidOrderChange"/> OR <see cref="ArchetypeChunk.DidChange">, read theirs <see cref="PropertyPointerChunk"/> and copy component data to compute buffer starting from chunk's range from value
    internal struct SyncPropertyByChangedChunkJob : IJobParallelForBatch
    {
        [ReadOnly] public NativeArray<ArchetypeChunk> chunks;
        // this should be filled every frame with GetDynamicComponentTypeHandle
        [ReadOnly] public DynamicComponentTypeHandle componentTypeHandle;
        [ReadOnly] public uint lastSystemVersion;
        [ReadOnly] public int typeSize;
        [ReadOnly] public ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH;
        [WriteOnly][NativeDisableParallelForRestriction] public NativeArray<byte> outputArray;

        public void Execute(int startIndex, int count)
        {
            var toIndex = startIndex + count;
            for (int chunkIndex = startIndex; chunkIndex < toIndex; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];

                // if chunk has no data changes AND has no new / lost entities then do nothing
                if (!chunk.DidChange(ref componentTypeHandle, lastSystemVersion) && !chunk.DidOrderChange(lastSystemVersion))
                    continue;

                SyncPropertyByQueryJob.WriteData(chunk, ref componentTypeHandle, chunk.GetChunkComponentData(ref propertyPointerChunk_CTH).from, outputArray, typeSize);
            }
        }
    }
#endif
#if !NSPRITES_STATIC_DISABLE
    [BurstCompile]
    /// job will take listed chunks, read theirs <see cref="PropertyPointerChunk"/> and copy component data to compute buffer starting from chunk's range from value
    internal struct SyncPropertyByListedChunkJob : IJobParallelForBatch
    {
        [ReadOnly] public NativeArray<ArchetypeChunk> chunks;
        [ReadOnly] public NativeList<int> chunkIndexes;
        // this should be filled every frame with GetDynamicComponentTypeHandle
        [ReadOnly] public DynamicComponentTypeHandle componentTypeHandle;
        [ReadOnly] public int typeSize;
        [ReadOnly] public ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH;
        [WriteOnly][NativeDisableParallelForRestriction][NativeDisableContainerSafetyRestriction] public NativeArray<byte> outputArray;

        public void Execute(int startIndex, int count)
        {
            var toIndex = startIndex + count;
            for (int i = startIndex; i < toIndex; i++)
            {
                var chunk = chunks[chunkIndexes[i]];
                SyncPropertyByQueryJob.WriteData(chunk, ref componentTypeHandle, chunk.GetChunkComponentData(ref propertyPointerChunk_CTH).from, outputArray, typeSize);
            }
        }
    }
#endif
    #endregion
    #region each-update (+ for reactive/static) properties jobs
    [BurstCompile]
    /// job will take all chunks through query, and then just copy component data to compute buffer starting from 1st entity in query index
    internal struct SyncPropertyByQueryJob : IJobChunk
    {
        [ReadOnly][DeallocateOnJobCompletion] public NativeArray<int> chunkBaseEntityIndices;
        // this should be filled every frame with GetDynamicComponentTypeHandle
        [ReadOnly]public DynamicComponentTypeHandle componentTypeHandle;
        public int typeSize;
        [WriteOnly][NativeDisableParallelForRestriction] public NativeArray<byte> outputArray;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            WriteData(chunk, ref componentTypeHandle, chunkBaseEntityIndices[unfilteredChunkIndex], outputArray, typeSize);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void WriteData(in ArchetypeChunk chunk, ref DynamicComponentTypeHandle componentTypeHandle, int startCopyToIndex, in NativeArray<byte> writeArray, int typeSize)
        {
#if NSPRITES_PROPERTY_FALLBACK_ENABLE
                // check if chunk has no prop component then allocate default values
                // ComputeBuffer by itself has already allocated memory, but it's uninitialized, so render result can be unexpected
                var data = chunk.Has(ref componentTypeHandle)
                    ? chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref componentTypeHandle, typeSize)
                    : new NativeArray<byte>(chunk.Count * typeSize, Allocator.Temp, NativeArrayOptions.ClearMemory);
#else
#if UNITY_EDITOR
            if (!chunk.Has(ref componentTypeHandle))
                throw new NSpritesException($"You trying to render entities but it missed one of the required component.");
#endif
            var data = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref componentTypeHandle, typeSize);
#endif
            NativeArray<byte>.Copy(data, 0, writeArray, startCopyToIndex * typeSize, chunk.Count * typeSize);
        }
    }
    #endregion
    #endregion

    #region properties
    /// <summary>
    /// Holds data to schedule property component's data with compute buffers,
    /// so per property in render archetype we have one instance of this class
    /// </summary>
    internal class InstancedProperty
    {
        /// property id from <see cref="Shader.PropertyToID"> to be able to pass to <see cref="MaterialPropertyBlock">
        internal readonly int _propertyID;
        /// buffer which synced with entities components data
        internal ComputeBuffer _computeBuffer;

        private int TypeSize => _computeBuffer.stride;

        /// cached component type + system to retrieve <see cref="DynamicComponentTypeHandle">
        public ComponentType ComponentType { get; }

        public InstancedProperty(in int propertyID, in int count, in int stride, in ComponentType componentType)
        {
            _propertyID = propertyID;
            ComponentType = componentType;

            _computeBuffer = new ComputeBuffer(count, stride, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
        }
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
        public JobHandle LoadAllChunkData(in NativeArray<ArchetypeChunk> chunks, in ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH, in DynamicComponentTypeHandle componentTypeHandle, in int writeCount, in JobHandle inputDeps)
        {
            return new SyncPropertyByChunkJob
            {
                chunks = chunks,
                componentTypeHandle = componentTypeHandle,
                typeSize = TypeSize,
                propertyPointerChunk_CTH = propertyPointerChunk_CTH,
                outputArray = GetBufferArray(writeCount)
            }.ScheduleBatch(chunks.Length, RenderArchetype.MinIndicesPerJobCount, inputDeps);
        }
        public JobHandle UpdateOnChangeChunkData(in NativeArray<ArchetypeChunk> chunks, in ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH, in DynamicComponentTypeHandle componentTypeHandle, in int writeCount, in uint lastSystemVersion, in JobHandle inputDeps)
        {
            return new SyncPropertyByChangedChunkJob
            {
                chunks = chunks,
                componentTypeHandle = componentTypeHandle,
                typeSize = TypeSize,
                propertyPointerChunk_CTH = propertyPointerChunk_CTH,
                lastSystemVersion = lastSystemVersion,
                outputArray = GetBufferArray(writeCount)
            }.ScheduleBatch(chunks.Length, RenderArchetype.MinIndicesPerJobCount, inputDeps);
        }
#endif
#if !NSPRITES_STATIC_DISABLE
        public JobHandle UpdateCreatedAndReorderedChunkData(in NativeArray<ArchetypeChunk> chunks, in NativeList<int> reorderedIndexes, in NativeList<int> createdIndexes, in ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH, in DynamicComponentTypeHandle componentTypeHandle, in int writeCount, in JobHandle inputDeps)
        {
            var writeArray = GetBufferArray(writeCount);
            var reorderedHandle = UpdateListedChunkData(chunks, writeArray, reorderedIndexes, propertyPointerChunk_CTH, componentTypeHandle, inputDeps);
            var createdHandle = UpdateListedChunkData(chunks, writeArray, createdIndexes, propertyPointerChunk_CTH, componentTypeHandle, inputDeps);
            return JobHandle.CombineDependencies(reorderedHandle, createdHandle);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JobHandle UpdateListedChunkData(in NativeArray<ArchetypeChunk> chunks, in NativeArray<byte> writeArray, in NativeList<int> chunkIndexes, in ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH, in DynamicComponentTypeHandle componentTypeHandle, in JobHandle inputDeps)
        {
            return chunkIndexes.Length > 0
                ? new SyncPropertyByListedChunkJob
                {
                    chunks = chunks,
                    chunkIndexes = chunkIndexes,
                    typeSize = TypeSize,
                    propertyPointerChunk_CTH = propertyPointerChunk_CTH,
                    componentTypeHandle = componentTypeHandle,
                    outputArray = writeArray
                }.ScheduleBatch(chunkIndexes.Length, RenderArchetype.MinIndicesPerJobCount, inputDeps)
                : inputDeps;
        }
#endif
        public JobHandle LoadAllQueryData(in EntityQuery query, in DynamicComponentTypeHandle componentTypeHandle, in int writeCount, in JobHandle inputDeps)
        {
            var updatePropertyJob = new SyncPropertyByQueryJob
            {
                componentTypeHandle = componentTypeHandle,
                chunkBaseEntityIndices = query.CalculateBaseEntityIndexArrayAsync(Allocator.TempJob, default, out var calculateChunkBaseIndices),
                typeSize = TypeSize,
                outputArray = GetBufferArray(writeCount)
            };
            return updatePropertyJob.ScheduleParallelByRef(query, JobHandle.CombineDependencies(inputDeps, calculateChunkBaseIndices));
        }
        private NativeArray<byte> GetBufferArray(int writeCount) => _computeBuffer.BeginWrite<byte>(0, writeCount * TypeSize);
        public void EndWrite(in int writeCount) => _computeBuffer.EndWrite<byte>(writeCount * TypeSize);
        
        public void Reallocate(in int size, MaterialPropertyBlock materialPropertyBlock)
        {
            var stride = _computeBuffer.stride;
            _computeBuffer.Release();
            _computeBuffer = new ComputeBuffer(size, stride, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
            materialPropertyBlock.SetBuffer(_propertyID, _computeBuffer);
        }
#if UNITY_EDITOR
        public override string ToString()
        {
            return $"propID: {_propertyID}, cb_stride: {_computeBuffer.stride}, cb_capacity: {_computeBuffer.count}, comp: {ComponentType}";
        }
#endif
    }
    #endregion

    /// <summary>
    /// Assuming there is no batching for different materials/textures/other not-instanced properties we can define some kind of render archetypes
    /// <br>Entities within the same <see cref="RenderArchetype"/> rendered together. It achieved by using SCD <see cref="SpriteRenderID"/></br>
    /// <br>To render sprites it uses <see cref="Graphics.DrawMeshInstancedProcedural"/></br>
    /// <br>Each render archetype is a combination of: </br>
    /// <list type="bullet">
    /// <item><description><see cref="Material"/> + <see cref="MaterialPropertyBlock"/> to render sprite entities</description></item>
    /// <item><description>Set of <see cref="InstancedProperty"/> to sync data between compute buffers assigned to <see cref="MaterialPropertyBlock"/> and property components</description></item>
    /// <item>int ID to query entities through <see cref="SpriteRenderID"/></item>
    /// </list>
    /// </summary>
    internal class RenderArchetype : IDisposable
    {
        #region per-chunk jobs
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
        [BurstCompile]
        internal struct CalculateChunksDataJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunks;
            [WriteOnly][NoAlias] public NativeCounter.ParallelWriter chunkCapacityCounter;
            [WriteOnly][NoAlias] public NativeCounter.ParallelWriter createdChunksCapacityCounter;
            [WriteOnly][NoAlias] public NativeCounter.ParallelWriter entityCounter;
            /// this job only used for reading chunks which separated by <see cref="SpriteRenderID"/> SCD
            /// <see cref="PinChunksJob"/>, <see cref="PinListedChunksJob"/> writes to <see cref="PropertyPointerChunk"/>
            /// here we can be sure we're operating on different chunks because of different SCD
            [ReadOnly][NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<PropertyPointerChunk> propertyBufferIndexRange_CTH;
            // don't worry about created/reordered chunks indexes list
            // if there is no new chunks, then there will no work with this list (it is intended to be persistent and resized on need)
            // else we already want to fill this lists, because otherwise it means more unnecessary work, we want to avoid extra chunk iteration
            /// used by <see cref="PinListedChunksJob"/> and <see cref="PinListedEntitiesJob"/>
            [WriteOnly][NoAlias] public NativeList<int>.ParallelWriter newChunksIndexes;
            /// used by <see cref="PinListedEntitiesJob"/>
            [WriteOnly][NoAlias] public NativeList<int>.ParallelWriter reorderedChunksIndexes;
            public uint lastSystemVersion;

            public void Execute(int startIndex, int count)
            {
                var toIndex = startIndex + count;
                for (int chunkIndex = startIndex; chunkIndex < toIndex; chunkIndex++)
                {
                    var chunk = chunks[chunkIndex];
                    var capacity = chunk.Capacity;
                    chunkCapacityCounter.Add(capacity);
                    entityCounter.Add(chunk.Count);
#if UNITY_EDITOR
                    if (!chunk.HasChunkComponent(ref propertyBufferIndexRange_CTH))
                        throw new NSpritesException($"{nameof(RenderArchetype)} has {nameof(PropertyUpdateMode.Reactive)} properties, but chunk has no {nameof(PropertyPointerChunk)}");
#endif
                    var propertyBufferIndexRange = chunk.GetChunkComponentData(ref propertyBufferIndexRange_CTH);
                    /// if <see cref="PropertyPointerChunk.count"/> (which is chunk's capacity) is 0 it means that chunk is newly created
                    if (propertyBufferIndexRange.count == 0)
                    {
                        createdChunksCapacityCounter.Add(capacity);
                        newChunksIndexes.AddNoResize(chunkIndex);
                    }
                    else if (chunk.DidOrderChange(lastSystemVersion))
                        reorderedChunksIndexes.AddNoResize(chunkIndex);
                }
            }
        }

        /// All "Pin" jobs will grab chunk / entity and pin it to compute buffer using <see cref="PropertyPointerChunk"/> for chunk and <see cref="PropertyPointer"/> for entity
        [BurstCompile]
        internal struct PinListedChunksJob : IJob
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunks;
            [WriteOnly][NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH;
            public int startingFromIndex;
            [ReadOnly] public NativeList<int> chunksIndexes;

            public void Execute()
            {
                for (int i = 0; i < chunksIndexes.Length; i++)
                {
                    var chunk = chunks[chunksIndexes[i]];
                    var capacity = chunk.Capacity;
                    chunk.SetChunkComponentData(ref propertyPointerChunk_CTH, new PropertyPointerChunk { from = startingFromIndex, count = capacity });
                    startingFromIndex += capacity;
                }
            }
        }
        [BurstCompile]
        internal struct PinListedEntitiesJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunks;
            [ReadOnly] public NativeList<int> chunkIndexes;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<PropertyPointer> propertyPointer_CTH_WO;
            [ReadOnly] public ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH_RO;

            public void Execute(int startIndex, int count)
            {
                var toIndex = startIndex + count;
                for (int i = startIndex; i < toIndex; i++)
                {
                    var chunk = chunks[chunkIndexes[i]];
                    var chunkPointer = chunk.GetChunkComponentData(ref propertyPointerChunk_CTH_RO);
                    var entityIndexes = chunk.GetNativeArray(ref propertyPointer_CTH_WO);
                    for (int entityIndex = 0; entityIndex < entityIndexes.Length; entityIndex++)
                        entityIndexes[entityIndex] = new PropertyPointer { bufferIndex = chunkPointer.from + entityIndex };
                }
            }
        }
        [BurstCompile]
        internal struct PinChunksJob : IJob
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunks;
            [WriteOnly][NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH;

            public void Execute()
            {
                var index = 0;
                for (int i = 0; i < chunks.Length; i++)
                {
                    var chunk = chunks[i];
                    var capacity = chunk.Capacity;
                    chunk.SetChunkComponentData(ref propertyPointerChunk_CTH, new PropertyPointerChunk { from = index, count = capacity });
                    index += capacity;
                }
            }
        }
        [BurstCompile]
        internal struct PinEntitiesJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunks;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<PropertyPointer> propertyPointer_CTH;
            [ReadOnly] public ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH;

            public void Execute(int startIndex, int count)
            {
                var toIndex = startIndex + count;
                for (int chunkIndex = startIndex; chunkIndex < toIndex; chunkIndex++)
                {
                    var chunk = chunks[chunkIndex];
                    var chunkPointer = chunk.GetChunkComponentData(ref propertyPointerChunk_CTH);
                    var entityIndexes = chunk.GetNativeArray(ref propertyPointer_CTH);
                    for (int entityIndex = 0; entityIndex < entityIndexes.Length; entityIndex++)
                        entityIndexes[entityIndex] = new PropertyPointer { bufferIndex = chunkPointer.from + entityIndex };
                }
            }
        }
#endif
#endregion
        /// id to query entities using <see cref="SpriteRenderID"/>
        internal readonly int _id;

        internal readonly Material _material;
        internal readonly Mesh _mesh;
        internal readonly Bounds _bounds;
        private readonly MaterialPropertyBlock _materialPropertyBlock;

        // minimum additional capacity we want allocate on exceed
        private readonly int _minCapacityStep;

        private int _entityCount;

        /// <summary>
        /// Contains all kind of properties one after another. Properties can be accessed by theirs update mode
        /// using <see cref="_propertiesModeCountAndOffsets"/>. 
        /// Here we use all kinds of update mode: 
        ///     <see cref="PropertyUpdateMode.Reactive"/> /
        ///     <see cref="PropertyUpdateMode.Static"/> /
        ///     <see cref="PropertyUpdateMode.EachUpdate"/>
        /// </summary>
        internal readonly InstancedProperty[] _properties;
        /// used to collect property <see cref="JobHandle"/> during data sync.
        private PropertyHandleCollector _handleCollector;
        /// <summary> Contains count of properties for each <see cref="PropertyUpdateMode"/>. c0 - counts, c1 - offsets</summary>
        private int3x2 _propertiesModeCountAndOffsets;

        // EUP  - Each Update Properties
        // SP   - Static Properties
        // RP   - Reactive Properties

#if !NSPRITES_EACH_UPDATE_DISABLE
        /// <summary> Contains how much space allocated / used / can be used in <see cref="PropertyUpdateMode.EachUpdate"/> and <see cref="PropertyUpdateMode.Static"/> properties</summary>
        private PropertySpaceCounter _perEntityPropertiesSpaceCounter;
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
        private readonly bool _shouldHandlePropertiesByEntity;
#endif
        internal int EUP_Count => _propertiesModeCountAndOffsets.c0.y;
        internal int EUP_Offset => _propertiesModeCountAndOffsets.c1.y;
#endif

#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
        /// each-update property for <see cref="PropertyPointer"/> data. Have this separately from <see cref="_properties"/> to not pass unnecessary handles to each-update properties + let them be disableable
        internal readonly InstancedProperty _pointersProperty;
        /// should archetype work with <see cref="PropertyUpdateMode.Reactive"/> or <see cref="PropertyUpdateMode.Static"/>
#if !NSPRITES_EACH_UPDATE_DISABLE
        /// because such update require working with <see cref="PropertyPointerChunk"/> and <see cref="PropertyPointer"/> data
        private readonly bool _shouldHandlePropertiesByChunk;
#endif
        /// <summary> Contains how much space <b>allocated / used / can be used</b> in <see cref="PropertyUpdateMode.Reactive"/> and <see cref="PropertyUpdateMode.Static"/> properties, because both use chunk iteration</summary>
        internal PropertySpaceCounter _perChunkPropertiesSpaceCounter;
        private ReusableNativeList<int> _createdChunksIndexes_RNL;
        private ReusableNativeList<int> _reorderedChunksIndexes_RNL;
        internal const int MinIndicesPerJobCount = 8;
#endif
#if !NSPRITES_STATIC_DISABLE
        internal int SP_Count => _propertiesModeCountAndOffsets.c0.z;
        internal int SP_Offset => _propertiesModeCountAndOffsets.c1.z;
#endif
#if !NSPRITES_REACTIVE_DISABLE
        /// <see cref="PropertyUpdateMode.Reactive"/> properties from index is always 0, so there is no RP_Offset
        internal int RP_Count => _propertiesModeCountAndOffsets.c0.x;
#endif

        public RenderArchetype(Material material, Mesh mesh, in Bounds bounds, IReadOnlyList<PropertyData> propertyDataSet
            , IReadOnlyDictionary<int, ComponentType> propertyMap, int id
            , MaterialPropertyBlock overrideMPB = null, int preallocatedSpace = 1, int minCapacityStep = 1)
        {
#if UNITY_EDITOR
            if (material == null)
                throw new NSpritesException($"While creating {nameof(RenderArchetype)} ({nameof(id)}: {id}) {nameof(Material)} {nameof(material)} was null passed");
            if (propertyDataSet == null)
                throw new NSpritesException($"While creating {nameof(RenderArchetype)} ({nameof(id)}: {id}) {nameof(IReadOnlyList<PropertyData>)} {nameof(propertyDataSet)} was null passed");
            if (preallocatedSpace < 1)
                throw new NSpritesException($"You're trying to create {nameof(RenderArchetype)} ({nameof(id)}: {id}) with {preallocatedSpace} initial capacity, which can't be below 1");
            if (minCapacityStep < 1)
                throw new NSpritesException($"You're trying to create {nameof(RenderArchetype)} ({nameof(id)}: {id}) with {minCapacityStep} minimum capacity step, which can't be below 1");
#endif
            _id = id;
            _material = material;
            _mesh = mesh;
            _bounds = bounds;
            _materialPropertyBlock = overrideMPB ?? new();
            _minCapacityStep = minCapacityStep;

#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
            _perChunkPropertiesSpaceCounter.capacity = preallocatedSpace;
            _createdChunksIndexes_RNL = new ReusableNativeList<int>(0, Allocator.Persistent);
            _reorderedChunksIndexes_RNL = new ReusableNativeList<int>(0, Allocator.Persistent);
            _pointersProperty = new InstancedProperty(Shader.PropertyToID(PropertyPointer.PropertyName), preallocatedSpace, sizeof(int), ComponentType.ReadOnly<PropertyPointer>());
#endif
#if !NSPRITES_EACH_UPDATE_DISABLE
            _perEntityPropertiesSpaceCounter.capacity = preallocatedSpace;
#endif

            #region initialize properties
            _properties = new InstancedProperty[propertyDataSet.Count];
            var propertiesInternalDataSet = new NativeArray<ComponentType>(_properties.Length, Allocator.Temp);
            /// 1st iteration fetch property data from map and count all types of <see cref="PropertyUpdateMode">
            for (int propIndex = 0; propIndex < _properties.Length; propIndex++)
            {
                var propData = propertyDataSet[propIndex];
#if UNITY_EDITOR
                if (!propertyMap.ContainsKey(propData.propertyID))
                    throw new NSpritesException($"There is no data in map for {propData.propertyID} shader's property ID. You can look at Window -> Entities -> NSprites to see what properties registered at a time.");
#endif
                var propInternalData = propertyMap[propData.propertyID];
                propertiesInternalDataSet[propIndex] = propInternalData;
                _propertiesModeCountAndOffsets.c0[(int)propData.updateMode]++;
            }
            /// 2nd iteration initialize <see cref="_properties"> with known indexes offsets
            _propertiesModeCountAndOffsets.c1 = new int3(0, _propertiesModeCountAndOffsets.c0.x, _propertiesModeCountAndOffsets.c0.x + _propertiesModeCountAndOffsets.c0.y);
            var offsets = _propertiesModeCountAndOffsets.c1; // copy to be able to increment
            for (int propIndex = 0; propIndex < _properties.Length; propIndex++)
            {
                var propData = propertyDataSet[propIndex];
                var propType = propertiesInternalDataSet[propIndex];
                var prop = new InstancedProperty(propData.propertyID, preallocatedSpace, UnsafeUtility.SizeOf(propType.GetManagedType()), propType);
                _properties[offsets[(int)propData.updateMode]++] = prop;
            }
#if !NSPRITES_EACH_UPDATE_DISABLE && (!NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE)
            /// we want update properties by chunk only if archetype has any <see cref="PropertyUpdateMode.Reactive"/> or <see cref="PropertyUpdateMode.Static"/> properties
            _shouldHandlePropertiesByChunk = _propertiesModeCountAndOffsets.c0.x != 0 || _propertiesModeCountAndOffsets.c0.z != 0;
#endif
            var conditionalCount =
#if NSPRITES_STATIC_DISABLE
            0;
#else
            SP_Count;
#endif
            var handleCollectorCapacity = _properties.Length;
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
            /// +1 for <see cref="_pointersProperty"/>
            handleCollectorCapacity++;
#endif
            _handleCollector = new(handleCollectorCapacity, conditionalCount, Allocator.Persistent);

#if !NSPRITES_EACH_UPDATE_DISABLE && (!NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE)
            /// we want update properties by entity only if archetype has any <see cref="PropertyUpdateMode.EachUpdate"/> properties
            _shouldHandlePropertiesByEntity = EUP_Count != 0;
#endif
#endregion
        }
        public JobHandle ScheduleUpdate(in SystemData systemData, ref SystemState systemState)
        {
            // we need to use this method every new frame, because query somehow gets invalidated
            var query = systemData.query;
            query.SetSharedComponentFilter(new SpriteRenderID() { id = _id });

            _handleCollector.Reset();

            #region handle reactive / static properties
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
            NativeArray<ArchetypeChunk> chunksArray = default;
#if !NSPRITES_EACH_UPDATE_DISABLE
            if (_shouldHandlePropertiesByChunk)
            {
#endif
#region chunk gather data
                // TODO: try to avoid sync point
                chunksArray = query.ToArchetypeChunkArray(Allocator.TempJob);

                var overallCapacityCounter = new NativeCounter(Allocator.TempJob);
                var createdChunksCapacityCounter = new NativeCounter(Allocator.TempJob);
                var entityCounter = new NativeCounter(Allocator.TempJob);

                // this lists will be reused every frame, which means less allocations for us, but those use Persistent allocation
                var createdChunksIndexes = _createdChunksIndexes_RNL.GetList(chunksArray.Length);
                var reorderedChunksIndexes = _reorderedChunksIndexes_RNL.GetList(chunksArray.Length);

                // here we collecting how much we have entity/chunk's overall capacity/new chunk's capacity
                var calculateChunksDataHandle = new CalculateChunksDataJob
                {
                    chunks = chunksArray,
                    chunkCapacityCounter = overallCapacityCounter,
                    createdChunksCapacityCounter = createdChunksCapacityCounter,
                    entityCounter = entityCounter,
                    propertyBufferIndexRange_CTH = systemData.propertyPointerChunk_CTH_RO,
                    newChunksIndexes = createdChunksIndexes.AsParallelWriter(),
                    reorderedChunksIndexes = reorderedChunksIndexes.AsParallelWriter(),
                    lastSystemVersion = systemData.lastSystemVersion
                }.ScheduleBatch(chunksArray.Length, MinIndicesPerJobCount);
                // we want to complete calculation job because next main thread logic depends on it
                // since only we do is just counting some data we have no need to chain inputDeps here
                calculateChunksDataHandle.Complete();

                _entityCount = entityCounter.Count;
#endregion

#region update capacity, sync data
                var neededOverallCapacity = overallCapacityCounter.Count;
                var createdChunksCapacity = createdChunksCapacityCounter.Count;
                var overallCapacityExceeded = neededOverallCapacity > _perChunkPropertiesSpaceCounter.capacity;
                // if overall capacity exceeds current capacity OR
                // there is new chunks and theirs sum capacity exceeds free space we have currently
                // then we need to reallocate all per-property compute buffers and reassign chunks / entities indexes
                if (overallCapacityExceeded || createdChunksCapacity > _perChunkPropertiesSpaceCounter.UnusedCapacity)
                {
                    // reassign all chunk's / entity's indexes
                    // this job will iterate through chunks one by one and increase theirs indexes
                    // execution can't be parallel because calculation is dependent, so this is weakest part (actually could)
                    // this part goes before buffer's reallocation because it is just work with indexes we already now
                    var pinHandle = new PinChunksJob
                    {
                        chunks = chunksArray,
                        propertyPointerChunk_CTH = systemData.propertyPointerChunk_CTH_RW
                    }.Schedule();
                    pinHandle = new PinEntitiesJob
                    {
                        chunks = chunksArray,
                        propertyPointerChunk_CTH = systemData.propertyPointerChunk_CTH_RO,
                        propertyPointer_CTH = systemData.propertyPointer_CTH_RW
                    }.ScheduleBatch(chunksArray.Length, MinIndicesPerJobCount, pinHandle);

                    // from this point we should pass inputDeps, because next we work with components which might be touched outside
                    var preReadDependency = JobHandle.CombineDependencies(pinHandle, systemData.inputDeps);

                    #region local methods
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    void ScheduleLoadAllChunkData(InstancedProperty property, in int propIndex, in ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH_RO, DynamicComponentTypeHandle property_DCTH, in JobHandle inputDeps)
                    {
                        var handle = property.LoadAllChunkData(chunksArray, propertyPointerChunk_CTH_RO, property_DCTH, _perChunkPropertiesSpaceCounter.count, inputDeps);
                        _handleCollector.Add(handle, propIndex);
                    }
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    void ReallocateAndReloadData(int fromPropIndex, int toPropIndex, in SystemData systemData, ref SystemState systemState)
                    {
                        for (int propIndex = fromPropIndex; propIndex < toPropIndex; propIndex++)
                        {
                            var property = _properties[propIndex];
                            property.Reallocate(_perChunkPropertiesSpaceCounter.capacity, _materialPropertyBlock);
                            ScheduleLoadAllChunkData(property, propIndex, systemData.propertyPointerChunk_CTH_RO, systemState.GetDynamicComponentTypeHandle(property.ComponentType), preReadDependency);
                        }
                    }
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    void ReloadData(int fromPropIndex, int toPropIndex, in SystemData systemData, ref SystemState systemState)
                    {
                        for (int propIndex = fromPropIndex; propIndex < toPropIndex; propIndex++)
                        {
                            var prop = _properties[propIndex];
                            ScheduleLoadAllChunkData(prop, propIndex, systemData.propertyPointerChunk_CTH_RO, systemState.GetDynamicComponentTypeHandle(prop.ComponentType), preReadDependency);
                        }
                    }
                    #endregion

                    // set to true because here we will update static properties, which are conditional
                    _handleCollector.includeConditional = true;

                    // since here we fully reallocate our buffers we can retrieve reactive/static buffers count as needed capacity
                    _perChunkPropertiesSpaceCounter.count = neededOverallCapacity;

                    /// if we have overall capacity exceed then we need to extend our buffers
                    /// so do reallocate + reload all data for <see cref="PropertyUpdateMode.Reactive"/> and <see cref="PropertyUpdateMode.Static"/> props
                    if (overallCapacityExceeded)
                    {
                        // reallocate compute buffers
                        // here we calculate new capacity no matter what the reason was to reallocate buffers
                        // new capacity depends on how much new space we need, but this space jump can't be lower then min capacity step
                        _perChunkPropertiesSpaceCounter.capacity += math.max(_minCapacityStep, neededOverallCapacity - _perChunkPropertiesSpaceCounter.capacity);
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
                        /// reallocate and load all <see cref="PropertyPointer"/> data
                        _pointersProperty.Reallocate(_perChunkPropertiesSpaceCounter.capacity, _materialPropertyBlock);
                        ScheduleLoadAllQueryData(_pointersProperty, _handleCollector.propertyPointerIndex, systemState.GetDynamicComponentTypeHandle(_pointersProperty.ComponentType), query, preReadDependency);
#endif
#if !NSPRITES_REACTIVE_DISABLE
                        ReallocateAndReloadData(0, RP_Count, systemData, ref systemState);
#endif
#if !NSPRITES_STATIC_DISABLE
                        ReallocateAndReloadData(SP_Offset, SP_Offset + SP_Count, systemData, ref systemState);
#endif
                    }
                    // if there is no exceed then just reload all data without any reallocation
                    else
                    {
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
                        /// load all <see cref="PropertyPointer"/> data
                        ScheduleLoadAllQueryData(_pointersProperty, _handleCollector.propertyPointerIndex, systemState.GetDynamicComponentTypeHandle(_pointersProperty.ComponentType), query, preReadDependency);
#endif
#if !NSPRITES_REACTIVE_DISABLE
                        ReloadData(0, RP_Count, systemData, ref systemState);
#endif
#if !NSPRITES_STATIC_DISABLE
                        ReloadData(SP_Offset, SP_Offset + SP_Count, systemData, ref systemState);
#endif
                    }
                }
                // here we can relax because we have enough capacity
                else
                {
                    /// process chunks which ONLY got reordered NOT new chunks, which stored separately
                    /// here we want to reassign entities's indexes for chunks which have got/losed entity
                    /// since that is the only job which can write to reordered chunk's <see cref="PropertyPointer"/> we can safely schedule it independently
                    var pinHandle = new PinListedEntitiesJob
                    {
                        chunks = chunksArray,
                        chunkIndexes = reorderedChunksIndexes,
                        propertyPointerChunk_CTH_RO = systemData.propertyPointerChunk_CTH_RO,
                        propertyPointer_CTH_WO = systemData.propertyPointer_CTH_RW
                    }.ScheduleBatch(reorderedChunksIndexes.Length, MinIndicesPerJobCount);

                    // if we haven't reallocated buffers it means that we have enough space for all chunks we have by now
                    // so we can assign all indexes to new chunks if any and assign indexes for new/reordered chunk's entities
                    if (createdChunksCapacity > 0)
                    {
                        /// assign new chunks indexes starting from previous count
                        /// since that is only job wich can write to created chunk's <see cref="PropertyPointer"/> we can safely schedule it independently
                        var createdChunksPinHandle = new PinListedChunksJob
                        {
                            chunks = chunksArray,
                            propertyPointerChunk_CTH = systemData.propertyPointerChunk_CTH_RW,
                            chunksIndexes = createdChunksIndexes,
                            startingFromIndex = _perChunkPropertiesSpaceCounter.count
                        }.Schedule();

                        // don't forget to update count with new chunks capacity even if we have enough space
                        _perChunkPropertiesSpaceCounter.count += createdChunksCapacity;

                        createdChunksPinHandle = new PinListedEntitiesJob
                        {
                            chunks = chunksArray,
                            chunkIndexes = createdChunksIndexes,
                            propertyPointerChunk_CTH_RO = systemData.propertyPointerChunk_CTH_RO,
                            propertyPointer_CTH_WO = systemData.propertyPointer_CTH_RW
                        }.ScheduleBatch(createdChunksIndexes.Length, MinIndicesPerJobCount, createdChunksPinHandle);

                        pinHandle = JobHandle.CombineDependencies(pinHandle, createdChunksPinHandle);
                    }

                    // from this point we should pass inputDeps, because next we work with components which might be touched outside
                    var preReadDependency = JobHandle.CombineDependencies(systemData.inputDeps, pinHandle);

                    /// finally because there was no need to reallocate buffers we can: 
                    /// 0. just update changed (<see cref="ArchetypeChunk.DidOrderChange(uint)"/> OR <see cref="ArchetypeChunk.DidChange(DynamicComponentTypeHandle, uint)"/>) <see cref="PropertyUpdateMode.Reactive"/> props
#if !NSPRITES_REACTIVE_DISABLE
                    for (int propIndex = 0; propIndex < RP_Count; propIndex++)
                    {
                        var property = _properties[propIndex];
                        var handle = property.UpdateOnChangeChunkData
                        (
                            chunksArray,
                            systemData.propertyPointerChunk_CTH_RO,
                            systemState.GetDynamicComponentTypeHandle(property.ComponentType),
                            _perChunkPropertiesSpaceCounter.count,
                            systemData.lastSystemVersion,
                            preReadDependency
                        );
                        _handleCollector.Add(handle, propIndex);
                    }
#endif
#if !NSPRITES_STATIC_DISABLE
                    /// 1. if there is any reordered / created chunks then just update <see cref="createdChunksIndexes"/> AND <see cref="reorderedChunksIndexes"/> chunks
                    /// for each such property we want to schedule job per each list of indexes, so we need to combine dependecies every iteration
                    if (reorderedChunksIndexes.Length > 0 || createdChunksIndexes.Length > 0)
                    {
                        // set to true because here we will update static properties, which are conditional
                        _handleCollector.includeConditional = true;

                        for (int propIndex = SP_Offset; propIndex < SP_Offset + SP_Count; propIndex++)
                        {
                            var property = _properties[propIndex];
                            var handle = property.UpdateCreatedAndReorderedChunkData
                            (
                                chunksArray,
                                reorderedChunksIndexes,
                                createdChunksIndexes,
                                systemData.propertyPointerChunk_CTH_RO,
                                systemState.GetDynamicComponentTypeHandle(property.ComponentType),
                                _perChunkPropertiesSpaceCounter.count,
                                preReadDependency
                            );
                            _handleCollector.Add(handle, propIndex);
                        }
                    }
#endif
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
                    /// load all <see cref="PropertyPointer"/> data
                    ScheduleLoadAllQueryData(_pointersProperty, _handleCollector.propertyPointerIndex, systemState.GetDynamicComponentTypeHandle(_pointersProperty.ComponentType), query, preReadDependency);
#endif
                }
                #endregion

                #region dispose containers
                overallCapacityCounter.Dispose();
                createdChunksCapacityCounter.Dispose();
                entityCounter.Dispose();
            #endregion
#if !NSPRITES_EACH_UPDATE_DISABLE
            }
            // if we have no reactive properties, then calculate entities count as always
            else
                _entityCount = query.CalculateEntityCount();
#endif
#endif
            #endregion

            #region handle each-update
#if !NSPRITES_EACH_UPDATE_DISABLE
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
            /// check if we have <see cref="PropertyUpdateMode.EachUpdate"/> properties to update
            if (_shouldHandlePropertiesByEntity)
            {
#endif
#if NSPRITES_REACTIVE_DISABLE && NSPRITES_STATIC_DISABLE
                // calculate entity count before any actions if reactive and static properties code disabled
                _entityCount = query.CalculateEntityCount();
#endif
                /// reallocate buffers if capacity excedeed by <see cref="_minCapacityStep"/> and load all data for each-update and static properties
                if (_perEntityPropertiesSpaceCounter.capacity < _entityCount)
                {
                    _perEntityPropertiesSpaceCounter.capacity = (int)math.ceil((float)_entityCount / _minCapacityStep);
                    /// reallocate and reload all data for <see cref="PropertyUpdateMode.EachUpdate"/> properties
                    for (int propIndex = EUP_Offset; propIndex < EUP_Offset + EUP_Count; propIndex++)
                    {
                        var property = _properties[propIndex];
                        property.Reallocate(_perEntityPropertiesSpaceCounter.capacity, _materialPropertyBlock);
                        ScheduleLoadAllQueryData(property, propIndex, systemState.GetDynamicComponentTypeHandle(property.ComponentType), query, systemData.inputDeps);
                    }
                }
                /// if there was no exceed just load all <see cref="PropertyUpdateMode.EachUpdate"/> properties data
                else
                    for (int propIndex = EUP_Offset; propIndex < EUP_Offset + EUP_Count; propIndex++)
                    {
                        var property = _properties[propIndex];
                        ScheduleLoadAllQueryData(property, propIndex, systemState.GetDynamicComponentTypeHandle(property.ComponentType), query, systemData.inputDeps);
                    }

#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
            }
#endif

            _perEntityPropertiesSpaceCounter.count = _entityCount;
#endif
            #endregion

            var outputHandle = _handleCollector.GetCombined();
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
#if !NSPRITES_EACH_UPDATE_DISABLE
            if (_shouldHandlePropertiesByChunk)
#endif
            chunksArray.Dispose(outputHandle);
#endif
            return outputHandle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ScheduleLoadAllQueryData(InstancedProperty property, in int propIndex, in DynamicComponentTypeHandle property_DCTH, in EntityQuery query, in JobHandle inputDeps)
        {
            _handleCollector.Add(property.LoadAllQueryData(query, property_DCTH, _entityCount, inputDeps), propIndex);
        }
        /// <summary>Forces complete all properties update jobs. Call it after <see cref="ScheduleUpdate"/> and before <see cref="Draw"/> method to ensure all data is updated.</summary>
        private void CompleteUpdate()
        {
#if !NSPRITES_REACTIVE_DISABLE
            /// complete <see cref="PropertyUpdateMode.Reactive"/> properties
            for (int propIndex = 0; propIndex < RP_Count; propIndex++)
            {
                _handleCollector[propIndex].Complete();
                _properties[propIndex].EndWrite(_perChunkPropertiesSpaceCounter.count);
            }
#endif
#if !NSPRITES_EACH_UPDATE_DISABLE
            /// complete <see cref="PropertyUpdateMode.EachUpdate"/> properties
            for (int propIndex = EUP_Offset; propIndex < EUP_Offset + EUP_Count; propIndex++)
            {
                _handleCollector[propIndex].Complete();
                _properties[propIndex].EndWrite(_perEntityPropertiesSpaceCounter.count);
            }
#endif
#if !NSPRITES_STATIC_DISABLE
            /// if there was update for <see cref="PropertyUpdateMode.Static"/> properties complete them
            if (_handleCollector.includeConditional)
            {
                for (int propIndex = SP_Offset; propIndex < SP_Offset + SP_Count; propIndex++)
                {
                    _handleCollector[propIndex].Complete();
                    _properties[propIndex].EndWrite(_perChunkPropertiesSpaceCounter.count);
                }
            }
#endif
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
            _pointersProperty.EndWrite(_entityCount);
            _handleCollector[_handleCollector.propertyPointerIndex].Complete();
#endif
        }
        /// <summary>Draws instances in quantity based on the number of entities related to this <see cref="RenderArchetype"/>. Call it after <see cref="ScheduleUpdate"/> and <see cref="CompleteUpdate"/>.</summary>
        private void Draw()
        {
            if(_entityCount != 0)
                Graphics.DrawMeshInstancedProcedural(_mesh, 0, _material, _bounds, _entityCount, _materialPropertyBlock);
        }
        
        /// <summary><inheritdoc cref="CompleteUpdate"/>.
        /// Then <inheritdoc cref="Draw"/>.</summary>
        public void CompleteAndDraw()
        {
            CompleteUpdate();
            Draw();
        }

        public void Dispose()
        {
            for (var propIndex = 0; propIndex < _properties.Length; propIndex++)
                _properties[propIndex]._computeBuffer.Release();
            _handleCollector.Dispose();
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
            _pointersProperty._computeBuffer.Release();
            _createdChunksIndexes_RNL.Dispose();
            _reorderedChunksIndexes_RNL.Dispose();
#endif
        }
    }
}