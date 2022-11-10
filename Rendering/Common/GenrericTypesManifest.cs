using NSprites;
using Unity.Jobs;
using Unity.Mathematics;

#if !NSPRITES_REACTIVE_PROPERTIES_DISABLE || !NSPRITES_STATIC_PROPERTIES_DISABLE
#region SyncPropertyByChunkJob
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChunkJob<int>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChunkJob<int2>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChunkJob<int3>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChunkJob<int4>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChunkJob<int2x2>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChunkJob<int3x3>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChunkJob<int4x4>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChunkJob<float>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChunkJob<float2>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChunkJob<float3>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChunkJob<float4>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChunkJob<float2x2>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChunkJob<float3x3>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChunkJob<float4x4>))]
#endregion
#region SyncPropertyByChangedChunkJob
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChangedChunkJob<int>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChangedChunkJob<int2>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChangedChunkJob<int3>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChangedChunkJob<int4>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChangedChunkJob<int2x2>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChangedChunkJob<int3x3>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChangedChunkJob<int4x4>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChangedChunkJob<float>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChangedChunkJob<float2>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChangedChunkJob<float3>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChangedChunkJob<float4>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChangedChunkJob<float2x2>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChangedChunkJob<float3x3>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByChangedChunkJob<float4x4>))]
#endregion
#endif
#if !NSPRITES_STATIC_PROPERTIES_DISABLE
#region SyncPropertyByListedChunkJob
[assembly: RegisterGenericJobType(typeof(SyncPropertyByListedChunkJob<int>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByListedChunkJob<int2>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByListedChunkJob<int3>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByListedChunkJob<int4>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByListedChunkJob<int2x2>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByListedChunkJob<int3x3>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByListedChunkJob<int4x4>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByListedChunkJob<float>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByListedChunkJob<float2>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByListedChunkJob<float3>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByListedChunkJob<float4>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByListedChunkJob<float2x2>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByListedChunkJob<float3x3>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByListedChunkJob<float4x4>))]
#endregion
#endif
#region SyncPropertyByQueryJob
[assembly: RegisterGenericJobType(typeof(SyncPropertyByQueryJob<int>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByQueryJob<int2>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByQueryJob<int3>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByQueryJob<int4>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByQueryJob<int2x2>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByQueryJob<int3x3>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByQueryJob<int4x4>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByQueryJob<float>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByQueryJob<float2>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByQueryJob<float3>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByQueryJob<float4>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByQueryJob<float2x2>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByQueryJob<float3x3>))]
[assembly: RegisterGenericJobType(typeof(SyncPropertyByQueryJob<float4x4>))]
#endregion