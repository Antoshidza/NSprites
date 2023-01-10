using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace NSprites
{
    /// <summary>
    /// Renders entities (both in runtime and editor) with <see cref="SpriteRenderID"/> : <see cref="ISharedComponentData"/> as 2D sprites depending on registered data through <see cref="RegisterRender"/>
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct SpriteRenderingSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
#if NSPRITES_REACTIVE_DISABLE && NSPRITES_STATIC_DISABLE && NSPRITES_EACH_UPDATE_DISABLE
            throw new Exception($"You can't disable Reactive, Static and Each-Update properties modes at the same time, there should be at least one mode if you want system to work. Please, enable at least one mode.");
#endif
            // instansiate and initialize system data
            var renderArchetypeStorage = new RenderArchetypeStorage{ SystemData = new SystemData { query = state.GetEntityQuery(NSpritesUtils.GetDefaultComponentTypes()) }};
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
            systemData.lastSystemVersion = state.LastSystemVersion;
            systemData.propertyPointer_CTH_RW = SystemAPI.GetComponentTypeHandle<PropertyPointer>(false);
            systemData.propertyPointerChunk_CTH_RW = SystemAPI.GetComponentTypeHandle<PropertyPointerChunk>(false);
            systemData.propertyPointerChunk_CTH_RO = SystemAPI.GetComponentTypeHandle<PropertyPointerChunk>(true);
#endif
            systemData.inputDeps = state.Dependency;

            // schedule render archetype's properties data update
            var renderArchetypeHandles = new NativeArray<JobHandle>(renderArchetypeStorage.RenderArchetypes.Count, Allocator.Temp);
            for (int archetypeIndex = 0; archetypeIndex < renderArchetypeStorage.RenderArchetypes.Count; archetypeIndex++)
                renderArchetypeHandles[archetypeIndex] = renderArchetypeStorage.RenderArchetypes[archetypeIndex].ScheduleUpdate(systemData, ref state);

            // force complete properties data update and draw archetypes
            for (int archetypeIndex = 0; archetypeIndex < renderArchetypeStorage.RenderArchetypes.Count; archetypeIndex++)
            {
                var archetype = renderArchetypeStorage.RenderArchetypes[archetypeIndex];
                archetype.CompleteUpdate();
                archetype.Draw(renderArchetypeStorage.Quad, new Bounds(new Vector3(0f, 0f, archetypeIndex), Vector3.one * 1000f));
            }

            // combine handles from all render archetypes we have updated
            state.Dependency = JobHandle.CombineDependencies(renderArchetypeHandles);
        }
    }
}