using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

namespace NSprites
{
    /// <summary>
    /// Contains jobs which are used to sync properties data with GPU buffers 
    /// </summary>
    [BurstCompile]
    public static class SyncDataJobs
    {
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteChunkData(this in ArchetypeChunk chunk, ref DynamicComponentTypeHandle componentTypeHandle, [NoAlias]int startCopyToIndex, in NativeArray<byte> writeArray, [NoAlias]int typeSize)
        {
            chunk.GetData(ref componentTypeHandle, typeSize, out var data);
            WriteData(data, writeArray, startCopyToIndex, typeSize);
        }
        
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteFullChunkData(this in ArchetypeChunk chunk, ref DynamicComponentTypeHandle componentTypeHandle, [NoAlias]int startCopyToIndex, in NativeArray<byte> writeArray, [NoAlias]int typeSize)
        {
            chunk.GetData(ref componentTypeHandle, typeSize, out var data);
            var chunkCapacityInBytes = chunk.Capacity * typeSize;
            
            if(data.Length < chunkCapacityInBytes)
                unsafe
                {
                    data = CollectionHelper.ConvertExistingDataToNativeArray<byte>(data.GetUnsafeReadOnlyPtr(), chunk.Capacity * typeSize, Allocator.Temp, true);
                }

            WriteData(data, writeArray, startCopyToIndex, typeSize);
        } 

        /// <summary>
        /// Returns byte array reinterpreted data from chunk. Make fallback if such option enabled + make safety check if such option enabled
        /// </summary>
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GetData(this in ArchetypeChunk chunk, ref DynamicComponentTypeHandle componentTypeHandle, int typeSize, out NativeArray<byte> data)
        {
#if NSPRITES_PROPERTY_FALLBACK_ENABLE
            // check if chunk has no prop component then allocate default values
            // ComputeBuffer by itself has already allocated memory, but it's uninitialized, so render result can be unexpected
            data = chunk.Has(ref componentTypeHandle)
                ? chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref componentTypeHandle, typeSize)
                : new NativeArray<byte>(chunk.Count * typeSize, Allocator.Temp, NativeArrayOptions.ClearMemory);
#elif UNITY_EDITOR || DEVELOPEMENT_BUILD
            // TODO: add #if NSPRITES_SAFETY
            if (!chunk.Has(ref componentTypeHandle))
                throw new NSpritesException($"You trying to render entities but it missed one of the required component.");
#endif
            data = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref componentTypeHandle, typeSize);
        }

        /// <summary>
        /// Copy <see cref="data"/> array into <see cref="writeArray"/> starting from <see cref="startCopyToIndex"/> if it would be two array of any type with size of <see cref="typeSize"/> bytes
        /// </summary>
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteData([NoAlias]in NativeArray<byte> data, [NoAlias]in NativeArray<byte> writeArray, [NoAlias]int startCopyToIndex, [NoAlias]int typeSize)
            => NativeArray<byte>.Copy(data, 0, writeArray, startCopyToIndex * typeSize, data.Length);

        /// <summary> Takes chunks, read theirs <see cref="PropertyPointerChunk"/> and copy theirs component data to output array starting from chunk's range from value </summary>
        [BurstCompile]
        internal struct SyncPropertyByChunkJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
            // this should be filled every frame with GetDynamicComponentTypeHandle
            [ReadOnly] public DynamicComponentTypeHandle ComponentTypeHandle;
            [ReadOnly] public ComponentTypeHandle<PropertyPointerChunk> PropertyPointerChunk_CTH;
            [WriteOnly][NativeDisableParallelForRestriction] public NativeArray<byte> OutputArray;
            public int TypeSize;

            public void Execute(int startIndex, int count)
            {
                var toIndex = startIndex + count;
                for (var chunkIndex = startIndex; chunkIndex < toIndex; chunkIndex++)
                {
                    var chunk = Chunks[chunkIndex];
                    chunk.WriteFullChunkData(ref ComponentTypeHandle, chunk.GetChunkComponentData(ref PropertyPointerChunk_CTH).From, OutputArray, TypeSize);
                }
            }
        }
        
        /// <summary> Takes changed / reordered chunks, read theirs <see cref="PropertyPointerChunk"/> and copy component data to output array starting from chunk's range from value </summary>
        [BurstCompile]
        internal struct SyncPropertyByChangedChunkJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
            // this should be filled every frame with GetDynamicComponentTypeHandle
            [ReadOnly] public DynamicComponentTypeHandle ComponentTypeHandle;
            [ReadOnly] public ComponentTypeHandle<PropertyPointerChunk> PropertyPointerChunk_CTH;
            [WriteOnly][NativeDisableParallelForRestriction] public NativeArray<byte> OutputArray;
            public int TypeSize;
            public uint LastSystemVersion;

            public void Execute(int startIndex, int count)
            {
                var toIndex = startIndex + count;
                for (var chunkIndex = startIndex; chunkIndex < toIndex; chunkIndex++)
                {
                    var chunk = Chunks[chunkIndex];

                    // if chunk has no data changes AND has no new / lost entities then do nothing
                    if (!chunk.DidChange(ref ComponentTypeHandle, LastSystemVersion) && !chunk.DidOrderChange(LastSystemVersion))
                        continue;
                    
                    chunk.WriteFullChunkData(ref ComponentTypeHandle, chunk.GetChunkComponentData(ref PropertyPointerChunk_CTH).From, OutputArray, TypeSize);
                }
            }
        }
        
        /// <summary> Takes listed chunks, read theirs <see cref="PropertyPointerChunk"/> and copy component data to output array starting from chunk's range from value </summary>
        [BurstCompile]
        internal struct SyncPropertyByListedChunkJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
            [ReadOnly] public NativeList<int> ChunkIndexes;
            // this should be filled every frame with GetDynamicComponentTypeHandle
            [ReadOnly] public DynamicComponentTypeHandle ComponentTypeHandle;
            [ReadOnly] public ComponentTypeHandle<PropertyPointerChunk> PropertyPointerChunk_CTH;
            [WriteOnly][NativeDisableParallelForRestriction][NativeDisableContainerSafetyRestriction] public NativeArray<byte> OutputArray;
            public int TypeSize;

            public void Execute(int startIndex, int count)
            {
                var toIndex = startIndex + count;
                for (var i = startIndex; i < toIndex; i++)
                {
                    var chunk = Chunks[ChunkIndexes[i]];
                    chunk.WriteFullChunkData(ref ComponentTypeHandle, chunk.GetChunkComponentData(ref PropertyPointerChunk_CTH).From, OutputArray, TypeSize);
                }
            }
        }
        
        /// <summary> Iterates chunks from query, and then just copy component data to compute buffer starting from 1st entity in query index </summary>
        [BurstCompile]
        internal struct SyncPropertyByQueryJob : IJobChunk
        {
            [ReadOnly][DeallocateOnJobCompletion] public NativeArray<int> ChunkBaseEntityIndices;
            // this should be filled every frame with GetDynamicComponentTypeHandle
            [ReadOnly]public DynamicComponentTypeHandle ComponentTypeHandle;
            [WriteOnly][NativeDisableParallelForRestriction] public NativeArray<byte> OutputArray;
            public int TypeSize;
            
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask) 
                => chunk.WriteChunkData(ref ComponentTypeHandle, ChunkBaseEntityIndices[unfilteredChunkIndex], OutputArray, TypeSize);
        }
    }
}