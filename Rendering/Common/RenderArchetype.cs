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
        internal readonly int PropertyID;
        internal readonly PropertyUpdateMode UpdateMode;

        public PropertyData(int propertyID, in PropertyUpdateMode updateMode = default)
        {
            PropertyID = propertyID;
            UpdateMode = NSpritesUtils.GetActualMode(updateMode);
        }

        public PropertyData(in string propertyName, in PropertyUpdateMode updateMode = default)
            : this(Shader.PropertyToID(propertyName), updateMode) { }

        public static implicit operator PropertyData(in string propertyName) => new(propertyName);
    }
    internal struct SystemData
    {
        public EntityQuery Query;
        public JobHandle InputDeps;
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
        public ComponentTypeHandle<PropertyPointerChunk> PropertyPointerChunk_CTH_RW;
        public ComponentTypeHandle<PropertyPointer> PropertyPointer_CTH_RW;
        public ComponentTypeHandle<PropertyPointerChunk> PropertyPointerChunk_CTH_RO;
        public uint LastSystemVersion;
#endif
    }
    internal struct PropertySpaceCounter
    {
        // how much space property has
        public int Capacity;
        // how much space property use
        public int Count;
        // how much more space property can use
        public int UnusedCapacity => Capacity - Count;
    }
    
    // TODO: rework sequence of properties handles in a way that SP...RP...EUP
    internal struct PropertyHandleCollector : IDisposable
    {
        private NativeArray<JobHandle> _handles;
        private readonly int _conditionalOffset;
        public bool IncludeConditional;
        public readonly int PropertyPointerIndex;

        public JobHandle this[int i] => _handles[i];

        public PropertyHandleCollector(in int capacity, in int conditionalCount, in Allocator allocator)
        {
            _handles = new NativeArray<JobHandle>(capacity, allocator);
            _conditionalOffset = capacity - conditionalCount - 1;
            IncludeConditional = false;
            PropertyPointerIndex = capacity - 1;
        }

        public void Add(in JobHandle handle, in int index) => _handles[index] = handle;
        public JobHandle GetCombined()
        {
            if (IncludeConditional)
                return JobHandle.CombineDependencies(_handles);
            
            var handle = JobHandle.CombineDependencies(new NativeSlice<JobHandle>(_handles, 0, _conditionalOffset));
            return JobHandle.CombineDependencies(handle, _handles[PropertyPointerIndex]);

        }

        public void Reset() => IncludeConditional = false;

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
    #region reactive / static properties jobs
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
    /// <summary> Takes all chunks, read theirs <see cref="PropertyPointerChunk"/> and copy component data to compute buffer starting from chunk's range from value </summary>
    [BurstCompile]
    internal struct SyncPropertyByChunkJob : IJobParallelForBatch
    {
        [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
        // this should be filled every frame with GetDynamicComponentTypeHandle
        [ReadOnly] public DynamicComponentTypeHandle ComponentTypeHandle;
        [ReadOnly] public int TypeSize;
        [ReadOnly] public ComponentTypeHandle<PropertyPointerChunk> PropertyPointerChunk_CTH;
        [WriteOnly][NativeDisableParallelForRestriction] public NativeArray<byte> OutputArray;

        public void Execute(int startIndex, int count)
        {
            var toIndex = startIndex + count;
            for (int chunkIndex = startIndex; chunkIndex < toIndex; chunkIndex++)
            {
                var chunk = Chunks[chunkIndex];
                SyncPropertyByQueryJob.WriteData(chunk, ref ComponentTypeHandle, chunk.GetChunkComponentData(ref PropertyPointerChunk_CTH).from, OutputArray, TypeSize);
            }
        }
    }
    /// <summary> Takes changed / reordered chunks, read theirs <see cref="PropertyPointerChunk"/> and copy component data to compute buffer starting from chunk's range from value </summary>
    [BurstCompile]
    internal struct SyncPropertyByChangedChunkJob : IJobParallelForBatch
    {
        [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
        // this should be filled every frame with GetDynamicComponentTypeHandle
        [ReadOnly] public DynamicComponentTypeHandle ComponentTypeHandle;
        [ReadOnly] public uint LastSystemVersion;
        [ReadOnly] public int TypeSize;
        [ReadOnly] public ComponentTypeHandle<PropertyPointerChunk> PropertyPointerChunk_CTH;
        [WriteOnly][NativeDisableParallelForRestriction] public NativeArray<byte> OutputArray;

        public void Execute(int startIndex, int count)
        {
            var toIndex = startIndex + count;
            for (int chunkIndex = startIndex; chunkIndex < toIndex; chunkIndex++)
            {
                var chunk = Chunks[chunkIndex];

                // if chunk has no data changes AND has no new / lost entities then do nothing
                if (!chunk.DidChange(ref ComponentTypeHandle, LastSystemVersion) && !chunk.DidOrderChange(LastSystemVersion))
                    continue;

                SyncPropertyByQueryJob.WriteData(chunk, ref ComponentTypeHandle, chunk.GetChunkComponentData(ref PropertyPointerChunk_CTH).from, OutputArray, TypeSize);
            }
        }
    }
#endif
#if !NSPRITES_STATIC_DISABLE
    /// <summary> Takes listed chunks, read theirs <see cref="PropertyPointerChunk"/> and copy component data to compute buffer starting from chunk's range from value </summary>
    [BurstCompile]
    internal struct SyncPropertyByListedChunkJob : IJobParallelForBatch
    {
        [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
        [ReadOnly] public NativeList<int> ChunkIndexes;
        // this should be filled every frame with GetDynamicComponentTypeHandle
        [ReadOnly] public DynamicComponentTypeHandle ComponentTypeHandle;
        public int TypeSize;
        [ReadOnly] public ComponentTypeHandle<PropertyPointerChunk> PropertyPointerChunk_CTH;
        [WriteOnly][NativeDisableParallelForRestriction][NativeDisableContainerSafetyRestriction] public NativeArray<byte> OutputArray;

        public void Execute(int startIndex, int count)
        {
            var toIndex = startIndex + count;
            for (int i = startIndex; i < toIndex; i++)
            {
                var chunk = Chunks[ChunkIndexes[i]];
                SyncPropertyByQueryJob.WriteData(chunk, ref ComponentTypeHandle, chunk.GetChunkComponentData(ref PropertyPointerChunk_CTH).from, OutputArray, TypeSize);
            }
        }
    }
#endif
    #endregion
    #region each-update (+ for reactive/static) properties jobs
    /// <summary> Takes all chunks through query, and then just copy component data to compute buffer starting from 1st entity in query index </summary>
    [BurstCompile]
    internal struct SyncPropertyByQueryJob : IJobChunk
    {
        [ReadOnly][DeallocateOnJobCompletion] public NativeArray<int> ChunkBaseEntityIndices;
        // this should be filled every frame with GetDynamicComponentTypeHandle
        [ReadOnly]public DynamicComponentTypeHandle ComponentTypeHandle;
        public int TypeSize;
        [WriteOnly][NativeDisableParallelForRestriction] public NativeArray<byte> OutputArray;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            WriteData(chunk, ref ComponentTypeHandle, ChunkBaseEntityIndices[unfilteredChunkIndex], OutputArray, TypeSize);
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
        /// <summary> Property id from <see cref="Shader.PropertyToID"/> to be able to pass to <see cref="MaterialPropertyBlock"/> </summary>
        internal readonly int PropertyID;
        /// <summary> Buffer which synced with entities components data </summary>
        internal ComputeBuffer ComputeBuffer;

        private int TypeSize => ComputeBuffer.stride;

        /// <summary> Cached component type + system to retrieve <see cref="DynamicComponentTypeHandle"/> </summary>
        public ComponentType ComponentType { get; }

        public InstancedProperty(in int propertyID, in int count, in int stride, in ComponentType componentType)
        {
            PropertyID = propertyID;
            ComponentType = componentType;

            ComputeBuffer = new ComputeBuffer(count, stride, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
        }
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
        public JobHandle LoadAllChunkData(in NativeArray<ArchetypeChunk> chunks, in ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH, in DynamicComponentTypeHandle componentTypeHandle, in int writeCount, in JobHandle inputDeps)
        {
            return new SyncPropertyByChunkJob
            {
                Chunks = chunks,
                ComponentTypeHandle = componentTypeHandle,
                TypeSize = TypeSize,
                PropertyPointerChunk_CTH = propertyPointerChunk_CTH,
                OutputArray = GetBufferArray(writeCount)
            }.ScheduleBatch(chunks.Length, RenderArchetype.MinIndicesPerJobCount, inputDeps);
        }
        public JobHandle UpdateOnChangeChunkData(in NativeArray<ArchetypeChunk> chunks, in ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH, in DynamicComponentTypeHandle componentTypeHandle, in int writeCount, in uint lastSystemVersion, in JobHandle inputDeps)
        {
            return new SyncPropertyByChangedChunkJob
            {
                Chunks = chunks,
                ComponentTypeHandle = componentTypeHandle,
                TypeSize = TypeSize,
                PropertyPointerChunk_CTH = propertyPointerChunk_CTH,
                LastSystemVersion = lastSystemVersion,
                OutputArray = GetBufferArray(writeCount)
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
                    Chunks = chunks,
                    ChunkIndexes = chunkIndexes,
                    TypeSize = TypeSize,
                    PropertyPointerChunk_CTH = propertyPointerChunk_CTH,
                    ComponentTypeHandle = componentTypeHandle,
                    OutputArray = writeArray
                }.ScheduleBatch(chunkIndexes.Length, RenderArchetype.MinIndicesPerJobCount, inputDeps)
                : inputDeps;
        }
#endif
        public JobHandle LoadAllQueryData(in EntityQuery query, in DynamicComponentTypeHandle componentTypeHandle, in int writeCount, in JobHandle inputDeps)
        {
            var updatePropertyJob = new SyncPropertyByQueryJob
            {
                ComponentTypeHandle = componentTypeHandle,
                ChunkBaseEntityIndices = query.CalculateBaseEntityIndexArrayAsync(Allocator.TempJob, default, out var calculateChunkBaseIndices),
                TypeSize = TypeSize,
                OutputArray = GetBufferArray(writeCount)
            };
            return updatePropertyJob.ScheduleParallelByRef(query, JobHandle.CombineDependencies(inputDeps, calculateChunkBaseIndices));
        }
        private NativeArray<byte> GetBufferArray(int writeCount) => ComputeBuffer.BeginWrite<byte>(0, writeCount * TypeSize);
        public void EndWrite(in int writeCount) => ComputeBuffer.EndWrite<byte>(writeCount * TypeSize);

        public void Reallocate(in int size, MaterialPropertyBlock materialPropertyBlock)
        {
            var stride = ComputeBuffer.stride;
            ComputeBuffer.Release();
            ComputeBuffer = new ComputeBuffer(size, stride, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
            materialPropertyBlock.SetBuffer(PropertyID, ComputeBuffer);
        }
#if UNITY_EDITOR
        public override string ToString()
        {
            return $"propID: {PropertyID}, cb_stride: {ComputeBuffer.stride}, cb_capacity: {ComputeBuffer.count}, comp: {ComponentType}";
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
    /// <item><description><see cref="UnityEngine.Material"/> + <see cref="MaterialPropertyBlock"/> to render sprite entities</description></item>
    /// <item><description>Set of <see cref="InstancedProperty"/> to sync data between compute buffers assigned to <see cref="MaterialPropertyBlock"/> and property components</description></item>
    /// <item>int ID to query entities through <see cref="SpriteRenderID"/></item>
    /// </list>
    /// </summary>
    internal class RenderArchetype : IDisposable
    {
        #region per-chunk jobs
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
        [BurstCompile]
        private struct CalculateChunksDataJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
            [WriteOnly][NoAlias] public NativeCounter.ParallelWriter ChunkCapacityCounter;
            [WriteOnly][NoAlias] public NativeCounter.ParallelWriter CreatedChunksCapacityCounter;
            [WriteOnly][NoAlias] public NativeCounter.ParallelWriter EntityCounter;
            /// this job only used for reading chunks which separated by <see cref="SpriteRenderID"/> SCD
            /// <see cref="PinChunksJob"/>, <see cref="PinListedChunksJob"/> writes to <see cref="PropertyPointerChunk"/>
            /// here we can be sure we're operating on different chunks because of different SCD
            [ReadOnly][NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<PropertyPointerChunk> PropertyBufferIndexRange_CTH;
            // don't worry about created / reordered chunks indices list
            // if there is no new chunks, then there will no work with this list (it is intended to be persistent and resized on need)
            // else we already want to fill this lists, because otherwise it means more unnecessary work, we want to avoid extra chunk iteration
            /// used by <see cref="PinListedChunksJob"/> and <see cref="PinListedEntitiesJob"/>
            [WriteOnly][NoAlias] public NativeList<int>.ParallelWriter NewChunksIndexes;
            /// used by <see cref="PinListedEntitiesJob"/>
            [WriteOnly][NoAlias] public NativeList<int>.ParallelWriter ReorderedChunksIndexes;
            public uint LastSystemVersion;

            public void Execute(int startIndex, int count)
            {
                var toIndex = startIndex + count;
                for (int chunkIndex = startIndex; chunkIndex < toIndex; chunkIndex++)
                {
                    var chunk = Chunks[chunkIndex];
                    var capacity = chunk.Capacity;
                    ChunkCapacityCounter.Add(capacity);
                    EntityCounter.Add(chunk.Count);
#if UNITY_EDITOR
                    if (!chunk.HasChunkComponent(ref PropertyBufferIndexRange_CTH))
                        throw new NSpritesException($"{nameof(RenderArchetype)} has {nameof(PropertyUpdateMode.Reactive)} properties, but chunk has no {nameof(PropertyPointerChunk)}");
#endif
                    var propertyBufferIndexRange = chunk.GetChunkComponentData(ref PropertyBufferIndexRange_CTH);
                    // if PropertyPointerChunk's count (which is chunk's capacity) is 0 then chunk is newly created
                    if (propertyBufferIndexRange.count == 0)
                    {
                        CreatedChunksCapacityCounter.Add(capacity);
                        NewChunksIndexes.AddNoResize(chunkIndex);
                    }
                    else if (chunk.DidOrderChange(LastSystemVersion))
                        ReorderedChunksIndexes.AddNoResize(chunkIndex);
                }
            }
        }

        // All "Pin" jobs grab chunk / entity and pin it to compute buffer using PropertyPointerChunk for chunk and PropertyPointer for entity
        [BurstCompile]
        private struct PinListedChunksJob : IJob
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
            [WriteOnly][NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<PropertyPointerChunk> PropertyPointerChunk_CTH;
            public int StartingFromIndex;
            [ReadOnly] public NativeList<int> ChunksIndexes;

            public void Execute()
            {
                for (var i = 0; i < ChunksIndexes.Length; i++)
                {
                    var chunk = Chunks[ChunksIndexes[i]];
                    var capacity = chunk.Capacity;
                    chunk.SetChunkComponentData(ref PropertyPointerChunk_CTH, new PropertyPointerChunk { from = StartingFromIndex, count = capacity });
                    StartingFromIndex += capacity;
                }
            }
        }
        [BurstCompile]
        private struct PinListedEntitiesJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
            [ReadOnly] public NativeList<int> ChunkIndexes;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<PropertyPointer> PropertyPointer_CTH_Wo;
            [ReadOnly] public ComponentTypeHandle<PropertyPointerChunk> PropertyPointerChunk_CTH_RO;

            public void Execute(int startIndex, int count)
            {
                var toIndex = startIndex + count;
                for (var i = startIndex; i < toIndex; i++)
                {
                    var chunk = Chunks[ChunkIndexes[i]];
                    var chunkPointer = chunk.GetChunkComponentData(ref PropertyPointerChunk_CTH_RO);
                    var entityIndexes = chunk.GetNativeArray(ref PropertyPointer_CTH_Wo);
                    for (var entityIndex = 0; entityIndex < entityIndexes.Length; entityIndex++)
                        entityIndexes[entityIndex] = new PropertyPointer { bufferIndex = chunkPointer.from + entityIndex };
                }
            }
        }
        [BurstCompile]
        private struct PinChunksJob : IJob
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
            [WriteOnly][NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<PropertyPointerChunk> PropertyPointerChunk_CTH;

            public void Execute()
            {
                var index = 0;
                for (var i = 0; i < Chunks.Length; i++)
                {
                    var chunk = Chunks[i];
                    var capacity = chunk.Capacity;
                    chunk.SetChunkComponentData(ref PropertyPointerChunk_CTH, new PropertyPointerChunk { from = index, count = capacity });
                    index += capacity;
                }
            }
        }
        [BurstCompile]
        private struct PinEntitiesJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<PropertyPointer> PropertyPointer_CTH;
            [ReadOnly] public ComponentTypeHandle<PropertyPointerChunk> PropertyPointerChunk_CTH;

            public void Execute(int startIndex, int count)
            {
                var toIndex = startIndex + count;
                for (var chunkIndex = startIndex; chunkIndex < toIndex; chunkIndex++)
                {
                    var chunk = Chunks[chunkIndex];
                    var chunkPointer = chunk.GetChunkComponentData(ref PropertyPointerChunk_CTH);
                    var entityIndexes = chunk.GetNativeArray(ref PropertyPointer_CTH);
                    for (var entityIndex = 0; entityIndex < entityIndexes.Length; entityIndex++)
                        entityIndexes[entityIndex] = new PropertyPointer { bufferIndex = chunkPointer.from + entityIndex };
                }
            }
        }
#endif
#endregion
        /// id to query entities using <see cref="SpriteRenderID"/>
        internal readonly int ID;

        internal readonly Material Material;
        private readonly Mesh _mesh;
        private readonly Bounds _bounds;
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
        internal readonly InstancedProperty[] Properties;
        /// used to collect property <see cref="JobHandle"/> during data sync.
        private PropertyHandleCollector _handleCollector;
        /// <summary> Contains count of properties for each <see cref="PropertyUpdateMode"/>. c0 - counts, c1 - offsets</summary>
        private readonly int3x2 _propertiesModeCountAndOffsets;

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
        /// each-update property for <see cref="PropertyPointer"/> data. Have this separately from <see cref="Properties"/> to not pass unnecessary handles to each-update properties + let them be disableable
        internal readonly InstancedProperty PointersProperty;
#if !NSPRITES_EACH_UPDATE_DISABLE
        /// <summary> should archetype work with <see cref="PropertyUpdateMode.Reactive"/> or <see cref="PropertyUpdateMode.Static"/>
        /// because such update require working with <see cref="PropertyPointerChunk"/> and <see cref="PropertyPointer"/> data </summary>
        private readonly bool _shouldHandlePropertiesByChunk;
#endif
        /// <summary> Contains how much space <b>allocated / used / can be used</b> in <see cref="PropertyUpdateMode.Reactive"/> and <see cref="PropertyUpdateMode.Static"/> properties, because both use chunk iteration</summary>
        internal PropertySpaceCounter PerChunkPropertiesSpaceCounter;
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
            , MaterialPropertyBlock override_MPB = null, int preallocatedSpace = 1, int minCapacityStep = 1)
        {
#if UNITY_EDITOR
            if (material == null)
                throw new NSpritesException($"While creating {nameof(RenderArchetype)} ({nameof(id)}: {id}) {nameof(UnityEngine.Material)} {nameof(material)} was null passed");
            if (propertyDataSet == null)
                throw new NSpritesException($"While creating {nameof(RenderArchetype)} ({nameof(id)}: {id}) {nameof(IReadOnlyList<PropertyData>)} {nameof(propertyDataSet)} was null passed");
            if (preallocatedSpace < 1)
                throw new NSpritesException($"You're trying to create {nameof(RenderArchetype)} ({nameof(id)}: {id}) with {preallocatedSpace} initial capacity, which can't be below 1");
            if (minCapacityStep < 1)
                throw new NSpritesException($"You're trying to create {nameof(RenderArchetype)} ({nameof(id)}: {id}) with {minCapacityStep} minimum capacity step, which can't be below 1");
#endif
            ID = id;
            Material = material;
            _mesh = mesh;
            _bounds = bounds;
            _materialPropertyBlock = override_MPB ?? new();
            _minCapacityStep = minCapacityStep;

#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
            PerChunkPropertiesSpaceCounter.Capacity = preallocatedSpace;
            _createdChunksIndexes_RNL = new ReusableNativeList<int>(0, Allocator.Persistent);
            _reorderedChunksIndexes_RNL = new ReusableNativeList<int>(0, Allocator.Persistent);
            PointersProperty = new InstancedProperty(Shader.PropertyToID(PropertyPointer.PropertyName), preallocatedSpace, sizeof(int), ComponentType.ReadOnly<PropertyPointer>());
#endif
#if !NSPRITES_EACH_UPDATE_DISABLE
            _perEntityPropertiesSpaceCounter.Capacity = preallocatedSpace;
#endif

            #region initialize properties
            Properties = new InstancedProperty[propertyDataSet.Count];
            var propertiesInternalDataSet = new NativeArray<ComponentType>(Properties.Length, Allocator.Temp);
            // 1st iteration fetch property data from map and count all types of update modes
            for (var propIndex = 0; propIndex < Properties.Length; propIndex++)
            {
                var propData = propertyDataSet[propIndex];
#if UNITY_EDITOR
                if (!propertyMap.ContainsKey(propData.PropertyID))
                    throw new NSpritesException($"There is no data in map for {propData.PropertyID} shader's property ID. You can look at Window -> Entities -> NSprites to see what properties registered at a time.");
#endif
                var propInternalData = propertyMap[propData.PropertyID];
                propertiesInternalDataSet[propIndex] = propInternalData;
                _propertiesModeCountAndOffsets.c0[(int)propData.UpdateMode]++;
            }
            // 2nd iteration initialize Properties with known indices offsets
            _propertiesModeCountAndOffsets.c1 = new int3(0, _propertiesModeCountAndOffsets.c0.x, _propertiesModeCountAndOffsets.c0.x + _propertiesModeCountAndOffsets.c0.y);
            var offsets = _propertiesModeCountAndOffsets.c1; // copy to be able to increment
            for (var propIndex = 0; propIndex < Properties.Length; propIndex++)
            {
                var propData = propertyDataSet[propIndex];
                var propType = propertiesInternalDataSet[propIndex];
                var prop = new InstancedProperty(propData.PropertyID, preallocatedSpace, UnsafeUtility.SizeOf(propType.GetManagedType()), propType);
                Properties[offsets[(int)propData.UpdateMode]++] = prop;
            }
#if !NSPRITES_EACH_UPDATE_DISABLE && (!NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE)
            // we want update properties by chunk only if archetype has any Reactive or Static properties
            _shouldHandlePropertiesByChunk = _propertiesModeCountAndOffsets.c0.x != 0 || _propertiesModeCountAndOffsets.c0.z != 0;
#endif
            var conditionalCount =
#if NSPRITES_STATIC_DISABLE
            0;
#else
            SP_Count;
#endif
            var handleCollectorCapacity = Properties.Length;
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
            // +1 for pointers property
            handleCollectorCapacity++;
#endif
            _handleCollector = new(handleCollectorCapacity, conditionalCount, Allocator.Persistent);

#if !NSPRITES_EACH_UPDATE_DISABLE && (!NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE)
            // we want update properties by entity only if archetype has any EachUpdate properties
            _shouldHandlePropertiesByEntity = EUP_Count != 0;
#endif
#endregion
        }
        public JobHandle ScheduleUpdate(in SystemData systemData, ref SystemState systemState)
        {
            // we need to use this method every new frame, because query somehow gets invalidated
            var query = systemData.Query;
            query.SetSharedComponentFilter(new SpriteRenderID() { id = ID });

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
                    Chunks = chunksArray,
                    ChunkCapacityCounter = overallCapacityCounter,
                    CreatedChunksCapacityCounter = createdChunksCapacityCounter,
                    EntityCounter = entityCounter,
                    PropertyBufferIndexRange_CTH = systemData.PropertyPointerChunk_CTH_RO,
                    NewChunksIndexes = createdChunksIndexes.AsParallelWriter(),
                    ReorderedChunksIndexes = reorderedChunksIndexes.AsParallelWriter(),
                    LastSystemVersion = systemData.LastSystemVersion
                }.ScheduleBatch(chunksArray.Length, MinIndicesPerJobCount);
                // we want to complete calculation job because next main thread logic depends on it
                // since only we do is just counting some data we have no need to chain inputDeps here
                calculateChunksDataHandle.Complete();

                _entityCount = entityCounter.Count;
#endregion

#region update capacity, sync data
                var neededOverallCapacity = overallCapacityCounter.Count;
                var createdChunksCapacity = createdChunksCapacityCounter.Count;
                var overallCapacityExceeded = neededOverallCapacity > PerChunkPropertiesSpaceCounter.Capacity;
                // if overall capacity exceeds current capacity OR
                // there is new chunks and theirs sum capacity exceeds free space we have currently
                // then we need to reallocate all per-property compute buffers and reassign chunks / entities indexes
                if (overallCapacityExceeded || createdChunksCapacity > PerChunkPropertiesSpaceCounter.UnusedCapacity)
                {
                    // reassign all chunk's / entity's indexes
                    // this job will iterate through chunks one by one and increase theirs indexes
                    // execution can't be parallel because calculation is dependent, so this is weakest part (actually could)
                    // this part goes before buffer's reallocation because it is just work with indexes we already now
                    var pinHandle = new PinChunksJob
                    {
                        Chunks = chunksArray,
                        PropertyPointerChunk_CTH = systemData.PropertyPointerChunk_CTH_RW
                    }.Schedule();
                    pinHandle = new PinEntitiesJob
                    {
                        Chunks = chunksArray,
                        PropertyPointerChunk_CTH = systemData.PropertyPointerChunk_CTH_RO,
                        PropertyPointer_CTH = systemData.PropertyPointer_CTH_RW
                    }.ScheduleBatch(chunksArray.Length, MinIndicesPerJobCount, pinHandle);

                    // from this point we should pass inputDeps, because next we work with components which might be touched outside
                    var preReadDependency = JobHandle.CombineDependencies(pinHandle, systemData.InputDeps);

                    #region local methods
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    void ScheduleLoadAllChunkData(InstancedProperty property, in int propIndex, in ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH_RO, DynamicComponentTypeHandle property_DCTH, in JobHandle inputDeps)
                    {
                        var handle = property.LoadAllChunkData(chunksArray, propertyPointerChunk_CTH_RO, property_DCTH, PerChunkPropertiesSpaceCounter.Count, inputDeps);
                        _handleCollector.Add(handle, propIndex);
                    }
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    void ReallocateAndReloadData(int fromPropIndex, int toPropIndex, in SystemData systemData, ref SystemState systemState)
                    {
                        for (int propIndex = fromPropIndex; propIndex < toPropIndex; propIndex++)
                        {
                            var property = Properties[propIndex];
                            property.Reallocate(PerChunkPropertiesSpaceCounter.Capacity, _materialPropertyBlock);
                            ScheduleLoadAllChunkData(property, propIndex, systemData.PropertyPointerChunk_CTH_RO, systemState.GetDynamicComponentTypeHandle(property.ComponentType), preReadDependency);
                        }
                    }
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    void ReloadData(int fromPropIndex, int toPropIndex, in SystemData systemData, ref SystemState systemState)
                    {
                        for (int propIndex = fromPropIndex; propIndex < toPropIndex; propIndex++)
                        {
                            var prop = Properties[propIndex];
                            ScheduleLoadAllChunkData(prop, propIndex, systemData.PropertyPointerChunk_CTH_RO, systemState.GetDynamicComponentTypeHandle(prop.ComponentType), preReadDependency);
                        }
                    }
                    #endregion

                    // set to true because here we will update static properties, which are conditional
                    _handleCollector.IncludeConditional = true;

                    // since here we fully reallocate our buffers we can retrieve reactive/static buffers count as needed capacity
                    PerChunkPropertiesSpaceCounter.Count = neededOverallCapacity;

                    // if we have overall capacity exceed then we need to extend our buffers
                    // so do reallocate + reload all data for Reactive / Static props
                    if (overallCapacityExceeded)
                    {
                        // reallocate compute buffers
                        // here we calculate new capacity no matter what the reason was to reallocate buffers
                        // new capacity depends on how much new space we need, but this space jump can't be lower then min capacity step
                        PerChunkPropertiesSpaceCounter.Capacity += math.max(_minCapacityStep, neededOverallCapacity - PerChunkPropertiesSpaceCounter.Capacity);
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
                        // reallocate and load all property pointers data
                        PointersProperty.Reallocate(PerChunkPropertiesSpaceCounter.Capacity, _materialPropertyBlock);
                        ScheduleLoadAllQueryData(PointersProperty, _handleCollector.PropertyPointerIndex, systemState.GetDynamicComponentTypeHandle(PointersProperty.ComponentType), query, preReadDependency);
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
                        // load all property pointer data
                        ScheduleLoadAllQueryData(PointersProperty, _handleCollector.PropertyPointerIndex, systemState.GetDynamicComponentTypeHandle(PointersProperty.ComponentType), query, preReadDependency);
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
                    // process chunks which ONLY got reordered NOT new chunks, which stored separately
                    // here we want to reassign entities indexes for chunks which have got / lost entity
                    // since that is the only job which can write to reordered chunk's property pointer we can safely schedule it independently
                    var pinHandle = new PinListedEntitiesJob
                    {
                        Chunks = chunksArray,
                        ChunkIndexes = reorderedChunksIndexes,
                        PropertyPointerChunk_CTH_RO = systemData.PropertyPointerChunk_CTH_RO,
                        PropertyPointer_CTH_Wo = systemData.PropertyPointer_CTH_RW
                    }.ScheduleBatch(reorderedChunksIndexes.Length, MinIndicesPerJobCount);

                    // if we haven't reallocated buffers it means that we have enough space for all chunks we have by now
                    // so we can assign all indexes to new chunks if any and assign indexes for new / reordered chunk's entities
                    if (createdChunksCapacity > 0)
                    {
                        // assign new chunks indexes starting from previous count
                        // since that is only job which can write to created chunk's property pointer we can safely schedule it independently
                        var createdChunksPinHandle = new PinListedChunksJob
                        {
                            Chunks = chunksArray,
                            PropertyPointerChunk_CTH = systemData.PropertyPointerChunk_CTH_RW,
                            ChunksIndexes = createdChunksIndexes,
                            StartingFromIndex = PerChunkPropertiesSpaceCounter.Count
                        }.Schedule();

                        // don't forget to update count with new chunks capacity even if we have enough space
                        PerChunkPropertiesSpaceCounter.Count += createdChunksCapacity;

                        createdChunksPinHandle = new PinListedEntitiesJob
                        {
                            Chunks = chunksArray,
                            ChunkIndexes = createdChunksIndexes,
                            PropertyPointerChunk_CTH_RO = systemData.PropertyPointerChunk_CTH_RO,
                            PropertyPointer_CTH_Wo = systemData.PropertyPointer_CTH_RW
                        }.ScheduleBatch(createdChunksIndexes.Length, MinIndicesPerJobCount, createdChunksPinHandle);

                        pinHandle = JobHandle.CombineDependencies(pinHandle, createdChunksPinHandle);
                    }

                    // from this point we should pass inputDeps, because next we work with components which might be touched outside
                    var preReadDependency = JobHandle.CombineDependencies(systemData.InputDeps, pinHandle);

                    // finally because there was no need to reallocate buffers we can: 
                    // 0. just update changed Reactive props
#if !NSPRITES_REACTIVE_DISABLE
                    for (var propIndex = 0; propIndex < RP_Count; propIndex++)
                    {
                        var property = Properties[propIndex];
                        var handle = property.UpdateOnChangeChunkData
                        (
                            chunksArray,
                            systemData.PropertyPointerChunk_CTH_RO,
                            systemState.GetDynamicComponentTypeHandle(property.ComponentType),
                            PerChunkPropertiesSpaceCounter.Count,
                            systemData.LastSystemVersion,
                            preReadDependency
                        );
                        _handleCollector.Add(handle, propIndex);
                    }
#endif
#if !NSPRITES_STATIC_DISABLE
                    // 1. if there are any reordered / created chunks then just update createdChunksIndexes AND reorderedChunksIndexes chunks
                    // for each such property we want to schedule job per each list of indices, so we need to combine dependencies every iteration
                    if (reorderedChunksIndexes.Length > 0 || createdChunksIndexes.Length > 0)
                    {
                        // set to true because here we will update static properties, which are conditional
                        _handleCollector.IncludeConditional = true;

                        for (int propIndex = SP_Offset; propIndex < SP_Offset + SP_Count; propIndex++)
                        {
                            var property = Properties[propIndex];
                            var handle = property.UpdateCreatedAndReorderedChunkData
                            (
                                chunksArray,
                                reorderedChunksIndexes,
                                createdChunksIndexes,
                                systemData.PropertyPointerChunk_CTH_RO,
                                systemState.GetDynamicComponentTypeHandle(property.ComponentType),
                                PerChunkPropertiesSpaceCounter.Count,
                                preReadDependency
                            );
                            _handleCollector.Add(handle, propIndex);
                        }
                    }
#endif
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
                    // load all property pointer data
                    ScheduleLoadAllQueryData(PointersProperty, _handleCollector.PropertyPointerIndex, systemState.GetDynamicComponentTypeHandle(PointersProperty.ComponentType), query, preReadDependency);
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
            // check if we have each-update properties to update (initialized in constructor)
            if (_shouldHandlePropertiesByEntity)
            {
#endif
#if NSPRITES_REACTIVE_DISABLE && NSPRITES_STATIC_DISABLE
                // calculate entity count before any actions if reactive and static properties code disabled
                _entityCount = query.CalculateEntityCount();
#endif
                // reallocate buffers if capacity exceeded by _minCapacityStep and load all data for each-update and static properties
                if (_perEntityPropertiesSpaceCounter.Capacity < _entityCount)
                {
                    _perEntityPropertiesSpaceCounter.Capacity = (int)math.ceil((float)_entityCount / _minCapacityStep);
                    // reallocate and reload all data for each-update properties
                    for (var propIndex = EUP_Offset; propIndex < EUP_Offset + EUP_Count; propIndex++)
                    {
                        var property = Properties[propIndex];
                        property.Reallocate(_perEntityPropertiesSpaceCounter.Capacity, _materialPropertyBlock);
                        ScheduleLoadAllQueryData(property, propIndex, systemState.GetDynamicComponentTypeHandle(property.ComponentType), query, systemData.InputDeps);
                    }
                }
                // if there was no exceed just load all each-update properties data
                else
                    for (var propIndex = EUP_Offset; propIndex < EUP_Offset + EUP_Count; propIndex++)
                    {
                        var property = Properties[propIndex];
                        ScheduleLoadAllQueryData(property, propIndex, systemState.GetDynamicComponentTypeHandle(property.ComponentType), query, systemData.InputDeps);
                    }

#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
            }
#endif

            _perEntityPropertiesSpaceCounter.Count = _entityCount;
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
            // complete reactive> properties
            for (var propIndex = 0; propIndex < RP_Count; propIndex++)
            {
                _handleCollector[propIndex].Complete();
                Properties[propIndex].EndWrite(PerChunkPropertiesSpaceCounter.Count);
            }
#endif
#if !NSPRITES_EACH_UPDATE_DISABLE
            // complete each-update properties
            for (var propIndex = EUP_Offset; propIndex < EUP_Offset + EUP_Count; propIndex++)
            {
                _handleCollector[propIndex].Complete();
                Properties[propIndex].EndWrite(_perEntityPropertiesSpaceCounter.Count);
            }
#endif
#if !NSPRITES_STATIC_DISABLE
            // if there was update for static properties complete them
            if (_handleCollector.IncludeConditional)
            {
                for (var propIndex = SP_Offset; propIndex < SP_Offset + SP_Count; propIndex++)
                {
                    _handleCollector[propIndex].Complete();
                    Properties[propIndex].EndWrite(PerChunkPropertiesSpaceCounter.Count);
                }
            }
#endif
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
            if (_shouldHandlePropertiesByChunk)
            {
                _handleCollector[_handleCollector.PropertyPointerIndex].Complete();
                PointersProperty.EndWrite(_entityCount);
            }
#endif
        }
        /// <summary>Draws instances in quantity based on the number of entities related to this <see cref="RenderArchetype"/>. Call it after <see cref="ScheduleUpdate"/> and <see cref="CompleteUpdate"/>.</summary>
        public void Draw()
        {
            if(_entityCount != 0)
                Graphics.DrawMeshInstancedProcedural(_mesh, 0, Material, _bounds, _entityCount, _materialPropertyBlock);
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
            for (var propIndex = 0; propIndex < Properties.Length; propIndex++)
                Properties[propIndex].ComputeBuffer.Release();
            _handleCollector.Dispose();
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
            PointersProperty.ComputeBuffer.Release();
            _createdChunksIndexes_RNL.Dispose();
            _reorderedChunksIndexes_RNL.Dispose();
#endif
        }
    }
}