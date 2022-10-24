using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;

namespace NSprites
{
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
