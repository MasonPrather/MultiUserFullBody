/*
 * Script Name: SessionManager.cs
 * Author: Mason Prather
 * Description: Session Manager manages authentication, sessions/lobbies, transports, voice chat, and high-level multiplayer connection state.
 * Project Role: Multiplayer services layer for shared VR sessions.
 * Key Inputs: Serialized scene references, Unity lifecycle events, and related subsystem state.
 * Key Outputs: Runtime state updates, scene object changes, UI updates, network messages, or diagnostic logs as appropriate for the component.
 */

using System.Collections.Generic;
using Unity.Services.Lobbies;
using UnityEngine;
using System.Threading.Tasks;
using System;
using Unity.XR.CoreUtils.Bindings.Variables;
using UnityEngine.SceneManagement;
using Unity.Services.Multiplayer;
using System.Collections;
using Unity.Netcode.Transports.UTP;
using Unity.Netcode;

namespace XRMultiplayer
{
    public enum SessionType
    {
        DistributedAuthority,
        LocalOnly,
    }
    /// <summary>
    /// This class manages the relationship between Lobby, Relay, and Unity Transport.
    /// </summary>
    public class SessionManager : MonoBehaviour
    {
        // Constants for Lobby Data.
        public const string k_JoinCodeKeyIdentifier = "j";
        public const string k_RegionKeyIdentifier = "r";
        public const string k_BuildIdKeyIdentifier = "b";
        public const string k_SceneKeyIdentifier = "s";
        public const string k_EditorKeyIdentifier = "e";

        /// <summary>
        /// The type of session to create.
        /// This is used to determine the type of session to create when using the <see cref="CreateSession"/> method.
        /// The default is <see cref="SessionType.DistributedAuthority"/>.
        /// </summary>
        public SessionType sessionType => m_SessionType;

        [SerializeField, Tooltip("The type of session to create.")]
        SessionType m_SessionType = SessionType.DistributedAuthority;

        [Tooltip("This will hide editor created rooms from external builds.\nNOTE: This will not hide editor created rooms from other editors.")]
        public bool hideEditorFromLobby = false;
        [Tooltip("When disabled, Quick Join only joins a compatible existing room.")]
        public bool quickJoinCreatesSession = true;
        int m_OperationGeneration;

        // Action that gets invoked when you fail to connect to a lobby. Primarily used for noting failure messages.
        public Action<string> OnSessionFailed;
        public string lastFailureReason { get; private set; }

        void ReportFailure(string message)
        {
            StopAllCoroutines();
            lastFailureReason = message;
            m_Status.Value = message;
            OnSessionFailed?.Invoke(message);
        }

        public ISession currentSession => m_CurrentSession;
        ISession m_CurrentSession;

        /// <summary>
        /// Subscribe to this bindable string for status updates from this class
        /// </summary>
        public static IReadOnlyBindableVariable<string> status
        {
            get => m_Status;
        }
        readonly static BindableVariable<string> m_Status = new("");

        const string k_DebugPrepend = "<color=#EC0CFA>[Lobby Manager]</color> ";

        UnityTransport m_DATransport;
        UnityTransport m_LocalTransport;

        /// <summary>
        /// See <see cref="MonoBehaviour"/>.
        /// </summary>
        private void Awake()
        {
            if (!Application.isEditor)
            {
                hideEditorFromLobby = false;
            }

            // If no internet connection, force session type to be local only
            if (sessionType == SessionType.DistributedAuthority && Application.internetReachability == NetworkReachability.NotReachable)
            {
                Utils.Log($"{k_DebugPrepend}Distributed Authority request, but no internet connection detected. Falling back to Local Only connection. Check network settings.", 1);
                m_SessionType = SessionType.LocalOnly;
            }
        }

        /// <summary>
        /// See <see cref="MonoBehaviour"/>.
        /// </summary>
        private void Start()
        {
            // Check for the session type and set the transport accordingly.
            if (sessionType == SessionType.LocalOnly)
                SetupLocalTransport();
        }

        /// <summary>
        ///  Sets up the local transport when the session type is set to Local Only.
        ///  It disables the voice chat manager and sets up the Unity Transport with the connection data from the Distributed Authority transport.
        ///  It also destroys the Distributed Authority transport as it is no longer needed.
        /// </summary>
        void SetupLocalTransport()
        {
            // If the session type is Local Only, disable voice chat and set the transport to local.
            if (TryGetComponent(out VoiceChatManager voiceChatManager))
            {
                voiceChatManager.enabled = false;
                XRINetworkGameManager.Instance.positionalVoiceChat = false;
                Utils.LogWarning($"{k_DebugPrepend}VoiceChatManager is not supported in {sessionType} sessions. Disabling it.");
            }

            if (!NetworkManager.Singleton.gameObject.TryGetComponent(out m_DATransport))
            {
                Utils.LogError($"{k_DebugPrepend}No Unity Transport found.");
                return;
            }

            // Create a new UnityTransport, copy the connection data from the DA transport, set the server listen address to allow remote connections.
            m_LocalTransport = NetworkManager.Singleton.gameObject.AddComponent<UnityTransport>();
            m_LocalTransport.ConnectionData = m_DATransport.ConnectionData;
            m_LocalTransport.ConnectionData.ServerListenAddress = "0.0.0.0";
            NetworkManager.Singleton.NetworkConfig.NetworkTransport = m_LocalTransport;

            Destroy(m_DATransport); // Destroy the Distributed Authority transport as it is no longer needed
        }

        /// <summary>
        /// Quick Join Function will try and find any lobbies via QuickJoinLobbyAsync().
        /// If no lobbies are found then a new lobby is created.
        /// </summary>
        /// <returns></returns>
        public async Task<ISession> QuickJoinLobby()
        {
            int generation = m_OperationGeneration;
            lastFailureReason = null;
            m_Status.Value = "Looking for an open room…";
            try
            {
                var results = await MultiplayerService.Instance.QuerySessionsAsync(GetQuickJoinFilterOptions());
                foreach (var session in results.Sessions)
                {
                    if (generation != m_OperationGeneration) return null;
                    if (session.AvailableSlots <= 0 || session.IsLocked || session.HasPassword ||
                        CheckForSessionFilter(session) || CheckForIncompatibilityFilter(session)) continue;
                    var joined = await JoinLobby(session);
                    if (joined != null) return joined;
                }
                if (generation != m_OperationGeneration) return null;
                if (quickJoinCreatesSession) return await CreateSession();
                ReportFailure("No open room found. Ask your colleague to Host, then try Join again.");
            }
            catch (Exception e)
            {
                Utils.LogWarning($"{k_DebugPrepend}Quick Join failed: {e.Message}");
                ReportFailure("Unable to find a room. Check Wi-Fi and try again.");
            }
            return null;
        }

        /// <summary>
        /// Joins a lobby.
        /// </summary>
        /// <param name="sessionInfo">Lobby to join.</param>
        /// <param name="roomCode">Lobby Code to join with.</param>
        /// <returns>Returns the Lobby.</returns>
        public async Task<ISession> JoinLobby(ISessionInfo sessionInfo = null, string roomCode = null)
        {
            int generation = m_OperationGeneration;
            lastFailureReason = null;
            try
            {
                if (sessionInfo != null)     // Check for session info
                {
                    m_Status.Value = "Connecting To Room: " + sessionInfo.Name;
                    StopAllCoroutines();
                    StartCoroutine(PlayConnectionMessage());
                    if (!await AcceptSession(await MultiplayerService.Instance.JoinSessionByIdAsync(sessionInfo.Id), generation)) return null;
                }
                else if (!string.IsNullOrEmpty(roomCode)) // Check for room code
                {
                    m_Status.Value = "Connecting To Room Code: " + roomCode;
                    StopAllCoroutines();
                    StartCoroutine(PlayConnectionMessage());
                    if (!await AcceptSession(await MultiplayerService.Instance.JoinSessionByCodeAsync(roomCode), generation)) return null;
                }
                else
                {
                    return await QuickJoinLobby();
                }

                ConnectedToSession();

                return m_CurrentSession;

            }
            catch (Exception e)
            {
                string failureMessage = "Failed to Join Lobby.";
                Utils.Log($"{k_DebugPrepend}{e.Message}", 1);

                if (e is LobbyServiceException)
                {
                    string message = e.Message.ToLower();

                    if (message.Contains("Rate limit".ToLower()))
                        failureMessage = "Rate limit exceeded. Please try again later.";
                    else if (message.Contains("Lobby not found".ToLower()))
                        failureMessage = "Lobby not found. Please try a new Lobby.";
                    else
                        failureMessage = e.Message;
                }
                Utils.Log($"{k_DebugPrepend}{failureMessage}\n\n{e}", 1);
                ReportFailure($"{failureMessage}");
                return null;
            }
        }

        /// <summary>
        /// This function will try to create a lobby and host a networked session.
        /// </summary>
        /// <returns></returns>
        public async Task<ISession> CreateSession(string roomName = null, bool isPrivate = false, int maxPlayers = XRINetworkGameManager.maxPlayers)
        {
            int generation = m_OperationGeneration;
            lastFailureReason = null;
            string sessionName = roomName;

            if (string.IsNullOrEmpty(sessionName))
                sessionName = $"{XRINetworkGameManager.LocalPlayerName.Value}'s Room";

            try
            {
                m_Status.Value = "Connecting To Room: " + sessionName;
                try
                {
                    var options = GetSessionOptions(sessionName, isPrivate, maxPlayers).WithDistributedAuthorityNetwork();
                    Guid sessionId = Guid.NewGuid();

                    StopAllCoroutines();
                    StartCoroutine(PlayConnectionMessage());
                    if (!await AcceptSession(await MultiplayerService.Instance.CreateOrJoinSessionAsync(sessionId.ToString(), options), generation)) return null;

                    ConnectedToSession();
                }
                catch (Exception e)
                {
                    string failureMessage = $"Failed to connect to {sessionName}. Please try again.";
                    Utils.Log($"{k_DebugPrepend}{failureMessage}\n\n{e}", 1);
                    // Debug.LogWarning($"[XRMPT] {failureMessage}\n\n{e}");
                    ReportFailure(failureMessage);
                    return null;
                }

                return m_CurrentSession;
            }
            catch (Exception e)
            {
                string failureMessage = "Failed to Create Lobby. Please try again.";
                Utils.Log($"{k_DebugPrepend}{failureMessage}\n\n{e}", 1);
                // Debug.LogWarning($"[XRMPT] {failureMessage}\n\n{e}");
                ReportFailure(failureMessage);
                return null;
            }
        }

        SessionOptions GetSessionOptions(string lobbyName, bool isPrivate = false, int maxPlayers = XRINetworkGameManager.maxPlayers)
        {
            SessionOptions options = new SessionOptions()
            {
                Name = lobbyName,
                MaxPlayers = maxPlayers,
                SessionProperties = new Dictionary<string, SessionProperty>
                {
                    { k_BuildIdKeyIdentifier, new SessionProperty(Application.version) },
                    { k_SceneKeyIdentifier, new SessionProperty(SceneManager.GetActiveScene().name) },
                    { k_EditorKeyIdentifier, new SessionProperty(hideEditorFromLobby.ToString()) }
                },
                IsPrivate = sessionType == SessionType.LocalOnly ? true : isPrivate,
            };

            return options;
        }

        IEnumerator PlayConnectionMessage()
        {
            m_Status.Value = "Connecting to session…";
            yield return new WaitForSecondsRealtime(5f);
            m_Status.Value = "Waiting for network and player avatar…";
        }

        async Task<bool> AcceptSession(ISession session, int generation)
        {
            if (generation != m_OperationGeneration)
            {
                await session.LeaveAsync();
                return false;
            }
            m_CurrentSession = session;
            float deadline = Time.realtimeSinceStartup + 25f;
            while (generation == m_OperationGeneration && Time.realtimeSinceStartup < deadline)
            {
                var manager = NetworkManager.Singleton;
                if (manager && manager.IsConnectedClient && manager.LocalClient?.PlayerObject != null)
                {
                    StopAllCoroutines();
                    m_Status.Value = "Connected";
                    return true;
                }
                await Task.Delay(50);
            }
            if (generation == m_OperationGeneration)
            {
                m_CurrentSession = null;
                await session.LeaveAsync();
                StopAllCoroutines();
                ReportFailure("Network connection timed out before the player spawned. Please retry.");
            }
            return false;
        }

        /// <summary>
        /// This function will be called when the player connects to a session.
        /// </summary>
        void ConnectedToSession()
        {
            if (m_CurrentSession != null)
            {
                XRINetworkGameManager.ConnectedRoomCode = m_CurrentSession.Code;
                m_CurrentSession.SessionPropertiesChanged += OnSessionPropertiesChanged;
            }
        }

        private void OnSessionPropertiesChanged()
        {
            XRINetworkGameManager.ConnectedRoomCode = m_CurrentSession.Code;
            XRINetworkGameManager.ConnectedRoomName.Value = m_CurrentSession.Name;
        }

        public async Task LeaveSession()
        {
            ++m_OperationGeneration; // Reject and leave any create/join that completes after cancellation.
            StopAllCoroutines();
            var session = m_CurrentSession;
            m_CurrentSession = null;
            if (session == null) return;
            session.SessionPropertiesChanged -= OnSessionPropertiesChanged;
            try { await session.LeaveAsync(); }
            catch (Exception e) { Utils.LogWarning($"{k_DebugPrepend}Leave failed: {e.Message}"); }
        }

        public static QuerySessionsOptions GetQuickJoinFilterOptions()
        {
            return new QuerySessionsOptions
            {
                FilterOptions = new List<FilterOption>
                {
                    new(FilterField.AvailableSlots, "0", FilterOperation.Greater),
                    new(FilterField.IsLocked, "false", FilterOperation.Equal),
                    new(FilterField.HasPassword, "false", FilterOperation.Equal)
                }
            };
        }

        /// <summary>
        /// Changes the existing lobbies name.
        /// </summary>
        /// <param name="lobbyName">Name to change the lobby to.</param>
        public async void UpdateLobbyName(string lobbyName)
        {
            if (m_CurrentSession != null && m_CurrentSession.IsHost)
            {
                try
                {
                    IHostSession hostSession = m_CurrentSession as IHostSession;

                    hostSession.Name = lobbyName;
                    await hostSession.SavePropertiesAsync();

                    XRINetworkGameManager.ConnectedRoomName.Value = lobbyName;
                }
                catch (LobbyServiceException e)
                {
                    Utils.Log($"{k_DebugPrepend}{e}");
                }
            }
            else
            {
                Utils.Log($"{k_DebugPrepend}Connected Lobby is null");
            }
        }

        /// <summary>
        /// Updates the privacy setting for the current room.
        /// </summary>
        /// <param name="privateRoom">Whether or not to make the room private.</param>
        public async void UpdateRoomPrivacy(bool privateRoom)
        {
            if (m_CurrentSession != null)
            {
                try
                {
                    IHostSession hostSession = m_CurrentSession as IHostSession;
                    hostSession.IsPrivate = privateRoom;
                    await hostSession.SavePropertiesAsync();
                }
                catch (LobbyServiceException e)
                {
                    Utils.Log($"{k_DebugPrepend}{e}");
                }
            }
            else
            {
                Utils.Log($"{k_DebugPrepend}Connected Lobby is null");
            }
        }

        public static bool CheckForSessionFilter(ISessionInfo sessionInfo)
        {
            return sessionInfo.Properties == null ||
                !sessionInfo.Properties.TryGetValue(k_SceneKeyIdentifier, out var scene) ||
                scene.Value != SceneManager.GetActiveScene().name;
        }

        public static bool CheckForIncompatibilityFilter(ISessionInfo sessionInfo)
        {
            return sessionInfo.Properties == null ||
                !sessionInfo.Properties.TryGetValue(k_BuildIdKeyIdentifier, out var build) ||
                build.Value != Application.version;
        }

        public static bool CanJoinLobby(ISessionInfo session)
        {
            return (XRINetworkGameManager.Instance.sessionManager.currentSession == null) ||
            (XRINetworkGameManager.Instance.sessionManager.currentSession != null && session.Id != XRINetworkGameManager.Instance.sessionManager.currentSession.Id);
        }
    }
}
