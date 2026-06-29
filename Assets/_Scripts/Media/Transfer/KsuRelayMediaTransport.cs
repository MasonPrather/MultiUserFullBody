/*
 * Script Name: KsuRelayMediaTransport.cs
 * Description: Placeholder for a future KSU-owned in-memory session media relay.
 * Project Role: Remote-friendly architecture hook when users are not on the same LAN.
 */

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class KsuRelayMediaTransport : MonoBehaviour, IMediaShareTransport
{
    public bool CanUseForCurrentSession()
    {
        // Do not call external services from this prototype. A production relay must be approved, privacy reviewed,
        // short-lived, session-scoped, and avoid durable cloud storage of bereavement support photos.
        return false;
    }

    public Task SendMediaAsync(SharedMediaEntry entry, string localPath, IReadOnlyList<ulong> targets)
    {
        throw new NotImplementedException("KSU relay media transport is a future placeholder and intentionally makes no external service calls.");
    }
}
