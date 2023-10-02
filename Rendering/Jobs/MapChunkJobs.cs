#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

namespace NSprites
{
    // All "Map" jobs grab chunk / entity and map it to compute buffer using PropertyPointerChunk for chunk and PropertyPointer for entity
    [BurstCompile]
    internal struct MapListedChunksJob : IJob
    {
        [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
        [WriteOnly][NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<PropertyPointerChunk> PropertyPointerChunk_CTH;
        public int StartingFromIndex;
        [ReadOnly] public NativeList<int> ChunksIndices;

        public void Execute()
        {
            for (var i = 0; i < ChunksIndices.Length; i++)
            {
                var chunk = Chunks[ChunksIndices[i]];
                var capacity = chunk.Capacity;
                chunk.SetChunkComponentData(ref PropertyPointerChunk_CTH, new PropertyPointerChunk { From = StartingFromIndex, Initialized = true });
                StartingFromIndex += capacity;
            }
        }
    }
    
    [BurstCompile]
    internal struct MapListedEntitiesJob : IJobParallelForBatch
    {
        [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
        [ReadOnly] public NativeList<int> ChunkIndexes;
        [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<PropertyPointer> PropertyPointer_CTH_Wo;
        [ReadOnly] public ComponentTypeHandle<PropertyPointerChunk> PropertyPointerChunk_CTH_RO;

        public void Execute(int startIndex, int count)
        {
            var toIndex = startIndex + count;
            for (var i = startIndex; i < toIndex; i++)
            {
                var chunk = Chunks[ChunkIndexes[i]];
                var chunkPointer = chunk.GetChunkComponentData(ref PropertyPointerChunk_CTH_RO);
                var entityIndexes = chunk.GetNativeArray(ref PropertyPointer_CTH_Wo);
                for (var entityIndex = 0; entityIndex < entityIndexes.Length; entityIndex++)
                    entityIndexes[entityIndex] = new PropertyPointer { bufferIndex = chunkPointer.From + entityIndex };
            }
        }
    }
    
    [BurstCompile]
    internal struct MapChunksJob : IJob
    {
        [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
        [WriteOnly][NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<PropertyPointerChunk> PropertyPointerChunk_CTH;

        public void Execute()
        {
            var index = 0;
            for (var i = 0; i < Chunks.Length; i++)
            {
                var chunk = Chunks[i];
                var capacity = chunk.Capacity;
                chunk.SetChunkComponentData(ref PropertyPointerChunk_CTH, new PropertyPointerChunk { From = index, Initialized = true });
                index += capacity;
            }
        }
    }
    
    [BurstCompile]
    internal struct MapEntitiesJob : IJobParallelForBatch
    {
        [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
        [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<PropertyPointer> PropertyPointer_CTH;
        [ReadOnly] public ComponentTypeHandle<PropertyPointerChunk> PropertyPointerChunk_CTH;

        public void Execute(int startIndex, int count)
        {
            var toIndex = startIndex + count;
            for (var chunkIndex = startIndex; chunkIndex < toIndex; chunkIndex++)
            {
                var chunk = Chunks[chunkIndex];
                var chunkPointer = chunk.GetChunkComponentData(ref PropertyPointerChunk_CTH);
                var entityIndexes = chunk.GetNativeArray(ref PropertyPointer_CTH);
                for (var entityIndex = 0; entityIndex < entityIndexes.Length; entityIndex++)
                    entityIndexes[entityIndex] = new PropertyPointer { bufferIndex = chunkPointer.From + entityIndex };
            }
        }
    }
}
#endif