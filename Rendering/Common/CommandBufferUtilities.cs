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

        public static List<CommandBuffer> GetCommandBuffersToDraw(int cullingMask)
        {
            //For now, ignore the culling mask need to design a better management
            var list = new List<CommandBuffer>(DrawQueue);
            DrawQueue.Clear();
            return list;
        }

        public static void ReleaseCommandBuffersToPool(IList<CommandBuffer> cmdBuffers)
        {
            foreach (var cmd in cmdBuffers)
            {
                AvailablePool.Push(cmd);
            }
        }
    }
}
