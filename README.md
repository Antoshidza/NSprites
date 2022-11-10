# NSprites - Unity DOTS Sprite Rendering Package
This framework provides sprite rendering system compatible with Entities package (unity ECS).
> Note: Currently supports only Entities 0.51.x. Support for 1.0 is the next development stage.

Basically it sync whatever entity component you want with GPU data to perform instanced rendering. As a result all entities with same Material can be rendered with single DrawCall.

## Basic API
```csharp
// registrate components as properties at assembly level anywhere in project
[assembly: InstancedPropertyComponent(typeof(WorldPosition2D), "_pos2D", PropertyFormat.Float2)]
[assembly: InstancedPropertyComponent(typeof(SpriteColor), "_color", PropertyFormat.Float4)]
```
```csharp
// registrate render with ID, Material, capacity data and set of properties
var renderSystem = World.GetSystem<SpriteRenderingSystem>();
// don't registrate same renderID
renderSystem.RegistrateRender
(
    renderID,
    material,   // material with [Enable GPU Instancing] enabled and shader supporting instancing
    null,       // override for MaterialPropertyBlock if needed
    128,        // initial ComputeBuffers capacity
    128,        // minimal capacity step for ComputeBuffers
    "_pos2D",   // world 2D position property
    "_color"    // color property
);
```
```csharp
// initialize sprite entity with all needed components for rendering
entityManager.AddSpriteRenderComponents(spriteEntity, renderID);
entityManager.AddComponentData(new WorldPosition2D());          // WorldPosition2D and SpriteColor are example client's components
entityManager.AddComponentData(new SpriteColor(Color.White));
```
Also shader you're using should be compatible with instancing. Check my [example shader gist](https://gist.github.com/Antoshidza/387bf4a3a3efd62c8ca4267e800ad3bc). The main idea is to use `StructuredBuffer<T> _propertyName`. Though it is possible to use instanced properties with ShaderGraph, so you may try your option.

## How it works
[`SpriteRenderingSystem`](https://github.com/Antoshidza/NSprites/blob/main/Rendering/Systems/SpriteRenderingSystem.cs) sync registered entity components with [ComputeBuffers](https://docs.unity3d.com/ScriptReference/ComputeBuffer.html) to send data to GPU and then renders entities with [`Graphics.DrawMeshInstancedProcedural`](https://docs.unity3d.com/ScriptReference/Graphics.DrawMeshInstancedProcedural.html). System also controls how ComputeBuffers reallocates if capacity exceeds. Sprites are simple entities with no limits what components you use.

## Instalation
### Requirements
* Unity 2020.3.x / 2021.3.x
* Entities 0.51.x

### [Install via Package Manager](https://docs.unity3d.com/2021.3/Documentation/Manual/upm-ui-giturl.html)
* Window -> Package Manager -> + button -> Add package from git url
* Paste `https://github.com/Antoshidza/NSprites.git`
### Install via git submodule
* `cd` to your project's `/Packages` folder
* git submodule https://github.com/Antoshidza/NSprites.git
