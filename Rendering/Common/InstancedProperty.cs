using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace NSprites
{
    /// <summary>
    /// Holds data to schedule property component's data with compute buffers,
    /// so per property in render archetype we have one instance of this class
    /// </summary>
    internal class InstancedProperty : IDisposable
    {
        /// <summary> Property id from <see cref="Shader.PropertyToID"/> to be able to pass to <see cref="MaterialPropertyBlock"/> </summary>
        internal readonly int PropertyID;
        /// <summary> Buffer which synced with entities components data </summary>
        internal ComputeBuffer ComputeBuffer;

        private JobHandle _handle;
        private bool _wasScheduled;
        private int _writeBytesCount;
        
#if UNITY_EDITOR || DEVELOPEMENT_BUILD
        internal bool IsWriting;
#endif

        private int TypeSize => ComputeBuffer.stride;

        /// <summary> Cached component type + system to retrieve <see cref="DynamicComponentTypeHandle"/> </summary>
        public ComponentType ComponentType { get; }

        public InstancedProperty(in int propertyID, in int count, in int stride, in ComponentType componentType)
        {
            PropertyID = propertyID;
            ComponentType = componentType;

            ComputeBuffer = new ComputeBuffer(count, stride, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
        }
        
#if !NSPRITES_REACTIVE_DISABLE || !NSPRITES_STATIC_DISABLE
        /// <summary> Schedule sync job by each <see cref="ArchetypeChunk"/> in <see cref="chunks"/> array. Each chunk will sync it's data by it's whole capacity. </summary>
        public JobHandle SyncByChunks(in NativeArray<ArchetypeChunk> chunks, in ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH, in DynamicComponentTypeHandle componentTypeHandle, in int writeCount, in JobHandle inputDeps)
        {
            _handle = new SyncDataJobs.SyncPropertyByChunkJob
            {
                Chunks = chunks,
                ComponentTypeHandle = componentTypeHandle,
                TypeSize = TypeSize,
                PropertyPointerChunk_CTH = propertyPointerChunk_CTH,
                OutputArray = BeginSyncBuffer(writeCount)
            }.ScheduleBatch(chunks.Length, RenderArchetype.MinIndicesPerJobCount, inputDeps);
            _wasScheduled = true;
            return _handle;
        }
        
        /// <summary> Schedule sync job by each <see cref="ArchetypeChunk"/> in <see cref="chunks"/> array, skip unchanged. Each chunk will sync it's data by it's whole capacity. </summary>
        public JobHandle SyncByChangedChunks(in NativeArray<ArchetypeChunk> chunks, in ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH, in DynamicComponentTypeHandle componentTypeHandle, in int writeCount, in uint lastSystemVersion, in JobHandle inputDeps)
        {
            _handle = new SyncDataJobs.SyncPropertyByChangedChunkJob
            {
                Chunks = chunks,
                ComponentTypeHandle = componentTypeHandle,
                TypeSize = TypeSize,
                PropertyPointerChunk_CTH = propertyPointerChunk_CTH,
                LastSystemVersion = lastSystemVersion,
                OutputArray = BeginSyncBuffer(writeCount)
            }.ScheduleBatch(chunks.Length, RenderArchetype.MinIndicesPerJobCount, inputDeps);
            _wasScheduled = true;
            return _handle;
        }
#endif
#if !NSPRITES_STATIC_DISABLE
        /// <summary> Schedule 2 sync job by each <see cref="ArchetypeChunk"/> in <see cref="chunks"/> array but only referred by <see cref="reorderedIndexes"/> and <see cref="createdIndexes"/>. Each chunk will sync it's data by it's whole capacity.
        /// Two jobs run in parallel because created and reordered chunks never intersect.</summary>
        public JobHandle SyncByCreatedAndReorderedChunks(in NativeArray<ArchetypeChunk> chunks, in NativeList<int> reorderedIndexes, in NativeList<int> createdIndexes, in ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH, in DynamicComponentTypeHandle componentTypeHandle, in int writeCount, in JobHandle inputDeps)
        {
            var writeArray = BeginSyncBuffer(writeCount);
            var reorderedHandle = SyncByIndexedChunkData(chunks, writeArray, reorderedIndexes, propertyPointerChunk_CTH, componentTypeHandle, inputDeps);
            var createdHandle = SyncByIndexedChunkData(chunks, writeArray, createdIndexes, propertyPointerChunk_CTH, componentTypeHandle, inputDeps);
            _handle = JobHandle.CombineDependencies(reorderedHandle, createdHandle);
            _wasScheduled = true;
            return _handle;
        }
        
        /// <summary> Schedule sync job by each <see cref="ArchetypeChunk"/> in <see cref="chunks"/> array but only referred by <see cref="chunkIndexes"/>. Each chunk will sync it's data by it's whole capacity. </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private JobHandle SyncByIndexedChunkData(in NativeArray<ArchetypeChunk> chunks, in NativeArray<byte> writeArray, in NativeList<int> chunkIndexes, in ComponentTypeHandle<PropertyPointerChunk> propertyPointerChunk_CTH, in DynamicComponentTypeHandle componentTypeHandle, in JobHandle inputDeps)
        {
            _handle = new SyncDataJobs.SyncPropertyByListedChunkJob
            {
                Chunks = chunks,
                ChunkIndexes = chunkIndexes,
                TypeSize = TypeSize,
                PropertyPointerChunk_CTH = propertyPointerChunk_CTH,
                ComponentTypeHandle = componentTypeHandle,
                OutputArray = writeArray
            }.ScheduleBatch(chunkIndexes.Length, RenderArchetype.MinIndicesPerJobCount, inputDeps);
            _wasScheduled = true;
            return _handle;
        }
#endif
        /// <summary> Schedule sync job by <see cref="EntityQuery"/>. Query will lay in chunk entity-by-entity without writing full chunk's capacity. </summary>
        public JobHandle SyncByQuery(ref EntityQuery query, in DynamicComponentTypeHandle componentTypeHandle, in int writeCount, in JobHandle inputDeps)
        {
            var updatePropertyJob = new SyncDataJobs.SyncPropertyByQueryJob
            {
                ComponentTypeHandle = componentTypeHandle,
                ChunkBaseEntityIndices = query.CalculateBaseEntityIndexArrayAsync(Allocator.TempJob, default, out var calculateChunkBaseIndices),
                TypeSize = TypeSize,
                OutputArray = BeginSyncBuffer(writeCount)
            };
            _handle = updatePropertyJob.ScheduleParallelByRef(query, JobHandle.CombineDependencies(inputDeps, calculateChunkBaseIndices));
            _wasScheduled = true;
            return _handle;
        }
        
        /// <summary> Begins write into <see cref="ComputeBuffer"/> </summary>
        private NativeArray<byte> BeginSyncBuffer(int writeCount)
        {
#if UNITY_EDITOR || DEVELOPEMENT_BUILD
            if(IsWriting)
                throw new NSpritesException($"{nameof(InstancedProperty)} {ComponentType.GetManagedType().Name} is already writing into it's buffer. Multiple calling to {nameof(BeginSyncBuffer)} isn't allowed.");
            IsWriting = true;
#endif
            _writeBytesCount = writeCount * TypeSize;

            return ComputeBuffer.BeginWrite<byte>(0, _writeBytesCount);
        }

        /// <summary> Ends write into <see cref="ComputeBuffer"/>. Any jobs which writes into buffer should be completed. </summary>
        private void EndWrite()
        {
#if UNITY_EDITOR || DEVELOPEMENT_BUILD
            if (!IsWriting)
                throw new NSpritesException($"{nameof(InstancedProperty)} {ComponentType.GetManagedType().Name} isn't writing to buffer. Calling to {nameof(EndWrite)} before writing to buffer isn't allowed.");
            IsWriting = false;
#endif
            
            ComputeBuffer.EndWrite<byte>(_writeBytesCount);
        }

        /// <summary> Forces complete running sync job and ends write to <see cref="ComputeBuffer"/> </summary>
        public void Complete()
        {
            if(!_wasScheduled)
                return;
            
            _handle.Complete();
            _wasScheduled = false;
            EndWrite();
        }

        /// <summary> Reallocate <see cref="ComputeBuffer"/> with new size and previous stride (tmp) and assign buffer to passed <see cref="MaterialPropertyBlock"/> </summary>
        public void Reallocate(in int length, MaterialPropertyBlock materialPropertyBlock)
        {
            var stride = ComputeBuffer.stride;
            ComputeBuffer.Release();
            ComputeBuffer = new ComputeBuffer(length, stride, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
            materialPropertyBlock.SetBuffer(PropertyID, ComputeBuffer);
        }

        public void Dispose() 
            => ComputeBuffer.Release();

#if UNITY_EDITOR
        public override string ToString()
        {
            return $"propID: {PropertyID}, cb_stride: {ComputeBuffer.stride}, cb_capacity: {ComputeBuffer.count}, comp: {ComponentType}";
        }
#endif
    }
}