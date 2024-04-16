// using System;
// using System.Collections.Generic;
// using Unity.Burst;
// using Unity.Collections;
// using Unity.Entities;
// using Unity.Jobs;
//
// namespace NSprites
// {
//     [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
//     public partial struct ValidateChunkMappingSystem : ISystem
//     {
//         [BurstCompile]
//         private struct FilterUninitializedChunks : IJobParallelFor
//         {
//             [ReadOnly][DeallocateOnJobCompletion] public NativeArray<ArchetypeChunk> Chunks;
//             [ReadOnly] public ComponentTypeHandle<PropertyPointerChunk> PropertyPointerChunk_CTH_RO;
//             [WriteOnly] public NativeList<ArchetypeChunk>.ParallelWriter InitializedChunks;
//
//             public void Execute(int index)
//             {
//                 var chunk = Chunks[index];
//                 if(chunk.GetChunkComponentData(ref PropertyPointerChunk_CTH_RO).Initialized)
//                     InitializedChunks.AddNoResize(chunk);
//             }
//         }
//         
//         [BurstCompile]
//         private struct ExtractPropertyPointersToArrayJob : IJobParallelFor
//         {
//             [ReadOnly] public NativeList<ArchetypeChunk> Chunks;
//             [ReadOnly] public ComponentTypeHandle<PropertyPointerChunk> PropertyPointerChunk_CTH_RO;
//             [WriteOnly] public NativeArray<ChunkInfo> ChunkInfoArray;
//
//             public void Execute(int index)
//             {
//                 var chunk = Chunks[index];
//                 ChunkInfoArray[index] = new ChunkInfo
//                 {
//                     Chunk = chunk,
//                     Ptr = chunk.GetChunkComponentData(ref PropertyPointerChunk_CTH_RO)
//                 };
//             }
//         }
//         
//         [BurstCompile]
//         private struct ValidateChunkMappingJob : IJobParallelFor
//         {
//             // should be sorted
//             [NativeDisableParallelForRestriction] public NativeArray<ChunkInfo> ChunkInfoArray;
//
//             public void Execute(int index)
//             {
//                 var curr = ChunkInfoArray[index];
//                 
//                 if (index == ChunkInfoArray.Length - 1) 
//                     curr.Validated = true;
//                 else
//                 {
//                     var next = ChunkInfoArray[index + 1];
//
//                     // * chunk might be newly created and have no map data yet, so such chunk is validated
//                     // * prev chunk should start before next
//                     // * prev chunk count nor capacity should end before next chunk starts
//                     curr.Validated = !curr.Ptr.Initialized || (curr.Ptr.From < next.Ptr.From && curr.Ptr.From + curr.Chunk.Count <= next.Ptr.From && curr.Ptr.From + curr.Chunk.Capacity <= next.Ptr.From);
//                 }
//                 
//                 ChunkInfoArray[index] = curr;
//             }
//         }
//
//         private struct ChunkInfo : IComparable<ChunkInfo>
//         {
//             public ArchetypeChunk Chunk;
//             public PropertyPointerChunk Ptr;
//             public bool Validated;
//             
//             public int CompareTo(ChunkInfo other)
//                 => Ptr.From.CompareTo(other.Ptr.From);
//         }
//         
//         private struct SystemData : IComponentData
//         {
//             public EntityQuery RenderQuery;
//         }
//         
//         public void OnCreate(ref SystemState state)
//         {
//             state.EntityManager.AddComponentData(state.SystemHandle, new SystemData 
//             { 
//                 RenderQuery = state.GetEntityQuery(ComponentType.ReadOnly<SpriteRenderID>()),
//             });
//         }
//
//         //[BurstCompile]
//         public void OnUpdate(ref SystemState state)
//         {
//             if (!SystemAPI.ManagedAPI.TryGetSingleton<RenderArchetypeStorage>(out var renderArchetypeStorage)
//                 || !SystemAPI.TryGetSingleton<SystemData>(out var systemData))
//                 return;
//
//             for (var archetypeIndex = 0; archetypeIndex < renderArchetypeStorage.RenderArchetypes.Count; archetypeIndex++)
//             {
//                 var renderArchetype = renderArchetypeStorage.RenderArchetypes[archetypeIndex];
//                 var query = systemData.RenderQuery;
//                 query.SetSharedComponentFilter(new SpriteRenderID { id = renderArchetype.ID });
//
//                 var chunks = query.ToArchetypeChunkArray(Allocator.TempJob);
//                 var filteredChunks = new NativeList<ArchetypeChunk>(chunks.Length, Allocator.TempJob);
//
//                 var filterChunksJob = new FilterUninitializedChunks
//                 { 
//                     Chunks = chunks,
//                     PropertyPointerChunk_CTH_RO = SystemAPI.GetComponentTypeHandle<PropertyPointerChunk>(true),
//                     InitializedChunks = filteredChunks.AsParallelWriter()
//                 };
//                 state.Dependency = filterChunksJob.ScheduleByRef(chunks.Length, 32, state.Dependency);
//                 state.Dependency.Complete();
//                 
//                 var chunkInfoArray = new NativeArray<ChunkInfo>(filteredChunks.Length, Allocator.TempJob);
//
//                 var extractPropPointersJob = new ExtractPropertyPointersToArrayJob
//                 {
//                     Chunks = filteredChunks,
//                     PropertyPointerChunk_CTH_RO = SystemAPI.GetComponentTypeHandle<PropertyPointerChunk>(true),
//                     ChunkInfoArray = chunkInfoArray
//                 };
//                 state.Dependency = extractPropPointersJob.ScheduleByRef(chunkInfoArray.Length, 32, state.Dependency);
//                 state.Dependency.Complete();
//
//                 state.Dependency = chunkInfoArray.SortJob().Schedule();
//
//                 var validateJob = new ValidateChunkMappingJob
//                 {
//                     ChunkInfoArray = chunkInfoArray
//                 };
//                 state.Dependency = validateJob.ScheduleByRef(chunkInfoArray.Length, 32, state.Dependency);
//                 state.Dependency.Complete();
//
//                 for (var i = 0; i < chunkInfoArray.Length - 1; i++)
//                 {
//                     var info = chunkInfoArray[i];
//                     var nextInfo = chunkInfoArray[i + 1];
//
//                     if (!info.Validated)
//                     {
//                         filteredChunks.Dispose();
//                         chunkInfoArray.Dispose();
//                         throw new NSpritesException($"Chunk {i} starts from {info.Ptr.From} cap {info.Chunk.Count} / {info.Chunk.Capacity} init:{info.Ptr.Initialized} has problems with next chunk: {(info.Chunk.Archetype.StableHash == nextInfo.Chunk.Archetype.StableHash ? "same archetype" : "diff archetypes")} starts from {nextInfo.Ptr.From} cap {nextInfo.Chunk.Count} / {nextInfo.Chunk.Capacity} init:{nextInfo.Ptr.Initialized}");
//                     }
//                 }
//                 
//                 filteredChunks.Dispose();
//                 chunkInfoArray.Dispose();
//                 query.ResetFilter();
//             }
//         }
//     }
// }