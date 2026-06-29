/*
 * Script Name: FaceSyncData.cs
 * Author: Mason Prather
 * Description: Serializes facial-expression blendshape weights and eye-forward vectors for experimental network face synchronization paths.
 * Project Role: Legacy data container retained for face-sync experiments alongside the current M_NetFaceMirror implementation.
 * Key Inputs: Blendshape weight arrays and eye-forward vectors.
 * Key Outputs: Netcode serialization payloads for readers and writers.
 */

using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Struct for synchronizing facial expressions and eye direction over the network.
/// </summary>
public struct FaceSyncData : INetworkSerializable
{
    public float[] blendshapeWeights;
    public Vector3 eyeForward;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        int count = blendshapeWeights?.Length ?? 0;
        serializer.SerializeValue(ref count);

        if (serializer.IsReader)
            blendshapeWeights = new float[count];

        for (int i = 0; i < count; i++)
            serializer.SerializeValue(ref blendshapeWeights[i]);

        serializer.SerializeValue(ref eyeForward);
    }
}