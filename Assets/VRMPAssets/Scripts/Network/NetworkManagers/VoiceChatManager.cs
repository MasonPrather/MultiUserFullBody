/*
 * Script Name: VoiceChatManager.cs
 * Author: Mason Prather
 * Description: Manages Vivox channel login, player voice state, participant energy, and positional audio updates for shared VR sessions.
 * Project Role: Voice communication layer for multiplayer embodiment and participant awareness.
 * Key Inputs: Vivox service state, microphone permission state, XRINetworkPlayer instances, and positional audio settings.
 * Key Outputs: Voice connection state, per-player voice energy, mute/squelch state, and participant dictionaries.
 */

using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using Unity.Services.Vivox;
using Unity.XR.CoreUtils.Bindings.Variables;
using UnityEngine;
using UnityEngine.Android;
using System.Collections;
using System.Threading.Tasks;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace XRMultiplayer
{
    /// <summary>
    /// Manages Vivox voice chat for the shared VR multiplayer scenes.
    /// </summary>
    public class VoiceChatManager : MonoBehaviour
    {
        /// <summary>
        /// String used to notify the player that they need to enable microphone permissions.
        /// </summary>
        const string k_MicrophonePersmissionDialogue = "Microphone Permissions Required.";

        /// <summary>
        /// Shared microphone permission state used by voice UI.
        /// </summary>
        public static BindableVariable<bool> s_HasMicrophonePermission = new(false);
        /// <summary>
        /// Dictionary of all the <see cref="XRINetworkPlayer"/>'s in the voice chat.
        /// </summary>
        public static Dictionary<string, XRINetworkPlayer> m_PlayersDictionary = new();

        /// <summary>
        /// Bindable state for local self-mute changes.
        /// </summary>
        public IReadOnlyBindableVariable<bool> selfMuted
        {
            get => m_SelfMuted;
        }
        readonly BindableVariable<bool> m_SelfMuted = new(false);

        /// <summary>
        /// Bindable state for Vivox connection status messages.
        /// </summary>
        public IReadOnlyBindableVariable<string> connectionStatus
        {
            get => m_ConnectionStatus;
        }
        readonly BindableVariable<string> m_ConnectionStatus = new();

        /// <summary>
        /// Chat capability used when joining the Vivox channel.
        /// </summary>
        [SerializeField, Tooltip("Chat capability used when joining the Vivox channel.")] ChatCapability m_ChatCapability = ChatCapability.AudioOnly;

        /// <summary>
        /// Update frequency for audio callbacks.
        /// </summary>
        [SerializeField, Tooltip("Update frequency for audio callbacks")] ParticipantPropertyUpdateFrequency m_UpdateFrequency = ParticipantPropertyUpdateFrequency.TenPerSecond;

        /// <summary>
        /// The maximum distance from the listener that a speaker can be heard.
        /// </summary>
        public int AudibleDistance
        {
            get => m_AudibleDistance;
            set => m_AudibleDistance = value;
        }
        [Header("Voice Chat Properties")]
        [SerializeField, Tooltip("The maximum distance from the listener that a speaker can be heard.")]
        int m_AudibleDistance = 32;

        /// <summary>
        /// The distance from the listener within which a speaker’s voice is heard at its original volume, and beyond which the speaker's voice begins to fade.
        /// </summary>
        public int ConversationalDistance
        {
            get => m_ConversationalDistance;
            set => m_ConversationalDistance = value;
        }
        [SerializeField, Tooltip("The distance from the listener within which a speaker’s voice is heard at its original volume, and beyond which the speaker's voice begins to fade.")]
        int m_ConversationalDistance = 7;

        /// <summary>
        /// The strength of the audio fade effect as the speaker moves away from the listener past the conversational distance.
        /// </summary>
        public float AudioFadeIntensity
        {
            get => m_AudioFadeIntensity;
            set => m_AudioFadeIntensity = value;
        }
        [SerializeField, Tooltip("The strength of the audio fade effect as the speaker moves away from the listener past the conversational distance.")]
        float m_AudioFadeIntensity = 1.0f;

        /// <summary>
        /// The model that determines the distance falloff of the voice chat.
        /// </summary>
        /// The strength of the audio fade effect as the speaker moves away from the listener past the conversational distance.
        /// </summary>
        public AudioFadeModel AudioFadeModel
        {
            get => m_AudioFadeModel;
            set => m_AudioFadeModel = value;
        }
        [SerializeField, Tooltip("The model that determines the distance falloff of the voice chat.")]
        AudioFadeModel m_AudioFadeModel = AudioFadeModel.LinearByDistance;

        /// <summary>
        /// The minimum and maximum volume for the voice output.
        /// </summary>
        [SerializeField, Tooltip("The minimum and maximum volume for the voice output.")] Vector2 m_MinMaxVoiceOutputVolume = new Vector2(-10.0f, 10.0f);

        /// <summary>
        /// The minimum and maximum volume for the voice input.
        /// </summary>
        [SerializeField, Tooltip("The minimum and maximum volume for the voice input.")] Vector2 m_MinMaxVoiceInputVolume = new Vector2(-10.0f, 10.0f);

        /// <summary>
        /// The local participant in the voice chat.
        /// </summary>
        VivoxParticipant m_LocalParticpant;

        /// <summary>
        /// The current lobby id the player is connected to.
        /// </summary>
        string m_CurrentLobbyId;

        /// <summary>
        /// If the player is connected to a room.
        /// </summary>
        bool m_ConnectedToRoom;

        /// <summary>
        /// If the voice chat service is initialized.
        /// </summary>
        bool m_IsInitialized;
        Task<bool> m_InitializationTask;
        Task m_VoiceOperation = Task.CompletedTask;
        int m_VoiceGeneration;
        const string k_DebugPrepend = "<color=#0CFAFA>[Voice Chat Manager]</color> ";
        ///<inheritdoc/>
        private void Awake()
        {
            m_ConnectedToRoom = false;
        }

        void Start()
        {
            // Voice chat is not available in local only sessions.
            if (XRINetworkGameManager.CurrentSessionType == SessionType.LocalOnly)
                return;

            XRINetworkGameManager.CurrentConnectionState.Subscribe(ConnectionStateUpdated);
            XRINetworkGameManager.Connected.Subscribe(ConnectedToGame);
        }

        ///<inheritdoc/>
        private void OnDestroy()
        {
            ++m_VoiceGeneration;
            if (VivoxService.Instance != null)
            {
                UnbindParticipantEvents();
            }

            // Voice chat is not available in local only sessions.
            if (XRINetworkGameManager.CurrentSessionType == SessionType.LocalOnly)
                return;

            XRINetworkGameManager.CurrentConnectionState.Unsubscribe(ConnectionStateUpdated);
            XRINetworkGameManager.Connected.Unsubscribe(ConnectedToGame);
        }

        /// <summary>
        /// Callback for when the local player connection state is updated.
        /// </summary>
        /// <param name="connected">Wether or not a player is connected.</param>
        void ConnectedToGame(bool connected)
        {
            string room = connected ? XRINetworkGameManager.Instance.sessionManager.currentSession?.Id : null;
            int generation = ++m_VoiceGeneration;
            m_VoiceOperation = UpdateVoiceSession(m_VoiceOperation, room, generation);
        }

        void ConnectionStateUpdated(XRINetworkGameManager.ConnectionState connectionState)
        {
            if (!m_IsInitialized && connectionState == XRINetworkGameManager.ConnectionState.Authenticated)
            {
                Utils.Log($"{k_DebugPrepend}Initializing Voice Chat");
                m_ConnectionStatus.Value = "Initializing Voice Service";
                _ = EnsureInitialized();
                if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
                {
                    StartCoroutine(ShowPermissionsAfterDelay());
                }
                else
                {
                    MicrophonePermissionGranted();
                }
            }
        }

        IEnumerator ShowPermissionsAfterDelay(float delay = 1.0f)
        {
            Utils.Log($"{k_DebugPrepend}Requesting Microphone Permissions");
            PlayerHudNotification.Instance.ShowText("Requesting Microphone Permissions", 3.0f);
            yield return new WaitForSeconds(delay);
            PermissionCallbacks permissionCallbacks = new();
            permissionCallbacks.PermissionDenied += PermissionDeniedCallback;
            permissionCallbacks.PermissionGranted += PermissionGrantedCallback;
            Permission.RequestUserPermission(Permission.Microphone, permissionCallbacks);
        }

        void PermissionGrantedCallback(string permissionName)
        {
            if (permissionName == Permission.Microphone)
            {
                MicrophonePermissionGranted();
            }
        }

        void PermissionDeniedCallback(string permissionName)
        {
            if (permissionName == Permission.Microphone)
            {
                PlayerHudNotification.Instance.ShowText("Microphone Permissions Denied", 3.0f);
            }
        }

        void MicrophonePermissionGranted()
        {
            Utils.Log($"{k_DebugPrepend}Microphone Permissions Granted");
            s_HasMicrophonePermission.Value = true;
            PlayerHudNotification.Instance.ShowText("Microphone Permissions Granted", 3.0f);
        }

        Task<bool> EnsureInitialized()
        {
            if (m_InitializationTask == null || (m_InitializationTask.IsCompleted && !m_IsInitialized))
                m_InitializationTask = InitializeVoice();
            return m_InitializationTask;
        }

        async Task<bool> InitializeVoice()
        {
            try
            {
                await VivoxService.Instance.InitializeAsync();
                m_ConnectionStatus.Value = "Voice Service Initialized";
                BindToParticipantEvents();
                m_IsInitialized = true;
                return true;
            }
            catch (System.Exception e)
            {
#if UNITY_EDITOR
                EditorGUI.hyperLinkClicked += HyperlinkClicked;
                Utils.Log($"{k_DebugPrepend}Vivox Initialization Failed. Please check the Vivox Service Window <a data=\"OpenVivoxSettings\"><b>Project Settings > Services > Vivox</b></a>\n\n{e}", 2);
#else
                Utils.Log($"{k_DebugPrepend}Vivox Initialization Failed.\n\n{e}", 2);
#endif
                m_ConnectionStatus.Value = "Voice initialization failed";
                return false;
            }
        }

#if UNITY_EDITOR
        void HyperlinkClicked(EditorWindow window, HyperLinkClickedEventArgs args)
        {
            if(args.hyperLinkData.ContainsValue("OpenVivoxSettings"))
            {
                SettingsService.OpenProjectSettings("Project/Services/Vivox");
            }
        }
#endif

        // Serialize leave/login/join so a fast Host or leave/rejoin cannot race Vivox startup.
        async Task UpdateVoiceSession(Task previous, string room, int generation)
        {
            try
            {
                await previous;
                if (generation != m_VoiceGeneration) return;
                if (string.IsNullOrEmpty(room) && !m_IsInitialized) return;
                if (!await EnsureInitialized() || generation != m_VoiceGeneration) return;
                var voice = VivoxService.Instance;
                m_ConnectedToRoom = false;
                if (voice.IsLoggedIn)
                {
                    await voice.LeaveAllChannelsAsync();
                    await voice.LogoutAsync();
                }
                m_PlayersDictionary.Clear();
                if (string.IsNullOrEmpty(room) || generation != m_VoiceGeneration) return;
                m_CurrentLobbyId = room;
                m_ConnectionStatus.Value = "Logging In To Voice Service";
                await voice.LoginAsync(new LoginOptions
                {
                    DisplayName = XRINetworkGameManager.AuthenicationId,
                    ParticipantUpdateFrequency = m_UpdateFrequency
                });
                if (generation != m_VoiceGeneration) return;
                m_ConnectionStatus.Value = "Joining Voice Channel";
                var properties = new Channel3DProperties(AudibleDistance, ConversationalDistance, AudioFadeIntensity, AudioFadeModel);
                await voice.JoinPositionalChannelAsync(room, m_ChatCapability, properties);
            }
            catch (System.Exception e)
            {
                if (!this || generation != m_VoiceGeneration) return;
                m_ConnectionStatus.Value = "Voice connection failed. Use Reconnect to retry.";
                Utils.LogWarning($"{k_DebugPrepend}{e.Message}");
            }
        }

        void BindToParticipantEvents()
        {
            VivoxService.Instance.ParticipantAddedToChannel += OnParticipantAdded;
            VivoxService.Instance.ParticipantRemovedFromChannel += OnParticipantRemoved;
        }

        void UnbindParticipantEvents()
        {
            VivoxService.Instance.ParticipantAddedToChannel -= OnParticipantAdded;
            VivoxService.Instance.ParticipantRemovedFromChannel -= OnParticipantRemoved;
        }

        [ContextMenu("Reconnect")]
        public void Reconnect()
        {
            if (XRINetworkGameManager.CurrentSessionType == SessionType.LocalOnly || !XRINetworkGameManager.Connected.Value) return;
            ConnectedToGame(true);
        }

        public void Set3DAudio(Transform localPlayerHeadTransform)
        {
            if (VivoxService.Instance.IsLoggedIn && VivoxService.Instance.ActiveChannels.Count > 0 && VivoxService.Instance.TransmittingChannels.Contains(m_CurrentLobbyId))
            {
                VivoxService.Instance.Set3DPosition(localPlayerHeadTransform.position,
                    localPlayerHeadTransform.position,
                    localPlayerHeadTransform.forward,
                    localPlayerHeadTransform.up,
                    m_CurrentLobbyId);
            }
        }


        public void ToggleSelfMute(bool setManual = false, bool mutedOverrideValue = false)
        {
            if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                m_SelfMuted.Value = setManual ? mutedOverrideValue : !m_SelfMuted.Value;
            }
            else
            {
                m_SelfMuted.Value = false;
            }

            if (XRINetworkGameManager.CurrentSessionType == SessionType.DistributedAuthority && VivoxService.Instance.IsLoggedIn)
            {
                if (m_SelfMuted.Value)
                {
                    VivoxService.Instance.MuteInputDevice();
                }
                else
                {
                    VivoxService.Instance.UnmuteInputDevice();
                }
            }
            else
            {
                OfflinePlayerAvatar.muted = m_SelfMuted.Value;
            }

            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                PlayerHudNotification.Instance.ShowText(k_MicrophonePersmissionDialogue, 3.0f);
            }
        }

        public void SetInputVolume(float volume)
        {
            volume = Mathf.Clamp(volume, m_MinMaxVoiceInputVolume.x, m_MinMaxVoiceInputVolume.y);

            VivoxService.Instance.SetInputDeviceVolume((int)volume);

            // Since the slider goes to .001 percent, add a buffer to mute the mic
            if (volume <= (m_MinMaxVoiceInputVolume.x + .05f))
            {
                ToggleSelfMute(true, true);
            }
            else
            {
                ToggleSelfMute(true, false);
            }
        }

        public void SetOutputVolume(float volume)
        {
            volume = Mathf.Clamp(volume, m_MinMaxVoiceOutputVolume.x, m_MinMaxVoiceOutputVolume.y);
            VivoxService.Instance.SetOutputDeviceVolume((int)volume);
        }

        void OnParticipantAdded(VivoxParticipant participant)
        {
            if (participant.IsSelf)
            {
                m_ConnectedToRoom = true;
                m_LocalParticpant = participant;
                m_SelfMuted.Value = false;
                if (XRINetworkPlayer.LocalPlayer) XRINetworkPlayer.LocalPlayer.SetVoiceId(m_LocalParticpant.PlayerId);
                Utils.Log($"{k_DebugPrepend}Joined Voice Channel: {m_CurrentLobbyId}");
                m_ConnectionStatus.Value = "Joined Voice Channel";
                PlayerHudNotification.Instance.ShowText("Joined Voice Chat", 3.0f);
            }
            else
            {
                Utils.Log($"{k_DebugPrepend}Non-Local Player Joined Voice Channel: {participant.PlayerId}");
                foreach (XRINetworkPlayer player in FindObjectsByType<XRINetworkPlayer>(FindObjectsSortMode.None))
                {
                    if (player.playerVoiceId == participant.PlayerId)
                    {
                        player.SetupVoicePlayer();
                    }
                }
            }
        }

        void OnParticipantRemoved(VivoxParticipant participant)
        {
            RemoveVivoxPlayer(participant.PlayerId);
            if (participant.IsSelf)
            {
                Utils.Log($"{k_DebugPrepend}Left Voice Channel: {m_CurrentLobbyId}");
                m_ConnectionStatus.Value = "Left Voice Channel";
                m_ConnectedToRoom = false;
                m_LocalParticpant = null;
                PlayerHudNotification.Instance.ShowText("Voice Chat Disconnected", 3.0f);
            }
        }

        public VivoxParticipant GetVivoxParticipantById(string participantPlayerId)
        {
            if (string.IsNullOrEmpty(m_CurrentLobbyId) || !VivoxService.Instance.ActiveChannels.TryGetValue(m_CurrentLobbyId, out var participants)) return null;
            foreach (var participant in participants)
            {
                if (participantPlayerId == participant.PlayerId)
                    return participant;
            }
            return null;
        }

        // Gets called as soon as participant ID is synced
        public static void AddNewVivoxPlayer(string participantID, XRINetworkPlayer networkPlayer)
        {
            if (!m_PlayersDictionary.ContainsKey(participantID))
            {
                m_PlayersDictionary.Add(participantID, networkPlayer);
            }
            else
            {
                Utils.Log($"{k_DebugPrepend}Attempting to load multiple players with same id {participantID}", 1);
            }
        }

        public static void RemoveVivoxPlayer(string participantID)
        {
            if (XRINetworkPlayer.LocalPlayer && participantID == XRINetworkPlayer.LocalPlayer.playerVoiceId)
            {
                Utils.Log($"{k_DebugPrepend}Local Player Left Voice Chat.");
                return;
            }
            if (m_PlayersDictionary.ContainsKey(participantID))
            {
                m_PlayersDictionary.Remove(participantID);
            }
        }

        [ContextMenu("Disconnect")]
        public void Disconnect()
        {
            ConnectedToGame(false);
        }

        [ContextMenu("Debug Particpants")]
        void DebugParticipants()
        {
            StringBuilder output = new StringBuilder();
            output.Append($"[Room Type: Positional\n[Room Code: {m_CurrentLobbyId}]");
            foreach (var participant in VivoxService.Instance.ActiveChannels[m_CurrentLobbyId])
            {
                output.Append($"\n[ParticipantID: {participant.PlayerId}]\n[AudioEnergy: {participant.AudioEnergy}]");
            }
            Utils.Log($"{k_DebugPrepend}{output}");
        }
    }
}
