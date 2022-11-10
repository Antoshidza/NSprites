using System.Collections.Generic;
using System;
using UnityEngine;
using System.Reflection;
using Unity.Entities;
using System.Runtime.CompilerServices;

namespace NSprites
{
    public static class NSpritesUtils
    {
        #region add components methods
        /// <summary>
        /// Adds all necessary components for rendering to entity:
        /// <br>* <see cref="SpriteRenderID"></see> (empty, should be seted on play)</br>
        /// <br>* <see cref="PropertyPointer"></see> (empty, will automatically initialized by render system)</br>
        /// <br>* <see cref="PropertyPointerChunk"></see> to entity's chunk (empty, will automatically initialized by render system)</br>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddSpriteRenderComponents(in Entity entity, in EntityManager entityManager, in int renderID = default, in bool hasReactiveComponents = true)
        {
            entityManager.AddSharedComponentData(entity, new SpriteRenderID { id = renderID });

#if !NSPRITES_REACTIVE_PROPERTIES_DISABLE || !NSPRITES_STATIC_PROPERTIES_DISABLE
            if (hasReactiveComponents)
            {
                entityManager.AddComponentData(entity, new PropertyPointer());
                entityManager.AddChunkComponentData<PropertyPointerChunk>(entity);
            }
#endif
        }
        /// <summary>
        /// Adds all necessary components for rendering to entity:
        /// <br>* <see cref="SpriteRenderID"></see> (empty, should be seted on play)</br>
        /// <br>* <see cref="PropertyPointer"></see> (empty, will automatically initialized by render system)</br>
        /// <br>* <see cref="PropertyPointerChunk"></see> to entity's chunk (empty, will automatically initialized by render system)</br>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddSpriteRenderComponents(this in EntityManager entityManager, in Entity entity, in int renderID = default, in bool hasReactiveComponents = true)
        {
            AddSpriteRenderComponents(entity, entityManager, renderID, hasReactiveComponents);
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
            var qaud = new Mesh();
            qaud.vertices = new Vector3[4]
            {
                new Vector3(0f, 1f, 0f),    //left up
                new Vector3(1f, 1f, 0f),    //right up
                new Vector3(0f, 0f, 0f),    //left down
                new Vector3(1f, 0f, 0f)     //right down
            };

            qaud.triangles = new int[6]
            {
                // upper left triangle
                0, 1, 2,
                // down right triangle
                3, 2, 1
            };

            qaud.normals = new Vector3[4]
            {
                -Vector3.forward,
                -Vector3.forward,
                -Vector3.forward,
                -Vector3.forward
            };

            qaud.uv = new Vector2[4]
            {
                new Vector2(0f, 1f),    //left up
                new Vector2(1f, 1f),    //right up
                new Vector2(0f, 0f),    //left down
                new Vector2(1f, 0f)     //right down
            };

            return qaud;
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
            /// if property is <see cref="PropertyUpdateMode.Reactive"/> but this mode is disabled then switch to <see cref="PropertyUpdateMode.EachUpdate"/> if not disabled, otherwise switch to <see cref="PropertyUpdateMode.Static"/>
#if NSPRITES_REACTIVE_DISABLE
            if (mode == PropertyUpdateMode.Reactive)
            {
#if !NSPRITES_EACH_UPDATE_DISABLE
                    return PropertyUpdateMode.EachUpdate;
#else
                    return PropertyUpdateMode.Static;
#endif
            }
#endif
            /// if property is <see cref="PropertyUpdateMode.Static"/> but this mode is disabled then switch to <see cref="PropertyUpdateMode.Reactive"/> if not disabled, otherwise switch to <see cref="PropertyUpdateMode.EachUpdate"/>
#if NSPRITES_STATIC_DISABLE
            if (mode == PropertyUpdateMode.Static)
            {
#if !NSPRITES_REACTIVE_DISABLE
                    return PropertyUpdateMode.Reactive;
#else
                    return PropertyUpdateMode.EachUpdate;
#endif
            }
#endif
            /// if property is <see cref="PropertyUpdateMode.EachUpdate"/> but this mode is disabled then switch to <see cref="PropertyUpdateMode.Reactive"/> if not disabled, otherwise switch to <see cref="PropertyUpdateMode.Static"/>
#if NSPRITES_EACH_UPDATE_DISABLE
            /// if property is <see cref="PropertyUpdateMode.EachUpdate"/> but this mode is disabled then switch to <see cref="PropertyUpdateMode.Reactive"/> if not disabled, otherwise switch to <see cref="PropertyUpdateMode.Static"/>
            if (mode == PropertyUpdateMode.EachUpdate)
            {
#if !NSPRITES_REACTIVE_DISABLE
                    return PropertyUpdateMode.Reactive;
#else
                    return PropertyUpdateMode.Static;
#endif
            }
#endif
            return mode;
        }
    }
}
