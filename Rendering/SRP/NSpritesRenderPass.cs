using Rendering.Common;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Rendering.SRP
{
    public class NSpritesRenderPass : ScriptableRenderPass
    {
        
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            int layerMask = renderingData.cameraData.camera.cullingMask;
            var commandBuffersToDraw = CommandBufferUtilities.GetCommandBuffersToDraw(layerMask);
            foreach (var cmd in commandBuffersToDraw)
            {
                context.ExecuteCommandBuffer(cmd);
            }
            CommandBufferUtilities.ReleaseCommandBuffersToPool(commandBuffersToDraw);

        }
    }
}