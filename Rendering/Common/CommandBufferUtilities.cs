using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rendering.Common
{
    public static class CommandBufferUtilities
    {
        private static readonly Queue<CommandBuffer> DrawQueue = new();
        private static readonly Stack<CommandBuffer> AvailablePool = new();
        
        public static CommandBuffer AcquireCommandBuffer() 
            => AvailablePool.Count > 0 ? AvailablePool.Pop() : new CommandBuffer();

        public static void QueueCommandBufferForDraw(CommandBuffer commandBuffer)
        {
            DrawQueue.Enqueue(commandBuffer);
#if !NSPRITES_SRP 
            Debug.Log("Execute immediate");
            Graphics.ExecuteCommandBuffer(DrawQueue.Dequeue());
#endif
        }
    }
}
