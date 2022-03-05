using Unity.Entities;

namespace NSprites
{
    public struct SortingGroup : IComponentData
    {
        public Entity groupID;    //parent's entity.Index;
        public int index;
    }
}
