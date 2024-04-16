using Unity.Burst;
using Unity.Entities;

namespace NSprites
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateBefore(typeof(SpriteRenderingSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor | WorldSystemFilterFlags.EntitySceneOptimizations)]
    public partial struct AddMissedRenderingComponentSystem : ISystem
    {
        private EntityQuery _query;
        
        [BurstCompile]
        public void OnCreate(ref SystemState state) 
        {
            _query = SystemAPI.QueryBuilder()
                .WithAll<PropertyPointer>()
                .WithNoneChunkComponent<PropertyPointerChunk>()
                .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.Default | EntityQueryOptions.IgnoreComponentEnabledState)
                .Build();
            
            state.RequireForUpdate(_query);   
        }
        
        [BurstCompile]
        public void OnUpdate(ref SystemState state) 
            => state.EntityManager.AddChunkComponentData(_query, new PropertyPointerChunk());
    }
}