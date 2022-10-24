using System.Collections.Generic;
using System;
using UnityEngine;
using System.Reflection;
using Unity.Entities;

namespace NSprites
{
    public static class NSpritesUtils
    {
        #region add components methods
        /// <summary>
        /// Adds all necessary components for rendering to entity:
        /// <br>* <see cref="SpriteRendererTag"></see></br>
        /// <br>* <see cref="SpriteRenderID"></see> (empty, should be seted on play)</br>
        /// <br>* <see cref="PropertyBufferIndex"></see> (empty, will automatically initialized by render system)</br>
        /// <br>* <see cref="PropertyBufferIndexRange"></see> to entity's chunk (empty, will automatically initialized by render system)</br>
        /// </summary>
        public static void AddSpriteRenderComponents(in Entity entity, in EntityManager entityManager, in bool hasReactiveComponents = true, in int renderID = default)
        {
            entityManager.AddComponentData(entity, new SpriteRendererTag());
            entityManager.AddSharedComponentData(entity, new SpriteRenderID { id = renderID });

#if !NSPRITES_REACTIVE_PROPERTIES_DISABLE
            if (hasReactiveComponents)
            {
                entityManager.AddComponentData(entity, new PropertyBufferIndex());
                entityManager.AddChunkComponentData<PropertyBufferIndexRange>(entity);
            }
#endif
        }
        /// <summary>
        /// Adds all necessary components for rendering to entity:
        /// <br>* <see cref="SpriteRendererTag"></see></br>
        /// <br>* <see cref="SpriteRenderID"></see> (empty, should be seted on play)</br>
        /// <br>* <see cref="PropertyBufferIndex"></see> (empty, will automatically initialized by render system)</br>
        /// <br>* <see cref="PropertyBufferIndexRange"></see> to entity's chunk (empty, will automatically initialized by render system)</br>
        /// </summary>
        public static void AddSpriteRenderComponents(this in EntityManager entityManager, in Entity entity, in bool hasReactiveComponents = true, in int renderID = default)
        {
            AddSpriteRenderComponents(entity, entityManager, hasReactiveComponents, renderID);
        }
        #endregion

        /// <summary>
        /// Returns <b>Tiling and Offset</b> value which can be helpfull to locate texture on atlas in shader
        /// </summary>
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
    }
}
