using Unity.Entities;

namespace NSprites
{
    /// <summary>
    /// Used to ignore entities in <seealso cref="Parent2DSystem"/>, means such children will never be re/unparented or destroyed without parent
    /// and such parents will never have any children which need to be handled after parent got destroyed.
    /// For example you have root entity with some hierarchy inside and this hierarchy will never be changed. When you want to destroy whole
    /// heierarchy you don't want to process complex logic in <seealso cref="Parent2DSystem"/>, because just simple deletion is enough,
    /// nothing to hande.
    /// </summary>
    [GenerateAuthoringComponent]
    public struct StaticRelationshipsTag : ISystemStateComponentData
    {
    }
}