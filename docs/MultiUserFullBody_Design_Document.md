# MultiUserFullBody

**Media Upload Pipeline and Avatar System Design Document**

**Phone Web Upload, Pairing Code, Quest Scene Display, and Avatar/Facial System Architecture**

| Metadata | Value |
| --- | --- |
| To | Dr. Lei Zhang, Kennesaw State University |
| From | Mason Prather, Graduate Research Assistant |
| Last Updated | June 1, 2026 |
| Version | 1.5 |
| Primary Audience | Supervisor review and future Graduate Research Assistant maintenance |
| Primary Demo Scene | Assets/_Scenes/PhonePhotoUpload.unity |
| Unity Version | 6000.3.8f1, verified from ProjectSettings/ProjectVersion.txt. |

**Purpose:** This document describes the current supervisor-review deliverable and related system architecture for the MultiUserFullBody Unity project. The active review path is the Quest-hosted media upload pipeline, including the phone web upload page, pairing-code workflow, local browser upload, persistent storage, and Unity/Quest scene display. The document also records the Avatar and Facial System developed during earlier project iterations, including Ready Player Me avatar loading, FinalIK embodiment, Quest Pro eye/facial tracking, networked avatar experiments, and Vivox/social presence support.

## Revision History

| Version | Date | Author | Summary |
| --- | --- | --- | --- |
| 1.5 | June 1, 2026 | Mason Prather | Documented the Quest Pro logcat input-system fix and launch-visible Quest user menu behavior. |
| 1.4 | June 1, 2026 | Mason Prather | Documented the Quest user menu, real Meta XR passthrough repair, legacy generated-control opt-in behavior, and Quest Pro validation checklist. |
| 1.3 | May 29, 2026 | Mason Prather | Documented the shared-media synchronization diagnostic and the minimal Netcode RPC target fix for Distributed Authority sessions. |
| 1.2 | May 28, 2026 | Mason Prather | Integrated supervisor feedback by documenting both the active media upload pipeline and the Avatar and Facial System; moved early phone-interaction prototypes into media upload design history. |
| 1.1 | May 22, 2026 | Mason Prather | Refocused the design document around the active web-based media upload pipeline and separated legacy script inventories into an appendix. |

## Table of Contents

- 1. Executive Summary
- 2. Project Areas
- 3. Document Scope
- 4. Current Project Status
- 5. Media Upload Pipeline Design Documentation
- 6. Media Upload Supervisor Review and Success Criteria
- 7. Media Upload Runtime Flow and System Diagrams
- 8. Media Upload Configuration, Storage, Logging, and Security
- 9. Early Media Upload and Phone-Interaction Experiments
- 10. Last Validated / Validation Status
- 11. Avatar and Facial System
- 12. Dependencies and Repository Map
- 13. Known Limitations and Maintenance Guidance
- Appendix A: Deprecated and Retained Script Inventory

## 1. Executive Summary

MultiUserFullBody currently includes an active Quest-based Media Upload Pipeline. In the current supervisor-review workflow, the Quest hosts a local upload page, the headset displays an upload URL and pairing code, a phone browser on the same local network submits an image, the upload is stored under Application.persistentDataPath/Uploads, and the newest image appears inside the Unity/Quest scene.

MultiUserFullBody also includes an earlier Avatar and Facial System. That subsystem used Ready Player Me avatars, FinalIK/VRIK binding to headset and controller anchors, local and network avatar representation, Quest Pro eye/facial expression support, optional lip-sync and face-network replication experiments, and Vivox/social presence support. The Avatar and Facial System is inactive for the current deliverable because the project scope shifted to the media upload workflow, and the RPM/FinalIK/Quest Pro tracking stack introduced dependency, access/licensing, maintenance, and validation overhead that is not required for the active supervisor demo. This is a project-specific deprecation decision, not a universal claim that RPM or Unity avatar workflows are unusable in every context.

## 2. Project Areas

| Project Area | Status | Purpose | Primary Scene / Assets | Main Output |
| --- | --- | --- | --- | --- |
| Project Area 1: Media Upload Pipeline Design Documentation | Active / Primary Supervisor Demo | Browser-based phone-to-Quest media transfer. | Assets/_Scenes/PhonePhotoUpload.unity | Uploaded image displayed in the Unity/Quest scene. |
| Project Area 2: Avatar and Facial System Design Documentation | Documented / Inactive for Current Media Upload Demo | Full-body/social avatar representation using RPM, FinalIK, Quest Pro tracking, optional networking, and Vivox/social presence support. | EyeFaceTrackingScene variants and avatar/network prefabs | Local/remote avatars with IK-driven body pose and facial expression mapping when restored and validated. |

The second project area is included because it represents substantial prior research/development work and supervisor feedback requested clearer technical documentation for future maintenance and deprecation decisions.

## 3. Document Scope

- In scope: browser-based phone-to-Quest media upload, pairing-code workflow, persistent upload storage, Unity display bridge, supervisor review path, and documentation of the Avatar and Facial System developed during earlier project iterations.
- Avatar documentation scope: Ready Player Me avatar loading, FinalIK/VRIK embodiment, Quest Pro eye/facial tracking, local and network avatar setup, face/lip-sync/network replication experiments, and Vivox/social presence support where present in the repository.
- Out of active demo scope but documented for handoff: RPM avatar loading/embodiment, RPM face tracking, Vivox/social presence, broad multiplayer/template systems, phone mirroring/WebRTC experiments, and old app-side companion upload clients.
- Repository basis: ProjectSettings, Packages/manifest.json, Assets/_Scenes, Assets/_Scripts, Assets/FromCompanion, Assets/VRMPAssets, Assets/_Prefabs, README.md, and docs/.

## 4. Current Project Status

The status summary is split by project area so the active media upload deliverable is not confused with the documented avatar/social-presence architecture.

### 4.1 Media Upload Pipeline Status

| System | Status | Notes |
| --- | --- | --- |
| Phone Web Upload Page | Active / Primary Demo | Quest hosts the local browser upload page through M_ServerBootstrap and M_SimpleHttpServer. |
| Pairing Code Pipeline | Active / Primary Demo | M_ServerBootstrap generates or loads the code; M_SimpleHttpServer validates browser form submissions. |
| Phone Media Upload | Active / Primary Demo | Phone uses a standard browser on the same local network. No phone Unity companion app is required. |
| Uploaded Image Display | Active / Primary Demo | M_PhoneUploadToDisplay loads the newest saved upload and applies it to M_QuestPhotoDisplay. |
| Quest Gallery Import | Supporting / Optional | Gallery scripts and MediaUploadSetup support headset-side image import, but they are not required for the core phone browser upload. |
| Multiplayer Shared Media Sync | Optional / Patched; Requires Two-Quest Validation | M_NetworkedPhotoSync can broadcast media through XRINetworkPlayer when an owning local network player is spawned. A May 29, 2026 patch corrected the remote relay RPC target for Distributed Authority sessions. |
| Companion App Upload Flow | Deprecated Early Experiment | Unity phone/client upload scripts remain in Assets/FromCompanion/Scripts/Client as historical support code. |
| Phone Mirroring / WebRTC | Deprecated Early Experiment | Phone mirroring scripts remain in Assets/_Scripts/PhoneMirror and are not part of the current media upload demo. |
| Automated Tests | Manual Validation Required | No repository-level Unity test runner or dated automated result set was found for the active workflows. |

### 4.2 Avatar and Facial System Status

| System | Status | Notes |
| --- | --- | --- |
| Ready Player Me Avatar Loading | Documented / Inactive for Current Demo | RPM package and avatar scripts remain in the repository; avatar loading is not part of the active media upload path. |
| FinalIK / VRIK Avatar Embodiment | Documented / Inactive for Current Demo | RootMotion FinalIK is present and M_LocalAvatarManager/M_NetAvatar configure VRIK targets when avatar workflows are restored. |
| Local Avatar Setup | Documented / Inactive for Current Demo | M_LocalAvatarManager loads an RPM avatar, parents it under a local avatar root, hides first-person head meshes, and binds OVR/XR anchors. |
| Network Avatar Setup | Documented / Inactive for Current Demo | M_NetPlayer, M_NetPoseDriver, and M_NetAvatar provide avatar URL and head/hand pose replication paths. |
| Quest Pro Eye/Face Tracking | Documented / Inactive for Current Demo | M_LocalFaceDriver reads OVRFaceExpressions and maps expression values to RPM-compatible blendshapes. |
| Face Blendshape Mapping | Documented / Inactive for Current Demo | Mapping includes ARKit-style RPM blendshape names, smoothing, gain, neutral thresholds, and neutral relaxation. |
| Lip-sync / Face Sync Experiments | Documented / Inactive for Current Demo | M_OVRLipSyncAutoBinder, M_NetFaceMirror, M_FaceDebugProbe, and FaceSyncData remain available for experiment restoration. |
| Vivox Voice / Social Presence | Retained / Requires Separate Validation | Vivox package and scripts exist for social/avatar/multiplayer presence, but Vivox is not required for the media upload supervisor demo. |
| Broad VRMP Multiplayer Assets | Retained Template / Legacy Support | VRMP managers, network player prefabs, and room assets remain in the project and should be validated separately before use. |

## 5. Media Upload Pipeline Design Documentation

The Media Upload Pipeline is the current active deliverable. Its design keeps the phone side lightweight: the Quest hosts the page and endpoint, while the phone acts only as a local-network browser client.

### 5.1 System Purpose

The pipeline supports no-install phone-to-Quest image transfer for supervisor review. It lets a reviewer open the active scene, use the launch-visible headset-side Quest user menu, read the displayed URL and pairing code, upload a phone image through a browser, and see that image appear in the Unity/Quest scene.

### 5.2 Active Components

- M_ServerBootstrap starts the HTTP server, prepares persistent upload storage, resolves local network addresses, generates or loads the pairing code, publishes headset instructions, and starts UDP discovery beacons.
- M_SimpleHTTPServer.cs contains the M_SimpleHttpServer class, which serves the browser upload page and handles /upload-photo, /ping, and /status.
- M_QuestUserMenu presents the launch-visible and summonable headset-side menu for phone pairing instructions, passthrough, recentering, and dismissal. Its editor keyboard fallback uses the Input System so Quest builds configured for new input do not call Unity's legacy Input API.
- M_PhoneImportHeadsetMode supplies phone pairing text for the menu and retains optional prompt/pairing mode behavior for scenes that explicitly opt into it.
- M_PhoneUploadToDisplay observes M_SimpleHttpServer.LastSavedPhotoPath, reads the saved image file, decodes it into a Texture2D, and passes it to M_QuestPhotoDisplay.
- M_QuestPhotoDisplay applies the latest uploaded texture to a RawImage or renderer material and updates status/file labels.

### 5.3 Current Active Script Map

| Script / Asset | Active Role | Required? | Notes |
| --- | --- | --- | --- |
| Assets/_Scenes/PhonePhotoUpload.unity | Primary review/demo scene and only enabled build scene. | Yes | Use this scene for the current media upload review. |
| Assets/FromCompanion/Scripts/Server/M_ServerBootstrap.cs | Starts the HTTP server, creates/verifies upload storage, publishes URL/code instructions, and adds UDP discovery. | Yes | Default ports: HTTP 8080, UDP discovery 7777. |
| Assets/FromCompanion/Scripts/Server/M_SimpleHTTPServer.cs | Serves the Quest-hosted upload page and handles /upload-photo, /ping, and /status. | Yes | File uses HTTP capitalization; class/runtime prefix is M_SimpleHttpServer. |
| Assets/FromCompanion/Scripts/Server/M_NetworkDiscoveryServer.cs | Broadcasts PHOTO_SERVER UDP beacons for upload-server discovery. | Supporting | Added at runtime by M_ServerBootstrap. Direct local IP URL remains the primary path. |
| Assets/_Scripts/Menus/M_QuestUserMenu.cs | Displays the launch-visible and summonable headset menu with URL/code, passthrough, recenter, and close controls. | Yes | Primary user-facing headset UI for the phone upload workflow. |
| Assets/_Scripts/MediaUpload/M_PhoneImportHeadsetMode.cs | Supplies headset URL/code text and retains optional passthrough pairing mode. | Supporting | Separate prompt generation is opt-in and disabled in the active PhonePhotoUpload scene. |
| Assets/_Scripts/MediaUpload/M_PhoneUploadToDisplay.cs | Watches LastSavedPhotoPath, reads the saved upload, decodes it, and routes it to display. | Yes | Can refresh gallery and optionally broadcast through M_NetworkedPhotoSync. |
| Assets/_Scripts/MediaUpload/M_QuestPhotoDisplay.cs | Applies uploaded textures to a RawImage or renderer material and updates status labels. | Yes | Referenced through the MediaUploadSetup display path. |
| Assets/_Prefabs/MediaUploadSetup.prefab | Provides media display, gallery, and related scene support components. | Yes | Contains display/gallery-related media upload components. |
| Assets/_Scripts/MediaUpload/M_PassthroughModeController.cs | Controls real Meta XR passthrough, camera transparency, scene renderer visibility, and restoration. | Supporting | Required for validating passthrough; not required to prove raw upload behavior. |
| Assets/_Scripts/MediaUpload/M_PassthroughMountedToggleUI.cs | Legacy opt-in headset-mounted passthrough toggle. | Optional | Disabled in the active PhonePhotoUpload scene. |
| Assets/_Scripts/MediaUpload/M_QuestGalleryController.cs | Maintains gallery state and can refresh after a phone upload. | Supporting | Secondary to the browser upload path. |
| Assets/_Scripts/MediaUpload/ImagePicker.cs | Supports direct headset image picking for gallery/import workflows. | Optional | Secondary gallery import path. |
| Assets/_Scripts/MediaUpload/M_NetworkedPhotoSync.cs | Optional shared-media bridge to XRINetworkPlayer. | Optional | Sends encoded media payloads through the owning local XRINetworkPlayer and receives reconstructed remote payloads for display. |
| Assets/VRMPAssets/Scripts/Network/NetworkPlayer/XRINetworkPlayer.cs | Optional shared-media network relay. | Optional | Chunks selected image bytes, receives upload chunks on the authoritative player object, and relays reconstructed chunks with Distributed Authority-compatible RPC targets. |

### 5.4 Supporting and Optional Media Upload Components

Supporting components extend the media upload pipeline without changing the core browser upload requirement. Quest gallery import, passthrough pairing support, and optional shared-media sync can improve demonstrations or future experiments, but the supervisor demo succeeds when the phone browser upload reaches the local scene display.

### 5.5 Multiplayer Shared Media Sync Diagnostic and Fix

The shared-media feature is separate from the core phone-browser upload demo, but it is intended to let multiple connected Quest Pro users see the same selected image once a user imports or shares media. The traced flow is:

1. `ImagePicker` or `M_QuestGalleryController` loads or selects a local image.
2. `M_QuestPhotoDisplay` updates the uploading user's local display immediately.
3. `M_NetworkedPhotoSync` encodes image bytes and calls `XRINetworkPlayer.BroadcastSharedMedia` on the spawned local owning player.
4. `XRINetworkPlayer` chunks the payload, receives the chunks on the authoritative player object, reconstructs the image, caches it as the latest shared media, and relays it to connected clients.
5. Remote clients raise `XRINetworkPlayer.onSharedMediaReceived`; `M_NetworkedPhotoSync` decodes the bytes and applies the texture to the remote `M_QuestPhotoDisplay`.
6. Later joiners clear stale local display state, then receive the cached latest image after their local player object spawns.

The observed bug was that step 2 worked but steps 4-5 did not reach the other connected headset. Each user therefore saw only the image they had already applied locally.

Root cause: the reconstructed-image download RPCs in `XRINetworkPlayer` were using relay targets that depended on authority exclusion semantics. That worked for some owner/non-owner cases, but it was the wrong shape for shared session state: the image should go to the connected clients and host, and late joiners need an explicit replay of the latest shared image. The old target could leave a headset outside the intended relay path even though the local display path had already succeeded.

Minimal fix: keep the existing chunking, ownership validation, event subscription, and display code, but change the reconstructed-image relay methods to `[Rpc(SendTo.SpecifiedInParams)]`. Normal relays target `ClientsAndHost`; late-join replay targets the joining owner only. The server-side player object caches a copy of the latest shared image bytes and sends that cached payload after a new owner spawns. `M_NetworkedPhotoSync` clears stale local display state before that replay can apply.

The relay also keeps transfer chunks below the active Unity Transport payload limit and paces chunks across frames. This reduces the chance that larger image RPC payloads overflow transport limits or saturate one frame while preserving the existing encoded-byte transfer model.

This change is intentionally narrow. It does not rewrite media storage, texture handling, gallery UI, or upload encoding. It corrects the network propagation target and adds retained latest-image replay so remote and later-joining clients can receive the shared media event.

## 6. Media Upload Supervisor Review and Success Criteria

### 6.1 Recommended Supervisor Review Path for Unity Project File

1. Open Assets/_Scenes/PhonePhotoUpload.unity.
2. Run the scene on the Quest/headset target or configured Quest runtime.
3. Confirm the Quest user menu opens automatically and displays a local upload URL and pairing code. If it is closed, reopen it with the left controller menu/start input.
4. Connect a phone to the same Wi-Fi/local network as the Quest.
5. Open the displayed local IP URL in the phone browser.
6. Enter the pairing code shown in the headset.
7. Select and upload a non-sensitive test image.
8. Confirm Unity saves the uploaded image under Application.persistentDataPath/Uploads.
9. Confirm the newest image appears in the VR scene display.
10. Check Unity console logs from M_ServerBootstrap, M_SimpleHttpServer, M_QuestUserMenu, M_PassthroughModeController, and M_PhoneUploadToDisplay.
11. Do not use RPM avatars, phone mirroring/WebRTC, Vivox, or old companion app flows as the main media upload demo path.

### 6.2 Demo Success Criteria

| Criterion | Expected Evidence |
| --- | --- |
| Headset displays upload URL | Quest user menu shows at least one local IP upload URL. |
| Headset displays pairing code | Quest user menu shows the active code for the current scene run. |
| Phone browser loads Quest-hosted page | Phone opens the displayed URL on the same local network. |
| Phone image upload succeeds | Browser upload returns success after the code is entered. |
| Upload is saved to persistent storage | Server logs or file inspection show storage under Application.persistentDataPath/Uploads. |
| Image appears in Unity/Quest scene | Newest uploaded image is visible on the scene display target. |
| Unity logs show pipeline events | Logs show server startup, upload receipt, saved path, and display update. |

## 7. Media Upload Runtime Flow and System Diagrams

### 7.1 Media Upload System Design Diagram

```text
[Phone Browser]
    |
    | 1. Open displayed local Quest URL
    | 2. Submit pairing code + selected image
    v
[Quest-Hosted Web Upload Server]
    M_ServerBootstrap
    M_SimpleHttpServer
    |
    | 3. Validate pairing code
    | 4. Save uploaded image
    v
[Persistent Upload Storage]
    Application.persistentDataPath/Uploads
    |
    | 5. Update LastSavedPhotoPath
    v
[Upload Display Bridge]
    M_PhoneUploadToDisplay
    |
    | 6. Read bytes + decode Texture2D
    v
[Unity/Quest Scene Display]
    M_QuestPhotoDisplay
    RawImage / Renderer Material
```

Caption: The phone does not require a Unity companion app. It acts as a browser client for the Quest-hosted upload page.

### 7.2 Runtime Data Flow Diagram

```text
Phone browser
    -> local Quest URL
    -> pairing code + image POST
    -> M_SimpleHttpServer /upload-photo
    -> pairing-code validation
    -> Application.persistentDataPath/Uploads
    -> M_SimpleHttpServer.LastSavedPhotoPath
    -> M_PhoneUploadToDisplay
    -> Texture2D decode
    -> M_QuestPhotoDisplay
    -> VR scene display
```

### 7.3 Active Runtime Sequence

1. Unity loads Assets/_Scenes/PhonePhotoUpload.unity.
2. The upload server/bootstrap component initializes.
3. The upload directory is created or verified under Application.persistentDataPath/Uploads.
4. A pairing code is generated or loaded from serialized configuration.
5. Local upload URL options and the pairing code are displayed in the headset UI.
6. A phone browser on the same local network opens the upload page.
7. The user selects and submits a non-sensitive image.
8. The server validates the submitted code and saves the image under persistent Uploads.
9. M_PhoneUploadToDisplay detects the saved file path through M_SimpleHttpServer.LastSavedPhotoPath.
10. The saved image is read, decoded into a Unity texture, and applied to the scene display.
11. Status text and Unity console logs update for review and debugging.

### 7.4 Optional Multiplayer Shared Media Flow

```text
[Local User Selects or Imports Image]
    |
    | local display update
    v
[M_QuestPhotoDisplay]
    |
    | encoded bytes
    v
[M_NetworkedPhotoSync]
    |
    | BroadcastSharedMedia on owning local player
    v
[XRINetworkPlayer]
    |
    | ServerRpc/authority upload chunks
    | reconstructed payload
    | cached latest image for late joiners
    | Rpc(SendTo.SpecifiedInParams) download chunks
    | ClientsAndHost for normal relay
    | Single(joining owner) for latest-image replay
    v
[Remote XRINetworkPlayer Instances]
    |
    | onSharedMediaReceived(fileName, bytes)
    v
[Remote M_NetworkedPhotoSync]
    |
    | decode Texture2D
    v
[Remote M_QuestPhotoDisplay]
```

Caption: Shared media sync does not synchronize Unity texture references or local file paths. It synchronizes encoded image bytes through the network player relay, then each client decodes and applies its own runtime texture. Joining clients clear stale local display state first, then receive the cached latest shared image if one exists in the active session.

## 8. Media Upload Configuration, Storage, Logging, and Security

### 8.1 Configuration (Unity editor, scripts)

| Item | Value | Notes |
| --- | --- | --- |
| Unity version | 6000.3.8f1 | Verified from ProjectSettings/ProjectVersion.txt. |
| Primary scene | Assets/_Scenes/PhonePhotoUpload.unity | Only enabled build scene in ProjectSettings/EditorBuildSettings.asset. |
| Other project scenes | MainScene, EyeFaceTrackingScene, EyeFaceTrackingScene_MediaUpload, MediaUpload | Present in build settings but disabled. |
| HTTP upload port | 8080 | M_ServerBootstrap.httpPort default. |
| UDP discovery port | 7777 | M_ServerBootstrap.discoveryPort default; M_NetworkDiscoveryServer broadcasts PHOTO_SERVER beacons. |
| Upload root | Application.persistentDataPath/Uploads | Created by M_ServerBootstrap and used by the HTTP server/display bridge. |
| Maximum upload size | 64 MB default | M_ServerBootstrap.maxUploadMegabytes default. |
| Pairing code | Required by default | M_ServerBootstrap.requirePairingCode default is enabled. |
| Legacy raw upload compatibility | Enabled by default | M_ServerBootstrap.allowLegacyRawUploadsWithoutCode preserves older raw endpoint clients. |
| Short display URL | Optional / usually empty | Only works if local DNS/routing maps the name to the current Quest IP. |

**Legacy raw upload compatibility note:** For supervisor demos that rely on pairing-code enforcement, `M_ServerBootstrap.allowLegacyRawUploadsWithoutCode` should be disabled unless intentionally testing backward compatibility. If enabled, older raw upload clients may bypass the normal browser pairing-code workflow.

### 8.2 Storage and Logging

Uploaded media is stored in Application.persistentDataPath/Uploads. The main diagnostic logs use [M_ServerBootstrap], [M_SimpleHttpServer], [M_QuestUserMenu], [M_PassthroughModeController], [M_PhoneUploadToDisplay], and [M_QuestPhotoDisplay] prefixes. Optional shared-media logs can also appear under [M_NetworkedPhotoSync] and XRINetworkPlayer when multiplayer sync is active.

### 8.3 Security, Privacy, and Network Assumptions

- The phone upload server is intended for trusted local lab networks only.
- Phone and Quest must be on the same local network, and that network must permit peer-to-peer traffic.
- The pairing code is lightweight access control, not full authentication.
- The system should not be used on public/open Wi-Fi without additional safeguards.
- Uploaded media should be cleared or controlled according to project review needs.
- Demo media should avoid sensitive participant images unless approved by the supervisor and any applicable study protocol.
- Deprecated phone mirroring/WebRTC scripts should not be treated as secure production infrastructure.

## 9. Early Media Upload and Phone-Interaction Experiments

These earlier phone-side experiments are documented as media upload design history. They are not current supervisor-demo requirements, but they explain why the current browser-based workflow is simpler and easier to maintain.

### 9.1 Companion App Upload Flow

- Former purpose: Unity-based phone/client upload path with UDP discovery and HTTP upload helper code.
- Problem it tried to solve: phone-to-Quest media transfer before the no-install browser workflow was established.
- Why replaced: browser-based upload avoids app installation, reduces phone-side maintenance, and is easier to demonstrate on a local network.
- Current status: deprecated early experiment. Server-side scripts remain active where they support the current browser upload path; app-side client scripts remain for traceability only.

### 9.2 Phone Mirroring / WebRTC Prototype

- Former purpose: LAN discovery/signaling, pairing code, WebRTC video reception, and VR ray-to-phone-panel input forwarding.
- Problem it tried to solve: richer phone-to-Quest interaction and live phone mirroring.
- Why replaced: the current deliverable only needs browser upload, not live mirroring or touch relay.
- Current status: deprecated early experiment. Retained for repository traceability only and not part of the current supervisor-review workflow.

## 10. Last Validated / Validation Status

The repository does not contain dated automated test results for the workflows below. These tables record the current validation posture and what should be checked manually before a supervisor review.

### 10.1 Media Upload Validation Status

| Workflow | Validation Status | Scene / Device Context | Notes |
| --- | --- | --- | --- |
| Phone upload page loads | Manual Validation Required | PhonePhotoUpload; Quest and phone on same local network | Open the displayed local URL from the phone browser. |
| Pairing code displayed | Manual Validation Required | Quest user menu in PhonePhotoUpload | Confirm the menu shows the current code and upload URL. |
| Image upload from phone browser | Requires Quest Smoke Test | Phone browser to Quest-hosted /upload-photo | Submit a non-sensitive test image with the displayed code. |
| Uploaded image saved to persistent storage | Manual Validation Required | Application.persistentDataPath/Uploads | Check Unity logs or filesystem access when available. |
| Uploaded image appears in scene | Requires Quest Smoke Test | M_PhoneUploadToDisplay to M_QuestPhotoDisplay | Confirm the VR display and status text update. |
| Quest gallery image import | Optional / Requires Separate Validation | MediaUploadSetup prefab and gallery scripts | Secondary path; validate only when in scope. |
| Multiplayer shared media sync | Optional / Patched; Requires Two-Quest Validation | Requires two connected Quest Pro clients with spawned owning XRINetworkPlayer instances | Share from each headset in turn and confirm the other headset receives and displays the image. |
| Early companion app upload flow | Deprecated Early Experiment | Assets/FromCompanion/Scripts/Client | Do not use as current demo success criterion. |
| Phone mirroring / WebRTC prototype | Deprecated Early Experiment | Assets/_Scripts/PhoneMirror | Do not use as current demo success criterion. |

### 10.2 Avatar and Facial System Validation Status

| Workflow | Validation Status | Scene / Device Context | Notes |
| --- | --- | --- | --- |
| RPM avatar loading | Documented / Inactive for Current Demo | M_LocalAvatarManager / M_NetAvatar | Requires package/API and avatar URL validation before reactivation. |
| FinalIK/VRIK binding | Not Recently Validated | RootMotion FinalIK with OVR/XR anchors | Confirm VRIK references, targets, first-person visibility, and solver behavior. |
| Local avatar setup | Documented / Inactive for Current Demo | EyeFaceTrackingScene variants | Disabled build scenes contain local avatar manager references. |
| Network avatar setup | Documented / Inactive for Current Demo | Assets/_Prefabs/NetworkedPlayer.prefab | Prefab includes M_NetPlayer, M_NetPoseDriver, M_NetAvatar, and M_NetFaceMirror. |
| Head/hand pose replication | Not Recently Validated | M_NetPoseDriver NetworkVariables | Requires Netcode session validation. |
| Quest Pro face-expression mapping | Not Recently Validated | OVRFaceExpressions to M_LocalFaceDriver | Requires Quest Pro or compatible face-expression source. |
| RPM blendshape mapping | Not Recently Validated | M_LocalFaceDriver channel map | Confirm target avatar exposes expected ARKit/RPM blendshape names. |
| Lip-sync binding | Not Recently Validated | M_OVRLipSyncAutoBinder and OVRLipSyncContextMorphTarget | Binder exists; no recent validation result found. |
| Face/network sync | Not Recently Validated | M_NetFaceMirror / FaceSyncData | Requires owner/remote Netcode test with matching avatar blendshape layouts. |
| Vivox voice/social presence | Retained / Requires Separate Validation | M_VivoxManager and VoiceChatManager | Validate Unity Services, authentication, microphone permission, and channel joining before review use. |

## 11. Avatar and Facial System

### 11.1 System Purpose

The Avatar and Facial System was designed to support embodied multi-user VR presence. It used Ready Player Me avatars as user avatar assets, FinalIK/VRIK to bind avatars to XR headset and controller targets, Quest Pro/OVR expression sources for face and eye-related expression mapping, and Netcode/Vivox components for networked avatar presentation and social presence.

### 11.2 Current Status

This subsystem is documented but inactive for the current media upload deliverable. It is retained for technical handoff, future reference, and repository traceability. It should not be presented as part of the current supervisor demo unless RPM loading, FinalIK/VRIK binding, Quest Pro tracking, Netcode state, and Vivox behavior are separately restored and validated.

### 11.3 Avatar System Components

| Script / Asset | Former Role | Current Status | Notes |
| --- | --- | --- | --- |
| Assets/_Scripts/Player/M_LocalAvatarManager.cs | Loads local RPM avatar, parents it under local avatar root, binds VRIK to OVR/XR anchors, hides first-person head meshes, and initializes face diagnostics. | Documented / inactive for current media upload demo | Uses PlayerPrefs RPM_URL with a fallback Ready Player Me URL requesting ARKit and Oculus Visemes morph targets. |
| Assets/_Scripts/Player/M_NetPlayer.cs | Stores owner-written network avatar URL in a Netcode NetworkVariable. | Documented / inactive for current media upload demo | Provides replicated avatar identity for network avatar loading. |
| Assets/_Scripts/Player/M_NetPoseDriver.cs | Publishes owner head/hand pose NetworkVariables and applies smoothed pose values to remote IK targets. | Documented / inactive for current media upload demo | Auto-finds OVRCameraRig anchors or XR Origin-style fallback object names. |
| Assets/_Scripts/Player/M_NetAvatar.cs | Loads networked RPM avatar, wires VRIK to replicated IK targets, binds face mesh to M_NetFaceMirror, and suppresses owner-local renderers. | Documented / inactive for current media upload demo | Referenced by Assets/_Prefabs/NetworkedPlayer.prefab. |
| Assets/_Scripts/Player/M_LocalFaceDriver.cs | Maps OVRFaceExpressions values to RPM/ARKit-style blendshape weights with gain, smoothing, and neutral relaxation. | Documented / inactive for current media upload demo | Dynamically attached by M_LocalAvatarManager after local avatar load. |
| Assets/_Scripts/Player/M_NetFaceMirror.cs | Samples owner local face weights, sends compact snapshots through ServerRpc/ClientRpc, and applies smoothed remote weights. | Documented / inactive for current media upload demo | Assumes local and network avatar blendshape indices align. |
| Assets/_Scripts/Player/M_OVRLipSyncAutoBinder.cs | Maps OVR lip-sync visemes and laughter to RPM blendshape indices. | Documented / inactive for current media upload demo | Helper exists for restored lip-sync workflows. |
| Assets/_Scripts/Player/M_FaceDebugProbe.cs | Collects neutral-expression diagnostics and logs blendshape summaries for calibration. | Documented / inactive for current media upload demo | Referenced by EyeFaceTrackingScene variants. |
| Assets/_Scripts/Network/FaceSyncData.cs | Serializes facial blendshape weights and an eye-forward vector for experimental face sync paths. | Documented / inactive for current media upload demo | Retained as a legacy data container beside M_NetFaceMirror. |
| Assets/_Scripts/Network/M_VivoxManager.cs | Initializes Unity Services/Vivox, joins/leaves positional voice channels, and manages a local Vivox audio tap. | Retained / requires separate validation | Referenced by EyeFaceTrackingScene variants. |
| Assets/VRMPAssets/Scripts/Network/NetworkManagers/VoiceChatManager.cs | Manages Vivox connection state, microphone permission, participant dictionaries, mute state, and positional voice settings. | Retained / requires separate validation | Part of the VRMP multiplayer/social presence layer. |
| Assets/_Prefabs/NetworkedPlayer.prefab | Networked avatar/player prefab containing the project avatar URL, pose, avatar presentation, and face mirror components. | Documented / inactive for current media upload demo | Contains M_NetPlayer, M_NetPoseDriver, M_NetAvatar, and M_NetFaceMirror references. |
| Assets/_Scenes/EyeFaceTrackingScene.unity and variants | Earlier avatar/face tracking scenes used for RPM, face diagnostics, Vivox, and media upload experiments. | Disabled in build settings | Present for reference; PhonePhotoUpload remains the primary enabled scene. |

### 11.4 Avatar Loading and FinalIK Design

M_LocalAvatarManager uses ReadyPlayerMe.Core AvatarObjectLoader with ARKit and Oculus Visemes morph targets. It reads PlayerPrefs RPM_URL or a serialized fallback URL, loads the avatar, parents the resulting GameObject under the local avatar root, sets animator behavior, optionally hides first-person head/eye/teeth/hair meshes, and configures VRIK targets from OVRCameraRig anchors or XR Origin-style fallbacks. Network presentation uses M_NetPlayer for the avatar URL, M_NetPoseDriver for replicated head/hand target poses, and M_NetAvatar to load the remote avatar and bind VRIK to replicated targets rather than the owner local rig hierarchy.

### 11.5 Eye and Facial Tracking Design

M_LocalFaceDriver reads OVRFaceExpressions values and maps them to RPM/ARKit-style blendshape names for jaw, mouth, cheeks, eyelids, eye-look directions, brows, and related expression channels. The driver applies configurable global gain, per-channel gain, smoothing, maximum weight clamps, per-channel cutoffs, and neutral-pose relaxation. M_FaceDebugProbe collects neutral samples and logs min/max/average blendshape behavior for calibration. M_OVRLipSyncAutoBinder can map OVR lip-sync visemes and laughter to RPM blendshape indices when a lip-sync workflow is restored.

### 11.6 Networked Avatar and Face Sync Design

M_NetPlayer stores the replicated RPM avatar URL, M_NetPoseDriver publishes owner head/hand poses through Netcode NetworkVariables, and M_NetAvatar loads each avatar and binds VRIK to replicated IK targets. M_NetFaceMirror samples owner-side local face blendshape weights, scales and thresholds them, sends snapshots through RPCs, and applies smoothed weights to network avatar face meshes. FaceSyncData remains as a legacy serializable payload for facial weights and eye-forward vectors. The current status is retained network avatar architecture, not active behavior in the PhonePhotoUpload review scene.

### 11.7 Vivox and Social Presence Support

Vivox belongs with the avatar/social presence work rather than the media upload status table. The repository contains Unity Services Vivox in Packages/manifest.json, project code in M_VivoxManager, and VRMP voice support in VoiceChatManager. These scripts initialize Unity Services/Vivox, manage channel membership, microphone permission, participant voice state, and positional audio. Vivox is retained for social/avatar/multiplayer presence and requires separate validation before being used in a review; it is not required for the browser-based media upload demo.

### 11.8 Avatar and Facial System Deprecation Justification

The Avatar and Facial System is inactive for the current deliverable because the project scope shifted to the media upload workflow, and the RPM/FinalIK/Quest Pro face-tracking stack introduced dependency, access/licensing, maintenance, and validation overhead that was not required for the active supervisor demo. This deprecation is project-specific and should not be read as a universal claim that Ready Player Me, FinalIK, or Unity avatar workflows are unusable in all contexts. Future maintainers should only reactivate this subsystem after verifying package availability, RPM avatar loading, FinalIK/VRIK binding, Quest tracking, Vivox state, blendshape mapping, prefab references, and Netcode ownership/state behavior.

### 11.9 Avatar and Facial System Diagrams

#### Diagram 1: Avatar Loading and IK Binding

```text
[Ready Player Me Avatar Source / URL]
    |
    v
[M_LocalAvatarManager]
    |
    | Loads avatar model
    v
[Local Avatar Root]
    |
    +-- [FinalIK / VRIK Solver]
    |       |
    |       +-- Head Target <- XR Camera / Headset Anchor
    |       +-- Left Hand Target <- Left Controller Anchor
    |       +-- Right Hand Target <- Right Controller Anchor
    |
    v
[Embodied Local Avatar]
```

Caption: This design allowed a downloaded RPM avatar to be bound to headset and hand targets through FinalIK/VRIK.

#### Diagram 2: Eye and Facial Tracking Pipeline

```text
[Quest Pro / OVR Tracking Sources]
    |
    +-- OVRFaceExpressions
    |       |
    |       v
    |   [M_LocalFaceDriver]
    |       |
    |       +-- smoothing / gain / neutral relaxation
    |       v
    |   [RPM Face Blendshapes]
    |
    +-- Optional OVR Lip Sync
            |
            v
        [M_OVRLipSyncAutoBinder]
            |
            v
        [RPM Viseme / Laughter Blendshapes]
```

Caption: This design connected Quest Pro expression data and optional lip-sync data to RPM-compatible face blendshapes.

#### Diagram 3: Networked Avatar Replication

```text
[Owning Player XR Rig]
    |
    +-- Head / Hand Anchors
    |       |
    |       v
    |   [M_NetPoseDriver]
    |       |
    |       v
    |   Netcode pose variables
    |
    +-- Avatar URL
            |
            v
        [M_NetPlayer]

Remote Client:
[Netcode pose variables + avatar URL]
    |
    v
[M_NetAvatar]
    |
    +-- Load RPM avatar
    +-- Bind FinalIK/VRIK to replicated IK targets
    +-- Optional M_NetFaceMirror binding
    v
[Remote Network Avatar]
```

Caption: The remote avatar design separated owner tracking from remote presentation by replicating pose targets and avatar identity.

## 12. Dependencies and Repository Map

### 12.1 Dependency Posture

| Dependency Area | Current Posture | Notes |
| --- | --- | --- |
| Meta XR SDK / OVR / XR Interaction Toolkit | Active Quest runtime support and historical Quest Pro tracking support | Supports current headset runtime/passthrough work and earlier OVRFaceExpressions avatar experiments. |
| UGUI and TextMesh Pro | Active UI support | Upload prompt, status labels, browser instructions, and display UI. |
| .NET sockets / Unity networking modules | Active support | Custom HTTP server uses sockets; Unity networking modules support runtime network behavior. |
| NativeGallery | Supporting / Optional | Used by headset gallery import and old companion client image-picking paths. |
| Netcode and Unity Services Multiplayer | Optional for media upload; avatar/network replication support in earlier project area | Supports optional shared media and earlier network avatar pose/face replication. |
| Ready Player Me, glTFast, DracoUnity, RootMotion FinalIK | Avatar and Facial System dependency set / inactive for current media upload demo | Retained because avatar-system scripts/assets remain in the repository; required only if avatar workflows are reactivated. |
| Vivox | Avatar/social presence support / requires separate validation | Moved into Avatar and Facial System documentation; not required for the media upload demo. |
| Unity WebRTC | Early phone-interaction experiment dependency | Retained for the phone mirroring/WebRTC prototype, not active media upload behavior. |

### 12.2 Repository Source Map

| Path | Role |
| --- | --- |
| Assets/_Scenes/PhonePhotoUpload.unity | Primary active scene for supervisor review and only enabled build scene. |
| Assets/_Scenes/EyeFaceTrackingScene.unity and variants | Earlier avatar/face tracking and mixed experiment scenes; disabled in build settings. |
| Assets/FromCompanion/Scripts/Server/ | Active Quest-side upload server scripts and server-scene diagnostics. |
| Assets/_Scripts/MediaUpload/ and Assets/_Scripts/Menus/ | Active display bridge, photo display, Quest user menu, passthrough support, optional gallery import, and optional network sync bridge. |
| Assets/_Prefabs/MediaUploadSetup.prefab | Scene-referenced media display and gallery prefab. |
| Assets/_Scripts/Player/ | Avatar and facial system scripts for RPM loading, VRIK binding, pose sync, face mapping, and lip-sync helpers. |
| Assets/_Scripts/Network/ | Project network helpers, including FaceSyncData and M_VivoxManager. |
| Assets/_Prefabs/NetworkedPlayer.prefab | Project network avatar/player prefab containing avatar and face mirror components. |
| Assets/FromCompanion/Scripts/Client/ | Deprecated app-side companion upload scripts retained for early experiment traceability. |
| Assets/_Scripts/PhoneMirror/ | Deprecated phone mirroring/WebRTC prototype scripts. |
| Assets/VRMPAssets/ | Retained multiplayer/template room assets, managers, voice chat, and network player support. |
| Packages/manifest.json | Unity package dependencies. |
| docs/ | Repository documentation and this design document. |

### 12.3 Related Repository Documentation

- `README.md`: project overview and quick start.
- `docs/SYSTEM_DESIGN.md`: broader architecture notes.
- `docs/SCRIPT_REFERENCE.md`: script reference organized by subsystem.
- `docs/SETUP_AND_USAGE.md`: setup, run workflows, startup order, and demonstration notes.
- `docs/TROUBLESHOOTING.md`: startup, network, dependency, and runtime diagnostics.
- `Assets/FromCompanion/PHONE_UPLOAD.md`: focused notes for the current phone photo upload flow.

## 13. Known Limitations and Maintenance Guidance

### 13.1 Media Upload Pipeline Limitations

- The upload server is intended for trusted local lab networks only.
- Phone and Quest must be on the same local network, and that network must permit peer-to-peer traffic.
- The pairing code is lightweight access control, not full authentication.
- Uploaded files remain in Application.persistentDataPath/Uploads until manually cleared or handled by future cleanup tooling.
- No automated Unity test suite or repository-level validation runner was found for the active workflows.
- Optional shared-media sync and gallery import require separate validation before being presented as active deliverables.
- Shared-media sync now relays with `Rpc(SendTo.SpecifiedInParams)` to `ClientsAndHost` and replays the cached latest image to late joiners, but still needs a two-Quest runtime test to confirm behavior over the target session service and Wi-Fi network.

### 13.2 Media Upload Maintenance Guidance

- Treat M_ServerBootstrap, M_SimpleHttpServer, M_QuestUserMenu, M_PassthroughModeController, M_PhoneImportHeadsetMode, M_PhoneUploadToDisplay, and M_QuestPhotoDisplay as the primary active maintenance surface.
- Preserve serialized Unity references, scene paths, prefab paths, and public script names unless performing a planned migration.
- Re-test upload URL display, code validation, file saving, texture decoding, and scene display after any upload pipeline change.
- Keep legacy raw upload compatibility disabled for code-enforced demos unless intentionally testing backward compatibility.
- For multiplayer shared media, preserve the explicit-target relay in `XRINetworkPlayer`; reverting to the old `NotAuthority` targeting can skip the object owner in Distributed Authority sessions and break late-join state replay.
- Record Unity version, Quest device, phone OS/browser, Wi-Fi network, and Unity console logs when reporting issues.

### 13.3 Avatar and Facial System Limitations

- RPM/FinalIK/avatar tracking should not be presented as active without separate validation.
- Ready Player Me package/API availability, private avatar access, and licensing constraints must be checked before restoring avatar loading.
- FinalIK/VRIK, Quest Pro tracking, Vivox, and Netcode ownership behavior create a larger validation surface than the current media upload workflow requires.
- Face blendshape mapping depends on the selected avatar exposing compatible blendshape names and indices.
- Vivox/social presence requires Unity Services authentication, microphone permission, channel configuration, and device-level audio validation.

### 13.4 Avatar and Facial System Maintenance Guidance

- Before reactivation, verify RPM package/API availability, avatar URL loading, FinalIK/VRIK dependencies, Quest Pro face tracking, blendshape mapping, prefab references, Vivox state, and Netcode ownership/state behavior.
- Preserve avatar scripts and prefabs unless a deliberate removal or migration is planned.
- Document package/API changes if Ready Player Me, FinalIK, Vivox, or Quest Pro tracking integration is restored.
- Use the disabled EyeFaceTrackingScene variants and NetworkedPlayer prefab as reference points, not as automatically validated demo scenes.

### 13.5 Recommended Manual Validation Before Sharing

- Run Assets/_Scenes/PhonePhotoUpload.unity on Quest.
- Verify the Quest user menu shows the upload URL and current pairing code.
- Upload a non-sensitive test image from a phone browser on the same local network.
- Confirm the image file is saved under persistent Uploads and appears in the VR display.
- For optional multiplayer sharing, connect two Quest Pro devices to the same session, share an imported image from each device, and confirm the other device updates its display.
- Capture Unity console logs for server start, URL/code publication, upload receipt, save path, and display update.
- Only demonstrate Avatar and Facial System behavior after separate RPM, FinalIK, Quest tracking, Netcode, and Vivox validation.

## Appendix A: Deprecated and Retained Script Inventory

This appendix preserves detailed script inventories for maintainers while keeping the main design narrative readable. Avatar scripts are tied to the Avatar and Facial System section rather than described only as generic legacy code.

| Group | Script / Name | Status Note |
| --- | --- | --- |
| A.1 Active Media Upload Scripts | Assets/FromCompanion/Scripts/Server/M_ServerBootstrap.cs | Current Quest-side upload flow. |
| A.1 Active Media Upload Scripts | Assets/FromCompanion/Scripts/Server/M_SimpleHTTPServer.cs | Current Quest-side upload flow; class/runtime prefix is M_SimpleHttpServer. |
| A.1 Active Media Upload Scripts | Assets/FromCompanion/Scripts/Server/M_NetworkDiscoveryServer.cs | Supporting discovery beacon for the upload server. |
| A.1 Active Media Upload Scripts | Assets/_Scripts/Menus/M_QuestUserMenu.cs | Active launch-visible and summonable headset menu for phone pairing, passthrough, recenter, and dismissal. |
| A.1 Active Media Upload Scripts | Assets/_Scripts/MediaUpload/M_PhoneImportHeadsetMode.cs | Phone pairing text provider and opt-in legacy prompt/pairing mode support. |
| A.1 Active Media Upload Scripts | Assets/_Scripts/MediaUpload/M_PhoneUploadToDisplay.cs | Loads server-saved uploads into the scene display. |
| A.1 Active Media Upload Scripts | Assets/_Scripts/MediaUpload/M_QuestPhotoDisplay.cs | Applies textures to RawImage or renderer material. |
| A.2 Supporting Media Upload Scripts | Assets/_Scripts/MediaUpload/M_PassthroughModeController.cs | Meta XR passthrough layer, camera clear, scene visibility, and restore behavior. |
| A.2 Supporting Media Upload Scripts | Assets/_Scripts/MediaUpload/M_PassthroughMountedToggleUI.cs | Legacy mounted passthrough toggle, disabled in the active scene. |
| A.2 Supporting Media Upload Scripts | Assets/_Scripts/MediaUpload/M_PassthroughHandVisualController.cs | Hides visual-only hand/controller meshes during passthrough and restores their original visibility. |
| A.2 Supporting Media Upload Scripts | Assets/_Scripts/MediaUpload/M_QuestGalleryAndroidBridge.cs | Headset gallery bridge for optional import workflow. |
| A.2 Supporting Media Upload Scripts | Assets/_Scripts/MediaUpload/M_QuestGalleryController.cs | Gallery state and refresh workflow. |
| A.2 Supporting Media Upload Scripts | Assets/_Scripts/MediaUpload/M_QuestGalleryTile.cs | Gallery tile UI support. |
| A.2 Supporting Media Upload Scripts | Assets/_Scripts/MediaUpload/ImagePicker.cs | Direct headset image selection support. |
| A.2 Supporting Media Upload Scripts | Assets/_Scripts/MediaUpload/M_NetworkedPhotoSync.cs | Optional shared-media sync bridge; encodes local image bytes and applies remote reconstructed payloads. |
| A.3 Avatar and Facial System Scripts | Assets/_Scripts/Player/M_LocalAvatarManager.cs | RPM local avatar loading, VRIK binding, local face diagnostics. |
| A.3 Avatar and Facial System Scripts | Assets/_Scripts/Player/M_NetPlayer.cs | Network avatar URL metadata. |
| A.3 Avatar and Facial System Scripts | Assets/_Scripts/Player/M_NetPoseDriver.cs | Head/hand pose replication and remote target smoothing. |
| A.3 Avatar and Facial System Scripts | Assets/_Scripts/Player/M_NetAvatar.cs | Network RPM avatar loading and VRIK binding. |
| A.3 Avatar and Facial System Scripts | Assets/_Scripts/Player/M_LocalFaceDriver.cs | OVRFaceExpressions to RPM blendshape mapping. |
| A.3 Avatar and Facial System Scripts | Assets/_Scripts/Player/M_NetFaceMirror.cs | Network face-weight mirroring. |
| A.3 Avatar and Facial System Scripts | Assets/_Scripts/Player/M_OVRLipSyncAutoBinder.cs | OVR lip-sync viseme mapping helper. |
| A.3 Avatar and Facial System Scripts | Assets/_Scripts/Player/M_FaceDebugProbe.cs | Neutral face/blendshape diagnostics. |
| A.3 Avatar and Facial System Scripts | Assets/_Scripts/Network/FaceSyncData.cs | Experimental face-sync serialization payload. |
| A.4 Vivox / Social Presence Scripts | Assets/_Scripts/Network/M_VivoxManager.cs | Project Vivox channel manager. |
| A.4 Vivox / Social Presence Scripts | Assets/VRMPAssets/Scripts/Network/NetworkManagers/VoiceChatManager.cs | VRMP voice chat manager and social presence support. |
| A.5 Early Media Upload / Companion App Experiment Scripts | Assets/FromCompanion/Scripts/Client/M_NetworkDiscoveryClient.cs | Historical Unity companion client discovery path. |
| A.5 Early Media Upload / Companion App Experiment Scripts | Assets/FromCompanion/Scripts/Client/M_PhotoUploader.cs | Historical Unity companion client photo picker/uploader. |
| A.5 Early Media Upload / Companion App Experiment Scripts | Assets/FromCompanion/Scripts/Client/M_RestClient.cs | Historical companion HTTP client. |
| A.5 Early Media Upload / Companion App Experiment Scripts | Assets/FromCompanion/Scripts/Common/UnityMainThreadDispatcher.cs | Historical support code for companion flow. |
| A.5 Early Media Upload / Companion App Experiment Scripts | Assets/FromCompanion/Scripts/Common/UnityMainThreadRunner.cs | Historical support code for companion flow. |
| A.6 Phone Mirroring / WebRTC Experiment Scripts | Assets/_Scripts/PhoneMirror/M_AndroidIpUtil.cs | Deprecated phone mirroring prototype support. |
| A.6 Phone Mirroring / WebRTC Experiment Scripts | Assets/_Scripts/PhoneMirror/M_PairingCodeProvider.cs | Deprecated phone mirroring pairing-code support. |
| A.6 Phone Mirroring / WebRTC Experiment Scripts | Assets/_Scripts/PhoneMirror/M_PairingUiPresenter.cs | Deprecated phone mirroring UI support; now auto-wires pairing show/hide and recenter controls when present. |
| A.6 Phone Mirroring / WebRTC Experiment Scripts | Assets/_Scripts/PhoneMirror/M_PhonePairingUiToggle.cs | Opt-in pairing UI show/hide helper. |
| A.6 Phone Mirroring / WebRTC Experiment Scripts | Assets/_Scripts/PhoneMirror/M_WorldSpaceUiRecenter.cs | Opt-in world-space UI recenter helper. |
| A.6 Phone Mirroring / WebRTC Experiment Scripts | Assets/_Scripts/PhoneMirror/M_QuestLanAdvertiserUdp.cs | Deprecated LAN advertising support. |
| A.6 Phone Mirroring / WebRTC Experiment Scripts | Assets/_Scripts/PhoneMirror/M_QuestSignalingHostTcp.cs | Deprecated TCP signaling host. |
| A.6 Phone Mirroring / WebRTC Experiment Scripts | Assets/_Scripts/PhoneMirror/M_PhoneMirrorQuestWebRTC.cs | Deprecated Quest-side WebRTC receiver. |
| A.6 Phone Mirroring / WebRTC Experiment Scripts | Assets/_Scripts/PhoneMirror/M_PhonePanelRayInputSender.cs | Deprecated VR ray-to-phone-panel input sender. |
| A.7 Previously Referenced Phone Mirroring Names Not Present in Repository | M_PairingController | No matching script found in the current project files. |
| A.7 Previously Referenced Phone Mirroring Names Not Present in Repository | M_CodeEntryUI | No matching script found in the current project files. |
| A.7 Previously Referenced Phone Mirroring Names Not Present in Repository | M_QuestLanDiscoveryClientUdp | No matching script found in the current project files. |
| A.7 Previously Referenced Phone Mirroring Names Not Present in Repository | M_QuestSignalingClientTcp | No matching script found in the current project files. |
| A.7 Previously Referenced Phone Mirroring Names Not Present in Repository | M_PhoneMirrorIosWebRTCClient | No matching script found in the current project files. |
| A.7 Previously Referenced Phone Mirroring Names Not Present in Repository | M_RemoteUIInputInjector | No matching script found in the current project files. |
| A.7 Previously Referenced Phone Mirroring Names Not Present in Repository | M_PhoneMirroringBootstrap | No matching script found in the current project files. |
| A.8 Retained VRMP / Multiplayer Template Areas | Assets/VRMPAssets/Scripts/Network/NetworkManagers/ | Authentication, lobby/session, Netcode, and voice managers retained from VRMP/template systems. |
| A.8 Retained VRMP / Multiplayer Template Areas | Assets/VRMPAssets/Scripts/Network/NetworkPlayer/ | XRINetworkPlayer, avatar visuals, IK, hands, and shared media support; shared-media remote relay uses explicit `Rpc(SendTo.SpecifiedInParams)` targets plus latest-image replay for late joiners. |
| A.8 Retained VRMP / Multiplayer Template Areas | Assets/VRMPAssets/Prefabs/ | Retained multiplayer, player, UI, and room prefabs. |
