using Unity.Entities;
using Unity.Mathematics;

namespace NSprites
{
    internal struct PropertyBufferIndexRange : IComponentData
    {
        public int from;
        public int count;
    }
}