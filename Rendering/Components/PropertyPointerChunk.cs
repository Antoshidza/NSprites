using Unity.Entities;

namespace NSprites
{
    internal struct PropertyPointerChunk : IComponentData
    {
        public int from;
        public int count;
    }
}