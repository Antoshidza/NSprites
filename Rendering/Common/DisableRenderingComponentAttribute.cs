using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;

namespace NSprites
{
    /// <summary>
    /// Marks component to use as <see cref="ComponentType.AccessMode.Exclude"/> for <see cref="SpriteRenderingSystem"/>.
    /// So entities with such components won't be handled / rendered.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public class DisableRenderingComponent : Attribute 
    {
        public Type componentType;

        public DisableRenderingComponent(Type componentType)
        {
            this.componentType = componentType;
        }

        public static IEnumerable<ComponentType> GetTypes()
        {
            return NSpritesUtils.GetAssemblyAttributes<DisableRenderingComponent>()
                .Select((DisableRenderingComponent attr) => new ComponentType(attr.componentType, ComponentType.AccessMode.Exclude));
        }
    }
}
