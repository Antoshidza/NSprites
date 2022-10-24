using NSprites;
using Unity.Entities;

[assembly: InstancedPropertyComponent(typeof(PropertyBufferIndex), "_dataIndexBuffer", PropertyFormat.Int, PropertyUpdateMode.EachUpdate)]

namespace NSprites
{
    public struct PropertyBufferIndex : IComponentData
    {
        public int value;
    }
}