using System.Linq;
using System.Reflection;
using NSprites;
using NSprites.Extensions;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;

public class PropertyContainerTests
{
    // A Test behaves as an ordinary method
    [Test]
    public void PropertyContainerTestsSimplePasses()
    {
        var container = new PropertiesContainer();
        
        Assert.NotNull(container.EachUpdate);
        Assert.NotNull(container.Reactive);
        Assert.NotNull(container.Static);
        Assert.NotNull(container[PropertyUpdateMode.EachUpdate]);
        Assert.NotNull(container[PropertyUpdateMode.Reactive]);
        Assert.NotNull(container[PropertyUpdateMode.Static]);

        var mockProperty = new InstancedProperty(0, 1, 4, default);
        
        container.AddProperty(mockProperty, PropertyUpdateMode.EachUpdate);
        
        container.AddProperty(mockProperty, PropertyUpdateMode.Reactive);
        container.AddProperty(mockProperty, PropertyUpdateMode.Reactive);
        
        container.AddProperty(mockProperty, PropertyUpdateMode.Static);
        container.AddProperty(mockProperty, PropertyUpdateMode.Static);
        container.AddProperty(mockProperty, PropertyUpdateMode.Static);
        
        Assert.AreEqual(1, container[PropertyUpdateMode.EachUpdate].Count());
        Assert.AreEqual(2, container[PropertyUpdateMode.Reactive].Count());
        Assert.AreEqual(3, container[PropertyUpdateMode.Static].Count());
        
        container.ConstructHandles(1);
        
        var field = typeof(PropertiesContainer).GetField("_handles", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var handles = (NativeList<JobHandle>)field.GetValue(container);
        Assert.IsTrue(handles.IsCreated);

        var propCount = container.GetPropertiesCount();
        Assert.AreEqual(6, propCount);
        
        container.AddHandle(default);
        container.AddHandle(default);
        container.AddHandle(default);

        var handlesArray = handles.AsArray();
        Assert.IsTrue(handlesArray.IsCreated);
        Assert.AreEqual(3, handlesArray.Length);
    }
}
