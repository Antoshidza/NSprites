using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace NSprites
{
    /// <summary>
    /// Renders entities (both in runtime and editor) with <see cref="SpriteRenderID"/> : <see cref="ISharedComponentData"/> as 2D sprites depending on registered data through <see cref="RenderArchetypeStorage.RegisterRender"/>
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct SpriteRenderingSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
#if NSPRITES_REACTIVE_DISABLE && NSPRITES_STATIC_DISABLE && NSPRITES_EACH_UPDATE_DISABLE
            throw new NSpritesException($"You can't disable {nameof(PropertyUpdateMode.Reactive)}, {nameof(PropertyUpdateMode.Static)} and {nameof(PropertyUpdateMode.EachUpdate)} properties modes at the same time, there should be at least one mode if you want system to work. Please, enable at least one mode.");
#endif
            // instantiate and initialize system data
            var renderArchetypeStorage = new RenderArchetypeStorage{ SystemData = new SystemData { Query = state.GetEntityQuery(NSpritesUtils.GetDefaultComponentTypes()) }};
            renderArchetypeStorage.Initialize();
            state.EntityManager.AddComponentObject(state.SystemHandle, renderArchetypeStorage);
        }

        public void OnDestroy(ref SystemState state)
        {
            SystemAPI.ManagedAPI.GetComponent<RenderArchetypeStorage>(state.SystemHandle).Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            var renderArchetypeStorage = SystemAPI.ManagedAPI.GetComponent<RenderArchetypeStorage>(state.SystemHandle);
#if UNITY_EDITOR
            if (!Application.isPlaying && renderArchetypeStorage.Quad == null)
                renderArchetypeStorage.Quad = NSpritesUtils.ConstructQuad();
#endif
            // update state to pass to render archetypes
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
            var systemData = renderArchetypeStorage.SystemData;
            systemData.LastSystemVersion = state.LastSystemVersion;
            systemData.PropertyPointer_CTH_RW = SystemAPI.GetComponentTypeHandle<PropertyPointer>(false);
            systemData.PropertyPointerChunk_CTH_RW = SystemAPI.GetComponentTypeHandle<PropertyPointerChunk>(false);
            systemData.PropertyPointerChunk_CTH_RO = SystemAPI.GetComponentTypeHandle<PropertyPointerChunk>(true);
#endif
            systemData.InputDeps = state.Dependency;

            // schedule render archetype's properties data update
            var renderArchetypeHandles = new NativeArray<JobHandle>(renderArchetypeStorage.RenderArchetypes.Count, Allocator.Temp);
            for (var archetypeIndex = 0; archetypeIndex < renderArchetypeStorage.RenderArchetypes.Count; archetypeIndex++)
                renderArchetypeHandles[archetypeIndex] = renderArchetypeStorage.RenderArchetypes[archetypeIndex].ScheduleUpdate(systemData, ref state);
            

            // force complete properties data update and draw archetypes
            for (var archetypeIndex = 0; archetypeIndex < renderArchetypeStorage.RenderArchetypes.Count; archetypeIndex++)
                renderArchetypeStorage.RenderArchetypes[archetypeIndex].CompleteAndDraw();

            // combine handles from all render archetypes we have updated
            state.Dependency = JobHandle.CombineDependencies(renderArchetypeHandles);
            
            // state.EntityManager.CompleteAllTrackedJobs();
        }
    }
}