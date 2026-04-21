using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace Reify.Editor.Bridge
{
    /// <summary>
    /// Marshals delegates onto Unity's main thread.
    ///
    /// HttpListener callbacks run on threadpool threads; every Unity API we
    /// touch (EditorSceneManager, Application, UnityEngine.Time, etc.) must
    /// run on the main thread or it will throw. We enqueue work here and
    /// drain it from <see cref="EditorApplication.update"/>.
    /// </summary>
    [InitializeOnLoad]
    internal static class MainThreadDispatcher
    {
        private static readonly ConcurrentQueue<Action> Queue = new();

        static MainThreadDispatcher()
        {
            EditorApplication.update += Drain;
        }

        public static Task<T> RunAsync<T>(Func<T> work)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Queue.Enqueue(() =>
            {
                try { tcs.SetResult(work()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }

        private static void Drain()
        {
            while (Queue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { UnityEngine.Debug.LogException(ex); }
            }
        }
    }
}
