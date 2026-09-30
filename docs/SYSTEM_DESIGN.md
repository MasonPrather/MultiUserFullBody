# System Design

## System Purpose

MultiUserFullBody is a Unity-based research prototype for multi-user immersive interaction. It combines networked Ready Player Me avatars, Quest/OVR tracking, optional face-expression experiments, Vivox voice, local media import, no-install phone media upload, and shared media synchronization inside a VR room.

## Design Goals

- Support multi-user avatar presence with replicated head and hand motion.
- Keep local avatar presentation separate from network avatar presentation so first-person rendering does not obstruct the headset view.
- Allow phone-to-Quest photo and video upload over a lab Wi-Fi network without installing a phone app.
- Make imported media visible locally first, then synchronize it to connected clients when a network player exists.
- Preserve Quest-friendly runtime behavior by using local storage, bounded media sizes, chunked network payloads, and explicit platform checks.
- Keep supervisor review flows understandable from scene prompts, console logs, and repository documentation.

## High-Level Architecture

```text
[Unity Scene]
    |
    +-- [Multiplayer Managers]
    |       |
    |       +-- Unity Services authentication/session/relay
    |       +-- Netcode player spawning
    |       +-- Vivox voice
    |
    +-- [Avatar Layer]
    |       |
    |       +-- Local RPM avatar + VRIK + OVR anchors
    |       +-- Network RPM avatar + replicated IK targets
    |       +-- Optional face/lip-sync diagnostics
    |
    +-- [Media Layer]
    |       |
    |       +-- Quest gallery/import picker
    |       +-- Quest-hosted phone upload page
    |       +-- Shared photo display
    |       +-- Shared-media network broadcast
    |
    +-- [Phone Mirror Layer]
            |
            +-- UDP advertisement
            +-- TCP signaling
            +-- WebRTC video + input data channel
```

## Major Components

### Unity Scenes

`ProjectSettings/EditorBuildSettings.asset` enables `Assets/_Scenes/PhonePhotoUpload.unity` for builds. Additional research scenes remain available under `Assets/_Scenes`, including media upload, face tracking, phone mirroring, conference-room, and multi-user conference variants. `Assets/FromCompanion/Scenes` contains isolated phone upload client/server scenes.

### Multiplayer Services

`XRINetworkGameManager`, `SessionManager`, `AuthenticationManager`, `NetworkManagerVRMultiplayer`, `LobbyManager`, and `VoiceChatManager` coordinate Unity Services authentication, session/lobby discovery, relay/transport setup, Netcode connection state, and Vivox voice. The project supports distributed Unity Services sessions and a local-only fallback path when configured by `SessionManager`.

### Avatar and Embodiment Layer

`M_LocalAvatarManager` loads the local Ready Player Me avatar, parents it under the local avatar root, binds VRIK to OVR or fallback XR anchors, hides first-person head meshes, and initializes local face-driving diagnostics. `M_NetPlayer`, `M_NetAvatar`, and `M_NetPoseDriver` replicate avatar URL and head/hand pose state for networked avatars. `M_LocalFaceDriver`, `M_NetFaceMirror`, `M_FaceDebugProbe`, and `M_OVRLipSyncAutoBinder` support face-expression and lip-sync experiments.

### Media Upload and Sharing Layer

`M_ServerBootstrap` starts the Quest-hosted upload server and publishes phone instructions. `M_SimpleHttpServer` serves the upload page and accepts `POST /upload-photo`. `M_PhoneUploadToDisplay` loads newly saved uploads into `M_QuestPhotoDisplay`. `M_QuestUserMenu` is the active headset-side control surface for phone instructions, passthrough, recentering, and dismissal. `M_PhoneImportHeadsetMode` supplies instruction text and retains opt-in pairing prompt behavior for scenes that explicitly need it. Headset-side media selection flows through `ImagePicker`, `M_QuestGalleryAndroidBridge`, `M_QuestGalleryController`, and `M_QuestGalleryTile`. `M_NetworkedPhotoSync` passes prepared image bytes to `XRINetworkPlayer.BroadcastSharedMedia`, which chunks uploads, caches the latest shared image, relays the payload to connected clients, and replays the latest image to later joiners.

### Phone Mirror Layer

`M_QuestLanAdvertiserUdp` broadcasts a local phone mirror beacon, `M_QuestSignalingHostTcp` validates the pairing code and relays framed JSON signaling messages, `M_PhoneMirrorQuestWebRTC` receives the remote video track and sends answer/ICE messages, and `M_PhonePanelRayInputSender` converts VR ray interaction into normalized touch messages. `M_PairingUiPresenter` updates the phone mirror pairing panel; its show/hide and recenter controls are opt-in so they do not create competing runtime UI by default.

## Data and Control Flow

### Phone Photo Upload

```text
[M_ServerBootstrap]
      |
      +-- starts M_SimpleHttpServer on port 8080
      +-- adds M_NetworkDiscoveryServer on UDP port 7777
      +-- publishes local URL and pairing code
      |
[Phone browser]
      |
      +-- GET / for upload page
      +-- POST /upload-photo with image bytes
      |
[Application.persistentDataPath/Uploads]
      |
[M_PhoneUploadToDisplay]
      |
[M_QuestPhotoDisplay]
      |
[M_NetworkedPhotoSync]
      |
[XRINetworkPlayer shared-media RPC chunks]
      |
[Remote M_QuestPhotoDisplay instances]
```

### Headset Media Import

```text
[ImagePicker or M_QuestGalleryAndroidBridge]
      |
      v
[M_QuestGalleryController]
      |
      +-- displays selected image through M_QuestPhotoDisplay
      +-- sends prepared image through M_NetworkedPhotoSync
      |
      v
[XRINetworkPlayer shared-media broadcast]
      |
      +-- server assembles chunks
      +-- caches latest shared image for late joiners
      +-- relays download chunks to ClientsAndHost
```

### Avatar Replication

```text
[Owner OVR anchors]
      |
      v
[M_NetPoseDriver NetworkVariables]
      |
      v
[Remote IK target transforms]
      |
      v
[M_NetAvatar VRIK avatar]
```

## Runtime Sequence

1. Unity loads the active scene.
2. Multiplayer managers initialize authentication, session, transport, and voice state when present.
3. Player prefabs spawn and initialize local/network avatar components.
4. `M_ServerBootstrap` creates the upload directory, generates or resolves a pairing code, starts HTTP upload service, and starts UDP discovery beacons.
5. `M_QuestUserMenu` creates a hidden summonable menu that reads phone pairing text from `M_ServerBootstrap`/`M_PhoneImportHeadsetMode`.
6. `M_PassthroughModeController` owns Meta XR passthrough state, the underlay layer, transparent camera clear behavior, and scene renderer visibility.
7. Phone or headset imports load image bytes into texture display components.
8. Joining clients clear stale local display state before shared session media is applied.
9. The shared-media layer encodes/chunks image data and relays it through the owning `XRINetworkPlayer` when multiplayer is active.
10. Remote clients reconstruct image payloads and display them through local scene receivers; later joiners receive the cached latest image once their player object spawns.

## Configuration Model

Configuration is mostly serialized in Unity scenes and prefabs. Important runtime values include:

- `M_ServerBootstrap.httpPort` default `8080`.
- `M_ServerBootstrap.discoveryPort` default `7777`.
- `M_ServerBootstrap.requirePairingCode` default enabled.
- `M_QuestUserMenu` default summon input is `OVRInput.Button.Start`, with the editor `M` key as a local fallback.
- `M_PassthroughModeController` requires an `OVRManager`, `OVRPassthroughLayer`, and Quest hardware validation for real passthrough.
- `M_SimpleHttpServer` upload root from `Application.persistentDataPath/Uploads`.
- `M_QuestGalleryAndroidBridge.importedMediaFolderName` default `ImportedSharedMedia`.
- `ImagePicker.cacheFolderName` default `PickedImages`.
- `M_QuestSignalingHostTcp.port` default `29000`.
- `XRINetworkPlayer` shared media chunking based on Unity Transport `MaxPayloadSize`.
- `SessionManager.sessionType` for distributed or local-only session behavior.

## External Dependencies

The project uses packages and SDKs recorded in `Packages/manifest.json`, including Meta XR SDK, Meta XR Interaction OVR, Meta XR Platform, Meta XR Simulator, Ready Player Me Core, Unity Netcode for GameObjects, Unity Services Multiplayer, Unity Services Vivox, Unity WebRTC, Unity XR Interaction Toolkit, Unity XR Hands, Unity XR Management, Unity OpenXR/Meta OpenXR, Universal Render Pipeline, Newtonsoft JSON, glTFast, DracoUnity, TextMesh Pro, RootMotion FinalIK, and NativeGallery.

## File and Data Storage

- Phone uploads are written below `Application.persistentDataPath/Uploads`.
- NativeGallery prepared payloads can be cached below `Application.persistentDataPath/PickedImages`.
- Imported shared media can be copied below `Application.persistentDataPath/ImportedSharedMedia`.
- Unity scenes, prefabs, packages, and project settings remain in the repository.
- `Library`, `Temp`, `Logs`, `.utmp`, generated `.csproj`/`.sln` files, build output, and package cache data are generated/editor artifacts rather than maintained source.

## Logging Model

Scripts use Unity console logging with component prefixes such as `[M_ServerBootstrap]`, `[M_SimpleHttpServer]`, `[M_QuestGalleryController]`, `[M_NetworkedPhotoSync]`, `[M_NetFaceMirror]`, `[Quest/WebRTC]`, and `[Vivox]`. Many runtime components expose `verboseLogging` fields for detailed diagnostics during supervisor demos or development sessions.

## Error Handling Model

Runtime components generally fail closed and log actionable warnings: missing references disable or skip the affected flow, HTTP uploads return JSON errors, file-read failures retry briefly, network media sync rejects invalid owners, and Android/Quest media access falls back to app-owned folders where possible. Server threads and network listeners catch exceptions to avoid terminating Unity play mode.

## Design Assumptions

- Quest and phone upload flows run on the same local Wi-Fi network.
- Some routers block peer-to-peer local traffic, which prevents phone-to-headset upload despite correct code and URL.
- Distributed multiplayer requires Unity Services project linking and internet access.
- Local face mirroring assumes local and networked Ready Player Me avatars use the same avatar URL so blendshape indices match.
- Quest media permissions can vary by Android version; app-owned imports remain available even when gallery-wide permission is denied.
- The phone upload path is implemented as a local lab-network prototype, not a remote upload service.

## Known Limitations

- No automated Unity test suite is present in the repository.
- Several research scenes are disabled in build settings and may require manual scene-specific validation.
- `M_CompanionLiveViewer` is intentionally inactive in the current implementation.
- Phone browser upload cannot browse a live camera roll beyond files selected by the browser picker.
- WebRTC phone mirroring depends on external companion-side behavior that is not fully represented in this Unity repository.
- Shared media transfer uses chunked RPCs and is constrained by Unity Transport payload limits and scene network state.

## Extension Points

- Add a remote media backend while preserving the local upload page contract.
- Expand `M_CompanionLiveViewer` once the companion live-view path is defined.
- Add Unity Test Runner coverage for upload parsing, gallery item identity, and shared-media chunk reconstruction.
- Consolidate disabled scene variants once the active study/demo scene set is finalized.
- Add build automation for Quest APK generation after lab deployment settings stabilize.

## Maintenance Considerations

Project-owned scripts should keep the standardized header format and concise comments that explain integration behavior. Imported SDKs, package samples, generated project files, and cache/build artifacts should remain separate from project cleanup work. Documentation should be updated when scene startup order, ports, persistent storage paths, or multiplayer service assumptions change.
