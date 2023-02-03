using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace NSprites
{
    public class RenderArchetypeStorage : IComponentData
    {
        /// <summary><see cref="Mesh"/> We will use to render every sprite, which can be created once in system</summary>
        internal Mesh Quad = NSpritesUtils.ConstructQuad();
        /// <summary>Shader property's id to property data map</summary>
        internal readonly Dictionary<int, PropertyInternalData> PropertyMap = new();
        /// <summary>All whenever registered render archetypes. Each registered archetype will be updated every frame no matter if there is any entities.</summary>
        internal readonly List<RenderArchetype> RenderArchetypes = new();
        /// <summary>System's state with all necessary data to pass to <see cref="RenderArchetype"/> to update</summary>
        internal SystemData SystemData;

        internal void Dispose()
        {
            foreach (var archetype in RenderArchetypes)
                archetype.Dispose();
        }
        internal void Initialize() => GatherPropertiesTypes();
        /// <summary>
        /// Binds component to shader's property. Binded components will be gathered from entities during render process to be passed to shader.
        /// By default system will automatically gather and bind all component types which have <see cref="InstancedPropertyComponent"/> attribute to specified property.
        /// But you can use this method to manually pass bind data.
        /// </summary>
        public void BindComponentToShaderProperty(in int propertyID, Type componentType, in PropertyFormat format)
        {
#if UNITY_EDITOR
            if (PropertyMap.ContainsKey(propertyID))
            {
                var propertyComponent = PropertyMap[propertyID];
                Debug.LogError($"It seems you're trying to bind multiple components to same shader property {propertyID}.\nYou're trying to add: {componentType.Name} {format}\nAlready registered: {propertyComponent.componentType.GetManagedType().Name} {propertyComponent.format}");
                return;
            }
#endif
            PropertyMap.Add(propertyID,
                new PropertyInternalData(new ComponentType(componentType, ComponentType.AccessMode.ReadOnly), format));
        }

        /// <summary>
        /// Binds component to shader's property. Binded components will be gathered from entities during render process to be passed to shader.
        /// By default system will automatically gather and bind all component types which have <see cref="InstancedPropertyComponent"/> attribute to specified property.
        /// But you can use this method to manually pass bind data.
        /// </summary>
        public void BindComponentToShaderProperty(in string propertyName, Type componentType, in PropertyFormat format)
            => BindComponentToShaderProperty(Shader.PropertyToID(propertyName), componentType, format);

        /// <summary>Fills <see cref="PropertyMap"/> with data of all types marked by <see cref="InstancedPropertyComponent"/> attribute</summary>
        private void GatherPropertiesTypes()
        {
            foreach (var property in InstancedPropertyComponent.GetProperties())
                BindComponentToShaderProperty(property.propertyName, property.componentType, property.format);
        }

        /// <summary>
        /// Register render, which is combination of Material + set of StructuredBuffer property names in shader.
        /// Every entity with <see cref="SpriteRenderID"/> component with ID value equal to passed ID, will be rendered by registered render.
        /// Entity without instanced property component from passed properties will be rendered with uninitialized values (please, initialize entities carefully, because render with uninitialized values can lead to strange visual results).
        /// Though you can use <b><see cref="NSPRITES_PROPERTY_FALLBACK_ENABLE"/></b> directive to enable fallback values, so any chunk without property component will pass default values.
        /// </summary>
        /// <param name="id">ID of <see cref="SpriteRenderID.id"/>. All entities with the same SCD will be updated by registering render archetype. Client should manage uniqueness (or not) of ids by himself.</param>
        /// <param name="material"><see cref="Material"/> which will be used to render sprites.</param>
        /// <param name="materialPropertyBlock"><see cref="MaterialPropertyBlock"/> you can pass if you want to do some extra overriding by yourself.</param>
        /// <param name="propertyDataSet">IDs of StructuredBuffer properties in shader AND <see cref="PropertyUpdateMode"/> for each property.</param>
        /// <param name="initialCapacity">compute buffers initial capacity.</param>
        /// <param name="capacityStep">compute buffers capacity increase step when the current limit on the number of entities is exceeded.</param>
        public void RegisterRender(in int id, Material material, MaterialPropertyBlock materialPropertyBlock = null, in int initialCapacity = 1, in int capacityStep = 1, params PropertyData[] propertyDataSet)
            => RenderArchetypes.Add(new RenderArchetype(material, propertyDataSet, PropertyMap, id, materialPropertyBlock, initialCapacity, capacityStep));
    }
}