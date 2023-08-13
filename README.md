# NSprites - Unity DOTS Sprite Rendering Package
This framework provides sprite rendering system compatible with Entities package (unity ECS). [Changelog](https://github.com/Antoshidza/NSprites/wiki/Changelog)

Basically it sync whatever entity component you want with GPU data to perform instanced rendering. As a result all entities with same Material can be rendered with single drawcall.

<img src="https://user-images.githubusercontent.com/19982288/203323912-3f0aec5a-543d-4145-bf8f-42e07af2d124.gif" width="700"/>

## Features
* Using power of :boom:**DOTS**:boom: + instancing to render numerous of sprites
* Using any public to you per-entity blittable component as shader instanced property
* Data update strategies to avoid unnecessary CPU load
* Edit-time rendering (subscene only)

## Basic API
**For more detailed information please read [project's wiki](https://github.com/Antoshidza/NSprites/wiki)** :blue_book:
```csharp
// registrate components as properties at assembly level anywhere in project
[assembly: InstancedPropertyComponent(typeof(WorldPosition2D), "_pos2D")]
[assembly: InstancedPropertyComponent(typeof(SpriteColor), "_color")]
```
```csharp
// registrate render with ID, Material, capacity data and set of properties
if (!SystemAPI.ManagedAPI.TryGetSingleton<RenderArchetypeStorage>(out var renderArchetypeStorage))
    return;
// don't registrate same renderID
renderArchetypeStorage.RegisterRender
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
entityManager.AddComponentData(spriteEntity, new WorldPosition2D { Value = /*your value here*/ });          
entityManager.AddComponentData(spriteEntity, new SpriteColor { Value = Color.White });

// or from baker
private class Baker : Baker<SpriteAuthoring>
{
    public override void Bake(SpriteAuthoring authoring)
    {
        AddComponent(new WorldPosition2D { Value = new float2(authoring.transform.position.x, authoring.transform.position.y) });
        AddComponent(new SpriteColor { Value = Color.White });
        this.AddSpriteComponents(authoring.RenderID); // Render ID is client defined unique per-render archetype int. You can define it manually or for example use Material's instance ID or whatever else.
    }
}
```
Also shader you're using should be compatible with instancing. Check my [example shader gist](https://gist.github.com/Antoshidza/387bf4a3a3efd62c8ca4267e800ad3bc). The main idea is to use `StructuredBuffer<T> _propertyName`. Though it is possible to use instanced properties with ShaderGraph, so you may try your option. For local example shader main part can look like:
```hlsl
// ...
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
StructuredBuffer<int> _propertyPointers;
StructuredBuffer<float4> _color;
#endif
// ...
Varyings UnlitVertex(Attributes attributes, uint instanceID : SV_InstanceID)
{
    // ...    
#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
    int propPointer = _propertyPointers[instanceID]; // this is internal package property to point right data during component sync
    float4 color = _color[propPointer];
#else
    //fallback if somehow instancing failed
    float4 color = float4(1,1,1,1);
#endif
    // ...
}
```

## How it works
[`SpriteRenderingSystem`](https://github.com/Antoshidza/NSprites/blob/main/Rendering/Systems/SpriteRenderingSystem.cs) sync registered entity components with [ComputeBuffers](https://docs.unity3d.com/ScriptReference/ComputeBuffer.html) to send data to GPU and then renders entities with [`Graphics.DrawMeshInstancedProcedural`](https://docs.unity3d.com/ScriptReference/Graphics.DrawMeshInstancedProcedural.html). System also controls how ComputeBuffers reallocates if capacity exceeds. Sprites are simple entities with no limits of what components you use.

## Check [Foundation](https://github.com/Antoshidza/NSprites-Foundation)
NSprites doesn't provide anything except rendering and managing data for it. Though you can implement anything you want on top of it. Also I want to share some foundation project where you can find examples and maybe even useful tools to work with this package. Foundation provides such things as sorting / culling / animation / 2D transforms / basic data authoring and registration.

## Check sample project - [Age of Sprites](https://github.com/Antoshidza/Age-of-Sprites)
This sample project covers basics of rendering with NSprites. Use it to get a main idea of how stuff can be implemented but not as production-ready solutions.

![RomeGIf](https://user-images.githubusercontent.com/19982288/204523105-7cabb122-954c-4fb0-97bc-becb27d2d2b9.gif)

## Installation
### Requirements
* Unity 2022.2+
* Entities v1.0.0-pre.65+

### [Install via Package Manager](https://docs.unity3d.com/2021.3/Documentation/Manual/upm-ui-giturl.html)
* Window -> Package Manager -> + button -> Add package from git url
* Paste `https://github.com/Antoshidza/NSprites.git`
### Install via git submodule
* `cd` to your project's `/Packages` folder
* git submodule https://github.com/Antoshidza/NSprites.git

## Support :+1: Contribute :computer: Contact :speech_balloon:
I wish this project will be helpful for any ECS early adopters! So feel free to send bug reports / pull requests, start discussions / critique, those all are **highly** appreciated!
You can contact with my [discord account](https://www.discordapp.com/users/219868910223228929) / join [discord server](https://discord.gg/rvxrHEFx8n)!
Also there is a [thread](https://forum.unity.com/threads/1-0-3-nsprites-sprite-rendering-package.1367463/) on unity dots forum!

[!["Buy Me A Coffee"](https://www.buymeacoffee.com/assets/img/custom_images/orange_img.png)](https://www.buymeacoffee.com/antoshidzamax)
