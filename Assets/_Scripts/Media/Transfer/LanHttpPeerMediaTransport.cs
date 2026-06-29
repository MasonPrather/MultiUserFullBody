/*
 * Script Name: LanHttpPeerMediaTransport.cs
 * Description: Scaffold for same-LAN Quest-to-Quest HTTP media downloads.
 * Project Role: Preferred future path for lab/shared-Wi-Fi sessions because Netcode/Relay should carry metadata, not bulk bytes.
 */

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class LanHttpPeerMediaTransport : MonoBehaviour, IMediaShareTransport
{
    [SerializeField] private bool enableLanHttpTransport = false;

    public bool CanUseForCurrentSession()
    {
        // Future implementation should advertise a short-lived signed token and local HTTP URL through NGO metadata,
        // then receivers download bytes directly from the sharing Quest and verify sha256 before displaying.
        // It is disabled by default until peer reachability, tokens, and timeout UX are fully implemented.
        return enableLanHttpTransport;
    }

    public Task SendMediaAsync(SharedMediaEntry entry, string localPath, IReadOnlyList<ulong> targets)
    {
        throw new NotImplementedException("LAN HTTP peer media transfer is scaffolded but not enabled. Use NgoHostChunkMediaTransport for the current prototype fallback.");
    }
}
