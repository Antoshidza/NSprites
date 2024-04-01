using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rendering.Common
{
    public static class CommandBufferUtilities
    {
        private static Queue<CommandBuffer> _CommandBufferQueue = new Queue<CommandBuffer>();
        private static Stack<CommandBuffer> _AvailablePool = new Stack<CommandBuffer>();

        
        public static CommandBuffer AcquireCommandBuffer()
        {
            CommandBuffer commandbuffer;
            if (_AvailablePool.Count > 0)
            {
                commandbuffer = _AvailablePool.Pop();
            }
            else
            {
                commandbuffer = new CommandBuffer();
            }

            return commandbuffer;
        }

        public static void QueueCommandBufferForDraw(CommandBuffer cmd)
        {
            _CommandBufferQueue.Enqueue(cmd);
#if !NSPRITES_SRP
            DrawImmediate();
#endif
        }

        static void DrawImmediate()
        {
            if (_CommandBufferQueue.Count > 0)
            {
                var cmd = _CommandBufferQueue.Dequeue();
                Graphics.ExecuteCommandBuffer(cmd);
            }
        }
    }
}
