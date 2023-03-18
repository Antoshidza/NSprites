#if (UNITY_EDITOR || DEVELOPEMENT_BUILD) && !NSPRITES_DEBUG_SYSTEM_DISABLE
using System;
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
    public partial struct DebugSystem : ISystem
    {
        [BurstCompile]
        private struct ExtractUniqueArchetypesJob : IJobParallelFor
        {
            [ReadOnly] public NativeList<ArchetypeChunk> chunks;
            [WriteOnly] public NativeParallelHashSet<EntityArchetype>.ParallelWriter uniqueArchetypes;

            public void Execute(int index) => uniqueArchetypes.Add(chunks[index].Archetype);
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
            [ReadOnly] public NativeArray<EntityArchetype> archetypes;
            [ReadOnly] public ComponentType propertyPointer_CT;
            [ReadOnly] public ComponentType propertyPointerChunk_CT;
            [ReadOnly][DeallocateOnJobCompletion] public NativeArray<ComponentType> requiredComponents;
            [WriteOnly][NativeDisableParallelForRestriction] public NativeArray<bool> hasIssues;

            public void Execute(int chunkIndex)
            {
                var archetype = archetypes[chunkIndex];
                var archetypeComponents = archetype.GetComponentTypes(Allocator.Temp);
                var perChunkIssueOffset = (3 + requiredComponents.Length) * chunkIndex;
                var missAnyComponent = false;

                bool HasComponent(in ComponentType comp)
                {
                    for (int i = 0; i < archetypeComponents.Length; i++)
                        if (archetypeComponents[i].TypeIndex == comp.TypeIndex)
                            return true;
                    return false;
                }

                for (int compIndex = 0; compIndex < requiredComponents.Length; compIndex++)
                {
                    var missComponent = !HasComponent(requiredComponents[compIndex]);
                    hasIssues[perChunkIssueOffset + compIndex] = missComponent;
                    missAnyComponent |= missComponent;
                }

                var hasPropertyPointer = HasComponent(propertyPointer_CT);
                var hasPropertyPointerChunk = HasComponent(propertyPointerChunk_CT);

                hasIssues[perChunkIssueOffset + requiredComponents.Length] = hasPropertyPointer && !hasPropertyPointerChunk || !hasPropertyPointer && hasPropertyPointerChunk || missAnyComponent;
                hasIssues[perChunkIssueOffset + requiredComponents.Length + 1] = hasPropertyPointer && !hasPropertyPointerChunk;
                hasIssues[perChunkIssueOffset + requiredComponents.Length + 2] = !hasPropertyPointer && hasPropertyPointerChunk;
            }
        }

        private struct SystemData : IComponentData
        {
            public EntityQuery renderQuery;
            public NativeHashSet<EntityArchetype> processedArchetypes;
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
                renderQuery = state.GetEntityQuery(ComponentType.ReadOnly<SpriteRenderID>()),
                processedArchetypes = new NativeHashSet<EntityArchetype>(1, Allocator.Persistent)
            });

            EditorGUI.hyperLinkClicked += OnRenderArchetypeLinkClicked;
        }
        public void OnDestroy(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<SystemData>(out var systemData))
                return;

            systemData.processedArchetypes.Dispose();

            EditorGUI.hyperLinkClicked -= OnRenderArchetypeLinkClicked;
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.ManagedAPI.TryGetSingleton<RenderArchetypeStorage>(out var renderArchetypeStorage)
                || !SystemAPI.TryGetSingleton<SystemData>(out var systemData))
                return;

            var chunkValidateHandles = new NativeArray<(JobHandle handle, ValidateArchetypesJob jobData)>(renderArchetypeStorage.RenderArchetypes.Count, Allocator.Temp);

            for (int archetypeIndex = 0; archetypeIndex < renderArchetypeStorage.RenderArchetypes.Count; archetypeIndex++)
            {
                var renderArchetype = renderArchetypeStorage.RenderArchetypes[archetypeIndex];
                var query = systemData.renderQuery;
                query.SetSharedComponentFilter(new SpriteRenderID { id = renderArchetype._id });

                var chunks = query.ToArchetypeChunkListAsync(Allocator.TempJob, state.Dependency, out var fetchingChunksHandle);

                var requiredComponents = new NativeArray<ComponentType>(renderArchetype._properties.Length, Allocator.TempJob);
                for (int propIndex = 0; propIndex < renderArchetype._properties.Length; propIndex++)
                    requiredComponents[propIndex] = renderArchetype._properties[propIndex].ComponentType;

                fetchingChunksHandle.Complete();
                var archetypesHashSet = new NativeParallelHashSet<EntityArchetype>(chunks.Length, Allocator.TempJob);

                var extractUniqueArchetypesJob = new ExtractUniqueArchetypesJob
                {
                    chunks = chunks,
                    uniqueArchetypes = archetypesHashSet.AsParallelWriter()
                };
                var extractUniqueArchetypesHandle = extractUniqueArchetypesJob.ScheduleByRef(chunks.Length, 32, state.Dependency);
                extractUniqueArchetypesHandle.Complete();
                chunks.Dispose();

                archetypesHashSet.ExceptWith(systemData.processedArchetypes);

                var uniqueArchetypes = archetypesHashSet.ToNativeArray(Allocator.TempJob);
                systemData.processedArchetypes.UnionWith(uniqueArchetypes);
                archetypesHashSet.Dispose();
                var issues = new NativeArray<bool>(uniqueArchetypes.Length * (3 + requiredComponents.Length), Allocator.TempJob);

                var validateArchetypesJob = new ValidateArchetypesJob
                {
                    archetypes = uniqueArchetypes,
                    propertyPointer_CT = ComponentType.ReadOnly<PropertyPointer>(),
                    propertyPointerChunk_CT = ComponentType.ChunkComponentReadOnly<PropertyPointerChunk>(),
                    hasIssues = issues,
                    requiredComponents = requiredComponents
                };
                chunkValidateHandles[archetypeIndex] = new 
                (
                    validateArchetypesJob.ScheduleByRef(uniqueArchetypes.Length, 32, state.Dependency),
                    validateArchetypesJob
                );

                query.ResetFilter();
            }

            for (int renderIndex = 0; renderIndex < renderArchetypeStorage.RenderArchetypes.Count; renderIndex++)
            {
                var validateResult = chunkValidateHandles[renderIndex];
                validateResult.handle.Complete();

                var renderArchetype = renderArchetypeStorage.RenderArchetypes[renderIndex];
                var archetypes = validateResult.jobData.archetypes;
                var issues = validateResult.jobData.hasIssues;
                var perArchetypeOffset = validateResult.jobData.requiredComponents.Length + 3;
                var anyIssuesIndex = validateResult.jobData.requiredComponents.Length;
                var propCount = renderArchetype._properties.Length;

                var issueReport = $"{nameof(RenderArchetype)} {renderArchetype._id} issue report:\n";
                var renderHasAnyIssue = false;

                for (int archetypIndex = 0; archetypIndex < archetypes.Length; archetypIndex++)
                {
                    if (!issues[anyIssuesIndex + perArchetypeOffset * archetypIndex])
                        continue;

                    renderHasAnyIssue = true;

                    var archetype = archetypes[archetypIndex];
                    issueReport += $"\t#{archetypIndex} {nameof(EntityArchetype)} <b><a hash=\"{archetype.StableHash:x}\">{archetype.StableHash:x}</a></b> has next issues:\n";

                    for (int propIndex = 0; propIndex < propCount; propIndex++)
                        if (issues[perArchetypeOffset * archetypIndex + propIndex])
                            issueReport += $"\t\tMiss <color=red>{renderArchetype._properties[propIndex].ComponentType.TypeIndex}</color> component\n";

                    if (issues[perArchetypeOffset * archetypIndex + propCount + 1])
                        issueReport += $"\t\t<color=red>Has {nameof(PropertyPointer)} but no {nameof(PropertyPointerChunk)}. It shouldn't happen, please, contact developer <a href=\"https://github.com/Antoshidza\">https://github.com/Antoshidza</a></color>\n";
                    if (issues[perArchetypeOffset * archetypIndex + propCount + 2])
                        issueReport += $"\t\t<color=red>Has {nameof(PropertyPointerChunk)} but no {nameof(PropertyPointer)}. It shouldn't happen, please, contact developer <a href=\"https://github.com/Antoshidza\">https://github.com/Antoshidza</a></color>\n";

                }

                if(renderHasAnyIssue)
                    Debug.LogError(new NSpritesException(issueReport));

                validateResult.jobData.hasIssues.Dispose();
                validateResult.jobData.archetypes.Dispose();
            }
        }
    }
}
#endif