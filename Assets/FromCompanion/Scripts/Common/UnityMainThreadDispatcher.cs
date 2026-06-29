/*
 * Script Name: UnityMainThreadDispatcher.cs
 * Author: Mason Prather
 * Description: Queues background-thread callbacks so they can be executed from Unity main-thread Update processing.
 * Project Role: Thread handoff utility for networking code that must update Unity objects safely.
 * Key Inputs: Actions enqueued by background network threads.
 * Key Outputs: Main-thread action execution when UnityMainThreadRunner calls Update.
 */

using System;
using System.Collections.Concurrent;

public static class UnityMainThreadDispatcher
{
    private static readonly ConcurrentQueue<Action> _actions = new ConcurrentQueue<Action>();

    /// <summary>
    /// Enqueues an action for execution during the next dispatcher update.
    /// </summary>
    public static void Enqueue(Action action)
    {
        if (action == null) return;
        _actions.Enqueue(action);
    }

    /// <summary>
    /// Executes all queued work on Unity's main thread.
    /// </summary>
    public static void Update()
    {
        while (_actions.TryDequeue(out var action))
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception)
            {
                // Dispatcher callbacks must not prevent later queued work from running.
            }
        }
    }
}
