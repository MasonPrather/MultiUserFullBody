/*
 * Script Name: M_MediaUploadStatusUI.cs
 * Description: Upload URL, pairing code, and server controls for phone-browser import.
 * Project Role: Makes import failures visible and avoids hidden background server state.
 */

using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class M_MediaUploadStatusUI : MonoBehaviour
{
    [SerializeField] private M_QuestMediaHttpServer httpServer;
    [SerializeField] private M_PairingCodeProvider pairingCodeProvider;
    [SerializeField] private TMP_Text uploadUrlText;
    [SerializeField] private TMP_Text pairingCodeText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button startButton;
    [SerializeField] private Button stopButton;
    [SerializeField] private Button regenerateCodeButton;

    private void Awake()
    {
        ResolveReferences();

        if (startButton != null)
            startButton.onClick.AddListener(() => httpServer?.StartServer());

        if (stopButton != null)
            stopButton.onClick.AddListener(() => httpServer?.StopServer());

        if (regenerateCodeButton != null)
            regenerateCodeButton.onClick.AddListener(() => pairingCodeProvider?.Regenerate());
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (httpServer != null)
        {
            httpServer.StatusChanged += HandleStatus;
            httpServer.UploadUrlChanged += HandleUrl;
        }

        if (pairingCodeProvider != null)
            pairingCodeProvider.OnCodeChanged += HandlePairingCode;

        Refresh();
    }

    private void OnDisable()
    {
        if (httpServer != null)
        {
            httpServer.StatusChanged -= HandleStatus;
            httpServer.UploadUrlChanged -= HandleUrl;
        }

        if (pairingCodeProvider != null)
            pairingCodeProvider.OnCodeChanged -= HandlePairingCode;
    }

    public void Refresh()
    {
        if (httpServer != null)
        {
            HandleUrl(httpServer.UploadUrl);
            HandleStatus(httpServer.LastStatus);
        }

        if (pairingCodeProvider != null)
            HandlePairingCode(pairingCodeProvider.PairingCode);
    }

    private void HandleUrl(string url)
    {
        if (uploadUrlText != null)
            uploadUrlText.text = url ?? string.Empty;
    }

    private void HandlePairingCode(string code)
    {
        if (pairingCodeText != null)
            pairingCodeText.text = code ?? string.Empty;
    }

    private void HandleStatus(string status)
    {
        if (statusText != null)
            statusText.text = status ?? string.Empty;
    }

    private void ResolveReferences()
    {
        if (httpServer == null)
            httpServer = FindFirstObjectByType<M_QuestMediaHttpServer>();

        if (pairingCodeProvider == null)
            pairingCodeProvider = FindFirstObjectByType<M_PairingCodeProvider>();
    }
}
