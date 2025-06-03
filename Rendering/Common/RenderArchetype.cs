using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace NSprites
{
    #region data structs
    /// <summary> Holds info about: what shader's property id / update mode property uses </summary>
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
    internal struct AllocationCounter
    {
        /// <summary>
        /// Current length of each property's buffer
        /// </summary>
        public int Allocated;
        /// <summary>
        /// How much space actually used by chunks. This value needed to understand where on buffer we write data.
        /// </summary>
        public int Used;
        /// <summary>
        /// Space in buffer which isn't placed by any chunk yet.
        /// </summary>
        public int Unused => Allocated - Used;
        
        #if UNITY_EDITOR || DEVELOPEMENT_BUILD
        public void Verify(int capacityNeeded)
        {
            if (Used > Allocated)
                throw new NSpritesException($"{nameof(Used)} can't be greater then {nameof(Allocated)}.");
            if (Used < capacityNeeded)
                throw new NSpritesException($"{Used} {nameof(Used)} but {capacityNeeded} capacity needed, {nameof(Used)} can't be less then this value.");
            if (Allocated < capacityNeeded)
                throw new NSpritesException($"{Allocated} {nameof(Allocated)} which less then capacity needed: {capacityNeeded}.");
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
    /// <item><description><see cref="Mesh"/> + <see cref="UnityEngine.Material"/> + <see cref="MaterialPropertyBlock"/> to render sprite entities</description></item>
    /// <item><description>Set of <see cref="InstancedProperty"/> to sync data between compute buffers assigned to <see cref="MaterialPropertyBlock"/> and property components</description></item>
    /// <item>int ID to query entities through <see cref="SpriteRenderID"/></item>
    /// </list>
    /// </summary>
    internal class RenderArchetype : IDisposable
    {
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
        [BurstCompile]
        private struct CalculateChunksDataJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
            [WriteOnly][NoAlias] public NativeCounter.ParallelWriter ChunkCapacityCounter;
            [WriteOnly][NoAlias] public NativeCounter.ParallelWriter CreatedChunksCapacityCounter;
            [WriteOnly][NoAlias] public NativeCounter.ParallelWriter EntityCounter;
            /// this job only used for reading chunks which separated by <see cref="SpriteRenderID"/> SCD
            /// <see cref="MapChunksJob"/>, <see cref="MapListedChunksJob"/> writes to <see cref="PropertyPointerChunk"/>
            /// here we can be sure we're operating on different chunks because of different SCD
            [ReadOnly][NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<PropertyPointerChunk> PropertyPointerChunk_CTH_RO;
            // don't worry about created / reordered chunks indices list
            // if there is no new chunks, then there will no work with this list (it is intended to be persistent and resized on need)
            // else we already want to fill this lists, because otherwise it means more unnecessary work, we want to avoid extra chunk iteration
            /// used later by <see cref="MapListedChunksJob"/> and <see cref="MapListedEntitiesJob"/>
            [WriteOnly][NoAlias] public NativeList<int>.ParallelWriter NewChunksIndices;
            /// used later by <see cref="MapListedEntitiesJob"/>
            [WriteOnly][NoAlias] public NativeList<int>.ParallelWriter ReorderedChunksIndices;
            public uint LastSystemVersion;

            public void Execute(int startIndex, int count)
            {
                var toIndex = startIndex + count;
                for (var chunkIndex = startIndex; chunkIndex < toIndex; chunkIndex++)
                {
                    var chunk = Chunks[chunkIndex];
                    var capacity = chunk.Capacity;
                    ChunkCapacityCounter.Add(capacity);
                    EntityCounter.Add(chunk.Count);
#if UNITY_EDITOR
                    if (!chunk.HasChunkComponent(ref PropertyPointerChunk_CTH_RO))
                        throw new NSpritesException($"{nameof(RenderArchetype)} has {nameof(PropertyUpdateMode.Reactive)} properties, but chunk has no {nameof(PropertyPointerChunk)}");
#endif
                    var propertyPointerChunk = chunk.GetChunkComponentData(ref PropertyPointerChunk_CTH_RO);
                    // if PropertyPointerChunk's count (which is set later equal to chunk's capacity) is 0 then chunk is newly created
                    if (!propertyPointerChunk.Initialized)
                    {
                        CreatedChunksCapacityCounter.Add(capacity);
                        NewChunksIndices.AddNoResize(chunkIndex);
                    }
                    else if (chunk.DidOrderChange(LastSystemVersion))
                        ReorderedChunksIndices.AddNoResize(chunkIndex);
                }
            }
        }
#endif

        /// id to query entities using <see cref="SpriteRenderID"/>
        internal readonly int ID;
        internal readonly Material Material;
        private readonly Mesh _mesh;
        private readonly Bounds _bounds;
        private readonly MaterialPropertyBlock _materialPropertyBlock;
        /// <summary> minimum additional capacity we want allocate on exceed </summary>
        private readonly int _minCapacityStep;

        private int _entityCount;

        /// <summary>
        /// Contains all kind of properties one after another. Properties can be accessed by theirs update mode
        /// Here we use all kinds of update mode: 
        ///     <see cref="PropertyUpdateMode.Reactive"/> /
        ///     <see cref="PropertyUpdateMode.Static"/> /
        ///     <see cref="PropertyUpdateMode.EachUpdate"/>
        /// </summary>
        //internal readonly InstancedProperty[] Properties;
        internal readonly PropertiesContainer PropertiesContainer = new ();

        // EUP  - Each Update Properties
        // SP   - Static Properties
        // RP   - Reactive Properties

#if !NSPRITES_EACH_UPDATE_DISABLE
        /// <summary> Contains how much space allocated / used / can be used in <see cref="PropertyUpdateMode.EachUpdate"/> and <see cref="PropertyUpdateMode.Static"/> properties</summary>
        private AllocationCounter _perEntityPropertiesSpaceCounter;
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
        private readonly bool _shouldHandlePropertiesByEntity;
#endif
#endif

#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
        /// each-update property for <see cref="PropertyPointer"/> data. Have this separately from <see cref="PropertiesContainer"/> to not pass unnecessary handles to each-update properties + let them be disableable
        internal readonly InstancedProperty PointersProperty;
#if !NSPRITES_EACH_UPDATE_DISABLE
        /// <summary> should archetype work with <see cref="PropertyUpdateMode.Reactive"/> or <see cref="PropertyUpdateMode.Static"/>
        /// because such update require working with <see cref="PropertyPointerChunk"/> and <see cref="PropertyPointer"/> data </summary>
        private readonly bool _shouldHandleReactiveOrStaticProperties;
#endif
        /// <summary> Contains how much space <b>allocated / used / can be used</b> in <see cref="PropertyUpdateMode.Reactive"/> and <see cref="PropertyUpdateMode.Static"/> properties, because both use chunk iteration</summary>
        internal AllocationCounter ReactiveAndStaticAllocationCounter;
        private ReusableNativeList<int> _createdChunksIndexes_RNL;
        private ReusableNativeList<int> _reorderedChunksIndexes_RNL;
        // TODO: expose this for client code
        internal const int MinIndicesPerJobCount = 8;
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
            ReactiveAndStaticAllocationCounter.Allocated = preallocatedSpace;
            _createdChunksIndexes_RNL = new ReusableNativeList<int>(0, Allocator.Persistent);
            _reorderedChunksIndexes_RNL = new ReusableNativeList<int>(0, Allocator.Persistent);
            PointersProperty = new InstancedProperty(Shader.PropertyToID(PropertyPointer.PropertyName), preallocatedSpace, sizeof(int), ComponentType.ReadOnly<PropertyPointer>(), _materialPropertyBlock);
#endif
#if !NSPRITES_EACH_UPDATE_DISABLE
            _perEntityPropertiesSpaceCounter.Allocated = preallocatedSpace;
#endif

            #region initialize properties
            for (var propIndex = 0; propIndex < propertyDataSet.Count; propIndex++)
            {
                var propData = propertyDataSet[propIndex];
                var propType = propertyMap[propData.PropertyID];
                var prop = new InstancedProperty(propData.PropertyID, preallocatedSpace, UnsafeUtility.SizeOf(propType.GetManagedType()), propType, _materialPropertyBlock);

                PropertiesContainer.AddProperty(prop, propData.UpdateMode);
            }
            
            // extra 1 for pointer property
            PropertiesContainer.ConstructHandles(1);
            
#if !NSPRITES_EACH_UPDATE_DISABLE && (!NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE)
            // we want update properties by chunk only if archetype has any Reactive or Static properties
            _shouldHandleReactiveOrStaticProperties = PropertiesContainer.HasPropertiesWithMode(PropertyUpdateMode.Reactive) || PropertiesContainer.HasPropertiesWithMode(PropertyUpdateMode.Static);
#endif

#if !NSPRITES_EACH_UPDATE_DISABLE && (!NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE)
            // we want update properties by entity only if archetype has any EachUpdate properties
            _shouldHandlePropertiesByEntity = PropertiesContainer.HasPropertiesWithMode(PropertyUpdateMode.EachUpdate);
#endif
#endregion
        }
        
        public JobHandle ScheduleUpdate(in SystemData systemData, ref SystemState systemState)
        {
            // we need to use this method every new frame, because query somehow gets invalidated
            var query = systemData.Query;
            query.SetSharedComponentFilter(new SpriteRenderID { id = ID });

            PropertiesContainer.ResetHandles();

            #region handle reactive / static properties
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
            NativeArray<ArchetypeChunk> chunksArray = default;
#if !NSPRITES_EACH_UPDATE_DISABLE
            if (_shouldHandleReactiveOrStaticProperties)
            {
#endif
                #region chunk gather data
                // TODO: try to avoid sync point. To do so we can obtain NativeList instead of NativeArray
                chunksArray = query.ToArchetypeChunkArray(Allocator.TempJob);

                var overallCapacityCounter = new NativeCounter(Allocator.TempJob);
                var createdChunksCapacityCounter = new NativeCounter(Allocator.TempJob);
                var entityCounter = new NativeCounter(Allocator.TempJob);

                // this lists will be reused every frame, which means less allocations for us, but those use Persistent allocation
                var createdChunksIndices = _createdChunksIndexes_RNL.GetList(chunksArray.Length);
                var reorderedChunksIndexes = _reorderedChunksIndexes_RNL.GetList(chunksArray.Length);

                // here we collecting how much we have entities / chunk's overall capacity / new chunk's capacity
                var calculateChunksDataHandle = new CalculateChunksDataJob
                {
                    Chunks = chunksArray,
                    ChunkCapacityCounter = overallCapacityCounter,
                    CreatedChunksCapacityCounter = createdChunksCapacityCounter,
                    EntityCounter = entityCounter,
                    PropertyPointerChunk_CTH_RO = systemData.PropertyPointerChunk_CTH_RO,
                    NewChunksIndices = createdChunksIndices.AsParallelWriter(),
                    ReorderedChunksIndices = reorderedChunksIndexes.AsParallelWriter(),
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
                var overallCapacityExceeded = neededOverallCapacity > ReactiveAndStaticAllocationCounter.Allocated;
                
                // if overall capacity exceeds current capacity OR
                // there is new chunks and theirs sum capacity exceeds free space we have currently
                // then we need to reallocate all per-property compute buffers and reassign chunks / entities indexes
                
                // NOTE: allocated space can be enough at the same time with unused space isn't for new chunks.
                // this is because chunks may be destroyed but we don't track it so summary capacity will decrease,
                // but freed space has arbitrary position if buffer with arbitrary length, so we can't reuse it until full chunks remap
                if (overallCapacityExceeded || createdChunksCapacity > ReactiveAndStaticAllocationCounter.Unused)
                {
                    // reassign all chunk's / entity's indices
                    // this job will iterate through chunks one by one and increase theirs `from` indices
                    // execution can't be parallel because calculation is dependent, so this is weakest part (actually could)
                    // this part goes before buffer's reallocation because it is just work with indices we already now
                    var mapHandle = new MapChunksJob
                    {
                        Chunks = chunksArray,
                        PropertyPointerChunk_CTH = systemData.PropertyPointerChunk_CTH_RW
                    }.Schedule();
                    mapHandle = new MapEntitiesJob
                    {
                        Chunks = chunksArray,
                        PropertyPointerChunk_CTH = systemData.PropertyPointerChunk_CTH_RO,
                        PropertyPointer_CTH = systemData.PropertyPointer_CTH_RW
                    }.ScheduleBatch(chunksArray.Length, MinIndicesPerJobCount, mapHandle);

                    // from this point we should pass inputDeps, because next we work with components which might be touched outside
                    var preReadDependency = JobHandle.CombineDependencies(mapHandle, systemData.InputDeps);

                    #region local methods
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    void SyncByChunks(InstancedProperty property, in ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH_RO, DynamicComponentTypeHandle property_DCTH, in JobHandle inputDeps)
                        => PropertiesContainer.AddHandle(property.SyncByChunks(chunksArray, propertyPointerChunk_CTH_RO, property_DCTH, ReactiveAndStaticAllocationCounter.Used, inputDeps));

                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    void ReallocateAndSyncManyByChunks(IEnumerable<InstancedProperty> props, in SystemData systemData, ref SystemState systemState)
                    {
                        foreach (var prop in props)
                        {
                            prop.Reallocate(ReactiveAndStaticAllocationCounter.Allocated, _materialPropertyBlock);
                            SyncByChunks(prop, systemData.PropertyPointerChunk_CTH_RO, systemState.GetDynamicComponentTypeHandle(prop.ComponentType), preReadDependency);
                        }
                    }
                    [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    void SyncManyByChunks(IEnumerable<InstancedProperty> props, in SystemData systemData, ref SystemState systemState)
                    {
                        foreach (var prop in props)
                            SyncByChunks(prop, systemData.PropertyPointerChunk_CTH_RO, systemState.GetDynamicComponentTypeHandle(prop.ComponentType), preReadDependency);
                    }
                    #endregion

                    // since here we fully reallocate our buffers we can retrieve reactive / static buffers count as needed capacity
                    ReactiveAndStaticAllocationCounter.Used = neededOverallCapacity;

                    // if we have overall capacity exceed then we need to extend our buffers
                    // so do reallocate + reload all data for Reactive / Static props
                    if (overallCapacityExceeded)
                    {
                        // reallocate compute buffers
                        // here we calculate new capacity no matter what the reason was to reallocate buffers
                        // new capacity depends on how much new space we need, but this space jump can't be lower then min capacity step
                        // TODO: move to NSprites utils
                        ReactiveAndStaticAllocationCounter.Allocated += math.max(_minCapacityStep, neededOverallCapacity - ReactiveAndStaticAllocationCounter.Allocated);
                        
                        // recall: capacity actually exceeded, so
                        // * reallocate + sync by query (entity-by-entity) pointers prop
                        // * reallocate + sync by each chunk reactive / static properties

#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
                        // reallocate and load all property pointers data
                        PointersProperty.Reallocate(ReactiveAndStaticAllocationCounter.Allocated, _materialPropertyBlock);
                        SyncByQuery(PointersProperty, systemState.GetDynamicComponentTypeHandle(PointersProperty.ComponentType), ref query, preReadDependency);
#endif
#if !NSPRITES_REACTIVE_DISABLE
                        ReallocateAndSyncManyByChunks(PropertiesContainer.Reactive, systemData, ref systemState);
#endif
#if !NSPRITES_STATIC_DISABLE
                        ReallocateAndSyncManyByChunks(PropertiesContainer.Static, systemData, ref systemState);
#endif
                    }
                    // if there is no exceed then just reload all data without any reallocation
                    else
                    {
                        // recall: allocated space in enough, but no place for new chunks, so full reload needed, so just:
                        // * sync pointer prop by query
                        // * sync reactive / static props by every chunk

#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
                        // load all property pointer data
                        SyncByQuery(PointersProperty, systemState.GetDynamicComponentTypeHandle(PointersProperty.ComponentType), ref query, preReadDependency);
#endif
#if !NSPRITES_REACTIVE_DISABLE
                        SyncManyByChunks(PropertiesContainer.Reactive, systemData, ref systemState);
#endif
#if !NSPRITES_STATIC_DISABLE
                        SyncManyByChunks(PropertiesContainer.Static, systemData, ref systemState);
#endif
                    }
                }
                // here we can relax because we have enough capacity
                else
                {
                    // process chunks which ONLY got reordered NOT new chunks, which stored separately
                    // here we want to reassign entities indexes for chunks which have got / lost entity
                    // since that is the only job which can write to reordered chunk's property pointer we can safely schedule it independently
                    var mapHandle = new MapListedEntitiesJob
                    {
                        Chunks = chunksArray,
                        ChunkIndexes = reorderedChunksIndexes,
                        PropertyPointerChunk_CTH_RO = systemData.PropertyPointerChunk_CTH_RO,
                        PropertyPointer_CTH_Wo = systemData.PropertyPointer_CTH_RW
                    }.ScheduleBatch(reorderedChunksIndexes.Length, MinIndicesPerJobCount);

                    // if we haven't reallocated buffers it means that we have enough space for all chunks we have by now
                    // so we can assign all indices to new chunks if any and assign indexes for new / reordered chunk's entities
                    if (createdChunksCapacity > 0)
                    {
                        // assign new chunks indexes starting from previous count
                        // since that is only job which can write to created chunk's property pointer we can safely schedule it independently
                        var createdChunksMapHandle = new MapListedChunksJob
                        {
                            Chunks = chunksArray,
                            ChunksIndices = createdChunksIndices,
                            PropertyPointerChunk_CTH = systemData.PropertyPointerChunk_CTH_RW,
                            StartingFromIndex = ReactiveAndStaticAllocationCounter.Used
                        }.Schedule();

                        // don't forget to update count with new chunks capacity even if we have enough space
                        ReactiveAndStaticAllocationCounter.Used += createdChunksCapacity;

                        createdChunksMapHandle = new MapListedEntitiesJob
                        {
                            Chunks = chunksArray,
                            ChunkIndexes = createdChunksIndices,
                            PropertyPointerChunk_CTH_RO = systemData.PropertyPointerChunk_CTH_RO,
                            PropertyPointer_CTH_Wo = systemData.PropertyPointer_CTH_RW
                        }.ScheduleBatch(createdChunksIndices.Length, MinIndicesPerJobCount, createdChunksMapHandle);

                        mapHandle = JobHandle.CombineDependencies(mapHandle, createdChunksMapHandle);
                    }

                    // from this point we should pass inputDeps, because next we work with components which might be touched outside
                    var preReadDependency = JobHandle.CombineDependencies(systemData.InputDeps, mapHandle);

                    // finally because there was no need to reallocate buffers we can: 
                    // 0. just update changed Reactive props
#if !NSPRITES_REACTIVE_DISABLE
                    foreach (var prop in PropertiesContainer.Reactive)
                    {
                        // recall: capacity is fully enough so no reallocation needed nor full resync so just:
                        // * sync reactive props by each CHANGED chunk
                        var handle = prop.SyncByChangedChunks
                        (
                            chunksArray,
                            systemData.PropertyPointerChunk_CTH_RO,
                            systemState.GetDynamicComponentTypeHandle(prop.ComponentType),
                            ReactiveAndStaticAllocationCounter.Used,
                            systemData.LastSystemVersion,
                            preReadDependency
                        );
                        PropertiesContainer.AddHandle(handle);
                    }
#endif
#if !NSPRITES_STATIC_DISABLE
                    // 1. if there are any reordered / created chunks then just update createdChunksIndices AND reorderedChunksIndices chunks
                    // for each such property we want to schedule job per each list of indices, so we need to combine dependencies every iteration
                    if (reorderedChunksIndexes.Length > 0 || createdChunksIndices.Length > 0)
                    {
                        foreach (var prop in PropertiesContainer.Static)
                        {
                            // recall: capacity is fully enough so no reallocation needed nor full resync so just:
                            // * sync static props by each CREATED / REORDERED chunk
                            var handle = prop.SyncByCreatedAndReorderedChunks
                            (
                                chunksArray,
                                reorderedChunksIndexes,
                                createdChunksIndices,
                                systemData.PropertyPointerChunk_CTH_RO,
                                systemState.GetDynamicComponentTypeHandle(prop.ComponentType),
                                ReactiveAndStaticAllocationCounter.Used,
                                preReadDependency
                            );
                            PropertiesContainer.AddHandle(handle);
                        }
                    }
#endif
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
                    // load all property pointer data
                    SyncByQuery(PointersProperty, systemState.GetDynamicComponentTypeHandle(PointersProperty.ComponentType), ref query, preReadDependency);
#endif
                }
                
#if UNITY_EDITOR || DEVELOPEMENT_BUILD
                ReactiveAndStaticAllocationCounter.Verify(neededOverallCapacity);
#endif
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
                // reallocate buffers if capacity exceeded and load all data for each-update and static properties
                if (_perEntityPropertiesSpaceCounter.Allocated < _entityCount)
                {
                    _perEntityPropertiesSpaceCounter.Allocated += math.max(_minCapacityStep, _entityCount - _perEntityPropertiesSpaceCounter.Allocated);
                    // reallocate and reload all data for each-update properties
                    foreach (var prop in PropertiesContainer.EachUpdate)
                    {
                        prop.Reallocate(_perEntityPropertiesSpaceCounter.Allocated, _materialPropertyBlock);
                        SyncByQuery(prop, systemState.GetDynamicComponentTypeHandle(prop.ComponentType), ref query, systemData.InputDeps);
                    }
                }
                // if there was no exceed just load all each-update properties data
                else
                    foreach (var prop in PropertiesContainer.EachUpdate)
                        SyncByQuery(prop, systemState.GetDynamicComponentTypeHandle(prop.ComponentType), ref query, systemData.InputDeps);

#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
            }
#endif
            // TODO: remove this, seems like it is never used, because `used` and `allocated` is always the same in EachUpdate logic
            //_perEntityPropertiesSpaceCounter.Used = _entityCount;
#endif
            #endregion

            var outputHandle = PropertiesContainer.GeneralHandle;
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
#if !NSPRITES_EACH_UPDATE_DISABLE
            if (_shouldHandleReactiveOrStaticProperties)
#endif
            chunksArray.Dispose(outputHandle);
#endif
            return outputHandle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SyncByQuery(InstancedProperty property, in DynamicComponentTypeHandle property_DCTH, ref EntityQuery query, in JobHandle inputDeps) 
            => PropertiesContainer.AddHandle(property.SyncByQuery(ref query, property_DCTH, _entityCount, inputDeps));

        /// <summary>Forces complete all properties update jobs. Call it after <see cref="ScheduleUpdate"/> and before <see cref="Draw"/> method to ensure all data is updated.</summary>
        private void CompleteUpdate()
        {
#if !NSPRITES_REACTIVE_DISABLE
            // complete reactive properties
            foreach (var props in PropertiesContainer.Reactive)
                props.Complete();
#endif
#if !NSPRITES_EACH_UPDATE_DISABLE
            // complete each-update properties
            foreach (var props in PropertiesContainer.EachUpdate)
                props.Complete();
#endif
#if !NSPRITES_STATIC_DISABLE
            // complete static properties
            foreach (var props in PropertiesContainer.Static)
                props.Complete();
#endif
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
            if (_shouldHandleReactiveOrStaticProperties)
                PointersProperty.Complete();
#endif
        }
        
        /// <summary>Draws instances in quantity based on the number of entities related to this <see cref="RenderArchetype"/>. Call it after <see cref="ScheduleUpdate"/> and <see cref="CompleteUpdate"/>.</summary>
        public void Draw()
        {
            if(_entityCount != 0)
            {
                RenderParams rp = new RenderParams(Material) 
                {
                    matProps = _materialPropertyBlock,
                    worldBounds = _bounds,
                    receiveShadows = Material.enableInstancing
                };
                Graphics.RenderMeshPrimitives(rp,_mesh, 0, _entityCount);
            }
        }
        
        /// <summary><inheritdoc cref="CompleteUpdate"/>
        /// Then <inheritdoc cref="Draw"/></summary>
        public void CompleteAndDraw()
        {
            CompleteUpdate();
            Draw();
        }

        public void Dispose()
        {
            PropertiesContainer.Dispose();
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
            PointersProperty.Dispose();
            _createdChunksIndexes_RNL.Dispose();
            _reorderedChunksIndexes_RNL.Dispose();
#endif
        }
    }
}