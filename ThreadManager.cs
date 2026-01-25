using System.Threading;
using System.Collections.Concurrent;

namespace RayTracing
{
    public static class ThreadManager
    {
        public static Dictionary<string, Thread> threads = [];
        private static readonly ConcurrentQueue<Action> _mainThreadActions = new();
        
        public static void Add(ThreadStart threadStarter, string name)
        {
            threads.Add(name, new(threadStarter));
        }

        public static void EnqueueMainThread(Action action)
        {
            if (action == null) return;
            _mainThreadActions.Enqueue(action);
        }

        public static void PumpMainThreadActions(int maxActions = 64)
        {
            // Drain up to maxActions to avoid long stalls in one frame
            int count = 0;
            while (count < maxActions && _mainThreadActions.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    // Log or handle; don't kill the render loop.
                    Console.WriteLine(ex);
                }
                count++;
            }
        }
    }
}