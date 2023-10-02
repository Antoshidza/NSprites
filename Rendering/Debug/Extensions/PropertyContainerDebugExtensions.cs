using System.Collections.Generic;
using System.Linq;

namespace NSprites.Extensions
{
    public static class PropertyContainerDebugExtensions
    {
        internal static int GetPropertiesCount(this PropertiesContainer container)
            => container.Reactive.Count() + container.EachUpdate.Count() + container.Static.Count();

        internal static IEnumerable<InstancedProperty> GetAllProperties(this PropertiesContainer container)
        {
            var list = new List<InstancedProperty>();
            list.AddRange(container.Reactive);
            list.AddRange(container.EachUpdate);
            list.AddRange(container.Static);
            return list;
        }
    }
}