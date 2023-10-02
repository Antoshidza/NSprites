using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;

namespace NSprites
{
    internal class PropertiesContainer : IDisposable
    {
        private readonly List<InstancedProperty>[] _propertiesLists = 
        {
            new List<InstancedProperty>(),  // Reactive
            new List<InstancedProperty>(),  // EachUpdate
            new List<InstancedProperty>()   // Static
        };

        private readonly IEnumerable<InstancedProperty>[] _propsEnumerables;

        private NativeList<JobHandle> _handles;

        public JobHandle GeneralHandle
            => JobHandle.CombineDependencies(_handles.AsArray());

        public IEnumerable<InstancedProperty> this[PropertyUpdateMode updateMode] 
            => _propsEnumerables[(int)updateMode];

        public IEnumerable<InstancedProperty> Reactive => this[PropertyUpdateMode.Reactive];
        public IEnumerable<InstancedProperty> EachUpdate => this[PropertyUpdateMode.EachUpdate];
        public IEnumerable<InstancedProperty> Static => this[PropertyUpdateMode.Static];

        public PropertiesContainer()
        {
            // avoid boxing each time property accessed
            _propsEnumerables = new IEnumerable<InstancedProperty>[_propertiesLists.Length];
            for (var i = 0; i < _propertiesLists.Length; i++)
                _propsEnumerables[i] = _propertiesLists[i];
        }

        public bool HasPropertiesWithMode(in PropertyUpdateMode updateMode)
            => _propertiesLists[(int)updateMode].Count > 0;

        public void AddProperty(InstancedProperty property, in PropertyUpdateMode updateMode) 
            => _propertiesLists[(int)updateMode].Add(property);

        public void ConstructHandles(int extraCount)
        {
            var propertiesCount = extraCount + 
                                  _propertiesLists[(int)PropertyUpdateMode.Reactive].Count +
                                  _propertiesLists[(int)PropertyUpdateMode.EachUpdate].Count +
                                  _propertiesLists[(int)PropertyUpdateMode.Static].Count;
            _handles = new NativeList<JobHandle>(propertiesCount, Allocator.Persistent);
        }

        public void AddHandle(in JobHandle handle) => _handles.AddNoResize(handle);
        public void ResetHandles() => _handles.Clear();
        public void Dispose()
        {
            foreach (var list in _propertiesLists)
                foreach (var prop in list)
                    prop.Dispose();
            
            _handles.Dispose();
        }
    }
}