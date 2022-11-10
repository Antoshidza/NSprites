using Unity.Entities;

namespace NSprites
{
    internal struct PropertyPointer : IComponentData
    {
        public const string PropertyName = "_propertyPointers";

        public int bufferIndex;
    }
}