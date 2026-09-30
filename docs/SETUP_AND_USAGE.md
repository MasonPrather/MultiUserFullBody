# Setup and Usage

## Environment Assumptions

- Unity Editor `6000.3.8f1`.
- macOS or Windows workstation capable of opening the Unity project.
- Meta Quest device for headset runtime validation.
- Android Build Support in Unity for Quest deployment.
- Phone and Quest connected to the same Wi-Fi network for local phone upload and phone mirror workflows.
- Unity Services project linking for distributed multiplayer, Relay/Multiplayer sessions, and Vivox voice.

## Hardware Assumptions

- Meta Quest headset with controller or hand tracking support.
- Phone with a modern browser for the no-install media upload page.
- Local Wi-Fi network that allows peer-to-peer device traffic.
- Microphone permission for Vivox voice.
- Quest passthrough support for headset-on phone pairing flows.

## Software Dependencies

Dependencies are declared in `Packages/manifest.json`. Major runtime dependencies include:

- Meta XR SDK packages `78.0.0`.
- Unity Netcode for GameObjects `2.8.0`.
- Unity Services Multiplayer `2.0.0`.
- Unity Services Vivox `16.9.0`.
- Unity WebRTC `3.0.0`.
- Unity XR Interaction Toolkit `3.3.1`.
- Unity XR Hands `1.7.3`.
- Unity XR Management, OpenXR, Meta OpenXR, and Oculus XR.
- Ready Player Me Core from GitHub.
- DracoUnity from GitHub.
- Universal Render Pipeline `17.3.0`.
- NativeGallery under `Assets/Plugins/NativeGallery`.
- RootMotion FinalIK under `Assets/Plugins/RootMotion`.

## Runtime Services

- Unity Services Authentication for session login.
- Unity Services Multiplayer/Relay for distributed sessions.
- Vivox for voice channels.
- Quest-hosted HTTP server for phone uploads on port `8080`.
- UDP discovery for phone uploads on port `7777`.
- Phone mirror TCP signaling on port `29000`.
- Phone mirror UDP advertisement on port `7777`.

## Configuration Files

- `ProjectSettings/ProjectVersion.txt` records the Unity version.
- `ProjectSettings/EditorBuildSettings.asset` enables `Assets/_Scenes/PhonePhotoUpload.unity`.
- `Packages/manifest.json` declares Unity package dependencies.
- `Assets/VRMPAssets/VRMP.asmdef` defines the VRMP assembly references.
- Scene and prefab serialized fields configure ports, UI references, upload paths, avatar defaults, and runtime behavior.

## Setup Steps

1. Open Unity Hub.
2. Add the repository folder `/Users/masonprather/Documents/Unity/MultiUserFullBody`.
3. Open the project with Unity `6000.3.8f1`.
4. Allow Unity to resolve packages and import assets.
5. Confirm `Assets/_Scenes/PhonePhotoUpload.unity` opens without missing-script errors.
6. Link the Unity project to Unity Services before testing distributed multiplayer or Vivox.
7. Configure Quest build support through Unity's Android build settings when deploying to headset.

No `package.json`, Makefile, shell runner, or repository-level build script is present. Unity Editor is the primary build and run environment.

## Build Steps

The repository does not include a custom build automation script. Build through Unity:

1. Open `File > Build Profiles` or Unity's platform build window.
2. Select Android for Quest deployment.
3. Confirm `Assets/_Scenes/PhonePhotoUpload.unity` is enabled in the scene list.
4. Build and run to the connected Quest device.

The active build scene can be verified in `ProjectSettings/EditorBuildSettings.asset`.

## Running the Phone Media Upload Flow

1. Open `Assets/_Scenes/PhonePhotoUpload.unity`.
2. Enter Play Mode or run the scene on Quest.
3. Confirm the Quest user menu opens automatically and displays local URL options and a code. If it is closed, reopen it with the left controller menu/start input or the editor `M` shortcut.
4. Confirm Unity console logs from `M_ServerBootstrap` show the upload root and one or more phone upload URLs.
5. Connect the phone to the same Wi-Fi network as the Quest.
6. Open one displayed local URL, such as `192.168.1.25:8080`.
7. Enter the headset code.
8. Select and upload one or more photos or videos.
9. Confirm the newest media item appears in the Unity display.
10. Use the menu's `Passthrough`, `Recenter`, and `Close` controls if the room needs passthrough review, the menu needs to move, or the user wants to dismiss it.
11. Confirm gallery refresh and shared-media broadcast logs when the scene includes multiplayer player objects.

## Running Headset Gallery Import

1. Open a scene with `M_QuestGalleryController`, `M_QuestPhotoDisplay`, and `ImagePicker` or `M_QuestGalleryAndroidBridge`.
2. Use the scene's import/sync control.
3. Grant media permission on Quest when prompted.
4. Select an image through the native picker or gallery browser.
5. Confirm the selected image appears in `M_QuestPhotoDisplay`.
6. Use the share action to route the image through `M_NetworkedPhotoSync` when multiplayer is active.

## Running Multiplayer Sessions

1. Link the Unity project to Unity Services.
2. Confirm internet access for distributed sessions.
3. Open a scene with `XRINetworkGameManager`, `SessionManager`, `NetworkManagerVRMultiplayer`, and the relevant player prefab.
4. Enter Play Mode or deploy to Quest.
5. Use the scene/menu controls to host, quick-join, or connect.
6. Confirm logs show authentication and session connection.
7. Confirm spawned players receive pose, voice, and shared-media updates.

## Running Phone Mirroring

1. Open a phone mirroring scene such as `Assets/_Scenes/EyeFaceTrackingScene_PhoneMirroring.unity`.
2. Confirm `M_QuestSignalingHostTcp`, `M_QuestLanAdvertiserUdp`, `M_PairingCodeProvider`, and `M_PhoneMirrorQuestWebRTC` are present.
3. Run on Quest or in the editor with the required networking setup.
4. Pair the phone-side companion flow with the displayed code.
5. Confirm `M_PhoneMirrorQuestWebRTC` receives a video track and updates the RawImage.
6. If the scene opts into `M_PhonePairingUiToggle` or `M_WorldSpaceUiRecenter`, use those controls to hide/show or reposition the code panel without stopping the signaling/WebRTC host.
7. Confirm ray input sends touch messages only after the WebRTC input data channel is available.

## Startup Order

For the phone upload scene:

1. Unity loads the scene.
2. `M_ServerBootstrap.Start` creates `Application.persistentDataPath/Uploads`, starts `M_SimpleHttpServer`, adds `M_NetworkDiscoveryServer`, and publishes instructions.
3. `M_QuestUserMenu` creates a hidden world-space menu and subscribes to phone instruction/passthrough state changes.
4. `M_PhoneImportHeadsetMode` resolves server/display/passthrough references and supplies pairing text for the menu without opening a separate prompt by default.
5. `M_PhoneUploadToDisplay.Update` watches `M_SimpleHttpServer.LastSavedPhotoPath`.
6. `M_QuestGalleryController` refreshes gallery state after imports.
7. `M_NetworkedPhotoSync` clears stale local display state on join before shared-media replay arrives.
8. `M_NetworkedPhotoSync` broadcasts media after a local owning `XRINetworkPlayer` is available.
9. `XRINetworkPlayer` reconstructs uploads, caches the latest shared image, relays chunks to `ClientsAndHost`, and replays the cached latest image to later joiners.

## Shutdown Process

- Exiting Play Mode or closing the scene triggers `OnDestroy`/`OnDisable` cleanup.
- `M_ServerBootstrap` stops `M_SimpleHttpServer`.
- UDP clients/listeners close in their disable/destroy handlers.
- Vivox teardown leaves channels and logs out when the application quits.
- Runtime textures created by display/tile scripts are destroyed by their owning components.

## Expected Runtime Behavior

- The Quest user menu opens in front of the current headset pose, can be summoned again after closing, and shows local URLs plus the pairing code.
- HTTP upload page loads on the phone when peer-to-peer Wi-Fi traffic is allowed.
- Uploaded images are saved under `Application.persistentDataPath/Uploads`.
- The newest uploaded image appears in the scene display.
- Imported images become browseable in the gallery after refresh.
- Shared media reaches connected clients when an owning local `XRINetworkPlayer` exists, and late joiners receive the most recent shared image after their local player spawns.
- A joining client clears any stale local display image before applying shared session media.
- Passthrough can be toggled repeatedly; opaque hidden objects and hand/controller visuals restore their original visibility when passthrough is disabled.
- Real passthrough validation must happen on Quest hardware; editor play mode cannot prove the headset camera feed is visible.
- Console logs report ports, URLs, upload root, upload status, media sync progress, and relevant warnings.

## Confirming the System Is Working

- Unity console shows `[M_ServerBootstrap] Upload root:` and phone upload URLs.
- The phone can load the Quest-hosted page.
- Entering an incorrect code rejects upload; entering the displayed code accepts upload.
- `M_PhoneUploadToDisplay` logs a displayed phone upload.
- `M_QuestPhotoDisplay` status text shows the displayed image dimensions.
- Passthrough on Quest shows the real camera feed rather than only changing the skybox or background color.
- Multiplayer scenes log session connection and player spawn.
- Shared media logs show upload/relay progress through `XRINetworkPlayer`.

## Demonstration Notes

- Use `Assets/_Scenes/PhonePhotoUpload.unity` for the clearest supervisor review of local phone-to-headset media upload.
- Keep phone and Quest on the same access point.
- Start with a small image during demonstrations to reduce upload and network-transfer time.
- Keep Unity console visible during review so URL, code, upload, and sync logs can be inspected.
- Local IP URLs are the supported path. Public short domains require separate DNS/local routing that is not part of this repository.
- Use the checklist in `docs/QUEST_USER_MENU_AND_PASSTHROUGH.md` before treating passthrough/menu behavior as validated.
