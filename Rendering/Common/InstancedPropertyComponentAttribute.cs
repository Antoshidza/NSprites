using System;
using System.Collections.Generic;
using System.Linq;

namespace NSprites
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class InstancedPropertyComponent : Attribute
    {
        public Type componentType;
        public string name;
        public PropertyFormat format;

        public InstancedPropertyComponent(Type componentType, string name, PropertyFormat format)
        {
            this.componentType = componentType;
            this.name = name;
            this.format = format;
        }

        public static IEnumerable<InstancedPropertyComponent> GetProperties()
        {
            return Utils.GetAssemblyAttributes<InstancedPropertyComponent>()
                .Select((InstancedPropertyComponent attr) => attr);
        }
    }
}
