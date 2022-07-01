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
using UnityEngine.Rendering;

[assembly: RegisterGenericJobType(typeof(NSprites.SpriteRenderingSystem.GatherPropertyJob<int>))]
[assembly: RegisterGenericJobType(typeof(NSprites.SpriteRenderingSystem.GatherPropertyJob<float>))]
[assembly: RegisterGenericJobType(typeof(NSprites.SpriteRenderingSystem.GatherPropertyJob<float4>))]
namespace NSprites
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public class SpriteRenderingSystem : SystemBase
    {
        //assuming that there is no batching for different materials/textures/other not-instanced properties we can define some kind of render archetypes
        //it is combination of material index + uniqueness of properties + all per-material properties values (mostly textures, because they are reason why we do this)
        //if sprites uses different IRenderArchetype then they should be rendered in different calls
        private class RenderArchetype
        {
            public interface IInstancedProperty
            {
                /// <summary>
                /// Calls BeginWrite on it's compute buffer with sortingDatas.Length count. Before use MPB in rendering we should call EndWrite() before rendering.
                /// </summary>
                public JobHandle GatherData(in EntityQuery spriteQuery, in int length, in JobHandle inputDeps, SystemBase system);
                public JobHandle GatherOrderedData(in EntityQuery spriteQuery, in NativeSlice<int> orderMap, in JobHandle inputDeps, SystemBase system);
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
                public JobHandle GatherData(in EntityQuery spriteQuery, in int length, in JobHandle inputDeps, SystemBase system)
                {
                    return new GatherPropertyJob<T>
                    {
                        componentTypeHandle = system.GetDynamicComponentTypeHandle(componentType),
                        typeSize = computeBuffer.stride,
                        outputArray = computeBuffer.BeginWrite<T>(0, length)
                    }.ScheduleParallel(spriteQuery, inputDeps);   
                }
                public JobHandle GatherOrderedData(in EntityQuery spriteQuery, in NativeSlice<int> orderMap, in JobHandle inputDeps, SystemBase system)
                {
                    return new GatherPropertyByOrderMapJob<T>
                    {
                        componentTypeHandle = system.GetDynamicComponentTypeHandle(componentType),
                        typeSize = computeBuffer.stride,
                        outputArray = computeBuffer.BeginWrite<T>(0, orderMap.Length),
                        orderMap = orderMap
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
            public readonly Material material;
            public readonly MaterialPropertyBlock materialPropertyBlock;
            public EntityQuery query;

            public ComputeBuffer matricesBuffer;
            private const int LTW_BUFFER_STRIDE = 16 * sizeof(float);
            private int _matricesPropertyID;

            private int _size;
            private int _capacityStep;
            private IInstancedProperty[] _instancedProperties;

            public RenderArchetype(Material material, IInstancedProperty[] instancedProperties, EntityQuery query, int id, int matricesPropertyID, MaterialPropertyBlock overrideMPB = null, int capacityStep = 1)
            {
                this.material = material;
                this.query = query;
                this.id = id;

                _matricesPropertyID = matricesPropertyID;
                _size = capacityStep;
                _capacityStep = capacityStep;

                //for each instanced property in shader copy it from material to MPB. this data will be immutable
                materialPropertyBlock = overrideMPB == null ? new MaterialPropertyBlock() : overrideMPB;
                var propertyCount = material.shader.GetPropertyCount();
                for(int i = 0; i < propertyCount; i++)
                {
                    var propertyType = material.shader.GetPropertyType(i);
                    var propertyID = material.shader.GetPropertyNameId(i);
                    switch(propertyType)
                    {
                        case ShaderPropertyType.Color:
                            materialPropertyBlock.SetColor(propertyID, material.GetColor(propertyID));
                            break;
                        case ShaderPropertyType.Vector:
                            materialPropertyBlock.SetVector(propertyID, material.GetVector(propertyID));
                            break;
                        case ShaderPropertyType.Float:
                            materialPropertyBlock.SetFloat(propertyID, material.GetFloat(propertyID));
                            break;
                        case ShaderPropertyType.Texture:
                            materialPropertyBlock.SetTexture(propertyID, material.GetTexture(propertyID));
                            break;
                        case ShaderPropertyType.Range:
                        default:
                            throw new Exception($"There is no handle for {propertyType} in {GetType().Name}");
                    }
                }

                _instancedProperties = instancedProperties;
                for(int i = 0; i < instancedProperties.Length; i++)
                    instancedProperties[i].PassToMaterialPropertyBlock(materialPropertyBlock);

                matricesBuffer = new ComputeBuffer(_size, LTW_BUFFER_STRIDE, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
                materialPropertyBlock.SetBuffer(_matricesPropertyID, matricesBuffer);
            }
            public JobHandle GatherPropertyData(in int length, SystemBase system, JobHandle inputDeps = default)
            {
                var resultHandle = new JobHandle();

                void ScheduleGatheringData(IInstancedProperty property, in int length)
                {
                    query.SetSharedComponentFilter(new SpriteRenderID() { id = id });
                    var gatherhandle = property.GatherData(query, length, inputDeps, system);
                    resultHandle = JobHandle.CombineDependencies(resultHandle, gatherhandle);
                }
                if(_size < length)
                {
                    _size = GetRequiredSize(length);
                    matricesBuffer.Release();
                    matricesBuffer = new ComputeBuffer(_size, LTW_BUFFER_STRIDE, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
                    materialPropertyBlock.SetBuffer(_matricesPropertyID, matricesBuffer);
                    for(int i = 0; i < _instancedProperties.Length; i++)
                    {
                        var property = _instancedProperties[i];
                        property.Resize(_size);
                        property.PassToMaterialPropertyBlock(materialPropertyBlock);
                        ScheduleGatheringData(property, length);
                    }
                }
                else
                    for(int i = 0; i < _instancedProperties.Length; i++)
                        ScheduleGatheringData(_instancedProperties[i], length);

                return resultHandle;
            }
            public JobHandle FillMatricesData(NativeSlice<float4x4> matrices, JobHandle inputDeps)
            {
                var matricesArray = matricesBuffer.BeginWrite<float4x4>(0, matrices.Length);
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
                matricesBuffer.EndWrite<float4x4>(count);
            }
            private int GetRequiredSize(int count)
            {
                return ((count - 1) / _capacityStep + 1) * _capacityStep;
            }
        }

        public enum PropertyFormat
        {
            Float,
            Float4,
            Int
        }
        internal struct PropertyData
        {
            public ComponentType componentType;
            public PropertyFormat format;
            public int stride;
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

                        id = entity.Index,
                        groupID = sortingGroup.groupID.Index,
                        groupSortingIndex = groupSortingIndex,
                        groupPosition = groupPosition,
                        sortingIndex = sortingGroup.index,
                        position = position,
                        scale = scale2DArray[i].value,
                        pivot = pivotArray[i].value
                    };
                }
            }
        }
        [BurstCompile]
        internal struct GatherPropertyByOrderMapJob<TProperty> : IJobChunk
            where TProperty : struct
            //TPropety supposed to be:
            //  * float
            //  * float4
            //  * float4x4
        {
            //this should be filled every frame with GetDynamicComponentTypeHandle
            [ReadOnly] public DynamicComponentTypeHandle componentTypeHandle;
            public int typeSize;
            [NoAlias][ReadOnly] public NativeSlice<int> orderMap;
            [NoAlias][WriteOnly][NativeDisableParallelForRestriction] public NativeArray<TProperty> outputArray;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                var dataArray = chunk.GetDynamicComponentDataArrayReinterpret<TProperty>(componentTypeHandle, typeSize);
                for(int i = 0; i < dataArray.Length; i++)
                    outputArray[orderMap[firstEntityIndex + i]] = dataArray[i];
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

            private const float PER_INDEX_OFFSET = .0001f; //below this value camera doesn't recognize difference

            public void Execute(int index)
            {
                var spriteData = spriteDataArray[index];
                var renderPosition = spriteData.position - spriteData.scale * spriteData.pivot;
                matricesArray[archetypeLayoutData[spriteData.archetypeIncludedIndex].stride + spriteData.entityInQueryIndex] = float4x4.TRS
                (
                    new float3(renderPosition.x, renderPosition.y, PER_INDEX_OFFSET * index),
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
        #endregion

        private Mesh _quad;
        private Dictionary<int, PropertyData> _instancedPropertiesFormats = new Dictionary<int, PropertyData>();
        private NativeArray<ComponentType> _defaultComponentTypes;
        private List<RenderArchetype> _renderArchetypes = new List<RenderArchetype>();

        protected override void OnCreate()
        {
            base.OnCreate();
            _quad = Utils.ConstructQuad();
            GatherPropertiesTypes();
            GatherDefaultComponentTypes();
        }
        protected override void OnDestroy()
        {
            base.OnDestroy();
            _defaultComponentTypes.Dispose();
        }
        protected override void OnUpdate()
        {
            #region calculate sprite counts
            var includedArchetypes = new NativeList<RenderArchetypeForSorting>(_renderArchetypes.Count, Allocator.TempJob);
            var totalSpriteCount = 0;
            for(int i = 0; i < _renderArchetypes.Count; i++)
            {
                var renderArchetype = _renderArchetypes[i];
                var query = renderArchetype.query;
                query.SetSharedComponentFilter(new SpriteRenderID() { id = renderArchetype.id });
                _renderArchetypes[i].query = query;
                var spriteCount = query.CalculateEntityCount();
                if(spriteCount > 0)
                {
                    includedArchetypes.Add(new RenderArchetypeForSorting() { id = renderArchetype.id, archetypeIndex = i, stride = totalSpriteCount, count = spriteCount });
                    totalSpriteCount += spriteCount;
                }
            }
            #endregion

            if(includedArchetypes.Length == 0)
            {
                includedArchetypes.Dispose();
                return;
            }

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
                var query = _renderArchetypes[renderArchetypeData.archetypeIndex].query;
                query.SetSharedComponentFilter(new SpriteRenderID() { id = renderArchetypeData.id });
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
                }.ScheduleParallel(query, Dependency);
                gatherSortingDataHandle = JobHandle.CombineDependencies(gatherSortingDataHandle, handle);
            }
            #endregion

            //the most expensive part
            var sortingHandle = Job.WithCode(() => { spriteDataArray.Sort(new SpriteData.GeneralComparer()); })
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                .WithName("Sorting")
#endif
                .WithBurst().Schedule(gatherSortingDataHandle);

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

            #region render archetypes
            for(int i = 0; i < includedArchetypes.Length; i++)
            {
                var includedArchetype = includedArchetypes[i];
                var renderArchetype = _renderArchetypes[includedArchetype.archetypeIndex];
                gatherPropertiesHandles[i].Complete();
                renderArchetype.EndWriteComputeBuffers(includedArchetype.count);

                Graphics.DrawMeshInstancedProcedural
                (
                    _quad,
                    0,
                    renderArchetype.material,
                    new Bounds(new Vector3(0f, 0f, i),
                    Vector3.one * 1000f),
                    includedArchetype.count,
                    renderArchetype.materialPropertyBlock
                );
            }
            #endregion

            includedArchetypes.Dispose();
            matrices.Dispose();
        }

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
            var propertyData = new PropertyData
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
            var instancedPropertyAttributeType = typeof(InstancedProperty);
            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach(var type in assembly.GetTypes())
                {
                    var instancedPropertiesAttributes = type.GetCustomAttributes(instancedPropertyAttributeType, true);
                    if(instancedPropertiesAttributes.Length > 0)
                        foreach(InstancedProperty instancedPropertyAttribute in instancedPropertiesAttributes)
                            BindComponentToShaderProperty(instancedPropertyAttribute.name, type, instancedPropertyAttribute.format);
                }
            }
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
    }
}