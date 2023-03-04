using System;
using System.Collections.Generic;
using System.Linq;

namespace NSprites
{
    /// <summary>
    /// Use this attribute to mark that component contains property data for particular shader's StructuredBuffer (instanced) property.
    /// <br><see cref="SpriteRenderingSystem.OnCreate"></see> will automatically fetch all component types and map them with shader property's names.</br>
    /// <br>Another way is to use <see cref="SpriteRenderingSystem.BindComponentToShaderProperty"></see> to bind component types to shader property's names manually.</br>
    /// <para> Note: attribute target is <see cref="AttributeTargets.Assembly"></see> to let you mark types outside from your assembly </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class InstancedPropertyComponent : Attribute
    {
        // entity component type we will fetch data for shader from
        public readonly Type componentType;
        // instanced property's name in shader
        public readonly string propertyName;
        // data's type which component contains

        /// <param name="componentType">entity component type which will be data source for shader's properties</param>
        /// <param name="propertyName">shader's instanced property name</param>
        public InstancedPropertyComponent(Type componentType, string propertyName)
        {
            this.componentType = componentType;
            this.propertyName = propertyName;
        }

        /// <summary>Returns all <see cref="InstancedPropertyComponent"></see> from each assembly from whole app</summary>
        public static IEnumerable<InstancedPropertyComponent> GetProperties()
        {
            return NSpritesUtils.GetAssemblyAttributes<InstancedPropertyComponent>()
                .Select(attr => attr);
        }
    }
}
