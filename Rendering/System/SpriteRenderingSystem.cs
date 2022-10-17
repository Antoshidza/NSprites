using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[assembly: RegisterGenericJobType(typeof(NSprites.SpriteRenderingSystem.GatherPropertyJob<int>))]
[assembly: RegisterGenericJobType(typeof(NSprites.SpriteRenderingSystem.GatherPropertyJob<float>))]
[assembly: RegisterGenericJobType(typeof(NSprites.SpriteRenderingSystem.GatherPropertyJob<float4>))]
namespace NSprites
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class SpriteRenderingSystem : SystemBase
    {
        #region data
        //assuming that there is no batching for different materials/textures/other not-instanced properties we can define some kind of render archetypes
        //it is combination of material index + uniqueness of properties + all per-material properties values (mostly textures, because they are reason why we do this)
        //if sprites uses different IRenderArchetype then they should be rendered in different calls
        private class RenderArchetype : IDisposable
        {
            public interface IInstancedProperty : IDisposable
            {
                /// <summary>
                /// Calls BeginWrite on it's compute buffer with sortingDatas.Length count. Before use MPB in rendering we should call EndWrite() before rendering.
                /// </summary>
                public JobHandle GatherData(in EntityQuery spriteQuery, in int length, in JobHandle inputDeps, SystemBase system);
                public void EndWrite(in int count);
                public void Resize(in int size);
                public void PassToMaterialPropertyBlock(MaterialPropertyBlock materialPropertyBlock);
            }
            internal class InstancedProperty<T> : IInstancedProperty
                where T : struct
            {
                public int propertyID;
                public ComponentType componentType;
                public ComputeBuffer computeBuffer;

                internal InstancedProperty(in int propertyID, in int count, in int stride, in ComponentType componentType)
                {
                    this.propertyID = propertyID;
                    this.componentType = componentType;
                    computeBuffer = new ComputeBuffer(count, stride, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
                }
                public void Dispose() => computeBuffer.Dispose();
                public JobHandle GatherData(in EntityQuery spriteQuery, in int length, in JobHandle inputDeps, SystemBase system)
                {
                    return new GatherPropertyJob<T>
                    {
                        componentTypeHandle = system.GetDynamicComponentTypeHandle(componentType),
                        typeSize = computeBuffer.stride,
                        outputArray = computeBuffer.BeginWrite<T>(0, length)
                    }.ScheduleParallel(spriteQuery, inputDeps);   
                }
                public void EndWrite(in int count)
                {
                    computeBuffer.EndWrite<T>(count);
                }
                public void Resize(in int size)
                {
                    var stride = computeBuffer.stride;
                    computeBuffer.Release();
                    computeBuffer = new ComputeBuffer(size, stride, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
                }
                public void PassToMaterialPropertyBlock(MaterialPropertyBlock materialPropertyBlock)
                {
                    materialPropertyBlock.SetBuffer(propertyID, computeBuffer);
                }
            }

            public readonly int id;

            private const int LTW_BUFFER_STRIDE = 16 * sizeof(float);

            private readonly Material _material;
            private readonly MaterialPropertyBlock _materialPropertyBlock;
            private readonly EntityQuery _query;
            private readonly int _matricesPropertyID;
            private ComputeBuffer _matricesBuffer;
            private int _size;
            private readonly int _capacityStep; 
            private readonly IInstancedProperty[] _instancedProperties;
            private NativeArray<JobHandle> _gatherDataHandles;

            public RenderArchetype(Material material, IInstancedProperty[] instancedProperties, EntityQuery query, int id, int matricesPropertyID, MaterialPropertyBlock overrideMPB = null, int capacityStep = 1)
            {
                this.id = id;
                _query = query;
                _material = material;
                _matricesPropertyID = matricesPropertyID;
                _size = capacityStep;
                _capacityStep = capacityStep;
                _materialPropertyBlock = overrideMPB ?? new MaterialPropertyBlock();
                _instancedProperties = instancedProperties;

                for(int i = 0; i < instancedProperties.Length; i++)
                    instancedProperties[i].PassToMaterialPropertyBlock(_materialPropertyBlock);

                _gatherDataHandles = new NativeArray<JobHandle>(_instancedProperties.Length, Allocator.Persistent);

                _matricesBuffer = new ComputeBuffer(_size, LTW_BUFFER_STRIDE, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
                _materialPropertyBlock.SetBuffer(_matricesPropertyID, _matricesBuffer);
            }
            public void Dispose()
            {
                _matricesBuffer.Dispose();
                _gatherDataHandles.Dispose();
                foreach (var instancedProperty in _instancedProperties)
                    instancedProperty.Dispose();
            }
            public EntityQuery GetQueryFilteredByID()
            {
                _query.SetSharedComponentFilter(new SpriteRenderID() { id = id });
                return _query;
            }
            public JobHandle GatherPropertyData(in int length, SystemBase system, in JobHandle inputDeps = default)
            {
                _query.SetSharedComponentFilter(new SpriteRenderID() { id = id });
                if (_size < length)
                {
                    _size = GetRequiredSize(length);
                    _matricesBuffer.Release();
                    _matricesBuffer = new ComputeBuffer(_size, LTW_BUFFER_STRIDE, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
                    _materialPropertyBlock.SetBuffer(_matricesPropertyID, _matricesBuffer);
                    for(int i = 0; i < _instancedProperties.Length; i++)
                    {
                        var property = _instancedProperties[i];
                        property.Resize(_size);
                        property.PassToMaterialPropertyBlock(_materialPropertyBlock);
                        _gatherDataHandles[i] = property.GatherData(_query, length, inputDeps, system);
                    }
                }
                else
                    for(int i = 0; i < _instancedProperties.Length; i++)
                        _gatherDataHandles[i] = _instancedProperties[i].GatherData(_query, length, inputDeps, system);

                return JobHandle.CombineDependencies(_gatherDataHandles);
            }
            public JobHandle FillMatricesData(NativeSlice<float4x4> matrices, JobHandle inputDeps)
            {
                var matricesArray = _matricesBuffer.BeginWrite<float4x4>(0, matrices.Length);
                return new CopyArray<float4x4>
                {
                    dstArray = matricesArray,
                    sourceArray = matrices
                }.Schedule(inputDeps);
            }
            public void EndWriteComputeBuffers(int count)
            {
                for(int i = 0; i < _instancedProperties.Length; i++)
                    _instancedProperties[i].EndWrite(count);
                _matricesBuffer.EndWrite<float4x4>(count);
            }
            private int GetRequiredSize(int count) 
                => ((count - 1) / _capacityStep + 1) * _capacityStep;
            public void Draw(Mesh mesh, in Bounds bounds, in int count)
            {
#if UNITY_EDITOR
                //ensure we pass not-null values for SubScene strange behaviour
                if (mesh == null)
                    Debug.LogWarning("Can't render sprite because mesh is null");
                if(_material == null)
                    Debug.LogWarning("Can't render sprite because material is null");
                if (mesh == null || _material == null)
                    return;
#endif
                Graphics.DrawMeshInstancedProcedural(mesh, 0, _material, bounds, count, _materialPropertyBlock);
            }
        }

        internal struct InstancedPropertyData
        {
            public ComponentType componentType;
            public PropertyFormat format;
        }

        //TODO: don't sort SortingData, instead sort it's indexes because struct is much bigger then single int32
        internal struct SpriteData
        {
            internal struct GeneralComparer : IComparer<SpriteData>
            {
                public int Compare(SpriteData x, SpriteData y)
                {
                    //can be rewrited with if statement
                    return x.groupSortingIndex.CompareTo(y.groupSortingIndex) * -32 //less index -> later in render
                        + x.groupPosition.CompareTo(y.groupPosition) * 16
                        + x.groupID.CompareTo(y.groupID) * 8
                        + x.sortingIndex.CompareTo(y.sortingIndex) * -4 //less index -> later in render
                        + x.position.y.CompareTo(y.position.y) * 2
                        + x.id.CompareTo(y.id);
                }
            }

            public int entityInQueryIndex;
            public int archetypeIncludedIndex;

            public int id;
            public int groupID;

            public int sortingIndex;
            public int groupSortingIndex;

            public float2 position;
            public float2 scale;
            public float2 pivot;
            public float groupPosition;

            public override string ToString()
            {
                return $"id: {id}, groupID: {groupID}, groupIndex: {groupSortingIndex}, groupPos: {groupPosition}, sortIndex: {sortingIndex}, pos: {position}";
            }
        }
        internal struct RenderArchetypeForSorting
        {
            public int id;
            /// <summary>int reference to _renderArchetypes</summary>
            public int archetypeIndex;
            /// <summary>sprite count before this archetype</summary>
            public int stride;
            /// <summary>sprite count</summary>
            public int count;
        }
        #endregion

        #region jobs
        [BurstCompile]
        private struct GatherSortingDataJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle entityTypeHandle;
            [ReadOnly] public ComponentTypeHandle<WorldPosition2D> worldPosition2D_CTH;
            [ReadOnly] public ComponentDataFromEntity<WorldPosition2D> worldPosition2D_CDFE;
            [ReadOnly] public ComponentTypeHandle<Scale2D> scale2D_CTH;
            [ReadOnly] public ComponentTypeHandle<Pivot> pivot_CTH;
            [ReadOnly] public ComponentTypeHandle<SortingGroup> sortingGroup_CTH;
            [ReadOnly] public ComponentDataFromEntity<SortingGroup> sortingGroup_CDFE;
            [ReadOnly] public int archetypeIncludedIndex;
            [WriteOnly][NativeDisableContainerSafetyRestriction] public NativeSlice<SpriteData> spriteData;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex, int indexOfFirstEntityInQuery)
            {
                var entityArray = batchInChunk.GetNativeArray(entityTypeHandle);
                var worldPosition2DArray = batchInChunk.GetNativeArray(worldPosition2D_CTH);
                var scale2DArray = batchInChunk.GetNativeArray(scale2D_CTH);
                var pivotArray = batchInChunk.GetNativeArray(pivot_CTH);
                var sortingGroupArray = batchInChunk.GetNativeArray(sortingGroup_CTH);
                for(int i = 0; i < entityArray.Length; i++)
                {
                    var entity = entityArray[i];
                    var sortingGroup = sortingGroupArray[i];
                    var position = worldPosition2DArray[i].value;

                    float groupPosition;
                    int groupSortingIndex;
                    //means it's root entity
                    if(sortingGroup.groupID == entity)
                    {
                        groupPosition = position.y;
                        groupSortingIndex = sortingGroup.index;
                    }
                    else
                    {
                        groupPosition = worldPosition2D_CDFE[sortingGroup.groupID].value.y;
                        groupSortingIndex = sortingGroup_CDFE[sortingGroup.groupID].index;
                    }

                    spriteData[indexOfFirstEntityInQuery + i] = new SpriteData
                    {
                        entityInQueryIndex = indexOfFirstEntityInQuery + i,
                        archetypeIncludedIndex = archetypeIncludedIndex,

                        position = position,
                        scale = scale2DArray[i].value,
                        pivot = pivotArray[i].value,

                        id = entity.Index,
                        sortingIndex = sortingGroup.index,

                        groupID = sortingGroup.groupID.Index,
                        groupPosition = groupPosition,
                        groupSortingIndex = groupSortingIndex,
                    };
                }
            }
        }
        [BurstCompile]
        internal struct GatherPropertyJob<TProperty> : IJobChunk
            where TProperty : struct
            //TPropety supposed to be:
            //  * int
            //  * float
            //  * float4
        {
            //this should be filled every frame with GetDynamicComponentTypeHandle
            [ReadOnly] public DynamicComponentTypeHandle componentTypeHandle;
            public int typeSize;
            [WriteOnly][NativeDisableParallelForRestriction] public NativeArray<TProperty> outputArray;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                var data = chunk.GetDynamicComponentDataArrayReinterpret<TProperty>(componentTypeHandle, typeSize);
                NativeArray<TProperty>.Copy
                (
                    data,
                    0,
                    outputArray,
                    firstEntityIndex,
                    data.Length
                );
            }
        }
        [BurstCompile]
        internal struct FillMatricesArrayJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<SpriteData> spriteDataArray;
            [ReadOnly] public NativeList<RenderArchetypeForSorting> archetypeLayoutData;
            [WriteOnly][NativeDisableParallelForRestriction] public NativeArray<float4x4> matricesArray;

            public void Execute(int index)
            {
                var spriteData = spriteDataArray[index];
                var renderPosition = spriteData.position - spriteData.scale * spriteData.pivot;
                matricesArray[archetypeLayoutData[spriteData.archetypeIncludedIndex].stride + spriteData.entityInQueryIndex] = float4x4.TRS
                (
                    new float3(renderPosition.x, renderPosition.y, 0),
                    quaternion.identity,
                    new float3(spriteData.scale.x, spriteData.scale.y, 1f)
                );
            }
        }
        [BurstCompile]
        internal struct CopyArray<T> : IJob
            where T : unmanaged
        {
            [NoAlias][WriteOnly] public NativeArray<T> dstArray;
            [NoAlias][ReadOnly] public NativeSlice<T> sourceArray;

            public void Execute()
            {
                sourceArray.CopyTo(dstArray);
            }
        }
        [BurstCompile]
        internal struct SortArrayJob<TElement, TComparer> : IJob
            where TElement : unmanaged
            where TComparer : unmanaged, IComparer<TElement>
        {
            public NativeArray<TElement> array;
            public TComparer comparer;

            public void Execute() => array.Sort(comparer);
        }
        #endregion

        private readonly Mesh _quad = Utils.ConstructQuad();
        private NativeArray<ComponentType> _defaultComponentTypes;
        private readonly Dictionary<int, InstancedPropertyData> _instancedPropertiesFormats = new();
        private readonly List<RenderArchetype> _renderArchetypes = new();

        protected override void OnCreate()
        {
            base.OnCreate();
            GatherPropertiesTypes();
            GatherDefaultComponentTypes();
        }
        protected override void OnDestroy()
        {
            base.OnDestroy();
            _defaultComponentTypes.Dispose();
            foreach (var renderArchetype in _renderArchetypes)
                renderArchetype.Dispose();
        }

        protected override void OnUpdate()
        {
            #region calculate sprite counts
            var includedArchetypes = new NativeList<RenderArchetypeForSorting>(_renderArchetypes.Count, Allocator.TempJob);
            var totalSpriteCount = 0;
            for(int i = 0; i < _renderArchetypes.Count; i++)
            {
                var renderArchetype = _renderArchetypes[i];
                //for some reason we need to update this SetSharedComponentFilter every time we get 
                var spriteCount = renderArchetype.GetQueryFilteredByID().CalculateEntityCount();
                if(spriteCount > 0)
                {
                    includedArchetypes.Add(new RenderArchetypeForSorting() { id = renderArchetype.id, archetypeIndex = i, stride = totalSpriteCount, count = spriteCount });
                    totalSpriteCount += spriteCount;
                }
            }

            if(includedArchetypes.Length == 0)
            {
                includedArchetypes.Dispose();
                return;
            }
            #endregion

            #region gather sprite data
            var gatherSortingDataHandle = new JobHandle();
            var spriteDataArray = new NativeArray<SpriteData>(totalSpriteCount, Allocator.TempJob);
            var entityTypeHandle = GetEntityTypeHandle();
            var scale2D_CTH = GetComponentTypeHandle<Scale2D>(true);
            var pivot_CTH = GetComponentTypeHandle<Pivot>(true);
            var worldPosition2D_CTH = GetComponentTypeHandle<WorldPosition2D>(true);
            var worldPosition2D_CDFE = GetComponentDataFromEntity<WorldPosition2D>(true);
            var sortingGroup_CTH = GetComponentTypeHandle<SortingGroup>(true);
            var sortingGroupCDFE = GetComponentDataFromEntity<SortingGroup>(true);
            for(int i = 0; i < includedArchetypes.Length; i++)
            {
                var renderArchetypeData = includedArchetypes[i];
                var handle = new GatherSortingDataJob()
                {
                    //CTH - ComponentTypeHandle
                    //CDFE - ComponentDataFromEntity
                    archetypeIncludedIndex = i,
                    entityTypeHandle = entityTypeHandle,
                    scale2D_CTH = scale2D_CTH,
                    pivot_CTH = pivot_CTH,
                    worldPosition2D_CTH = worldPosition2D_CTH,
                    worldPosition2D_CDFE = worldPosition2D_CDFE,
                    sortingGroup_CTH = sortingGroup_CTH,
                    sortingGroup_CDFE = sortingGroupCDFE,
                    spriteData = new NativeSlice<SpriteData>(spriteDataArray, renderArchetypeData.stride, renderArchetypeData.count)
                }.ScheduleParallel(_renderArchetypes[renderArchetypeData.archetypeIndex].GetQueryFilteredByID(), Dependency);
                gatherSortingDataHandle = JobHandle.CombineDependencies(gatherSortingDataHandle, handle);
            }
            #endregion

            //the most expensive part
            #region sort sprites
            var sortingHandle = new SortArrayJob<SpriteData, SpriteData.GeneralComparer>
            {
                array = spriteDataArray,
                comparer = new SpriteData.GeneralComparer()
            }.Schedule(gatherSortingDataHandle);
            #endregion

            #region rearrange data
            var matrices = new NativeArray<float4x4>(spriteDataArray.Length, Allocator.TempJob);
            var fillMatricesHandle = new FillMatricesArrayJob
            {
                archetypeLayoutData = includedArchetypes,
                spriteDataArray = spriteDataArray,
                matricesArray = matrices
            }.Schedule(spriteDataArray.Length, 64, sortingHandle);

            spriteDataArray.Dispose(fillMatricesHandle);

            var gatherPropertiesHandles = new NativeArray<JobHandle>(includedArchetypes.Length, Allocator.Temp);
            for(int i = 0; i < includedArchetypes.Length; i++)
            {
                var includedArchetype = includedArchetypes[i];
                var renderArchetype = _renderArchetypes[includedArchetype.archetypeIndex];
                gatherPropertiesHandles[i] = JobHandle.CombineDependencies
                (
                    renderArchetype.GatherPropertyData(includedArchetype.count, this, Dependency),
                    renderArchetype.FillMatricesData(new NativeSlice<float4x4>(matrices, includedArchetype.stride, includedArchetype.count), fillMatricesHandle)
                );
            }
            #endregion

            #region render archetypes
            for (int i = 0; i < includedArchetypes.Length; i++)
            {
                var includedArchetype = includedArchetypes[i];
                var renderArchetype = _renderArchetypes[includedArchetype.archetypeIndex];
                gatherPropertiesHandles[i].Complete();
                renderArchetype.EndWriteComputeBuffers(includedArchetype.count);
                renderArchetype.Draw(_quad, new Bounds(new Vector3(0f, 0f, i), Vector3.one * 1000f), includedArchetype.count);
            }
            #endregion

            includedArchetypes.Dispose();
            matrices.Dispose();
        }

        #region support methods
        public bool IsRegistred(in int id)
        {
            for(int i = 0; i < _renderArchetypes.Count; i++)
                if(_renderArchetypes[i].id == id)
                    return true;
            return false;
        }

        /// <summary>
        /// Registrate unique render, which is combination of Material + MaterialPropertyBlock + set of StrcutredBuffer property names in shader.
        /// Every entity with <see cref="SpriteRenderID"/> component with ID value equal to passed ID, with <see cref="WorldPosition2D"/> and with all components which belongs to instancedPropertyNames (through [<see cref="InstancedProperty"/>] attribute) will be rendered with registered render.
        /// Entity without at least one instanced property component from instancedPropertyNames won't be rendered at all without any errors.
        /// </summary>
        /// <param name="instancedPropertyNames">names of StructuredBuffer properties in shader</param>
        /// <param name="matricesPropertyID">propety ID of transform matrices structured buffer in shader</param>
        /// <param name="capacityStep">compute buffers capacity increase step when the current limit on the number of entities is exceeded</param>
        public int RegistrateRender(in int id, Material material, string[] instancedPropertyNames, in int matricesPropertyID, MaterialPropertyBlock materialPropertyBlock = null, in int capacityStep = 1)
        {
            //generates instanced properties
            var instancedProperties = new RenderArchetype.IInstancedProperty[instancedPropertyNames.Length];
            //for componentTypes we also need LTW and SpriteRendererTag, because those must always be with 
            var componentTypes = new NativeArray<ComponentType>(instancedPropertyNames.Length + _defaultComponentTypes.Length ,Allocator.Temp);
            NativeArray<ComponentType>.Copy(_defaultComponentTypes, 0, componentTypes, instancedPropertyNames.Length, _defaultComponentTypes.Length);
            for(int i = 0; i < instancedProperties.Length; i++)
            {
                var propertyID = Shader.PropertyToID(instancedPropertyNames[i]);
                var propertyData = _instancedPropertiesFormats[propertyID];

                instancedProperties[i] = propertyData.format switch
                {
                    PropertyFormat.Float => new RenderArchetype.InstancedProperty<float>(propertyID, capacityStep, sizeof(float), propertyData.componentType),
                    PropertyFormat.Float4 => new RenderArchetype.InstancedProperty<float4>(propertyID, capacityStep, 4 * sizeof(float), propertyData.componentType),
                    PropertyFormat.Int => new RenderArchetype.InstancedProperty<int>(propertyID, capacityStep, sizeof(int), propertyData.componentType),
                    _ => throw new Exception($"There is no handle for {propertyData.format} in {GetType().Name}")
                };

                componentTypes[i] = propertyData.componentType;
            }

            var query = GetEntityQuery(componentTypes);
            query.SetSharedComponentFilter(new SpriteRenderID { id = id });
            _renderArchetypes.Add(new RenderArchetype(material, instancedProperties, query, id, matricesPropertyID, materialPropertyBlock, capacityStep));

            componentTypes.Dispose();

            return _renderArchetypes.Count - 1;
        }

        /// <summary>
        /// Binds IComponentData to shader property. Binded components will be gathered from entities during render process to be passed to shader.
        /// By default system will automatically gather and bind all component types which have [<see cref="InstancedProperty"/>] attribute to specified property.
        /// But you can use this method to manually pass bind data.
        /// </summary>
        public void BindComponentToShaderProperty(in int propertyID, Type componentType, in PropertyFormat format)
        {
            var propertyData = new InstancedPropertyData
            {
                componentType = new ComponentType(componentType, ComponentType.AccessMode.ReadOnly),
                format = format
            };
            _instancedPropertiesFormats.Add(propertyID, propertyData);
        }
        /// <summary>
        /// Binds IComponentData to shader property. Binded components will be gathered from entities during render process to be passed to shader.
        /// By default system will automatically gather and bind all component types which have [<see cref="InstancedProperty"/>] attribute to specified property.
        /// But you can use this method to manually pass bind data.
        /// </summary>
        public void BindComponentToShaderProperty(in string propertyName, Type componentType, in PropertyFormat format)
        {
            BindComponentToShaderProperty(Shader.PropertyToID(propertyName), componentType, format);
        }
        private void GatherPropertiesTypes()
        {
            foreach (var property in InstancedProperty.GetProperties())
                BindComponentToShaderProperty(property.name, property.componentType, property.format);
        }

        private NativeArray<ComponentType> GetDisableRenderingComponentTypes()
        {
            var attributeType = typeof(DisableSpriteRenderingComponent);
            var disableRenderingTypes = AppDomain.CurrentDomain.GetAssemblies().SelectMany((assembly) => assembly.GetTypes().Where((Type t) => t.GetCustomAttributes(attributeType, true).Length > 0).Select((type) => new ComponentType(type, ComponentType.AccessMode.Exclude)));
            return new NativeArray<ComponentType>(disableRenderingTypes.ToArray(), Allocator.Persistent);
        }
        
        private void GatherDefaultComponentTypes()
        {
            var disableRenderingComponentTypes = GetDisableRenderingComponentTypes();
            _defaultComponentTypes = new NativeArray<ComponentType>(disableRenderingComponentTypes.Length + 5, Allocator.Persistent);
            NativeArray<ComponentType>.Copy(disableRenderingComponentTypes, 0, _defaultComponentTypes, 0, disableRenderingComponentTypes.Length);
            var index = _defaultComponentTypes.Length;
            _defaultComponentTypes[--index] = ComponentType.ReadOnly<SpriteRendererTag>();
            _defaultComponentTypes[--index] = ComponentType.ReadOnly<WorldPosition2D>();
            _defaultComponentTypes[--index] = ComponentType.ReadOnly<Scale2D>();
            _defaultComponentTypes[--index] = ComponentType.ReadOnly<Pivot>();
            _defaultComponentTypes[--index] = ComponentType.ReadOnly<SpriteRenderID>();
            disableRenderingComponentTypes.Dispose();
        }
        #endregion
    }
}