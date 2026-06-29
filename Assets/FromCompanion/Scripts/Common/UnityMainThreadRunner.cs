/*
 * Script Name: UnityMainThreadRunner.cs
 * Author: Mason Prather
 * Description: Runs the UnityMainThreadDispatcher queue from a MonoBehaviour Update loop.
 * Project Role: Scene component that activates thread-to-main-thread callback processing for companion networking flows.
 * Key Inputs: Queued dispatcher actions.
 * Key Outputs: Main-thread execution of queued actions.
 */

using UnityEngine;

/// <summary>
/// Scene component that drains the dispatcher from Unity's main thread.
/// </summary>
public class UnityMainThreadRunner : MonoBehaviour
{
    private void Update()
    {
        UnityMainThreadDispatcher.Update();
    }
}
