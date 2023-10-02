#if (UNITY_EDITOR || DEVELOPEMENT_BUILD) && !NSPRITES_DEBUG_SYSTEM_DISABLE
using System;
using System.Linq;
using NSprites.Extensions;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NSprites
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    public partial struct ValidateRenderArchetypesSystem : ISystem
    {
        [BurstCompile]
        private struct ExtractUniqueArchetypesJob : IJobParallelFor
        {
            [ReadOnly] public NativeList<ArchetypeChunk> Chunks;
            [WriteOnly] public NativeParallelHashSet<EntityArchetype>.ParallelWriter UniqueArchetypes;

            public void Execute(int index) => UniqueArchetypes.Add(Chunks[index].Archetype);
        }

        /// <summary>
        /// Fills bool arrays:
        /// <br> [0; N - 1] means lost particular component if true, where N is number of required components </br>
        /// <br> [N + 1] means chunk has <see cref="PropertyPointer"/> but has no <see cref="PropertyPointerChunk"/> </br>
        /// <br> [N + 2] vice versa from [N + 1] </br>
        /// <br> [N] means chunk has issues at all </br>
        /// </summary>
        [BurstCompile]
        private struct ValidateArchetypesJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<EntityArchetype> Archetypes;
            [ReadOnly] public ComponentType PropertyPointer_Ct;
            [ReadOnly] public ComponentType PropertyPointerChunk_Ct;
            [ReadOnly][DeallocateOnJobCompletion] public NativeArray<ComponentType> RequiredComponents;
            [WriteOnly][NativeDisableParallelForRestriction] public NativeArray<bool> HasIssues;

            public void Execute(int chunkIndex)
            {
                var archetype = Archetypes[chunkIndex];
                var archetypeComponents = archetype.GetComponentTypes(Allocator.Temp);
                var perChunkIssueOffset = (3 + RequiredComponents.Length) * chunkIndex;
                var missAnyComponent = false;

                bool HasComponent(in ComponentType comp)
                {
                    for (int i = 0; i < archetypeComponents.Length; i++)
                        if (archetypeComponents[i].TypeIndex == comp.TypeIndex)
                            return true;
                    return false;
                }

                for (int compIndex = 0; compIndex < RequiredComponents.Length; compIndex++)
                {
                    var missComponent = !HasComponent(RequiredComponents[compIndex]);
                    HasIssues[perChunkIssueOffset + compIndex] = missComponent;
                    missAnyComponent |= missComponent;
                }

                var hasPropertyPointer = HasComponent(PropertyPointer_Ct);
                var hasPropertyPointerChunk = HasComponent(PropertyPointerChunk_Ct);

                HasIssues[perChunkIssueOffset + RequiredComponents.Length] = hasPropertyPointer && !hasPropertyPointerChunk || !hasPropertyPointer && hasPropertyPointerChunk || missAnyComponent;
                HasIssues[perChunkIssueOffset + RequiredComponents.Length + 1] = hasPropertyPointer && !hasPropertyPointerChunk;
                HasIssues[perChunkIssueOffset + RequiredComponents.Length + 2] = !hasPropertyPointer && hasPropertyPointerChunk;
            }
        }

        private struct SystemData : IComponentData
        {
            public EntityQuery RenderQuery;
            public NativeHashSet<EntityArchetype> ProcessedArchetypes;
        }

        [BurstDiscard]
        private static void OnRenderArchetypeLinkClicked(EditorWindow window, HyperLinkClickedEventArgs args)
        {
            if (window.titleContent.text != "Console" ||
                !args.hyperLinkData.ContainsKey("hash"))
                return;

            var hash = args.hyperLinkData["hash"];
            GUIUtility.systemCopyBuffer = hash;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                if (assembly.GetName().Name != "Unity.Entities.Editor")
                    continue;

                EditorWindow.GetWindow(assembly.GetType("Unity.Entities.Editor.ArchetypesWindow"))
                    .rootVisualElement.Q<TextField>("search-element-text-field-search-string")
                    .value = hash;

                break;
            }
        }

        public void OnCreate(ref SystemState state)
        {
            state.EntityManager.AddComponentData(state.SystemHandle, new SystemData 
            { 
                RenderQuery = state.GetEntityQuery(ComponentType.ReadOnly<SpriteRenderID>()),
                ProcessedArchetypes = new NativeHashSet<EntityArchetype>(1, Allocator.Persistent)
            });

            EditorGUI.hyperLinkClicked += OnRenderArchetypeLinkClicked;
        }
        
        public void OnDestroy(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<SystemData>(out var systemData))
                return;

            systemData.ProcessedArchetypes.Dispose();

            EditorGUI.hyperLinkClicked -= OnRenderArchetypeLinkClicked;
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.ManagedAPI.TryGetSingleton<RenderArchetypeStorage>(out var renderArchetypeStorage)
                || !SystemAPI.TryGetSingleton<SystemData>(out var systemData))
                return;

            var chunkValidateHandles = new NativeArray<(JobHandle handle, ValidateArchetypesJob jobData)>(renderArchetypeStorage.RenderArchetypes.Count, Allocator.Temp);

            for (var archetypeIndex = 0; archetypeIndex < renderArchetypeStorage.RenderArchetypes.Count; archetypeIndex++)
            {
                var renderArchetype = renderArchetypeStorage.RenderArchetypes[archetypeIndex];
                var query = systemData.RenderQuery;
                query.SetSharedComponentFilter(new SpriteRenderID { id = renderArchetype.ID });

                var chunks = query.ToArchetypeChunkListAsync(Allocator.TempJob, state.Dependency, out var fetchingChunksHandle);

                var props = renderArchetype.PropertiesContainer.GetAllProperties().ToArray();
                var requiredComponents = new NativeArray<ComponentType>(props.Length, Allocator.TempJob);
                var propIndex = 0;
                foreach (var prop in props)
                    requiredComponents[propIndex++] = prop.ComponentType;

                fetchingChunksHandle.Complete();
                var archetypesHashSet = new NativeParallelHashSet<EntityArchetype>(chunks.Length, Allocator.TempJob);

                var extractUniqueArchetypesJob = new ExtractUniqueArchetypesJob
                {
                    Chunks = chunks,
                    UniqueArchetypes = archetypesHashSet.AsParallelWriter()
                };
                var extractUniqueArchetypesHandle = extractUniqueArchetypesJob.ScheduleByRef(chunks.Length, 32, state.Dependency);
                extractUniqueArchetypesHandle.Complete();
                chunks.Dispose();

                archetypesHashSet.ExceptWith(systemData.ProcessedArchetypes);

                var uniqueArchetypes = archetypesHashSet.ToNativeArray(Allocator.TempJob);
                systemData.ProcessedArchetypes.UnionWith(uniqueArchetypes);
                archetypesHashSet.Dispose();
                var issues = new NativeArray<bool>(uniqueArchetypes.Length * (3 + requiredComponents.Length), Allocator.TempJob);

                var validateArchetypesJob = new ValidateArchetypesJob
                {
                    Archetypes = uniqueArchetypes,
                    PropertyPointer_Ct = ComponentType.ReadOnly<PropertyPointer>(),
                    PropertyPointerChunk_Ct = ComponentType.ChunkComponentReadOnly<PropertyPointerChunk>(),
                    HasIssues = issues,
                    RequiredComponents = requiredComponents
                };
                chunkValidateHandles[archetypeIndex] = new 
                (
                    validateArchetypesJob.ScheduleByRef(uniqueArchetypes.Length, 32, state.Dependency),
                    validateArchetypesJob
                );

                query.ResetFilter();
            }

            for (var renderIndex = 0; renderIndex < renderArchetypeStorage.RenderArchetypes.Count; renderIndex++)
            {
                var validateResult = chunkValidateHandles[renderIndex];
                validateResult.handle.Complete();

                var renderArchetype = renderArchetypeStorage.RenderArchetypes[renderIndex];
                var archetypes = validateResult.jobData.Archetypes;
                var issues = validateResult.jobData.HasIssues;
                var perArchetypeOffset = validateResult.jobData.RequiredComponents.Length + 3;
                var anyIssuesIndex = validateResult.jobData.RequiredComponents.Length;
                var propCount = renderArchetype.PropertiesContainer.GetPropertiesCount();

                var issueReport = $"{nameof(RenderArchetype)} {renderArchetype.ID} issue report:\n";
                var renderHasAnyIssue = false;

                for (var archetypeIndex = 0; archetypeIndex < archetypes.Length; archetypeIndex++)
                {
                    if (!issues[anyIssuesIndex + perArchetypeOffset * archetypeIndex])
                        continue;

                    renderHasAnyIssue = true;

                    var archetype = archetypes[archetypeIndex];
                    issueReport += $"\t#{archetypeIndex} {nameof(EntityArchetype)} <b><a hash=\"{archetype.StableHash:x}\">{archetype.StableHash:x}</a></b> has next issues:\n";

                    // for (var propIndex = 0; propIndex < propCount; propIndex++)
                    //     if (issues[perArchetypeOffset * archetypeIndex + propIndex])
                    //         issueReport += $"\t\tMiss <color=red>{renderArchetype.Properties[propIndex].ComponentType.TypeIndex}</color> component\n";

                    if (issues[perArchetypeOffset * archetypeIndex + propCount + 1])
                        issueReport += $"\t\t<color=red>Has {nameof(PropertyPointer)} but no {nameof(PropertyPointerChunk)}. It shouldn't happen, please, contact developer <a href=\"https://github.com/Antoshidza\">https://github.com/Antoshidza</a></color>\n";
                    if (issues[perArchetypeOffset * archetypeIndex + propCount + 2])
                        issueReport += $"\t\t<color=red>Has {nameof(PropertyPointerChunk)} but no {nameof(PropertyPointer)}. It shouldn't happen, please, contact developer <a href=\"https://github.com/Antoshidza\">https://github.com/Antoshidza</a></color>\n";

                }

                if(renderHasAnyIssue)
                    Debug.LogError(new NSpritesException(issueReport));

                validateResult.jobData.HasIssues.Dispose();
                validateResult.jobData.Archetypes.Dispose();
            }
        }
    }
}
#endif