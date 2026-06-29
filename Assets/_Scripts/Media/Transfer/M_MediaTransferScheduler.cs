/*
 * Script Name: M_MediaTransferScheduler.cs
 * Description: Coroutine scheduler that throttles custom-message media chunk sends.
 * Project Role: Prevents photo transfer from monopolizing frames needed by VR interaction, voice, and gameplay.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class M_MediaTransferScheduler : MonoBehaviour
{
    [Header("Chunking")]
    [Tooltip("Small payloads reduce frame spikes and avoid treating Relay/NGO as a bulk file pipe.")]
    [SerializeField] private int chunkPayloadBytes = 4 * 1024;

    [Tooltip("Upper bound of chunk messages emitted in a single Unity frame by one transfer coroutine.")]
    [SerializeField] private int chunkWindowPerFrame = 16;

    [Header("Queues")]
    [SerializeField] private int maxActiveSendsPerSender = 1;
    [SerializeField] private int maxQueuedShares = 10;
    [SerializeField] private int maxSessionHistory = 50;

    private readonly Queue<IEnumerator> _sendQueue = new Queue<IEnumerator>();
    private int _activeSends;

    public int ChunkPayloadBytes => Mathf.Clamp(chunkPayloadBytes, 512, 16 * 1024);
    public int ChunkWindowPerFrame => Mathf.Clamp(chunkWindowPerFrame, 1, 64);
    public int MaxActiveSendsPerSender => Mathf.Clamp(maxActiveSendsPerSender, 1, 4);
    public int MaxQueuedShares => Mathf.Clamp(maxQueuedShares, 1, 100);
    public int MaxSessionHistory => Mathf.Clamp(maxSessionHistory, 1, 200);

    public event Action<string> StatusChanged;

    public bool EnqueueSend(IEnumerator sendRoutine)
    {
        if (sendRoutine == null)
            return false;

        if (_sendQueue.Count >= MaxQueuedShares)
        {
            StatusChanged?.Invoke("Too many photo shares are queued. Please wait for the current transfer to finish.");
            return false;
        }

        _sendQueue.Enqueue(sendRoutine);
        PumpQueue();
        return true;
    }

    private void PumpQueue()
    {
        while (_activeSends < MaxActiveSendsPerSender && _sendQueue.Count > 0)
            StartCoroutine(RunSend(_sendQueue.Dequeue()));
    }

    private IEnumerator RunSend(IEnumerator sendRoutine)
    {
        _activeSends++;
        yield return StartCoroutine(sendRoutine);
        _activeSends--;
        PumpQueue();
    }
}
