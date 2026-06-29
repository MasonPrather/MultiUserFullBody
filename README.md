# MultiUserFullBody

MultiUserFullBody is a Unity research and development project for multi-user immersive interaction, full-body avatar embodiment, face/voice communication, and in-headset media sharing. The project is maintained by Mason Prather as Graduate Research Assistant work at Kennesaw State University under the supervision of Dr. Lei Zhang.

The current repository centers on a Quest-based shared VR environment with Ready Player Me avatars, Netcode/Unity Services multiplayer, Vivox voice, OVR face and lip-sync experiments, Quest media browsing, and local-network phone photo upload. The Spring 2026 work shifted the project toward media sharing and supervisor-reviewable workflows that demonstrate how phones, headsets, and networked clients exchange visual media inside the VR space.

## System Purpose

The project supports research prototypes for:

- Networked avatar embodiment using Ready Player Me avatars, VRIK, replicated head/hand pose, and experimental face mirroring.
- Shared media workflows where headset-selected or phone-uploaded images appear on a shared in-world display.
- Local-network phone upload without a phone app install, using a Quest-hosted HTTP upload page and pairing code.
- Multiplayer room connection through Unity Services, Relay/Multiplayer sessions, Netcode for GameObjects, and Vivox voice.
- Quest passthrough support during phone pairing and media import.

## High-Level Workflow

```text
[Quest / Unity Scene]
      |
      +-- starts local upload server and UDP discovery
      |
[Phone browser on same Wi-Fi]
      |
      +-- opens displayed URL, enters headset code, uploads image
      |
[Application.persistentDataPath/Uploads]
      |
      +-- M_PhoneUploadToDisplay loads newest file
      |
[M_QuestPhotoDisplay]
      |
      +-- M_NetworkedPhotoSync / XRINetworkPlayer broadcasts image chunks
      |
[Connected multiplayer clients]
```

## Repository Structure

- `Assets/_Scripts/` - project-specific runtime scripts for media upload, phone mirroring, local/network avatars, face sync, Vivox, menus, and environment utilities.
- `Assets/FromCompanion/` - local phone upload client/server scenes, scripts, and phone upload notes.
- `Assets/VRMPAssets/` - project-maintained multiplayer scene assets, shared interaction scripts, network managers, player presentation scripts, mini-games, UI, shaders, and prefabs.
- `Assets/_Scenes/` - active research/development scenes, including `PhonePhotoUpload.unity`, `MediaUpload.unity`, and face-tracking variants.
- `Assets/_Prefabs/` - project prefabs such as `NetworkedPlayer`, media upload setup, media tiles, and locomotor support.
- `Assets/Plugins/`, `Assets/Oculus/`, `Assets/Ready Player Me/`, `Assets/Samples/`, `Assets/TextMesh Pro/` - imported SDKs, packages, samples, and third-party assets retained for Unity runtime support.
- `Packages/manifest.json` - Unity package dependencies.
- `ProjectSettings/` - Unity editor, XR, build, input, graphics, and service settings.
- `docs/` - supervisor and maintainer documentation for architecture, scripts, setup, and troubleshooting.

## Quick Start

1. Open the project with Unity `6000.3.8f1` as recorded in `ProjectSettings/ProjectVersion.txt`.
2. Allow Unity to restore packages from `Packages/manifest.json`.
3. Open `Assets/_Scenes/PhonePhotoUpload.unity`.
4. Enter Play Mode in the editor or build/run on a Meta Quest device.
5. Use the Quest user menu that opens on launch to read the local phone URL and pairing code. If it is closed, reopen it with the left controller menu/start input, or press `M` in the editor/simulator.
6. On a phone connected to the same Wi-Fi network, open the displayed URL, enter the code, and upload one or more images.
7. Confirm the uploaded image appears in the in-scene display and, when multiplayer is active, on connected clients.

The enabled build scene in `ProjectSettings/EditorBuildSettings.asset` is `Assets/_Scenes/PhonePhotoUpload.unity`. Other research scenes are present but disabled in the build settings.

## Main Components

- `M_ServerBootstrap` starts the local HTTP upload server, UDP discovery beacon, pairing-code flow, and headset instructions.
- `M_SimpleHttpServer` serves the phone upload page and writes uploaded image files to persistent storage.
- `M_QuestUserMenu` presents the launch-visible and summonable VR menu for passthrough, phone pairing instructions, recentering, and dismissal.
- `M_PhoneImportHeadsetMode` provides phone upload instruction text for the menu and remains available for explicit pairing-mode prompts when a scene opts into that legacy behavior.
- `M_QuestGalleryController`, `M_QuestGalleryAndroidBridge`, and `ImagePicker` manage headset-side media imports and gallery browsing.
- `M_QuestPhotoDisplay` displays selected, uploaded, or synchronized media.
- `M_NetworkedPhotoSync` and `XRINetworkPlayer` distribute shared media across connected multiplayer clients, clear stale local displays on join, and replay the latest shared image to later joiners.
- `M_PassthroughModeController` and `M_PassthroughHandVisualController` toggle real Meta XR passthrough while restoring camera, scene renderer, and visual-only hand/controller states. `M_PassthroughMountedToggleUI` is retained only as a legacy helper and is disabled in the active phone upload scene.
- `M_LocalAvatarManager`, `M_NetAvatar`, `M_NetPoseDriver`, `M_LocalFaceDriver`, and `M_NetFaceMirror` support avatar loading, pose replication, and face-expression experiments.
- `XRINetworkGameManager`, `SessionManager`, `AuthenticationManager`, `NetworkManagerVRMultiplayer`, and `VoiceChatManager` manage multiplayer connection and voice services.

## Configuration Overview

- Unity version: `6000.3.8f1`.
- Default phone upload HTTP port: `8080`.
- Default phone upload UDP discovery port: `7777`.
- Phone mirror TCP signaling port: `29000`.
- Phone mirror UDP discovery port: `7777`.
- Uploaded phone photos: `Application.persistentDataPath/Uploads`.
- Headset-picked cached media: `Application.persistentDataPath/PickedImages`.
- App-owned imported media: `Application.persistentDataPath/ImportedSharedMedia`.
- Default Ready Player Me avatar URL is serialized in avatar scripts and can be overridden through `PlayerPrefs` key `RPM_URL`.

## Expected Inputs and Outputs

Inputs include Quest headset/controller tracking, OVR face expressions, Ready Player Me avatar URLs, Unity Services session state, Vivox microphone input, Android/Quest gallery selections, browser-uploaded phone images, and local network discovery/signaling traffic.

Outputs include local and networked avatar transforms, face blendshape weights, in-scene photo display textures, persistent uploaded image files, shared-media Netcode RPC chunks, Vivox voice state, UI status labels, and Unity console diagnostics.

## Documentation

- [System Design](docs/SYSTEM_DESIGN.md)
- [Script Reference](docs/SCRIPT_REFERENCE.md)
- [Setup and Usage](docs/SETUP_AND_USAGE.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Quest User Menu and Passthrough](docs/QUEST_USER_MENU_AND_PASSTHROUGH.md)

## Maintenance Note

Future project contributors should keep project-specific scripts under `Assets/_Scripts`, `Assets/FromCompanion/Scripts`, or the maintained `Assets/VRMPAssets` runtime areas. Imported package, SDK, sample, generated, cache, and build-output files should remain unchanged unless a project integration explicitly requires it. New scripts should include the same header structure used across the repository and documentation should describe verified behavior from the current Unity project.
