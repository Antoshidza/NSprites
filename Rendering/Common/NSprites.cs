using System.Collections.Generic;
using System;
using UnityEngine;
using System.Reflection;
using Unity.Entities;
using System.Runtime.CompilerServices;
using System.Linq;
using Unity.Collections;

namespace NSprites
{
    public static partial class NSpritesUtils
    {
        #region add components methods
        /// <summary><inheritdoc cref="AddSpriteRenderComponents(in Entity, in EntityManager, in int, in bool)"/></summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddSpriteRenderComponents<TAuthoringType>(this Baker<TAuthoringType> baker, in int renderID = default, in bool hasPointerComponents = true)
            where TAuthoringType : Component 
            => baker.AddSpriteRenderComponents(baker.GetEntity(TransformUsageFlags.None), renderID, hasPointerComponents);

        /// <summary><inheritdoc cref="AddSpriteRenderComponents(in Entity, in EntityManager, in int, in bool)"/></summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddSpriteRenderComponents<TAuthoringType>(this Baker<TAuthoringType> baker, in Entity entity, in int renderID = default, in bool hasPointerComponents = true)
            where TAuthoringType : Component
        {
            baker.AddSharedComponent(entity, new SpriteRenderID { id = renderID });

#if !NSPRITES_REACTIVE_PROPERTIES_DISABLE || !NSPRITES_STATIC_PROPERTIES_DISABLE
            if (hasPointerComponents)
                baker.AddComponent(entity, new ComponentTypeSet
                (
                    ComponentType.ReadWrite<PropertyPointer>(),
                    ComponentType.ChunkComponentReadOnly<PropertyPointerChunk>()
                ));
#endif
        }
        /// <summary>
        /// Adds all necessary components for rendering to entity:
        /// <br>* <see cref="SpriteRenderID"></see> (empty, should be seted on play)</br>
        /// <br>* <see cref="PropertyPointer"></see> (empty, will automatically initialized by render system)</br>
        /// <br>* <see cref="PropertyPointerChunk"></see> to entity's chunk (empty, will automatically initialized by render system)</br>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddSpriteRenderComponents(in Entity entity, in EntityManager entityManager, in int renderID = default, in bool hasPointerComponents = true)
        {
            entityManager.AddSharedComponent(entity, new SpriteRenderID { id = renderID });

#if !NSPRITES_REACTIVE_PROPERTIES_DISABLE || !NSPRITES_STATIC_PROPERTIES_DISABLE
            if (hasPointerComponents)
                entityManager.AddComponent(entity, new ComponentTypeSet
                (
                    ComponentType.ReadOnly<PropertyPointer>(),
                    ComponentType.ChunkComponent<PropertyPointerChunk>()
                ));
#endif
        }
        /// <summary>
        /// Adds all necessary components for rendering to query:
        /// <br>* <see cref="SpriteRenderID"></see> (empty, should be set on play)</br>
        /// <br>* <see cref="PropertyPointer"></see> (empty, will automatically initialized by render system)</br>
        /// <br>* <see cref="PropertyPointerChunk"></see> to entity's chunk (empty, will automatically initialized by render system)</br>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddSpriteRenderComponents(in EntityQuery query, in EntityManager entityManager, in int renderID = default, in bool hasPointerComponents = true)
        {
            entityManager.AddSharedComponent(query, new SpriteRenderID { id = renderID });
#if !NSPRITES_REACTIVE_PROPERTIES_DISABLE || !NSPRITES_STATIC_PROPERTIES_DISABLE
            if (hasPointerComponents)
                entityManager.AddComponent(query, new ComponentTypeSet
                (
                    ComponentType.ReadOnly<PropertyPointer>(),
                    ComponentType.ChunkComponent<PropertyPointerChunk>()
                ));
#endif
        }
        /// <summary><inheritdoc cref="AddSpriteRenderComponents(in Entity, in EntityManager, in int, in bool)"/></summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddSpriteRenderComponents(this in EntityManager entityManager, in Entity entity, in int renderID = default, in bool hasPointerComponents = true)
        {
            AddSpriteRenderComponents(entity, entityManager, renderID, hasPointerComponents);
        }
        /// <summary><inheritdoc cref="AddSpriteRenderComponents(in EntityQuery, in EntityManager, in int, in bool)"/></summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddSpriteRenderComponents(this in EntityManager entityManager, in EntityQuery query, in int renderID = default, in bool hasPointerComponents = true)
        {
            AddSpriteRenderComponents(query, entityManager, renderID, hasPointerComponents);
        }
        #endregion

        /// <summary>
        /// Returns <b>Tiling and Offset</b> value which can be helpfull to locate texture on atlas in shader
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 GetTextureST(Sprite sprite)
        {
            var ratio = new Vector2(1f / sprite.texture.width, 1f / sprite.texture.height);
            var size = Vector2.Scale(sprite.textureRect.size, ratio);
            var offset = Vector2.Scale(sprite.textureRect.position, ratio);
            return new Vector4(size.x, size.y, offset.x, offset.y);
        }

        /// <summary>
        /// Generate a simple quad which can be used to render instanced sprites
        /// </summary>
        public static Mesh ConstructQuad()
        {
            var quad = new Mesh();
            quad.vertices = new Vector3[4]
            {
                new Vector3(0f, 1f, 0f),    //left up
                new Vector3(1f, 1f, 0f),    //right up
                new Vector3(0f, 0f, 0f),    //left down
                new Vector3(1f, 0f, 0f)     //right down
            };

            quad.triangles = new int[6]
            {
                // upper left triangle
                0, 1, 2,
                // down right triangle
                3, 2, 1
            };

            quad.normals = new Vector3[4]
            {
                -Vector3.forward,
                -Vector3.forward,
                -Vector3.forward,
                -Vector3.forward
            };

            quad.uv = new Vector2[4]
            {
                new Vector2(0f, 1f),    //left up
                new Vector2(1f, 1f),    //right up
                new Vector2(0f, 0f),    //left down
                new Vector2(1f, 0f)     //right down
            };

            return quad;
        }

        /// <summary>
        /// Returns all assembly target attribute instances from whole app. Can be used to fetch all meta data you are interested in.
        /// </summary>
        public static IEnumerable<TAttribute> GetAssemblyAttributes<TAttribute>()
            where TAttribute : Attribute
        {
            var result = new List<TAttribute>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
                result.AddRange(assembly.GetCustomAttributes<TAttribute>());
            return result;
        }

        /// <summary>
        /// Returns actual <see cref="PropertyUpdateMode"/> depending on what mode passed and what modes enabled in project
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static PropertyUpdateMode GetActualMode(in PropertyUpdateMode mode)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && mode == PropertyUpdateMode.Static)
            {
#if !NSPRITES_REACTIVE_DISABLE
                return PropertyUpdateMode.Reactive;
#elif !NSPRITES_EACH_UPDATE_DISABLE
                return PropertyUpdateMode.EachUpdate;
#endif
            }
#endif
            /// if property is <see cref="PropertyUpdateMode.Reactive"/> but this mode is disabled then switch to <see cref="PropertyUpdateMode.EachUpdate"/> if not disabled, otherwise switch to <see cref="PropertyUpdateMode.Static"/>
#if NSPRITES_REACTIVE_DISABLE
            if (mode == PropertyUpdateMode.Reactive)
#if !NSPRITES_EACH_UPDATE_DISABLE
                return PropertyUpdateMode.EachUpdate;
#else
                return PropertyUpdateMode.Static;
#endif
#endif
            /// if property is <see cref="PropertyUpdateMode.Static"/> but this mode is disabled then switch to <see cref="PropertyUpdateMode.Reactive"/> if not disabled, otherwise switch to <see cref="PropertyUpdateMode.EachUpdate"/>
#if NSPRITES_STATIC_DISABLE
            if (mode == PropertyUpdateMode.Static)
#if !NSPRITES_REACTIVE_DISABLE
                return PropertyUpdateMode.Reactive;
#else
                return PropertyUpdateMode.EachUpdate;
#endif
#endif
            /// if property is <see cref="PropertyUpdateMode.EachUpdate"/> but this mode is disabled then switch to <see cref="PropertyUpdateMode.Reactive"/> if not disabled, otherwise switch to <see cref="PropertyUpdateMode.Static"/>
#if NSPRITES_EACH_UPDATE_DISABLE
            /// if property is <see cref="PropertyUpdateMode.EachUpdate"/> but this mode is disabled then switch to <see cref="PropertyUpdateMode.Reactive"/> if not disabled, otherwise switch to <see cref="PropertyUpdateMode.Static"/>
            if (mode == PropertyUpdateMode.EachUpdate)
#if !NSPRITES_REACTIVE_DISABLE
                return PropertyUpdateMode.Reactive;
#else
                return PropertyUpdateMode.Static;
#endif
#endif
            return mode;
        }

        /// <summary>Returns array with all default components for rendering entities including types marked with <see cref="DisableRenderingComponent"/> attribute</summary>
        /// [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static NativeArray<ComponentType> GetDefaultComponentTypes(in Allocator allocator = Allocator.Temp)
        {
            var disableRenderingComponentTypes = new NativeArray<ComponentType>(DisableRenderingComponent.GetTypes().ToArray(), Allocator.Temp);
            var defaultComponents = new NativeArray<ComponentType>(disableRenderingComponentTypes.Length + 1, allocator);
            NativeArray<ComponentType>.Copy(disableRenderingComponentTypes, 0, defaultComponents, 0, disableRenderingComponentTypes.Length);
            defaultComponents[^1] = ComponentType.ReadOnly<SpriteRenderID>();
            disableRenderingComponentTypes.Dispose();
            return defaultComponents;
        }
#if UNITY_EDITOR
        /// <summary>
        /// Generate array of <see cref="InstancedProperty"/> and theirs <see cref="PropertyUpdateMode"/> of given <see cref="RenderArchetype"/>
        /// </summary>
        internal static (InstancedProperty, PropertyUpdateMode)[] GetPropertiesData(this RenderArchetype renderArchetype)
        {
            var array = new (InstancedProperty, PropertyUpdateMode)[renderArchetype._properties.Length];
            for (var i = 0; i < renderArchetype.RP_Count; i++)
                array[i] = new ValueTuple<InstancedProperty, PropertyUpdateMode>(renderArchetype._properties[i], PropertyUpdateMode.Reactive);
            for (var i = renderArchetype.SP_Offset; i < renderArchetype.SP_Offset + renderArchetype.SP_Count; i++)
                array[i] = new ValueTuple<InstancedProperty, PropertyUpdateMode>(renderArchetype._properties[i], PropertyUpdateMode.Static);
            for (var i = renderArchetype.EUP_Offset; i < renderArchetype.EUP_Offset + renderArchetype.EUP_Count; i++)
                array[i] = new ValueTuple<InstancedProperty, PropertyUpdateMode>(renderArchetype._properties[i], PropertyUpdateMode.EachUpdate);

            return array;
        }
#endif
    }
}
