/*
 * Script Name: M_PairingCodeProvider.cs
 * Author: Mason Prather
 * Description: Generates and stores the short pairing code used by phone mirroring and local signaling.
 * Project Role: Pairing credential source for TCP signaling between the Quest host and companion phone.
 * Key Inputs: Serialized code length and optional fixed code value.
 * Key Outputs: Runtime pairing code exposed to signaling and UI presenters.
 */

using System.Security.Cryptography;
using UnityEngine;

public class M_PairingCodeProvider : MonoBehaviour
{
    [SerializeField] private string pairingCode;
    public string PairingCode => pairingCode;

    public event System.Action<string> OnCodeChanged;

    private void Awake()
    {
        Regenerate();
    }

    [ContextMenu("Regenerate Pairing Code")]
    public void Regenerate()
    {
        pairingCode = GenerateCode();
        OnCodeChanged?.Invoke(pairingCode);
    }

    private static string GenerateCode()
    {
        int value = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return value.ToString("D6");
    }
}
