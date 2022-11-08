namespace NSprites
{
    /// <summary>
    /// Used to specify what data for shader's property component contains actually.
    /// <br>There is few possible types in HLSL: single/square-matrices int/float</br>.
    /// <br>If actual component's content not corresponds to choosed format then erros will appear during loading data process</br>
    /// </summary>
    public enum PropertyFormat
    {
        Float,
        Float2,
        Float3,
        Float4,
        Float2x2,
        Float3x3,
        Float4x4,
        Int,
        Int2,
        Int3,
        Int4,
        Int2x2,
        Int3x3,
        Int4x4,
    }
}
