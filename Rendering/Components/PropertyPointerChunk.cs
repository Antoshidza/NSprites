using Unity.Entities;

namespace NSprites
{
    internal struct PropertyPointerChunk : IComponentData
    {
        public int From;
        public int Count;
    }
}