using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

#region RegisterGenericJobType
//TODO: move it to manifest file
[assembly: RegisterGenericJobType(typeof(NSprites.SpriteRenderingSystem.GatherPropertyJob<int>))]
[assembly: RegisterGenericJobType(typeof(NSprites.SpriteRenderingSystem.GatherPropertyJob<int2>))]
[assembly: RegisterGenericJobType(typeof(NSprites.SpriteRenderingSystem.GatherPropertyJob<int3>))]
[assembly: RegisterGenericJobType(typeof(NSprites.SpriteRenderingSystem.GatherPropertyJob<int4>))]
[assembly: RegisterGenericJobType(typeof(NSprites.SpriteRenderingSystem.GatherPropertyJob<float>))]
[assembly: RegisterGenericJobType(typeof(NSprites.SpriteRenderingSystem.GatherPropertyJob<float2>))]
[assembly: RegisterGenericJobType(typeof(NSprites.SpriteRenderingSystem.GatherPropertyJob<float3>))]
[assembly: RegisterGenericJobType(typeof(NSprites.SpriteRenderingSystem.GatherPropertyJob<float4>))]
#endregion

namespace NSprites
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class SpriteRenderingSystem : SystemBase
    {
        #region data
        // TODO: rewrite this part, explain more
        // assuming there is no batching for different materials/textures/other not-instanced properties we can define some kind of render archetypes
        // it is combination of material + instanced properties set
        internal class RenderArchetype : IDisposable
        {
            internal interface IInstancedProperty : IDisposable
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
                where T : unmanaged
            {
                private int _propertyID;
                private ComponentType _componentType;
                private ComputeBuffer _computeBuffer;

                internal InstancedProperty(in int propertyID, in int count, in int stride, in ComponentType componentType)
                {
                    _propertyID = propertyID;
                    _componentType = componentType;
                    _computeBuffer = new ComputeBuffer(count, stride, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
                }
                public void Dispose() => _computeBuffer.Dispose();
                public JobHandle GatherData(in EntityQuery spriteQuery, in int length, in JobHandle inputDeps, SystemBase system)
                {
                    return new GatherPropertyJob<T>
                    {
                        componentTypeHandle = system.GetDynamicComponentTypeHandle(_componentType),
                        typeSize = _computeBuffer.stride,
                        outputArray = _computeBuffer.BeginWrite<T>(0, length)
                    }.ScheduleParallel(spriteQuery, inputDeps);   
                }
                public void EndWrite(in int count)
                {
                    _computeBuffer.EndWrite<T>(count);
                }
                public void Resize(in int size)
                {
                    var stride = _computeBuffer.stride;
                    _computeBuffer.Release();
                    _computeBuffer = new ComputeBuffer(size, stride, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
                }
                public void PassToMaterialPropertyBlock(MaterialPropertyBlock materialPropertyBlock)
                {
                    materialPropertyBlock.SetBuffer(_propertyID, _computeBuffer);
                }
            }

            private readonly int _id;
            private readonly Material _material;
            private readonly MaterialPropertyBlock _materialPropertyBlock;
            private readonly EntityQuery _query;
            private int _count;
            private readonly int _capacityStep; 
            private readonly IInstancedProperty[] _instancedProperties;
            private NativeArray<JobHandle> _gatherDataHandles;

            public RenderArchetype(Material material, IInstancedProperty[] instancedProperties, in EntityQuery query, in int id, MaterialPropertyBlock overrideMPB = null, in int capacityStep = 512)
            {
                _id = id;
                _query = query;
                _material = material;
                _count = capacityStep;
                _capacityStep = capacityStep;
                _materialPropertyBlock = overrideMPB ?? new MaterialPropertyBlock();
                _instancedProperties = instancedProperties;

                for(int i = 0; i < instancedProperties.Length; i++)
                    instancedProperties[i].PassToMaterialPropertyBlock(_materialPropertyBlock);

                _gatherDataHandles = new NativeArray<JobHandle>(_instancedProperties.Length, Allocator.Persistent);
            }
            public void Dispose()
            {
                _gatherDataHandles.Dispose();
                foreach (var instancedProperty in _instancedProperties)
                    instancedProperty.Dispose();
            }
            public EntityQuery GetQueryFilteredByID()
            {
                _query.SetSharedComponentFilter(new SpriteRenderID() { id = _id });
                return _query;
            }
            public JobHandle GatherPropertyData(in int length, SystemBase system, in JobHandle inputDeps = default)
            {
                _query.SetSharedComponentFilter(new SpriteRenderID() { id = _id });
                if (_count < length)
                {
                    _count = GetRequiredSize(length);
                    for(int i = 0; i < _instancedProperties.Length; i++)
                    {
                        var property = _instancedProperties[i];
                        property.Resize(_count);
                        property.PassToMaterialPropertyBlock(_materialPropertyBlock);
                        _gatherDataHandles[i] = property.GatherData(_query, length, inputDeps, system);
                    }
                }
                else
                    for(int i = 0; i < _instancedProperties.Length; i++)
                        _gatherDataHandles[i] = _instancedProperties[i].GatherData(_query, length, inputDeps, system);

                return JobHandle.CombineDependencies(_gatherDataHandles);
            }
            public void EndWriteComputeBuffers(int count)
            {
                for(int i = 0; i < _instancedProperties.Length; i++)
                    _instancedProperties[i].EndWrite(count);
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
        internal struct IncludedRenderArchetypeData
        {
            /// <summary>int reference to _renderArchetypes</summary>
            public int archetypeIndex;
            /// <summary>sprite count</summary>
            public int count;
        }
        #endregion

        #region jobs
        [BurstCompile]
        internal struct GatherPropertyJob<TProperty> : IJobChunk
            where TProperty : unmanaged
            // TPropety supposed to be: int/int2/int3/int4/float/float2/float3/float4
            // TODO: implement int2x2/int3x3/int4x4/float2x2/float3x3/float4x4 because HLSL only supports square matricies
        {
            // this should be filled every frame with GetDynamicComponentTypeHandle
            [ReadOnly] public DynamicComponentTypeHandle componentTypeHandle;
            public int typeSize;
            [WriteOnly][NativeDisableParallelForRestriction] public NativeArray<TProperty> outputArray;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                var data = chunk.GetDynamicComponentDataArrayReinterpret<TProperty>(componentTypeHandle, typeSize);
                NativeArray<TProperty>.Copy(data,0,outputArray,firstEntityIndex,data.Length);
            }
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
            var includedArchetypes = new NativeList<IncludedRenderArchetypeData>(_renderArchetypes.Count, Allocator.Temp);
            for(int i = 0; i < _renderArchetypes.Count; i++)
            {
                //for some reason we need to update this SetSharedComponentFilter every time we get 
                var spriteCount = _renderArchetypes[i].GetQueryFilteredByID().CalculateEntityCount();
                if (spriteCount > 0)
                    includedArchetypes.Add(new IncludedRenderArchetypeData() { archetypeIndex = i, count = spriteCount });
            }

            if(includedArchetypes.Length == 0)
            {
                includedArchetypes.Dispose();
                return;
            }
            #endregion

            #region gather properties data
            var gatherPropertiesHandles = new NativeArray<JobHandle>(includedArchetypes.Length, Allocator.Temp);
            for(int i = 0; i < includedArchetypes.Length; i++)
            {
                var includedArchetype = includedArchetypes[i];
                gatherPropertiesHandles[i] = _renderArchetypes[includedArchetype.archetypeIndex].GatherPropertyData(includedArchetype.count, this, Dependency);
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
        }

        #region support methods
        /// <summary>
        /// Registrate unique render, which is combination of Material + MaterialPropertyBlock + set of StrcutredBuffer property names in shader.
        /// Every entity with <see cref="SpriteRenderID"/> component with ID value equal to passed ID, with <see cref="WorldPosition2D"/> and with all components which belongs to instancedPropertyNames (through [<see cref="InstancedPropertyComponent"/>] attribute) will be rendered with registered render.
        /// Entity without at least one instanced property component from instancedPropertyNames won't be rendered at all without any errors.
        /// </summary>
        /// <param name="instancedPropertyNames">names of StructuredBuffer properties in shader</param>
        /// <param name="matricesPropertyID">propety ID of transform matrices structured buffer in shader</param>
        /// <param name="capacityStep">compute buffers capacity increase step when the current limit on the number of entities is exceeded</param>
        public int RegistrateRender(in int id, Material material, string[] instancedPropertyNames, MaterialPropertyBlock materialPropertyBlock = null, in int capacityStep = 1)
        {
            //generates instanced properties
            var instancedProperties = new RenderArchetype.IInstancedProperty[instancedPropertyNames.Length];
            var componentTypes = new NativeArray<ComponentType>(instancedPropertyNames.Length + _defaultComponentTypes.Length ,Allocator.Temp);
            NativeArray<ComponentType>.Copy(_defaultComponentTypes, 0, componentTypes, instancedPropertyNames.Length, _defaultComponentTypes.Length);
            for(int i = 0; i < instancedProperties.Length; i++)
            {
                var propertyID = Shader.PropertyToID(instancedPropertyNames[i]);
                var propertyData = _instancedPropertiesFormats[propertyID];

                instancedProperties[i] = propertyData.format switch
                {
                    ///TODO: replace num * sizeof(T) with <see cref="Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf(Type)"> inside <see cref="RenderArchetype.InstancedProperty{T}"> constructor
                    PropertyFormat.Float => new RenderArchetype.InstancedProperty<float>(propertyID, capacityStep, sizeof(float), propertyData.componentType),
                    PropertyFormat.Float2 => new RenderArchetype.InstancedProperty<float2>(propertyID, capacityStep, 2 * sizeof(float), propertyData.componentType),
                    PropertyFormat.Float3 => new RenderArchetype.InstancedProperty<float3>(propertyID, capacityStep, 3 * sizeof(float), propertyData.componentType),
                    PropertyFormat.Float4 => new RenderArchetype.InstancedProperty<float4>(propertyID, capacityStep, 4 * sizeof(float), propertyData.componentType),
                    PropertyFormat.Int => new RenderArchetype.InstancedProperty<int>(propertyID, capacityStep, sizeof(int), propertyData.componentType),
                    PropertyFormat.Int2 => new RenderArchetype.InstancedProperty<int2>(propertyID, capacityStep, 2 * sizeof(int), propertyData.componentType),
                    PropertyFormat.Int3 => new RenderArchetype.InstancedProperty<int3>(propertyID, capacityStep, 3 * sizeof(int), propertyData.componentType),
                    PropertyFormat.Int4 => new RenderArchetype.InstancedProperty<int4>(propertyID, capacityStep, 4 * sizeof(int), propertyData.componentType),
                    _ => throw new Exception($"There is no handle for {propertyData.format} in {GetType().Name}")
                };

                componentTypes[i] = propertyData.componentType;
            }

            var query = GetEntityQuery(componentTypes);
            query.SetSharedComponentFilter(new SpriteRenderID { id = id });
            _renderArchetypes.Add(new RenderArchetype(material, instancedProperties, query, id, materialPropertyBlock, capacityStep));

            componentTypes.Dispose();

            return _renderArchetypes.Count - 1;
        }

        /// <summary>
        /// Binds IComponentData to shader property. Binded components will be gathered from entities during render process to be passed to shader.
        /// By default system will automatically gather and bind all component types which have [<see cref="InstancedPropertyComponent"/>] attribute to specified property.
        /// But you can use this method to manually pass bind data.
        /// </summary>
        public void BindComponentToShaderProperty(in int propertyID, Type componentType, in PropertyFormat format)
        {
            _instancedPropertiesFormats.Add
            (
                propertyID,
                new InstancedPropertyData
                {
                    componentType = new ComponentType(componentType, ComponentType.AccessMode.ReadOnly),
                    format = format
                }
            );
        }
        /// <summary>
        /// Binds IComponentData to shader property. Binded components will be gathered from entities during render process to be passed to shader.
        /// By default system will automatically gather and bind all component types which have [<see cref="InstancedPropertyComponent"/>] attribute to specified property.
        /// But you can use this method to manually pass bind data.
        /// </summary>
        public void BindComponentToShaderProperty(in string propertyName, Type componentType, in PropertyFormat format)
        {
            BindComponentToShaderProperty(Shader.PropertyToID(propertyName), componentType, format);
        }
        private void GatherPropertiesTypes()
        {
            foreach (var property in InstancedPropertyComponent.GetProperties())
                BindComponentToShaderProperty(property.name, property.componentType, property.format);
        }

        private void GatherDefaultComponentTypes()
        {
            var disableRenderingComponentTypes = new NativeArray<ComponentType>(DisableRenderingComponent.GetTypes().ToArray(), Allocator.Persistent);
            _defaultComponentTypes = new NativeArray<ComponentType>(disableRenderingComponentTypes.Length + 2, Allocator.Persistent);
            NativeArray<ComponentType>.Copy(disableRenderingComponentTypes, 0, _defaultComponentTypes, 0, disableRenderingComponentTypes.Length);
            var index = _defaultComponentTypes.Length;
            _defaultComponentTypes[--index] = ComponentType.ReadOnly<SpriteRendererTag>();
            _defaultComponentTypes[--index] = ComponentType.ReadOnly<SpriteRenderID>();
            disableRenderingComponentTypes.Dispose();
        }
        #endregion
    }
}