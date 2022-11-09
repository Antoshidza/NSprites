namespace NSprites
{
    /// <summary> Tells how property should be updated.
    /// <para>The default mode is <see cref="Reactive"/> (if not disabled in project), which is the most common used and most performant in many cases.
    /// <br>With this mode data in <see cref="UnityEngine.ComputeBuffer"/> updates only on data-changed/ectities-created-destroyed.</br></para></summary>
    public enum PropertyUpdateMode
    {
        /// <summary> Reactive property will be updated if: new entity created or destroyed (chunk reordered) / data changed.
        /// Such properties layouted per-chunk. Additional space for buffer also allocated per-chunk.
        /// <para>Note: if you use reactive properties, then you need to load <see cref="PropertyPointer"></see> indexes to shader.
        /// <br>To do so, please, use <b>StructuredBuffer&lt;int&gt; _propertyPointers</b> in your shader, and access to reactive properties data through indexes from this buffer.</br></para>
        /// <para>Note: if you're not using <see cref="Reactive"/> properties at all you can disable related code section using <b>NSPRITES_REACTIVE_DISABLE</b>
        /// <br><see cref="Reactive"/> then will be registered as same as <see cref="EachUpdate"/> (if enabled) or as <see cref="Static"/></br></para>
        /// </summary>
        Reactive,
        /// <summary> EachUpdate property will be updated each frame. Such properties layoted and allocated per-entity,
        /// so you can just access it directly through instanceID in shader
        /// <para>Note: if you're not using <see cref="EachUpdate"/> properties at all you can disable related code section using <b>NSPRITES_EACH_UPDATE_DISABLE</b>
        /// <br><see cref="EachUpdate"/> then will be registered as same as <see cref="Reactive"/> (if enabled) or as <see cref="Static"/></br></para>
        /// </summary>
        EachUpdate,
        /// <summary> Static property is the same as <see cref="Reactive"/> but not updated on data changes only on entity created / destroyed (chunk reordered).
        /// Use this mode if you initialize data once on entity creation <b>(ONLY BEFORE <see cref="SpriteRenderingSystem"/> UPDATE)</b> and never change it.
        /// <para>Note: if you're not using <see cref="Static"/> properties at all you can disable related code section using <b>NSPRITES_STATIC_DISABLE</b>
        /// <br><see cref="Static"/> then will be registered as same as <see cref="Reactive"/> (if enabled) or as <see cref="EachUpdate"/></br></para>
        /// </summary>
        Static
    }
}
