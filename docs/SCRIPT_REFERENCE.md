# Script Reference

This reference covers scripts created for this repository, modified for this repository, or maintained as part of the current Unity scenes. Imported SDKs, package samples, generated project files, and third-party plugin internals are excluded unless they are directly wrapped by project code.

## Media Upload and Gallery

| File | Purpose | Inputs | Outputs / Runtime Usage |
| --- | --- | --- | --- |
| `Assets/_Scripts/MediaUpload/ImagePicker.cs` | Opens the Android/Quest image picker, loads the selected file, displays previews, and prepares encoded payloads. | NativeGallery path, display references, encoding settings. | Texture preview/display, cached payload in `PickedImages`, `ImagePrepared` event. |
| `Assets/_Scripts/MediaUpload/M_QuestGalleryAndroidBridge.cs` | Queries MediaStore, scans Quest-accessible image folders, manages imported media folders, and loads thumbnails/full textures. | Android permissions, file paths, content URIs, scan settings. | `GalleryItem` lists, thumbnail textures, imported-media files, deletion results. |
| `Assets/_Scripts/MediaUpload/M_QuestGalleryController.cs` | Coordinates gallery UI, device imports, phone/shared imports, tile selection, deletion, and network broadcast. | Bridge results, tile prefab, display, ImagePicker, network sync reference. | Spawned tiles, selected image display, status labels, gallery refresh, shared-media broadcasts. |
| `Assets/_Scripts/MediaUpload/M_QuestGalleryTile.cs` | Displays one gallery item with thumbnail, filename, selected-state visual, and delete control. | `GalleryItem`, controller reference, thumbnail texture, UI events. | Selection/delete callbacks and runtime thumbnail cleanup. |
| `Assets/_Scripts/MediaUpload/M_QuestPhotoDisplay.cs` | Displays selected, uploaded, or synchronized photos on a RawImage or renderer material. | Loaded textures, gallery items, RawImage bounds, renderer settings. | Texture assignment, fitted layout, filename/status text, fallback display state. |
| `Assets/_Scripts/MediaUpload/M_PhoneUploadToDisplay.cs` | Loads new files saved by the HTTP server and routes them into display, gallery, and shared-media sync. | `M_SimpleHttpServer.LastSavedPhotoPath`, file bytes, display/gallery/network references. | Displayed upload, gallery refresh, optional network broadcast, status logs. |
| `Assets/_Scripts/MediaUpload/M_NetworkedPhotoSync.cs` | Bridges local media selections and phone uploads to `XRINetworkPlayer` shared-media transfer. | Prepared images, selected gallery items, textures, local player lookup, shared-media callbacks. | Local display, local join-time display clearing, JPEG payloads, remote shared-media display updates. |
| `Assets/_Scripts/MediaUpload/M_PassthroughModeController.cs` | Controls real Quest passthrough layer state, camera transparency, scene renderer visibility, hand visuals, and toggle binding. | OVRManager, OVRPassthroughLayer, camera, toggle, optional keep/hide root lists, optional hand visual controller. | Passthrough state changes, camera clear state, restored renderer/object visibility, hand visual visibility, events. |
| `Assets/_Scripts/MediaUpload/M_PassthroughHandVisualController.cs` | Hides visual-only hand/controller meshes while passthrough is active without disabling tracking, rays, or interactors. | Assigned hand roots/renderers/behaviours, optional auto-search root and name tokens. | Renderer/visual-only behaviour visibility changes with original states restored after passthrough. |
| `Assets/_Scripts/MediaUpload/M_PassthroughMountedToggleUI.cs` | Legacy opt-in headset-following passthrough toggle button. Disabled in the active phone upload scene. | XR camera pose, controller reference, generated UI settings. | Runtime Canvas/Button hierarchy only when explicitly enabled. |
| `Assets/_Scripts/MediaUpload/M_PhoneImportHeadsetMode.cs` | Supplies phone upload URL/code text and retains opt-in prompt/pairing-mode behavior for scenes that need it. | Published server instructions, phone upload bridge, camera, passthrough controller. | Pairing instruction text, optional prompt text, optional scene isolation, restored camera/renderers when prompt mode is used. |

## Quest Runtime Menu

| File | Purpose | Inputs | Outputs / Runtime Usage |
| --- | --- | --- | --- |
| `Assets/_Scripts/Menus/M_QuestUserMenu.cs` | Creates the summonable VR user menu used by `PhonePhotoUpload.unity`. | OVR menu input, XR camera, `M_PassthroughModeController`, `M_ServerBootstrap`, optional `M_PhoneImportHeadsetMode`, optional `CharacterResetter`. | Stable world-space menu placement, phone URL/code display, passthrough toggle, recenter action, close/dismiss behavior. |

## Phone Upload Companion Flow

| File | Purpose | Inputs | Outputs / Runtime Usage |
| --- | --- | --- | --- |
| `Assets/FromCompanion/Scripts/Server/M_ServerBootstrap.cs` | Starts HTTP upload service, UDP discovery, pairing-code generation, and instruction publication. | Ports, upload size limit, local network interfaces, TMP labels. | Running server, UDP beacon component, published URLs/code, logs. |
| `Assets/FromCompanion/Scripts/Server/M_SimpleHTTPServer.cs` | Serves the browser upload page and accepts raw or multipart image uploads. | HTTP requests for `/`, `/ping`, `/upload-photo`; pairing code; upload bytes. | Saved files under `Uploads`, `LastSavedPhotoPath`, HTML/JSON responses. |
| `Assets/FromCompanion/Scripts/Server/M_NetworkDiscoveryServer.cs` | Broadcasts `PHOTO_SERVER` beacons on global and subnet broadcast addresses. | Discovery port, HTTP port, network interface addresses. | UDP discovery packets and logs. |
| `Assets/FromCompanion/Scripts/Server/M_LatestPhotoViewer.cs` | Displays the newest uploaded file in a RawImage preview. | `LastSavedPhotoPath`, uploaded image bytes. | Runtime texture preview and status text. |
| `Assets/FromCompanion/Scripts/Server/M_LatestPhotoViewer_Material.cs` | Applies the newest uploaded image to a renderer material. | `LastSavedPhotoPath`, renderer/material settings. | Runtime material texture update. |
| `Assets/FromCompanion/Scripts/Client/M_NetworkDiscoveryClient.cs` | Discovers the upload server by listening for UDP beacons. | `PHOTO_SERVER` UDP payloads. | Discovered server IP and HTTP port. |
| `Assets/FromCompanion/Scripts/Client/M_PhotoUploader.cs` | Picks a client-side photo, downsizes it, previews it, and posts JPEG bytes to the server. | NativeGallery selection, discovery result, upload quality settings. | Local preview and HTTP upload request. |
| `Assets/FromCompanion/Scripts/Client/M_RestClient.cs` | Maintains server base URL and posts image bytes to `/upload-photo`. | Discovered IP/port, JPEG bytes. | UnityWebRequest upload and response logs. |
| `Assets/FromCompanion/Scripts/Common/UnityMainThreadDispatcher.cs` | Queues callbacks for main-thread execution. | Background-thread actions. | Main-thread callback execution. |
| `Assets/FromCompanion/Scripts/Common/UnityMainThreadRunner.cs` | Pumps the dispatcher from a MonoBehaviour `Update`. | Dispatcher queue. | Per-frame processing of queued callbacks. |

## Phone Mirroring

| File | Purpose | Inputs | Outputs / Runtime Usage |
| --- | --- | --- | --- |
| `Assets/_Scripts/PhoneMirror/M_AndroidIpUtil.cs` | Reads the Android Wi-Fi IP address on Quest. | Android Wi-Fi manager state. | Local IPv4 string or `0.0.0.0`. |
| `Assets/_Scripts/PhoneMirror/M_PairingCodeProvider.cs` | Generates or stores the pairing code used by phone mirroring. | Code length and optional fixed code. | Runtime pairing code. |
| `Assets/_Scripts/PhoneMirror/M_PairingUiPresenter.cs` | Displays phone mirror host address, port, code, and status; runtime show/hide/recenter controls are opt-in. | Signaling host, code provider state, pairing UI canvas/root. | TMP text updates and optional pairing-panel controls. |
| `Assets/_Scripts/PhoneMirror/M_PhonePairingUiToggle.cs` | Shows or hides a pairing UI root from an assigned VR-friendly button without disabling the host/signaling components. | Pairing UI root, optional phone import mode, optional button/TMP label. | Pairing UI visibility changes and synchronized button label state. |
| `Assets/_Scripts/PhoneMirror/M_WorldSpaceUiRecenter.cs` | Recenters a world-space UI transform in front of the current headset/camera from an assigned or opt-in generated button. | UI root, headset/camera transform, optional button/TMP label. | Updated UI position/rotation. |
| `Assets/_Scripts/PhoneMirror/M_QuestLanAdvertiserUdp.cs` | Broadcasts local phone mirror signaling information. | Signaling host port and beacon settings. | UDP beacon packets. |
| `Assets/_Scripts/PhoneMirror/M_QuestSignalingHostTcp.cs` | Hosts framed TCP signaling, validates pairing code, and relays JSON. | TCP frames, pairing code, local IP. | Connection events and framed JSON responses. |
| `Assets/_Scripts/PhoneMirror/M_PhoneMirrorQuestWebRTC.cs` | Receives WebRTC video, sends signaling answer/ICE messages, and forwards input channel JSON. | Signaling JSON, remote video track, STUN URL, UI references. | RawImage video texture, status text, data channel messages. |
| `Assets/_Scripts/PhoneMirror/M_PhonePanelRayInputSender.cs` | Converts VR ray selection on the phone panel to normalized touch data. | RayInteractor, selector events, panel collider/rect, WebRTC input channel. | Touch down/move/up JSON messages. |

## Avatar, Face, and Player Embodiment

| File | Purpose | Inputs | Outputs / Runtime Usage |
| --- | --- | --- | --- |
| `Assets/_Scripts/Player/M_LocalAvatarManager.cs` | Loads the local Ready Player Me avatar, binds VRIK to headset/controller anchors, and initializes face diagnostics. | RPM URL, OVR/XR anchors, OVRFaceExpressions, local avatar root. | Local avatar, VRIK setup, hidden first-person head meshes, local face driver. |
| `Assets/_Scripts/Player/M_LocalFaceDriver.cs` | Maps OVR face expressions to RPM blendshapes and relaxes neutral states. | OVRFaceExpressions, face mesh, gain/smoothing thresholds. | Local blendshape weights. |
| `Assets/_Scripts/Player/M_FaceDebugProbe.cs` | Records neutral face samples and prints diagnostic summaries. | OVRFaceExpressions and local RPM face mesh. | Console diagnostics for expression/blendshape calibration. |
| `Assets/_Scripts/Player/M_NetPlayer.cs` | Stores owner-written replicated avatar URL. | `PlayerPrefs` key `RPM_URL`, fallback URL. | NetworkVariable avatar URL. |
| `Assets/_Scripts/Player/M_NetPoseDriver.cs` | Replicates head and hand poses through Netcode variables. | OVR or XR Origin anchors, IK targets, ownership state. | Network pose variables and smoothed remote IK target transforms. |
| `Assets/_Scripts/Player/M_NetAvatar.cs` | Loads networked RPM avatar, binds VRIK to replicated IK targets, and connects face mirror target. | Avatar URL, pose driver, IK targets, ownership state. | Network avatar GameObject, VRIK, face mirror binding, layer/visibility changes. |
| `Assets/_Scripts/Player/M_NetFaceMirror.cs` | Sends scaled owner face weights and applies smoothed replicated weights to network avatar faces. | Local source mesh, avatar face mesh, Netcode state. | ServerRpc/ClientRpc face snapshots and remote blendshape updates. |
| `Assets/_Scripts/Player/M_OVRContinuousLocomotion.cs` | Moves an OVR rig using controller input and CharacterController gravity. | OVR axes/buttons, headset yaw, CharacterController. | Local rig movement and snap-turn rotation. |
| `Assets/_Scripts/Player/M_OVRLipSyncAutoBinder.cs` | Matches OVR lip-sync visemes to RPM blendshape indices. | Face mesh blendshape names and OVR lip-sync component. | Configured viseme/laughter targets and editor diagnostics. |
| `Assets/_Scripts/Network/FaceSyncData.cs` | Serializes experimental facial weights and eye-forward vectors. | Float array and Vector3. | Netcode serialization payload. |

## Multiplayer, Voice, Menus, and Shared VR Assets

| File or Folder | Purpose | Notes |
| --- | --- | --- |
| `Assets/_Scripts/Network/M_VivoxManager.cs` | Initializes Unity Services/Vivox, joins/leaves positional voice channels, and manages local Vivox audio tap lifecycle. | Used by menu/session flows. |
| `Assets/_Scripts/Menus/M_MenuManager.cs` | Drives host/join menu panels and scene transition around Meta matchmaking. | Namespace remains `Meta.XR.MultiplayerBlocks.Shared` for compatibility with imported blocks. |
| `Assets/VRMPAssets/Scripts/Network/NetworkManagers/` | Project-maintained multiplayer manager layer for authentication, sessions, connection state, Netcode settings, and voice chat. | Key scripts: `XRINetworkGameManager`, `SessionManager`, `AuthenticationManager`, `NetworkManagerVRMultiplayer`, `VoiceChatManager`, `LobbyManager`. |
| `Assets/VRMPAssets/Scripts/Network/NetworkPlayer/` | Networked player layer for avatar state, hands, player metadata, shared media, name/color, and voice presentation. | `XRINetworkPlayer` contains shared media chunk upload/download logic. |
| `Assets/VRMPAssets/Scripts/Network/NetworkInteractions/` | Synchronizes shared XR interactables and socket ownership. | Supports network grabbables and socketed objects. |
| `Assets/VRMPAssets/Scripts/Network/NetworkUtilityComponents/` | Network transform, trigger, and networked UI helpers. | Used by shared room controls. |
| `Assets/VRMPAssets/Scripts/Gameplay/` | Room gameplay systems such as message board, drawing, target practice, music, fan, gravity zone, and object destruction. | Maintained as shared-room interaction support. |
| `Assets/VRMPAssets/Scripts/Player/` | Name tags, offline avatar presentation, hand pose visuals, player HUD notifications, connection toggle, and appearance menu. | Supports local/remote player presentation. |
| `Assets/VRMPAssets/Scripts/UI/` | Lobby list, player list, world canvas, tooltip, popout, greeting board, and menu UI helpers. | UI surface for session and room interaction. |
| `Assets/VRMPAssets/MiniGames/MiniGameScripts/` | Mini-game state, scoring, scoreboard slots, and activity-specific gameplay scripts. | Optional shared activities retained with the VRMP scenes. |
| `Assets/VRMPAssets/Tutorial/TutorialScripts/` | Tutorial video playback, render texture, and scrub controls. | Supports tutorial media assets. |

## Small Utilities

- `Assets/_Scripts/Environment/M_Rotator.cs` rotates scene objects for ambient environment motion.
- `Assets/_Scripts/Network/M_CompanionLiveViewer.cs` is an inactive integration point for future companion live-view display wiring.

## Maintainer Notes

- Serialized field names were preserved to avoid breaking Unity scene and prefab references.
- Several VRMP scripts retain external namespaces from Unity/Meta sample APIs because prefab type bindings depend on them.
- `Assets/Plugins`, `Assets/Oculus`, `Assets/Ready Player Me`, `Assets/Samples`, `Assets/TextMesh Pro`, generated `.csproj` files, `Library`, `Temp`, `Logs`, `.utmp`, and build/cache output are not project-authored source.
