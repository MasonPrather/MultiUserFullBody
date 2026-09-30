/*
 * Script Name: XRINetworkPlayer.cs
 * Author: Mason Prather
 * Description: XRINetwork Player represents synchronized avatar, hand pose, voice, shared media, and player metadata behavior for networked participants.
 * Project Role: Networked player layer for multiplayer embodiment and media sharing.
 * Key Inputs: Serialized scene references, Unity lifecycle events, and related subsystem state.
 * Key Outputs: Runtime state updates, scene object changes, UI updates, network messages, or diagnostic logs as appropriate for the component.
 */

using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.XR.CoreUtils;
using Unity.Collections;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Services.Vivox;
using Unity.XR.CoreUtils.Bindings.Variables;
using UnityEngine.XR.Templates.VRMultiplayer;

namespace XRMultiplayer
{
    /// <summary>
    /// XRINetworkPlayer class used for simple interactions.
    /// </summary>
    public class XRINetworkPlayer : NetworkBehaviour
    {
        /// <summary>
        /// Speed at which voice amplitude changes.
        /// </summary>
        const float k_VoiceAmplitudeSpeed = 15.0f;

        /// <summary>
        /// Singleton Reference for the Local Player.
        /// </summary>
        public static XRINetworkPlayer LocalPlayer;

        [Header("Avatar Transform References"), Tooltip("Assign to local avatar transform.")]
        /// <summary>
        /// Non-Local player transforms.
        /// </summary>
        public Transform head;

        /// <summary>
        /// Non-Local player transforms.
        /// </summary>
        public Transform leftHand;

        /// <summary>
        /// Non-Local player transforms.
        /// </summary>
        public Transform rightHand;

        /// <summary>
        /// Action called when the player name is updated.
        /// </summary>
        public Action<string> onNameUpdated;

        /// <summary>
        /// Action called when the player color is updated.
        /// </summary>
        public Action<Color> onColorUpdated;

        /// <summary>
        /// Action called when the Local Player is finished spawning in.
        /// </summary>
        public Action onSpawnedLocal;

        /// <summary>
        /// Action called when the Local Player is finished spawning in.
        /// </summary>
        public Action onSpawnedAll;

        /// <summary>
        /// Action called when the player color is updated.
        /// </summary>
        public Action<XRINetworkPlayer> onDisconnected;

        /// <summary>
        /// Raised when a shared media upload has been received and reconstructed on this client.
        /// </summary>
        public static event Action<string, byte[]> onSharedMediaReceived;

        /// <summary>
        /// Raised when a shared media upload has been received with type metadata.
        /// </summary>
        public static event Action<string, string, string, byte[]> onSharedMediaPayloadReceived;

        /// <summary>
        /// Raised after the local network player has spawned and finished local setup.
        /// </summary>
        public static event Action onLocalPlayerSpawned;

        /// <summary>
        /// Bindable Variable used for other clients to mute this user locally.
        /// </summary>
        public BindableVariable<bool> squelched = new BindableVariable<bool>(false);

        /// <summary>
        /// Current Voice Amplitude driven from Vivox.
        /// </summary>
        public float playerVoiceAmp { get => m_VoiceAmplitudeCurrent; }
        float m_VoiceAmplitudeCurrent;

        /// <summary>
        /// Player Voice Id string that reads from the internal NetworkVariable for the Player Voice Id.
        /// </summary>
        public string playerVoiceId { get => m_PlayerVoiceId.Value.ToString(); }
        readonly NetworkVariable<FixedString128Bytes> m_PlayerVoiceId = new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>
        /// Player Name string that reads from the internal NetworkVariable for the Player Name.
        /// </summary>
        public string playerName { get => m_PlayerName.Value.ToString(); }
        readonly NetworkVariable<FixedString128Bytes> m_PlayerName = new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>
        /// Player Color that reads from the internal NetworkVariable for the Player Color.
        /// </summary>
        public Color playerColor { get => m_PlayerColor.Value; }
        readonly NetworkVariable<Color> m_PlayerColor = new(Color.white, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public NetworkVariable<int> platformType => m_PlatformType;
        readonly NetworkVariable<int> m_PlatformType = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        [HideInInspector] public readonly NetworkVariable<bool> selfMuted = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);


        /// <summary>
        /// Player Name Tag.
        /// </summary>
        [Header("Player Name Tag"), SerializeField, Tooltip("Player Name Tag.")] protected bool m_UpdateObjectName = true;


        // /// <summary>
        // /// Head Renderers to change rendering mode for local players.
        // /// </summary>
        // [SerializeField, Tooltip("Head Renderers to change rendering mode for local players.")] protected Renderer[] m_HeadRends;

        /// <summary>
        /// Hand Objects to be disabled for the local player.
        /// </summary>
        [Header("Networked Hands"), SerializeField, Tooltip("Hand Objects to be disabled for the local player.")] protected GameObject[] m_handsObjects;

        /// <summary>
        /// Player Name Tag.
        /// </summary>
        [Header("Player Name Tag"), SerializeField, Tooltip("Player Name Tag.")] protected PlayerNameTag m_PlayerNameTag;

        /// <summary>
        /// Internal references to the Local Player Transforms.
        /// </summary>
        protected Transform m_LeftHandOrigin, m_RightHandOrigin, m_HeadOrigin;

        /// <summary>
        /// Reference to the local player XR Origin
        /// </summary>
        protected XROrigin m_XROrigin;

        /// <summary>
        /// If the player has been connected to the the game.
        /// </summary>
        protected bool m_InitialConnected = false;

        /// <summary>
        /// Reference to the VoiceChatManager.
        /// </summary>
        protected VoiceChatManager m_VoiceChat;

        /// <summary>
        /// Reference to the VivoxParticipant.
        /// </summary>
        protected VivoxParticipant m_VivoxParticipant;

        /// <summary>
        /// Time to update the voice position.
        /// </summary>
        protected float m_VoicePositionUpdateTime = .1f, m_VoiceUpdatePosotionDelta = .05f;

        /// <summary>
        /// Destination for the voice amplitude.
        /// </summary>
        protected float m_VoiceAmplitudeDestination;

        /// <summary>
        /// Timer to check the voice position.
        /// </summary>
        protected float m_VoicePositionCheckTimer;

        /// <summary>
        /// Previous position of the head.
        /// </summary>
        protected Vector3 m_PrevHeadPos;

        // The active UTP scenes currently use a 6144-byte max payload. Keep RPC chunk data
        // comfortably below that because NGO adds message/RPC metadata around the byte array.
        const int k_MaxSharedMediaChunkSize = 4 * 1024;
        const int k_MinSharedMediaChunkSize = 512;
        const int k_SharedMediaRpcOverheadReserve = 1024;
        const string k_SharedMediaKindImage = "image";
        const string k_SharedMediaKindVideo = "video";

        [Header("Shared Media Sync"), SerializeField, Tooltip("Maximum media chunks sent per frame by the uploading client and relay.")]
        int m_SharedMediaChunksPerFrame = 4;

        [SerializeField, Tooltip("If true, log shared-media transfer progress.")]
        bool m_LogSharedMediaSync = true;

        ulong m_LocalSharedMediaUploadId;
        readonly HashSet<ulong> m_LocalSharedMediaEchoSkips = new HashSet<ulong>();
        readonly Dictionary<ulong, PendingSharedMediaUpload> m_PendingServerSharedMediaUploads = new Dictionary<ulong, PendingSharedMediaUpload>();
        readonly Dictionary<ulong, PendingSharedMediaUpload> m_PendingClientSharedMediaDownloads = new Dictionary<ulong, PendingSharedMediaUpload>();
        static ulong s_ServerSharedMediaDownloadId = 1UL << 63;
        static bool s_HasLatestSharedMedia;
        static FixedString128Bytes s_LatestSharedMediaFileName;
        static FixedString32Bytes s_LatestSharedMediaKind;
        static FixedString32Bytes s_LatestSharedMediaMime;
        static byte[] s_LatestSharedMediaBytes;

        class PendingSharedMediaUpload
        {
            public FixedString128Bytes fileName;
            public FixedString32Bytes kind;
            public FixedString32Bytes mime;
            public int totalBytes;
            public int totalChunks;
            public byte[][] chunks;
            public int receivedChunkCount;
            public bool completionRequested;

            public PendingSharedMediaUpload(
                FixedString128Bytes uploadFileName,
                FixedString32Bytes uploadKind,
                FixedString32Bytes uploadMime,
                int uploadTotalBytes,
                int uploadTotalChunks)
            {
                fileName = uploadFileName;
                kind = uploadKind;
                mime = uploadMime;
                totalBytes = Mathf.Max(0, uploadTotalBytes);
                totalChunks = Mathf.Max(1, uploadTotalChunks);
                chunks = new byte[totalChunks][];
            }

            public void StoreChunk(int chunkIndex, byte[] chunkData)
            {
                if (chunkData == null || chunkIndex < 0 || chunkIndex >= totalChunks)
                    return;

                if (chunks[chunkIndex] == null)
                    receivedChunkCount++;

                chunks[chunkIndex] = chunkData;
            }

            public bool IsComplete()
            {
                return receivedChunkCount >= totalChunks;
            }

            public byte[] Combine()
            {
                byte[] combined = new byte[totalBytes];
                int writeOffset = 0;

                for (int i = 0; i < chunks.Length; i++)
                {
                    byte[] chunk = chunks[i];
                    if (chunk == null)
                        continue;

                    int copyLength = Mathf.Min(chunk.Length, combined.Length - writeOffset);
                    if (copyLength <= 0)
                        break;

                    Buffer.BlockCopy(chunk, 0, combined, writeOffset, copyLength);
                    writeOffset += copyLength;
                }

                return combined;
            }
        }

        protected void Awake()
        {
            m_VoiceChat = FindFirstObjectByType<VoiceChatManager>();
            m_VoicePositionCheckTimer = m_VoicePositionUpdateTime;
        }

        ///<inheritdoc/>
        protected virtual void OnEnable()
        {
            m_PlayerName.OnValueChanged += UpdatePlayerName;
            m_PlayerColor.OnValueChanged += UpdatePlayerColor;
        }

        ///<inheritdoc/>
        protected virtual void OnDisable()
        {
            m_PlayerName.OnValueChanged -= UpdatePlayerName;
            m_PlayerColor.OnValueChanged -= UpdatePlayerColor;
        }

        ///<inheritdoc/>
        protected virtual void Update()
        {
            if (IsOwner && XRINetworkGameManager.Instance.positionalVoiceChat)
            {
                if (Time.time > m_VoicePositionCheckTimer)
                {
                    m_VoicePositionCheckTimer += m_VoicePositionUpdateTime;

                    if (Vector3.Distance(m_PrevHeadPos, m_HeadOrigin.position) > m_VoiceUpdatePosotionDelta)
                    {
                        m_PrevHeadPos = m_HeadOrigin.position;
                        if (XRINetworkGameManager.Instance.positionalVoiceChat)
                        {
                            m_VoiceChat.Set3DAudio(m_HeadOrigin);
                        }
                    }
                }
            }

            m_VoiceAmplitudeCurrent = Mathf.Lerp(m_VoiceAmplitudeCurrent, m_VoiceAmplitudeDestination, Time.deltaTime * k_VoiceAmplitudeSpeed);
        }

        ///<inheritdoc/>
        protected virtual void LateUpdate()
        {
            if (!IsOwner) return;

            if (m_HeadOrigin != null)
                head.SetPositionAndRotation(m_HeadOrigin.position, m_HeadOrigin.rotation);

            if (m_LeftHandOrigin != null)
                leftHand.SetPositionAndRotation(m_LeftHandOrigin.position, m_LeftHandOrigin.rotation);

            if (m_RightHandOrigin != null)
                rightHand.SetPositionAndRotation(m_RightHandOrigin.position, m_RightHandOrigin.rotation);
        }

        ///<inheritdoc/>
        public override void OnDestroy()
        {
            base.OnDestroy();

            if (IsOwner)
            {
                // Local Name unsubscribe.
                XRINetworkGameManager.LocalPlayerName.Unsubscribe(UpdateLocalPlayerName);
                XRINetworkGameManager.LocalPlayerColor.Unsubscribe(UpdateLocalPlayerColor);
                m_VoiceChat.selfMuted.Unsubscribe(SelfMutedChanged);
            }
            else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
            {
                // Inform Network Manager that player left current session.
                XRINetworkGameManager.Instance.PlayerLeft(NetworkObject.OwnerClientId);
            }

            // Unsubscribe from color updating.
            m_PlayerColor.OnValueChanged -= UpdatePlayerColor;
        }

        ///<inheritdoc/>
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsLocalPlayer)
            {
                // Set Local Player.
                LocalPlayer = this;
                XRINetworkGameManager.Instance.OnLocalClientStarted(NetworkObject.OwnerClientId);

                // Setup Platform Type
                m_PlatformType.Value = (int)XRPlatformUnderstanding.CurrentPlatform;
                Debug.Log($"XRINetworkPlayer: Platform type set to {m_PlatformType.Value}");

                // Get Origin and set head.
                m_XROrigin = FindFirstObjectByType<XROrigin>();
                if (m_XROrigin != null)
                {
                    m_HeadOrigin = m_XROrigin.Camera.transform;
                }
                else
                {
                    Utils.Log("No XR Rig Available", 1);
                }

                SetupLocalPlayer();
                onLocalPlayerSpawned?.Invoke();
            }
            CompleteSetup();

            if (CanRelayLatestSharedMediaToOwner())
                StartCoroutine(SendLatestSharedMediaToOwnerWhenReady());
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            PlayerHudNotification.Instance.ShowText($"<b>{m_PlayerName.Value}</b> left");
            onDisconnected?.Invoke(this);
        }

        /// <summary>
        /// Called from <see cref="XRHandPoseReplicator"/> when swapping between hand tracking and controllers.
        /// </summary>
        /// <param name="left">Transform for Left Hand.</param>
        /// <param name="right">Transform for Right Hand.</param>
        public void SetHandOrigins(Transform left, Transform right)
        {
            m_LeftHandOrigin = left;
            m_RightHandOrigin = right;
        }

        /// <summary>
        /// Hides and disables Renderers and GameObjects on the Local Player.
        /// Also sets the initial values for <see cref="m_PlayerColor"/> and <see cref="m_PlayerName"/>.
        /// Finally we subscribe to any updates for Color and Name.
        /// </summary>
        /// <remarks>Only called on the Local Player.</remarks>
        protected virtual void SetupLocalPlayer()
        {
            foreach (var hand in m_handsObjects)
            {
                hand.SetActive(false);
            }

            m_PlayerColor.Value = XRINetworkGameManager.LocalPlayerColor.Value;
            m_PlayerName.Value = new FixedString128Bytes(XRINetworkGameManager.LocalPlayerName.Value);
            XRINetworkGameManager.LocalPlayerColor.Subscribe(UpdateLocalPlayerColor);
            XRINetworkGameManager.LocalPlayerName.Subscribe(UpdateLocalPlayerName);
            m_VoiceChat.selfMuted.Subscribe(SelfMutedChanged);
            m_VoiceChat.ToggleSelfMute(true, true);

            onSpawnedLocal?.Invoke();
        }

        /// <summary>
        /// Called from the local player only
        /// </summary>
        /// <param name="muted"></param>
        void SelfMutedChanged(bool muted)
        {
            selfMuted.Value = muted;
        }

        /// <summary>
        /// Callback for the bindable variable <see cref="XRINetworkGameManager.LocalPlayerColor"/>.
        /// </summary>
        /// <param name="color">New Color for player.</param>
        /// <remarks>Only called on Local Player.</remarks>
        protected virtual void UpdateLocalPlayerColor(Color color)
        {
            m_PlayerColor.Value = XRINetworkGameManager.LocalPlayerColor.Value;
        }

        /// <summary>
        /// Callback for the bindable variable <see cref="XRINetworkGameManager.LocalPlayerName"/>.
        /// </summary>
        /// <param name="name">New Name for player.</param>
        /// <remarks>Only called on Local Player.</remarks>
        protected virtual void UpdateLocalPlayerName(string name)
        {
            m_PlayerName.Value = new FixedString128Bytes(XRINetworkGameManager.LocalPlayerName.Value);
        }

        /// <summary>
        /// Called when the player object is finished being setup.
        /// </summary>
        void CompleteSetup()
        {
            // Add player to XRINetworkManager.
            XRINetworkGameManager.Instance.PlayerJoined(NetworkObject.OwnerClientId);

            // Update Color and Name.
            UpdatePlayerColor(Color.white, m_PlayerColor.Value);
            UpdatePlayerName(new FixedString128Bytes(""), m_PlayerName.Value);

            // Check if WorldCanvas exists
            WorldCanvas worldCanvas = FindFirstObjectByType<WorldCanvas>();
            if (worldCanvas != null)
            {
                // If we are using a World Canvas, reparent name tag and destroy local canvas.
                Canvas localCanvas = m_PlayerNameTag.GetComponentInParent<Canvas>();
                worldCanvas.SetupPlayerNameTag(this, m_PlayerNameTag);
                Destroy(localCanvas.gameObject);
            }
            else
            {
                // If we are not using a World Canvas, setup the name tag for local use.
                m_PlayerNameTag.SetupNameTag(this);
            }

            onSpawnedAll?.Invoke();
        }
        /// <summary>
        /// Callback anytime the local player sets <see cref="m_PlayerName"/>.
        /// </summary><remarks>Invokes the callback <see cref="onNameUpdated"/>.</remarks>
        void UpdatePlayerName(FixedString128Bytes oldName, FixedString128Bytes currentName)
        {
            onNameUpdated?.Invoke(currentName.ToString());

            if (!m_InitialConnected & !string.IsNullOrEmpty(currentName.ToString()))
            {
                m_InitialConnected = true;
                if (!IsLocalPlayer)
                    PlayerHudNotification.Instance.ShowText($"<b>{playerName}</b> joined");
            }

            if (m_UpdateObjectName)
                gameObject.name = currentName.ToString();
        }

        /// <summary>
        /// Callback when the local player sets <see cref="m_PlayerColor"/>.
        /// </summary><remarks>Invokes the callback <see cref="onColorUpdated"/>.</remarks>
        void UpdatePlayerColor(Color oldColor, Color newColor)
        {
            onColorUpdated?.Invoke(newColor);
        }

        void UpdatePlayerVoiceEnergy(float current)
        {
            m_VoiceAmplitudeDestination = Mathf.Clamp01(current);
        }

        /// <summary>
        /// Called when new players connect to the game and set their initial <see cref="m_PlayerVoiceId"/>
        /// and when <see cref="VoiceChatManager.OnParticipantAdded(VivoxParticipant)"/> is called for existing players.
        /// </summary>
        public void SetupVoicePlayer()
        {
            m_VivoxParticipant = m_VoiceChat.GetVivoxParticipantById(playerVoiceId);
            if (m_VivoxParticipant != null)
            {
                m_VivoxParticipant.ParticipantAudioEnergyChanged += ParticipantAudioEnergyChanged;
            }
            else
            {
                Utils.Log($"No Participant with id: {playerVoiceId}", 1);
            }

            if (!VoiceChatManager.m_PlayersDictionary.ContainsKey(playerVoiceId))
            {
                VoiceChatManager.AddNewVivoxPlayer(playerVoiceId, this);
            }
        }
        private void ParticipantAudioEnergyChanged()
        {
            UpdatePlayerVoiceEnergy((float)m_VivoxParticipant.AudioEnergy);
        }

        public void SetVoiceId(string voiceId)
        {
            if (!IsOwner) return;
            m_PlayerVoiceId.Value = new FixedString128Bytes(voiceId);
            SetupVoicePlayer();
            if (XRINetworkGameManager.Instance.positionalVoiceChat)
            {
                m_VoiceChat.Set3DAudio(m_HeadOrigin);
            }
        }

        /// <summary>
        /// Broadcasts a shared media payload from the local owning player to all connected clients.
        /// </summary>
        public void BroadcastSharedMedia(string fileName, byte[] imageBytes)
        {
            BroadcastSharedMedia(fileName, imageBytes, k_SharedMediaKindImage, null);
        }

        /// <summary>
        /// Broadcasts a shared media payload from the local owning player to all connected clients.
        /// </summary>
        public void BroadcastSharedMedia(string fileName, byte[] mediaBytes, string kind, string mime)
        {
            if (!IsOwner)
            {
                Utils.Log("BroadcastSharedMedia can only be called by the owning player.", 1);
                return;
            }

            if (mediaBytes == null || mediaBytes.Length == 0)
            {
                Utils.Log("BroadcastSharedMedia ignored an empty payload.", 1);
                return;
            }

            if (!IsSpawned)
            {
                Utils.Log("BroadcastSharedMedia ignored because the local player is not spawned.", 1);
                return;
            }

            m_LocalSharedMediaUploadId++;

            NormalizeSharedMediaMetadata(fileName, mediaBytes, kind, mime, out FixedString32Bytes fixedKind, out FixedString32Bytes fixedMime);
            var fixedFileName = new FixedString128Bytes(string.IsNullOrWhiteSpace(fileName) ? DefaultSharedMediaName(fixedKind) : fileName);
            int chunkSize = GetSharedMediaChunkSize();
            int totalChunks = Mathf.Max(1, Mathf.CeilToInt(mediaBytes.Length / (float)chunkSize));

            if (IsDistributedAuthoritySession())
            {
                CacheLatestSharedMedia(fixedFileName, fixedKind, fixedMime, mediaBytes);
                m_LocalSharedMediaEchoSkips.Add(m_LocalSharedMediaUploadId);
                StartCoroutine(RelaySharedMediaToClientsCoroutine(m_LocalSharedMediaUploadId, fixedFileName, fixedKind, fixedMime, mediaBytes));
                return;
            }

            if (ShouldSkipLocalSharedMediaEcho())
                m_LocalSharedMediaEchoSkips.Add(m_LocalSharedMediaUploadId);

            StartCoroutine(BroadcastSharedMediaCoroutine(m_LocalSharedMediaUploadId, fixedFileName, fixedKind, fixedMime, mediaBytes, chunkSize, totalChunks));
        }

        IEnumerator BroadcastSharedMediaCoroutine(
            ulong uploadId,
            FixedString128Bytes fixedFileName,
            FixedString32Bytes fixedKind,
            FixedString32Bytes fixedMime,
            byte[] mediaBytes,
            int chunkSize,
            int totalChunks)
        {
            BeginSharedMediaUploadServerRpc(uploadId, fixedFileName, fixedKind, fixedMime, mediaBytes.Length, totalChunks);

            int chunksSentThisFrame = 0;
            int chunksPerFrame = Mathf.Max(1, m_SharedMediaChunksPerFrame);

            for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
            {
                int sourceOffset = chunkIndex * chunkSize;
                int chunkLength = Mathf.Min(chunkSize, mediaBytes.Length - sourceOffset);
                byte[] chunk = new byte[chunkLength];
                Buffer.BlockCopy(mediaBytes, sourceOffset, chunk, 0, chunkLength);
                SubmitSharedMediaChunkServerRpc(uploadId, chunkIndex, chunk);

                chunksSentThisFrame++;
                if (chunksSentThisFrame >= chunksPerFrame)
                {
                    chunksSentThisFrame = 0;
                    yield return null;
                }
            }

            CompleteSharedMediaUploadServerRpc(uploadId);

            if (m_LogSharedMediaSync)
                Utils.Log($"Shared media upload queued: {fixedFileName} [{fixedKind}/{fixedMime}] ({mediaBytes.Length} bytes, {totalChunks} chunks at {chunkSize} bytes/chunk).");
        }

        static void NormalizeSharedMediaMetadata(
            string fileName,
            byte[] mediaBytes,
            string requestedKind,
            string requestedMime,
            out FixedString32Bytes fixedKind,
            out FixedString32Bytes fixedMime)
        {
            string normalizedMime = NormalizeSharedMediaMime(requestedMime, fileName, mediaBytes);
            bool isVideo = IsVideoKind(requestedKind) || IsVideoMime(normalizedMime);
            string normalizedKind = isVideo ? k_SharedMediaKindVideo : k_SharedMediaKindImage;

            if (string.IsNullOrWhiteSpace(normalizedMime))
                normalizedMime = isVideo ? "video/mp4" : "image/jpeg";

            fixedKind = ToFixed32(normalizedKind);
            fixedMime = ToFixed32(normalizedMime);
        }

        static FixedString32Bytes ToFixed32(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new FixedString32Bytes(string.Empty);

            string trimmed = value.Trim();
            return new FixedString32Bytes(trimmed.Length <= 29 ? trimmed : trimmed.Substring(0, 29));
        }

        static string DefaultSharedMediaName(FixedString32Bytes kind)
        {
            return IsSharedMediaImage(kind, default) ? "Uploaded photo" : "Uploaded video";
        }

        static bool IsSharedMediaImage(FixedString32Bytes kind, FixedString32Bytes mime)
        {
            string kindString = kind.ToString();
            string mimeString = mime.ToString();
            if (IsVideoKind(kindString) || IsVideoMime(mimeString))
                return false;

            return true;
        }

        static string NormalizeSharedMediaMime(string mime, string fileName, byte[] bytes)
        {
            string lower = string.IsNullOrWhiteSpace(mime) ? string.Empty : mime.Trim().ToLowerInvariant();
            int semicolon = lower.IndexOf(';');
            if (semicolon >= 0)
                lower = lower.Substring(0, semicolon).Trim();

            if (lower == "image/jpg")
                lower = "image/jpeg";

            if (IsSupportedSharedImageMime(lower) || IsSupportedSharedVideoMime(lower))
                return lower;

            if (bytes != null)
            {
                if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                    return "image/jpeg";

                if (bytes.Length >= 8
                    && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                    && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
                    return "image/png";

                if (LooksLikeIsoBaseMediaFile(bytes))
                    return GuessIsoBaseMediaMime(fileName);

                if (bytes.Length >= 4 && bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3)
                    return "video/webm";
            }

            string extension = GetLowerExtension(fileName);
            switch (extension)
            {
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".png":
                    return "image/png";
                case ".mp4":
                    return "video/mp4";
                case ".m4v":
                    return "video/x-m4v";
                case ".mov":
                    return "video/quicktime";
                case ".webm":
                    return "video/webm";
                default:
                    return lower;
            }
        }

        static bool IsSupportedSharedImageMime(string mime)
        {
            return string.Equals(mime, "image/jpeg", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(mime, "image/png", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsSupportedSharedVideoMime(string mime)
        {
            return string.Equals(mime, "video/mp4", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(mime, "video/quicktime", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(mime, "video/x-m4v", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(mime, "video/webm", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsVideoKind(string kind)
        {
            return string.Equals(kind, k_SharedMediaKindVideo, StringComparison.OrdinalIgnoreCase);
        }

        static bool IsVideoMime(string mime)
        {
            return !string.IsNullOrWhiteSpace(mime)
                   && mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
        }

        static bool LooksLikeIsoBaseMediaFile(byte[] bytes)
        {
            return bytes.Length >= 12
                   && bytes[4] == 0x66 && bytes[5] == 0x74 && bytes[6] == 0x79 && bytes[7] == 0x70;
        }

        static string GuessIsoBaseMediaMime(string fileName)
        {
            string extension = GetLowerExtension(fileName);
            if (extension == ".mov")
                return "video/quicktime";

            if (extension == ".m4v")
                return "video/x-m4v";

            return "video/mp4";
        }

        static string GetLowerExtension(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return string.Empty;

            int slashIndex = Mathf.Max(fileName.LastIndexOf('/'), fileName.LastIndexOf('\\'));
            int dotIndex = fileName.LastIndexOf('.');
            if (dotIndex < 0 || dotIndex < slashIndex || dotIndex == fileName.Length - 1)
                return string.Empty;

            return fileName.Substring(dotIndex).ToLowerInvariant();
        }

        bool ShouldSkipLocalSharedMediaEcho()
        {
            NetworkManager networkManager = this.NetworkManager;
            return networkManager != null &&
                   !networkManager.DistributedAuthorityMode;
        }

        bool IsDistributedAuthoritySession()
        {
            NetworkManager networkManager = this.NetworkManager != null ? this.NetworkManager : NetworkManager.Singleton;
            return networkManager != null && networkManager.DistributedAuthorityMode;
        }

        bool CanRelayLatestSharedMediaToOwner()
        {
            NetworkManager networkManager = this.NetworkManager != null ? this.NetworkManager : NetworkManager.Singleton;
            if (networkManager != null && networkManager.DistributedAuthorityMode)
                return networkManager.LocalClientId == networkManager.CurrentSessionOwner && !IsOwner;

            return IsServer;
        }

        XRINetworkPlayer ResolveSharedMediaRelayPlayer()
        {
            if (IsDistributedAuthoritySession() && LocalPlayer != null && LocalPlayer.IsSpawned)
                return LocalPlayer;

            return this;
        }

        bool IsValidSharedMediaSender(ServerRpcParams rpcParams)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;
            if (senderClientId == OwnerClientId)
                return true;

            Utils.Log($"Shared media RPC rejected because sender {senderClientId} does not own player object {OwnerClientId}.", 1);
            return false;
        }

        [ServerRpc(RequireOwnership = false)]
        void BeginSharedMediaUploadServerRpc(
            ulong uploadId,
            FixedString128Bytes fileName,
            FixedString32Bytes kind,
            FixedString32Bytes mime,
            int totalBytes,
            int totalChunks,
            ServerRpcParams rpcParams = default)
        {
            if (!IsValidSharedMediaSender(rpcParams))
                return;

            m_PendingServerSharedMediaUploads[uploadId] = new PendingSharedMediaUpload(fileName, kind, mime, totalBytes, totalChunks);
        }

        [ServerRpc(RequireOwnership = false)]
        void SubmitSharedMediaChunkServerRpc(ulong uploadId, int chunkIndex, byte[] chunkData, ServerRpcParams rpcParams = default)
        {
            if (!IsValidSharedMediaSender(rpcParams))
                return;

            if (!m_PendingServerSharedMediaUploads.TryGetValue(uploadId, out var pendingUpload))
            {
                if (m_LogSharedMediaSync)
                    Utils.Log($"Shared media server chunk ignored because upload {uploadId} was not started.", 1);

                return;
            }

            pendingUpload.StoreChunk(chunkIndex, chunkData);
            TryFinalizeSharedMediaUpload(uploadId);
        }

        [ServerRpc(RequireOwnership = false)]
        void CompleteSharedMediaUploadServerRpc(ulong uploadId, ServerRpcParams rpcParams = default)
        {
            if (!IsValidSharedMediaSender(rpcParams))
                return;

            if (!m_PendingServerSharedMediaUploads.TryGetValue(uploadId, out var pendingUpload))
            {
                if (m_LogSharedMediaSync)
                    Utils.Log($"Shared media upload {uploadId} could not complete because it was not started.", 1);

                return;
            }

            pendingUpload.completionRequested = true;
            TryFinalizeSharedMediaUpload(uploadId);
        }

        void TryFinalizeSharedMediaUpload(ulong uploadId)
        {
            if (!m_PendingServerSharedMediaUploads.TryGetValue(uploadId, out var pendingUpload))
                return;

            if (!pendingUpload.completionRequested || !pendingUpload.IsComplete())
                return;

            byte[] combinedMediaBytes = pendingUpload.Combine();
            var fileName = pendingUpload.fileName;
            var kind = pendingUpload.kind;
            var mime = pendingUpload.mime;

            m_PendingServerSharedMediaUploads.Remove(uploadId);

            CacheLatestSharedMedia(fileName, kind, mime, combinedMediaBytes);
            StartCoroutine(RelaySharedMediaToClientsCoroutine(uploadId, fileName, kind, mime, combinedMediaBytes));
        }

        static void CacheLatestSharedMedia(FixedString128Bytes fileName, FixedString32Bytes kind, FixedString32Bytes mime, byte[] combinedMediaBytes)
        {
            if (combinedMediaBytes == null || combinedMediaBytes.Length == 0)
                return;

            s_HasLatestSharedMedia = true;
            s_LatestSharedMediaFileName = fileName;
            s_LatestSharedMediaKind = kind;
            s_LatestSharedMediaMime = mime;
            s_LatestSharedMediaBytes = new byte[combinedMediaBytes.Length];
            Buffer.BlockCopy(combinedMediaBytes, 0, s_LatestSharedMediaBytes, 0, combinedMediaBytes.Length);
        }

        static ulong GetNextServerSharedMediaDownloadId()
        {
            s_ServerSharedMediaDownloadId++;
            if (s_ServerSharedMediaDownloadId == 0)
                s_ServerSharedMediaDownloadId = 1UL << 63;

            return s_ServerSharedMediaDownloadId;
        }

        IEnumerator SendLatestSharedMediaToOwnerWhenReady()
        {
            if (!s_HasLatestSharedMedia || s_LatestSharedMediaBytes == null || s_LatestSharedMediaBytes.Length == 0)
                yield break;

            // Let the target client's scene receiver and local-player listeners settle before replaying session state.
            yield return null;
            yield return null;

            if (!IsSpawned || !CanRelayLatestSharedMediaToOwner() || !s_HasLatestSharedMedia || s_LatestSharedMediaBytes == null || s_LatestSharedMediaBytes.Length == 0)
                yield break;

            byte[] latestBytes = new byte[s_LatestSharedMediaBytes.Length];
            Buffer.BlockCopy(s_LatestSharedMediaBytes, 0, latestBytes, 0, latestBytes.Length);

            XRINetworkPlayer relayPlayer = ResolveSharedMediaRelayPlayer();
            if (relayPlayer == null || !relayPlayer.IsSpawned)
                yield break;

            ulong downloadId = GetNextServerSharedMediaDownloadId();
            relayPlayer.StartCoroutine(relayPlayer.RelaySharedMediaToClientsCoroutine(downloadId, s_LatestSharedMediaFileName, s_LatestSharedMediaKind, s_LatestSharedMediaMime, latestBytes, OwnerClientId));
        }

        IEnumerator RelaySharedMediaToClientsCoroutine(
            ulong uploadId,
            FixedString128Bytes fileName,
            FixedString32Bytes kind,
            FixedString32Bytes mime,
            byte[] combinedMediaBytes,
            ulong? targetClientId = null)
        {
            if (combinedMediaBytes == null || combinedMediaBytes.Length == 0)
                yield break;

            int chunkSize = GetSharedMediaChunkSize();
            int totalChunks = Mathf.Max(1, Mathf.CeilToInt(combinedMediaBytes.Length / (float)chunkSize));

            BeginSharedMediaDownloadRpc(uploadId, fileName, kind, mime, combinedMediaBytes.Length, totalChunks, CreateSharedMediaTargetParams(targetClientId));

            int chunksSentThisFrame = 0;
            int chunksPerFrame = Mathf.Max(1, m_SharedMediaChunksPerFrame);

            for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
            {
                int sourceOffset = chunkIndex * chunkSize;
                int chunkLength = Mathf.Min(chunkSize, combinedMediaBytes.Length - sourceOffset);
                byte[] chunk = new byte[chunkLength];
                Buffer.BlockCopy(combinedMediaBytes, sourceOffset, chunk, 0, chunkLength);
                SubmitSharedMediaChunkRpc(uploadId, chunkIndex, chunk, CreateSharedMediaTargetParams(targetClientId));

                chunksSentThisFrame++;
                if (chunksSentThisFrame >= chunksPerFrame)
                {
                    chunksSentThisFrame = 0;
                    yield return null;
                }
            }

            CompleteSharedMediaDownloadRpc(uploadId, CreateSharedMediaTargetParams(targetClientId));

            if (m_LogSharedMediaSync)
                Utils.Log($"Shared media relayed: {fileName} [{kind}/{mime}] ({combinedMediaBytes.Length} bytes, {totalChunks} chunks at {chunkSize} bytes/chunk).");
        }

        RpcParams CreateSharedMediaTargetParams(ulong? targetClientId)
        {
            if (targetClientId.HasValue)
                return RpcTarget.Single(targetClientId.Value, RpcTargetUse.Temp);

            return RpcTarget.ClientsAndHost;
        }

        int GetSharedMediaChunkSize()
        {
            int chunkSize = k_MaxSharedMediaChunkSize;
            NetworkManager networkManager = NetworkManager.Singleton;

            if (networkManager != null &&
                networkManager.NetworkConfig != null &&
                networkManager.NetworkConfig.NetworkTransport is UnityTransport unityTransport)
            {
                int safePayloadBytes = unityTransport.MaxPayloadSize - k_SharedMediaRpcOverheadReserve;
                chunkSize = Mathf.Min(chunkSize, safePayloadBytes);
            }

            return Mathf.Max(k_MinSharedMediaChunkSize, chunkSize);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void BeginSharedMediaDownloadRpc(
            ulong uploadId,
            FixedString128Bytes fileName,
            FixedString32Bytes kind,
            FixedString32Bytes mime,
            int totalBytes,
            int totalChunks,
            RpcParams rpcParams = default)
        {
            m_PendingClientSharedMediaDownloads[uploadId] = new PendingSharedMediaUpload(fileName, kind, mime, totalBytes, totalChunks);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void SubmitSharedMediaChunkRpc(ulong uploadId, int chunkIndex, byte[] chunkData, RpcParams rpcParams = default)
        {
            if (!m_PendingClientSharedMediaDownloads.TryGetValue(uploadId, out var pendingUpload))
            {
                if (m_LogSharedMediaSync)
                    Utils.Log($"Shared media client chunk ignored because download {uploadId} was not started.", 1);

                return;
            }

            pendingUpload.StoreChunk(chunkIndex, chunkData);
            TryFinalizeSharedMediaDownload(uploadId);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void CompleteSharedMediaDownloadRpc(ulong uploadId, RpcParams rpcParams = default)
        {
            if (!m_PendingClientSharedMediaDownloads.TryGetValue(uploadId, out var pendingUpload))
            {
                if (m_LogSharedMediaSync)
                    Utils.Log($"Shared media download {uploadId} could not complete because it was not started.", 1);

                return;
            }

            pendingUpload.completionRequested = true;
            TryFinalizeSharedMediaDownload(uploadId);
        }

        void TryFinalizeSharedMediaDownload(ulong uploadId)
        {
            if (!m_PendingClientSharedMediaDownloads.TryGetValue(uploadId, out var pendingUpload))
                return;

            if (!pendingUpload.completionRequested || !pendingUpload.IsComplete())
                return;

            m_PendingClientSharedMediaDownloads.Remove(uploadId);

            byte[] combinedMediaBytes = pendingUpload.Combine();
            CacheLatestSharedMedia(pendingUpload.fileName, pendingUpload.kind, pendingUpload.mime, combinedMediaBytes);

            if (IsOwner && m_LocalSharedMediaEchoSkips.Remove(uploadId))
                return;

            bool isImagePayload = IsSharedMediaImage(pendingUpload.kind, pendingUpload.mime);
            bool invokedListener = false;

            if (onSharedMediaPayloadReceived != null)
            {
                onSharedMediaPayloadReceived.Invoke(pendingUpload.fileName.ToString(), pendingUpload.kind.ToString(), pendingUpload.mime.ToString(), combinedMediaBytes);
                invokedListener = true;
            }

            if (isImagePayload && onSharedMediaReceived != null)
            {
                onSharedMediaReceived.Invoke(pendingUpload.fileName.ToString(), combinedMediaBytes);
                invokedListener = true;
            }

            if (!invokedListener)
            {
                if (m_LogSharedMediaSync)
                    Utils.Log($"Shared media received with no scene listener: {pendingUpload.fileName}", 1);
            }
        }

        /// <summary>
        /// Called from clients to mute this player locally for that client.
        /// </summary>
        public void ToggleSquelch()
        {
            if (m_VivoxParticipant != null)
            {
                squelched.Value = !squelched.Value;
                if (squelched.Value)
                    m_VivoxParticipant.MutePlayerLocally();
                else
                    m_VivoxParticipant.UnmutePlayerLocally();
            }
        }
    }
}
