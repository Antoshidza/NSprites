using System;

namespace NSprites
{
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
    public class InstancedProperty : Attribute
    {
        public string name;
        public SpriteRenderingSystem.PropertyFormat format;

        public InstancedProperty(string name, SpriteRenderingSystem.PropertyFormat format)
        {
            this.name = name;
            this.format = format;
        }
    }
}
