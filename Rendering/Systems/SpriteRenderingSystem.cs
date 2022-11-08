using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace NSprites
{
    /// <summary>
    /// Renders entities (both in runtime and editor) with <see cref="SpriteRenderID"/> : <see cref="ISharedComponentData"/> as 2D sprites depending on registered data through <see cref="RegistrateRender"/>
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class SpriteRenderingSystem : SystemBase
    {
        /// <summary><see cref="Mesh"/> We will use to render every sprite, which can be created once in system</summary>
        private readonly Mesh _quad = NSpritesUtils.ConstructQuad();
        /// <summary>Shader property's id to property data map</summary>
        private readonly Dictionary<int, InstancedPropertyData> _propetyMap = new();
        /// <summary>All whenever registered render archetypes. Each registred archetype will be updated every frame no matter if there is any entities.</summary>
        private readonly List<RenderArchetype> _renderArchetypes = new();
        /// <summary>System's state with all necessary data to pass to <see cref="RenderArchetype"/> to update</summary>
        private SystemState _state;

        protected override void OnCreate()
        {
#if NSPRITES_REACTIVE_DISABLE && NSPRITES_STATIC_DISABLE && NSPRITES_EACH_UPDATE_DISABLE
            throw new Exception($"You can't disable Reactive, Static and Each-Update properties modes at the same time, there should be at least one mode if you want system to work. Please, enable at least one mode.");
#endif
            base.OnCreate();
            GatherPropertiesTypes();
            
            _state.system = this;
            _state.query = GetEntityQuery(GetDefaultComponentTypes());
        }
        protected override void OnDestroy()
        {
            base.OnDestroy();
            foreach (var archetype in _renderArchetypes)
                archetype.Dispose();
        }

        protected override void OnUpdate()
        {
            // update state to pass to render archetypes
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
            _state.lastSystemVersion = LastSystemVersion;
            _state.propertyPointer_CTH_RW = GetComponentTypeHandle<PropertyPointer>(false);
            _state.propertyPointerChunk_CTH_RW = GetComponentTypeHandle<PropertyPointerChunk>(false);
            _state.propertyPointerChunk_CTH_RO = GetComponentTypeHandle<PropertyPointerChunk>(true);
#endif
            _state.inputDeps = Dependency;

            // schedule render archetype's properties data update
            var renderArchetypeHandles = new NativeArray<JobHandle>(_renderArchetypes.Count, Allocator.Temp);
            for (int archetypeIndex = 0; archetypeIndex < _renderArchetypes.Count; archetypeIndex++)
                renderArchetypeHandles[archetypeIndex] = _renderArchetypes[archetypeIndex].ScheduleUpdate(_state);

            // force complete properties data update and draw archetypes
            for (int archetypeIndex = 0; archetypeIndex < _renderArchetypes.Count; archetypeIndex++)
            {
                var archetype = _renderArchetypes[archetypeIndex];
                archetype.CompleteUpdate();
                archetype.Draw(_quad, new Bounds(new Vector3(0f, 0f, archetypeIndex), Vector3.one * 1000f));
            }

            // combine handles from all render archetypes we have updated
            Dependency = JobHandle.CombineDependencies(renderArchetypeHandles);
        }

#region support methods
        /// <summary>
        /// Registrate render, which is combination of Material + set of StrcutredBuffer property names in shader.
        /// Every entity with <see cref="SpriteRenderID"/> component with ID value equal to passed ID, will be rendered by registered render.
        /// Entity without instanced property component from passed properties will be rendered with uninitialized values (please, initialize entities carefully, because render with uninitialized values can lead to strange visual results).
        /// Though you can use <b><see cref="NSPRITES_PROPERTY_FALLBACK_ENABLE"/></b> directive to enable fallback values, so any chunk without property component will pass default values.
        /// </summary>
        /// <param name="id">ID of <see cref="SpriteRenderID.id"/>. All entities with the same SCD will be updated by registering render archetype. Client should manage uniqueness (or not) of ids by himself.</param>
        /// <param name="material"><see cref="Material"/> wich will be used to render sprites.</param>
        /// <param name="materialPropertyBlock"><see cref="MaterialPropertyBlock"/> you can pass if you want to do some extra overriding by yourself.</param>
        /// <param name="instancedPropertyNames">names of StructuredBuffer properties in shader.</param>
        /// <param name="capacity">compute buffers intial capacity.</param>
        /// <param name="capacityStep">compute buffers capacity increase step when the current limit on the number of entities is exceeded.</param>
        public void RegistrateRender(in int id, Material material, string[] instancedPropertyNames, MaterialPropertyBlock materialPropertyBlock = null, in int capacity = 1, in int capacityStep = 1)
        {
            _renderArchetypes.Add(new RenderArchetype(material, instancedPropertyNames, _propetyMap, id, this, materialPropertyBlock, capacity, capacityStep));
        }

        /// <summary>
        /// Binds component to shader's property. Binded components will be gathered from entities during render process to be passed to shader.
        /// By default system will automatically gather and bind all component types which have <see cref="InstancedPropertyComponent"/> attribute to specified property.
        /// But you can use this method to manually pass bind data.
        /// </summary>
        public void BindComponentToShaderProperty(in int propertyID, Type componentType, in PropertyFormat format, in PropertyUpdateMode updateMode = default)
        {
            _propetyMap.Add(propertyID, new InstancedPropertyData(new ComponentType(componentType, ComponentType.AccessMode.ReadOnly), format, updateMode));
        }
        /// <summary>
        /// Binds component to shader's property. Binded components will be gathered from entities during render process to be passed to shader.
        /// By default system will automatically gather and bind all component types which have <see cref="InstancedPropertyComponent"/> attribute to specified property.
        /// But you can use this method to manually pass bind data.
        /// </summary>
        public void BindComponentToShaderProperty(in string propertyName, Type componentType, in PropertyFormat format, in PropertyUpdateMode updateMode = default)
        {
            BindComponentToShaderProperty(Shader.PropertyToID(propertyName), componentType, format, updateMode);
        }

        /// <summary>Fills <see cref="_propetyMap"/> with data of all types marked by <see cref="InstancedPropertyComponent"/> attribute</summary>
        private void GatherPropertiesTypes()
        {
            foreach (var property in InstancedPropertyComponent.GetProperties())
                BindComponentToShaderProperty(property.propertyName, property.componentType, property.format, property.updateMode);
        }
        /// <summary>Returns array with all default components for rendering entities including types marked with <see cref="DisableRenderingComponent"/> attribute</summary>
        private NativeArray<ComponentType> GetDefaultComponentTypes(in Allocator allocator = Allocator.Temp)
        {
            var disableRenderingComponentTypes = new NativeArray<ComponentType>(DisableRenderingComponent.GetTypes().ToArray(), Allocator.Persistent);
            var defaultComponents = new NativeArray<ComponentType>(disableRenderingComponentTypes.Length + 1, allocator);
            NativeArray<ComponentType>.Copy(disableRenderingComponentTypes, 0, defaultComponents, 0, disableRenderingComponentTypes.Length);
            defaultComponents[defaultComponents.Length - 1] = ComponentType.ReadOnly<SpriteRenderID>();
            disableRenderingComponentTypes.Dispose();
            return defaultComponents;
        }
#endregion
    }
}