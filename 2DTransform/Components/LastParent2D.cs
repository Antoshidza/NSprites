using Unity.Entities;

namespace NSprites
{
    public struct LastParent2D : ISystemStateComponentData
    {
        public Entity value;
    }
}