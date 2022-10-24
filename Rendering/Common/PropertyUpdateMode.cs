namespace NSprites
{
    /// <summary> Tells how property should be updated: each update / only on new entity added / only change.
    /// <para>The default mode is reactive (if not disabled in project), which is the most common used and most performant in many cases.
    /// <br>With this mode data in <see cref="UnityEngine.ComputeBuffer"></see> updates only on changes/created entity.</br>
    /// <br>Remember that there is at least one <see cref="EachUpdate"></see> property for <see cref="PropertyBufferIndex"></see> data to be able to access write data in shader.</br></para></summary>
    public enum PropertyUpdateMode
    {
        /// <summary> Reactive property will be updated if: new entity created / data changed. Such properties layouted per-chunk.
        /// <para>Note: if you use reactive properties, then you need to load <see cref="PropertyBufferIndex"></see> indexes to shader.
        /// <br>To do so, please, use <b>StructuredBuffer&lt;int&gt; _dataIndexBuffer</b> in your shader, and access to reactive properties data through indexes from this buffer. </br></para>
        /// <para>Note: if reactive properties disabled through <b>NSPRITES_REACTIVE_PROPERTIES_DISABLE</b> then <see cref="Reactive"></see> will be registered as same as <see cref="EachUpdate"></see></para></summary>
        Reactive,
        /// <summary> EachUpdate property will be updated each frame. Such properties layoted per-entity, so you can just access it through instanceID in shader </summary>
        EachUpdate,
        /// <summary> Static property will be updated only on new entity created.
        /// Static properties can't handle destroyed/moved entities, so any changes but create will invalidate this data.
        /// Such properties layoted per-entity, so you can just access it through instanceID in shader </summary>
        Static
    }
}
