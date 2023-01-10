using System.Linq;
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
    public partial class SpriteRenderingSystem : SystemBase
    {
        protected override void OnCreate()
        {
#if NSPRITES_REACTIVE_DISABLE && NSPRITES_STATIC_DISABLE && NSPRITES_EACH_UPDATE_DISABLE
            throw new Exception($"You can't disable Reactive, Static and Each-Update properties modes at the same time, there should be at least one mode if you want system to work. Please, enable at least one mode.");
#endif
            base.OnCreate();

            var renderArchetypeStorage = new RenderArchetypeStorage
            {
                _state = new SystemState
                {
                    system = this,
                    query = GetEntityQuery(GetDefaultComponentTypes())
                }
            };
            renderArchetypeStorage.Initialize();

            EntityManager.AddComponentObject(SystemHandle, renderArchetypeStorage);
        }
        protected override void OnDestroy()
        {
            base.OnDestroy();
            EntityManager.GetComponentObject<RenderArchetypeStorage>(SystemHandle).Dispose();
        }
        protected override void OnUpdate()
        {
            var renderArchetypeStorage = EntityManager.GetComponentObject<RenderArchetypeStorage>(SystemHandle);
#if UNITY_EDITOR
            if (!Application.isPlaying && renderArchetypeStorage._quad == null)
                renderArchetypeStorage._quad = NSpritesUtils.ConstructQuad();
#endif
            // update state to pass to render archetypes
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
            var state = renderArchetypeStorage._state;
            state.lastSystemVersion = LastSystemVersion;
            state.propertyPointer_CTH_RW = GetComponentTypeHandle<PropertyPointer>(false);
            state.propertyPointerChunk_CTH_RW = GetComponentTypeHandle<PropertyPointerChunk>(false);
            state.propertyPointerChunk_CTH_RO = GetComponentTypeHandle<PropertyPointerChunk>(true);
#endif
            state.inputDeps = Dependency;

            // schedule render archetype's properties data update
            var renderArchetypeHandles = new NativeArray<JobHandle>(renderArchetypeStorage._renderArchetypes.Count, Allocator.Temp);
            for (int archetypeIndex = 0; archetypeIndex < renderArchetypeStorage._renderArchetypes.Count; archetypeIndex++)
                renderArchetypeHandles[archetypeIndex] = renderArchetypeStorage._renderArchetypes[archetypeIndex].ScheduleUpdate(state);

            // force complete properties data update and draw archetypes
            for (int archetypeIndex = 0; archetypeIndex < renderArchetypeStorage._renderArchetypes.Count; archetypeIndex++)
            {
                var archetype = renderArchetypeStorage._renderArchetypes[archetypeIndex];
                archetype.CompleteUpdate();
                archetype.Draw(renderArchetypeStorage._quad, new Bounds(new Vector3(0f, 0f, archetypeIndex), Vector3.one * 1000f));
            }

            // combine handles from all render archetypes we have updated
            Dependency = JobHandle.CombineDependencies(renderArchetypeHandles);
        }

#region support methods
        /// <summary>Returns array with all default components for rendering entities including types marked with <see cref="DisableRenderingComponent"/> attribute</summary>
        private NativeArray<ComponentType> GetDefaultComponentTypes(in Allocator allocator = Allocator.Temp)
        {
            var disableRenderingComponentTypes = new NativeArray<ComponentType>(DisableRenderingComponent.GetTypes().ToArray(), Allocator.Persistent);
            var defaultComponents = new NativeArray<ComponentType>(disableRenderingComponentTypes.Length + 1, allocator);
            NativeArray<ComponentType>.Copy(disableRenderingComponentTypes, 0, defaultComponents, 0, disableRenderingComponentTypes.Length);
            defaultComponents[defaultComponents.Length - 1] = ComponentType.ReadOnly<SpriteRenderID>();
            disableRenderingComponentTypes.Dispose();
            return defaultComponents;
        }
#endregion
    }
}