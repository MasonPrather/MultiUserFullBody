/*
 * Script Name: NgoHostChunkMediaTransport.cs
 * Description: Adapter marker for the implemented NGO host-authoritative chunk fallback.
 * Project Role: Documents the available fallback transport while keeping the extensible transport choice explicit.
 */

using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class NgoHostChunkMediaTransport : MonoBehaviour, IMediaShareTransport
{
    [SerializeField] private M_MediaTransferManager transferManager;

    public bool CanUseForCurrentSession()
    {
        return transferManager != null;
    }

    public Task SendMediaAsync(SharedMediaEntry entry, string localPath, IReadOnlyList<ulong> targets)
    {
        // The active implementation lives in M_MediaTransferManager because it must coordinate thumbnail-first ordering,
        // host cache, late-join requests, and catalog state. This adapter exists so future transport selection can choose
        // between NGO chunks, LAN HTTP, or an approved remote relay without changing gallery UI code.
        Debug.LogWarning("[NgoHostChunkMediaTransport] Use M_LobbyMediaShareController/M_MediaTransferManager so thumbnails and full images stay ordered.");
        return Task.CompletedTask;
    }
}
