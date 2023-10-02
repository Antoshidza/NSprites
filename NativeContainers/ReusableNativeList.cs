using System;
using Unity.Collections;

namespace NSprites
{
    public struct ReusableNativeList<T> : IDisposable
        where T : unmanaged
    {
        private NativeList<T> _list;

        public ReusableNativeList(in int initialCapacity, in Allocator allocator) 
            => _list = new NativeList<T>(initialCapacity, allocator);

        public void Dispose()
        {
            if(_list.IsCreated)
                _list.Dispose();
        }

        public NativeList<T> GetList(in int capacity)
        {
            if (_list.Capacity < capacity)
                _list.SetCapacity(capacity);
            _list.Clear();
            return _list;
        }
    }
}