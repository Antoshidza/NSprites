using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;

namespace NSprites
{
    [StructLayout(LayoutKind.Sequential)]
    [NativeContainer]
    unsafe public struct NativeComparer<T, TComparer> : IDisposable
        where T : unmanaged
        where TComparer : unmanaged, IComparer<T>
    {
        private readonly int m_TPerCacheLine;

        // The actual pointer to the allocated count needs to have restrictions relaxed so jobs can be schedled with this container
        [NativeDisableUnsafePtrRestriction]
        T* m_Value;

        //[NativeDisableUnsafePtrRestriction]
        private readonly TComparer m_Comparer;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle m_Safety;
        // The dispose sentinel tracks memory leaks. It is a managed type so it is cleared to null when scheduling a job
        // The job cannot dispose the container, and no one else can dispose it until the job has run, so it is ok to not pass it along
        // This attribute is required, without it this NativeContainer cannot be passed to a job; since that would give the job access to a managed object
        [NativeSetClassTypeToNullOnSchedule]
        DisposeSentinel m_DisposeSentinel;
#endif

        // Keep track of where the memory for this was allocated
        Allocator m_AllocatorLabel;

        public NativeComparer(Allocator label, in TComparer comparer, in T defaultValue = default)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!UnsafeUtility.IsBlittable<T>())
                throw new ArgumentException($"{typeof(T)} used in {typeof(NativeComparer<T, TComparer>)} must be blittable");
#endif
            m_AllocatorLabel = label;

            var genericTypeSize = UnsafeUtility.SizeOf<T>();
            m_TPerCacheLine = JobsUtility.CacheLineSize / genericTypeSize;
            m_Value = (T*)UnsafeUtility.Malloc(genericTypeSize * m_TPerCacheLine * JobsUtility.MaxJobThreadCount, UnsafeUtility.AlignOf<T>(), label);
            if (m_Value == null)
                throw new Exception($"{typeof(T)} leads to value is null at constructor");
            m_Comparer = comparer;

            // Create a dispose sentinel to track memory leaks. This also creates the AtomicSafetyHandle
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 0, label);
#endif
            // Initialize the count to 0 to avoid uninitialized data
            for (int i = 0; i < JobsUtility.MaxJobThreadCount; ++i)
                m_Value[m_TPerCacheLine * i] = defaultValue;
        }

        /// <summary>
        /// Sets value with wich comapring will happen.
        /// </summary>
        public void SetValue(in T value)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            //set all cachelines to same value, to sync comparing
            for (int i = 0; i < JobsUtility.MaxJobThreadCount; ++i)
                m_Value[m_TPerCacheLine * i] = value;
        }
        /// <summary>
        /// Compares passed value with current, replace if compare passed (compare result is equal to 1).
        /// </summary>
        public void Account(in T value)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            // Compare all cachelines with passed value and set result to 0 line
            var current = value;
            for (int i = 0; i < JobsUtility.MaxJobThreadCount; ++i)
            {
                var i_value = m_Value[m_TPerCacheLine * i];
                current = m_Comparer.Compare(current, i_value) == 1 ? current : i_value;
            }
            *m_Value = current;
        }

        public T Value
        {
            get
            {
                T current = m_Value[0];
                for (int i = 1; i < JobsUtility.MaxJobThreadCount; ++i)
                {
                    var i_value = m_Value[m_TPerCacheLine * i];
                    current = m_Comparer.Compare(current, i_value) == 1 ? current : i_value;
                }
                return current;
            }
        }

        public bool IsCreated
        {
            get { return m_Value != null; }
        }

        public void Dispose()
        {
            // Let the dispose sentinel know that the data has been freed so it does not report any memory leaks
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref m_Safety, ref m_DisposeSentinel);
#endif
            UnsafeUtility.Free(m_Value, m_AllocatorLabel);
            m_Value = null;
        }

        [NativeContainer]
        // This attribute is what makes it possible to use NativeComparer.ParallelWriter in a ParallelFor job
        [NativeContainerIsAtomicWriteOnly]
        unsafe public struct ParallelWriter
        {
            private readonly int m_TPerCacheLine;

            // Copy of the pointer from the full NativeComparer
            [NativeDisableUnsafePtrRestriction]
            T* m_Value;

            private readonly TComparer m_Comparer;

            // The current worker thread index; it must use this exact name since it is injected
            [NativeSetThreadIndex]
            int m_ThreadIndex;

            // Copy of the AtomicSafetyHandle from the full NativeComparer. The dispose sentinel is not copied since this inner struct does not own the memory and is not responsible for freeing it.
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle m_Safety;
#endif
            public ParallelWriter(ref NativeComparer<T, TComparer> nativeComparer)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(nativeComparer.m_Safety);
                m_Safety = nativeComparer.m_Safety;
                AtomicSafetyHandle.UseSecondaryVersion(ref m_Safety);
#endif
                m_Value = nativeComparer.m_Value;
                m_Comparer = nativeComparer.m_Comparer;
                m_TPerCacheLine = nativeComparer.m_TPerCacheLine;
                m_ThreadIndex = 0;
            }
            // This is what makes it possible to assign to NativeComparer.ParallelWriter from NativeComparer
            public static implicit operator ParallelWriter(NativeComparer<T, TComparer> nativeComparer)
            {
                return new ParallelWriter(ref nativeComparer);
            }

            public void Account(in T value)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                var cacheLineIndex = m_TPerCacheLine * m_ThreadIndex;
                var current = m_Value[cacheLineIndex];
                m_Value[cacheLineIndex] = m_Comparer.Compare(current, value) == 1 ? current : value;
            }
        }
    }
}
