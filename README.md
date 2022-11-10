# NSprites - Unity DOTS Sprite Rendering Package
This framework provides sprite rendering system compatible with Entities package (unity ECS).
> Note: Currently supports only Entities 0.51.x. Support for 1.0 is the next development stage.

Basically it sync whatever entity component you want with GPU data to perform instanced rendering. As a result all entities with same Material can be rendered with single DrawCall.

## Basic API
**For more detailed information please read [project's wiki](https://github.com/Antoshidza/NSprites/wiki).**
```csharp
// registrate components as properties at assembly level anywhere in project
[assembly: InstancedPropertyComponent(typeof(WorldPosition2D), "_pos2D", PropertyFormat.Float2)]
[assembly: InstancedPropertyComponent(typeof(SpriteColor), "_color", PropertyFormat.Float4)]
```
```csharp
// registrate render with ID, Material, capacity data and set of properties
var renderSystem = World.GetSystem<SpriteRenderingSystem>();
// don't registrate same renderID
renderSystem.RegisterRender
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
// WorldPosition2D and SpriteColor are example client's components
entityManager.AddComponentData(new WorldPosition2D());          
entityManager.AddComponentData(new SpriteColor(Color.White));
```
Also shader you're using should be compatible with instancing. Check my [example shader gist](https://gist.github.com/Antoshidza/387bf4a3a3efd62c8ca4267e800ad3bc). The main idea is to use `StructuredBuffer<T> _propertyName`. Though it is possible to use instanced properties with ShaderGraph, so you may try your option. For local example shader main part can look like:
```hlsl
#if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) || defined(UNITY_STEREO_INSTANCING_ENABLED)
StructuredBuffer<int> _propertyPointers;
StructuredBuffer<float2> _pos2D;
StructuredBuffer<float4> _color;
#endif
// ...
Varyings UnlitVertex(Attributes attributes, uint instanceID : SV_InstanceID)
{
    // ...    
#if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) || defined(UNITY_STEREO_INSTANCING_ENABLED)
    int propPointer = _propertyPointers[instanceID]; // this is internal package property to point right data during component sync
    float2 pos = _pos2D[propPointer];
    float4 color = _color[propPointer];
#else
    //fallback if somehow instancing failed
    float2 pos = float2(0,0);
    float4 color = flaot4(1,1,1,1);
#endif
    // ...
}
```

## How it works
[`SpriteRenderingSystem`](https://github.com/Antoshidza/NSprites/blob/main/Rendering/Systems/SpriteRenderingSystem.cs) sync registered entity components with [ComputeBuffers](https://docs.unity3d.com/ScriptReference/ComputeBuffer.html) to send data to GPU and then renders entities with [`Graphics.DrawMeshInstancedProcedural`](https://docs.unity3d.com/ScriptReference/Graphics.DrawMeshInstancedProcedural.html). System also controls how ComputeBuffers reallocates if capacity exceeds. Sprites are simple entities with no limits what components you use.

## Installation
### Requirements
* Unity 2020.3.x / 2021.3.x
* Entities 0.51.x

### [Install via Package Manager](https://docs.unity3d.com/2021.3/Documentation/Manual/upm-ui-giturl.html)
* Window -> Package Manager -> + button -> Add package from git url
* Paste `https://github.com/Antoshidza/NSprites.git`
### Install via git submodule
* `cd` to your project's `/Packages` folder
* git submodule https://github.com/Antoshidza/NSprites.git

## Support :+1: Contribute :computer: Contanct :speech_balloon:
I wish this project will be helpful for any ECS early adopters! So feel free to send bug reports / pull requests, start discussions / critique, those all are **highly** appreciated!
You can contact with my [discord account](https://www.discordapp.com/users/219868910223228929)!

[!["Buy Me A Coffee"](https://www.buymeacoffee.com/assets/img/custom_images/orange_img.png)](https://www.buymeacoffee.com/antoshidzamax)
