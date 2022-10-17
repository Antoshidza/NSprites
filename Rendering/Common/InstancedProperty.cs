using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NSprites
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class InstancedProperty : Attribute
    {
        public Type componentType;
        public string name;
        public PropertyFormat format;

        public InstancedProperty(Type componentType, string name, PropertyFormat format)
        {
            this.componentType = componentType;
            this.name = name;
            this.format = format;
        }

        public static IEnumerable<InstancedProperty> GetProperties()
        {
            return GetAssemblyAttributes<InstancedProperty>()
                .Select((InstancedProperty attr) => attr);
        }
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
