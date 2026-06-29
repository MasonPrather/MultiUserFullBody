/*
 * Script Name: M_VivoxManager.cs
 * Author: Mason Prather
 * Description: Initializes Unity Services, signs into Vivox, joins/leaves positional voice channels, and manages the local Vivox audio tap lifecycle.
 * Project Role: Voice communication coordinator for multiplayer menu and session flows.
 * Key Inputs: Unity Services authentication state, Vivox channel names, optional local audio tap prefab, and menu manager calls.
 * Key Outputs: Vivox login state, channel membership, local audio tap instance, and readiness/channel events.
 */

using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Vivox;
using Unity.Services.Vivox.AudioTaps;
using System.Threading.Tasks;

public class M_VivoxManager : MonoBehaviour
{
    public static M_VivoxManager Instance { get; private set; }

    [Header("Optional")]
    [Tooltip("Local-only prefab with a VivoxAudioTap component (NOT a NetworkObject). Keep it disabled in the prefab.")]
    [SerializeField] private GameObject audioTapPrefab;

    public bool IsConnected => VivoxService.Instance != null && _initialized && _isLoggedIn;
    public bool IsReady => _isLoggedIn;

    public event System.Action VivoxReady;
    public event System.Action<string> ChannelJoined;
    public event System.Action<string> ChannelLeft;

    static Task _initTask;
    static bool _initialized;
    bool _isLoggedIn;

    string _currentChannelName;

    VivoxAudioTap _tap;

    private async void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        await InitializeVivoxAsync();
    }

    private async void OnApplicationQuit()
    {
        await SafeTearDownAsync();
    }

    public async Task InitializeVivoxAsync()
    {
        if (_initialized) return;
        if (_initTask != null) { await _initTask; return; }

        _initTask = InitializeInternalAsync();
        await _initTask;
        _initialized = true;
    }

    static bool AlreadySigningInError(System.Exception ex)
        => ex != null && (ex.Message?.ToLowerInvariant().Contains("already signing in") ?? false);

    async Task InitializeInternalAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            try { await AuthenticationService.Instance.SignInAnonymouslyAsync(); }
            catch (System.Exception ex) { if (!AlreadySigningInError(ex)) throw; }
        }

        await VivoxService.Instance.InitializeAsync();

        if (!VivoxService.Instance.IsLoggedIn)
            await VivoxService.Instance.LoginAsync();

        _isLoggedIn = true;
        Debug.Log("[Vivox] Initialized + Logged in");
        VivoxReady?.Invoke();
    }

    public async Task JoinChannelAsync(string channelName)
    {
        if (!_isLoggedIn)
        {
            Debug.LogWarning("[Vivox] Join requested before login; waiting for initialization.");
            await InitializeVivoxAsync();
        }

        if (!string.IsNullOrEmpty(_currentChannelName))
        {
            if (_currentChannelName == channelName)
            {
                Debug.Log($"[Vivox] Already in channel '{channelName}'.");
                ChannelJoined?.Invoke(channelName);
                EnsureAudioTapActive();
                return;
            }
            await LeaveChannelAsync();
        }

        // Positional channel properties match the room-scale voice behavior used by the shared VR scenes.
        var props = new Channel3DProperties(
            audibleDistance: 50,
            conversationalDistance: 2,
            audioFadeIntensityByDistanceaudio: 1.0f,
            audioFadeModel: AudioFadeModel.InverseByDistance
        );

        await VivoxService.Instance.JoinPositionalChannelAsync(
            channelName,
            ChatCapability.AudioOnly,
            props
        );

        _currentChannelName = channelName;

        EnsureAudioTapActive();

        Debug.Log($"[Vivox] Joined channel: {channelName}");
        ChannelJoined?.Invoke(channelName);
    }

    public async Task LeaveChannelAsync()
    {
        if (string.IsNullOrEmpty(_currentChannelName))
            return;

        var name = _currentChannelName;

        // Destroy the local audio tap before leaving so Vivox does not retain a stale channel binding.
        DestroyTap();

        try
        {
            await VivoxService.Instance.LeaveChannelAsync(name);
        }
        finally
        {
            _currentChannelName = null;
            Debug.Log($"[Vivox] Left channel: {name}");
            ChannelLeft?.Invoke(name);
        }
    }

    public async Task LeaveAllAsync()
    {
        DestroyTap();
        await VivoxService.Instance.LeaveAllChannelsAsync();
        _currentChannelName = null;
    }

    void EnsureAudioTapActive()
    {
        if (audioTapPrefab == null) return;

        if (_tap == null)
        {
            var go = Instantiate(audioTapPrefab);
            go.SetActive(false);
            _tap = go.GetComponent<VivoxAudioTap>();
        }

        if (_tap != null && !_tap.gameObject.activeSelf)
        {
            _tap.gameObject.SetActive(true);
        }
    }

    void DestroyTap()
    {
        if (_tap != null)
        {
            // The Vivox package used here does not expose an unregister call for this tap.
            Destroy(_tap.gameObject);
            _tap = null;
        }
    }

    async Task SafeTearDownAsync()
    {
        if (!_isLoggedIn || VivoxService.Instance == null) return;

        DestroyTap();
        try { await VivoxService.Instance.LeaveAllChannelsAsync(); } catch { }
        try { await VivoxService.Instance.LogoutAsync(); } catch { }
        _isLoggedIn = false;
    }

    public void JoinChannel(string lobbyName) { _ = JoinChannelAsync(lobbyName); }
    public void LeaveChannel() { _ = LeaveChannelAsync(); }
}
