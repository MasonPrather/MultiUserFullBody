# Troubleshooting

## Problem: Phone upload page does not open

Symptoms:
- The phone browser cannot load the displayed Quest URL.
- Unity logs show the upload server started, but the phone times out.

Likely Causes:
- Phone and Quest are not on the same Wi-Fi network.
- The router blocks peer-to-peer local traffic.
- The displayed IP belongs to a non-Wi-Fi interface.
- Port `8080` is blocked by the network.

Diagnosis:
- Check Unity console logs from `M_ServerBootstrap` for all published local URLs.
- Try each displayed local URL.
- Confirm the phone and Quest are on the same access point.
- Confirm the scene is running and `M_SimpleHttpServer` logged its listening port.

Resolution:
- Move phone and Quest to the same Wi-Fi network.
- Use another lab router or hotspot that allows local device-to-device traffic.
- Restart the scene to republish current local IP addresses.
- Keep the local IP URL path; public domains require external routing not included in this project.

Notes:
- `M_ServerBootstrap` can show multiple local fallback URLs because Unity/Android can report more than one network interface.

## Problem: Pairing code is rejected

Symptoms:
- Upload page loads, but upload fails after entering the code.
- Server response indicates rejected pairing or authorization failure.

Likely Causes:
- Code entered from a previous scene run.
- `M_ServerBootstrap.pairingCode` changed or generated a new code.
- Browser page was left open while the Unity scene restarted.

Diagnosis:
- Compare the phone entry against the currently displayed headset code.
- Check logs from `M_ServerBootstrap` for the active phone upload code.
- Reload the upload page after restarting the scene.

Resolution:
- Enter the current headset code.
- Refresh the phone page after each scene restart.
- For repeat lab runs, set a fixed `pairingCode` in `M_ServerBootstrap` only when that controlled behavior is required.

## Problem: Upload succeeds but image does not appear

Symptoms:
- Phone page reports success.
- File is saved, but the in-scene display remains unchanged.

Likely Causes:
- `M_PhoneUploadToDisplay` is missing or disabled.
- `M_QuestPhotoDisplay` reference was not resolved.
- File write completed after the first display read attempt and retries were exhausted.
- Display RawImage or renderer reference is missing.

Diagnosis:
- Check `M_SimpleHttpServer.LastSavedPhotoPath` logs.
- Check warnings from `M_PhoneUploadToDisplay`.
- Confirm a scene object has `M_QuestPhotoDisplay`.
- Confirm target RawImage or renderer references are assigned.

Resolution:
- Add or enable `M_PhoneUploadToDisplay` in the scene.
- Assign `photoDisplay`, or keep `autoFindPhotoDisplay` enabled.
- Increase `maxLoadRetries` or `retryDelaySeconds` on slow devices.
- Confirm display target references on `M_QuestPhotoDisplay`.

## Problem: Quest gallery is empty

Symptoms:
- Gallery browser shows no tiles.
- Imported phone photos exist but the gallery does not refresh.

Likely Causes:
- Android media permission was denied.
- `M_QuestGalleryAndroidBridge` scan settings exclude the relevant folder.
- App-owned imported media folder is empty.
- Gallery reload was not requested after import.

Diagnosis:
- Check `M_QuestGalleryAndroidBridge` permission and scan logs.
- Confirm `importedMediaFolderName` and `phoneUploadFolderName` values.
- Use `M_QuestGalleryController.RefreshGallery`.
- Check whether app-owned phone uploads exist under `Application.persistentDataPath/Uploads`.

Resolution:
- Grant Quest media permission when prompted.
- Keep phone uploads in the app-owned `Uploads` folder for reliable browsing.
- Trigger gallery refresh after phone upload.
- Review `supportedExtensions` for the imported file type.

## Problem: Shared media does not reach connected clients

Symptoms:
- Local image display works, but remote clients do not update.
- Console logs mention missing local player or no scene listener.

Likely Causes:
- No owning local `XRINetworkPlayer` is spawned.
- `M_NetworkedPhotoSync` cannot find the local player within its lookup timeout.
- Remote scene lacks `M_QuestPhotoDisplay` or scene receiver setup.
- Network session is disconnected or Unity Transport payload limits are exceeded.
- The scene is using an older player prefab/script version that still relays shared media with `SendTo.NotAuthority` instead of the explicit `ClientsAndHost` relay.

Diagnosis:
- Check `[M_NetworkedPhotoSync]` warnings.
- Check shared-media logs from `XRINetworkPlayer`.
- Confirm the local client owns a spawned player object.
- Confirm remote clients have a display target.
- For late join behavior, confirm the joining client logs local player spawn before expecting the cached latest image replay.

Resolution:
- Start or join the multiplayer session before sharing media.
- Confirm player prefab includes `XRINetworkPlayer`.
- Keep `M_QuestPhotoDisplay` active in the receiving scene.
- Reduce image size or JPEG quality for large transfers.
- Confirm `M_NetworkedPhotoSync` is enabled so it subscribes to `XRINetworkPlayer.onSharedMediaReceived`.

## Problem: Joining headset shows an old image

Symptoms:
- A newly joined client briefly or permanently shows a stale local display image.
- The shared image panel does not start blank before current session media arrives.

Likely Causes:
- `M_NetworkedPhotoSync` is disabled or missing from the display object.
- `clearDisplayWhenSceneReceiverInitializes` or `clearDisplayWhenLocalPlayerSpawns` was disabled.
- Another script applies a local fallback texture after the sync receiver clears the display.

Diagnosis:
- Check for `[M_NetworkedPhotoSync] Cleared local image display on join` in the console.
- Confirm the active display object has `M_QuestPhotoDisplay` and `M_NetworkedPhotoSync`.
- Check whether the cached latest shared image is replayed after local player spawn.

Resolution:
- Keep the scene receiver enabled on the display object.
- Leave join-time clearing enabled unless the scene intentionally owns its starting image.
- If using custom display code, apply fallback visuals before `M_NetworkedPhotoSync` initializes or after shared-media state is known.

## Problem: Passthrough toggles but hand visuals stay visible

Symptoms:
- Quest passthrough turns on, but virtual hands or controller models remain over the camera feed.
- Turning passthrough off does not restore the original hand visual state.

Likely Causes:
- `M_PassthroughHandVisualController` is missing.
- The hand visual objects do not match the default auto-find name tokens.
- Visual-only roots/renderers were not assigned in the inspector.

Diagnosis:
- Confirm the scene has `M_PassthroughModeController` and `M_PassthroughHandVisualController`.
- Check `handRenderers`, `handSkinnedMeshRenderers`, and `handVisualRoots` at runtime.
- Enable verbose logging on the passthrough visual controller for a toggle pass.

Resolution:
- Add `M_PassthroughHandVisualController` near the passthrough controller and assign it to `handVisualController`.
- Assign visual-only hand roots/renderers manually when auto-find names do not match the rig.
- Do not assign tracking roots, interactors, or ray/input objects as visual-only roots.

## Problem: Passthrough only changes the skybox or background

Symptoms:
- The passthrough button label changes, but the user still sees the virtual room.
- Only the skybox/background appears different.
- The real headset camera feed is not visible.

Likely Causes:
- The scene geometry is still rendering in front of the passthrough underlay.
- `OVRPassthroughLayer` is missing, disabled, hidden, or not configured as an underlay.
- `OVRManager.isInsightPassthroughEnabled` is not being set by `M_PassthroughModeController`.
- The XR camera is still clearing to an opaque skybox/background while passthrough is enabled.

Diagnosis:
- Confirm `PhonePhotoUpload.unity` has `M_PassthroughModeController`, `OVRManager`, and `OVRPassthroughLayer` on `MetaXRPassthroughRuntime`.
- Confirm `hideSceneRenderersWhileEnabled` is enabled on `M_PassthroughModeController`.
- Confirm the controller logs real passthrough state changes and no missing-reference warnings.
- Validate on Quest Pro hardware; editor play mode cannot prove real passthrough.

Resolution:
- Keep `OVRPassthroughLayer` enabled, hidden at startup, and configured as `Underlay`.
- Let `M_PassthroughModeController` own camera clear state and renderer hiding instead of changing skybox materials.
- Add critical UI roots to `objectsKeptVisibleDuringPassthrough` only when they must remain visible.
- Run the checklist in `docs/QUEST_USER_MENU_AND_PASSTHROUGH.md`.

## Problem: Quest user menu does not appear

Symptoms:
- The phone pairing instructions are not visible.
- The old prompt is gone, but the new menu does not open.

Likely Causes:
- `M_QuestUserMenu` is missing or disabled on `MetaXRPassthroughRuntime`.
- The configured `menuToggleButton` conflicts with another input or is unavailable on the current device.
- The menu has no active XR camera reference.

Diagnosis:
- Check `[M_QuestUserMenu]` logs.
- In the editor, press `M` to test the fallback toggle.
- Inspect `xrCamera`, `passthroughController`, `serverBootstrap`, and `phoneImportMode` on `M_QuestUserMenu`.

Resolution:
- Add or enable `M_QuestUserMenu` on the active runtime object.
- Assign the active XR camera or leave `Camera.main` available.
- Change `menuToggleButton` in the inspector if the default OVR menu input is reserved by the current headset/runtime.

## Problem: Unity Services authentication or session connection fails

Symptoms:
- Connection state remains authenticating or connecting.
- Logs mention Unity Cloud linking, authentication failure, session failure, or relay failure.

Likely Causes:
- Project is not linked to Unity Services.
- Internet access is unavailable for distributed sessions.
- Unity Services credentials/session state expired.
- Distributed session is selected while the environment lacks network access.

Diagnosis:
- Check logs from `XRINetworkGameManager`, `AuthenticationManager`, and `SessionManager`.
- Check Unity Project Settings > Services linking.
- Confirm `SessionManager.sessionType`.
- Confirm workstation or headset has internet access.

Resolution:
- Link the Unity project to Unity Services.
- Sign into Unity with the correct account.
- Use local-only session mode for offline lab checks.
- Restart Play Mode after Services settings change.

## Problem: Vivox voice does not connect

Symptoms:
- Player joins session but no voice audio is heard.
- Voice connection status remains empty or disconnected.
- Microphone permission prompt appears repeatedly.

Likely Causes:
- Microphone permission denied.
- Vivox initialization failed because Unity Services login failed.
- `VoiceChatManager` or `M_VivoxManager` is missing from the active flow.
- Local audio tap prefab is missing or disabled incorrectly.

Diagnosis:
- Check `[Vivox]` logs and `VoiceChatManager.connectionStatus`.
- Confirm microphone permission on Quest.
- Confirm Unity Services authentication succeeds first.
- Inspect `audioTapPrefab` on `M_VivoxManager` where used.

Resolution:
- Grant microphone permission.
- Resolve Unity Services authentication first.
- Confirm voice manager components are present in the scene.
- Rejoin the channel after permission changes.

## Problem: Ready Player Me avatar does not load

Symptoms:
- Local or network avatar stays missing.
- Logs mention RPM load failure or null avatar.

Likely Causes:
- Avatar URL is invalid or unreachable.
- Ready Player Me package did not import correctly.
- `RPM_URL` PlayerPrefs value points to an unavailable asset.
- Avatar root or required IK target references are missing.

Diagnosis:
- Check `[M_LocalAvatarManager]` and `[M_NetAvatar]` logs.
- Confirm the serialized fallback URL is reachable.
- Clear or update PlayerPrefs key `RPM_URL`.
- Inspect local avatar root and network player prefab references.

Resolution:
- Use a valid Ready Player Me `.glb` URL.
- Allow Unity to restore Ready Player Me package dependencies.
- Assign avatar root and IK target references.
- Re-enter Play Mode after package import completes.

## Problem: Face mirroring appears too strong, too weak, or stuck

Symptoms:
- Remote avatar face overreacts, barely moves, or keeps small expressions after neutral pose.

Likely Causes:
- `M_LocalFaceDriver` gain/smoothing values do not match current participant tracking.
- `M_NetFaceMirror.weightScale`, deadzones, or neutral smoothing need calibration.
- Local and networked avatars do not share matching blendshape indices.
- OVR face tracking is unavailable or invalid.

Diagnosis:
- Enable diagnostics on `M_FaceDebugProbe`.
- Check `M_LocalFaceDriver` channel count logs.
- Check `M_NetFaceMirror` owner/client debug logs.
- Confirm local and network avatars use the same RPM avatar URL.

Resolution:
- Calibrate `globalGain`, `maxWeight`, `weightScale`, and deadzone values.
- Confirm OVR face tracking is available on the device.
- Keep matching RPM avatar URLs for local and networked avatars.
- Use neutral sampling logs to tune thresholds.

## Problem: Phone mirror does not connect

Symptoms:
- Phone mirror status remains waiting for phone.
- TCP client connects and is rejected.
- WebRTC offer arrives but video never appears.

Likely Causes:
- Pairing code mismatch.
- Phone and Quest are not on the same LAN.
- TCP port `29000` is blocked.
- WebRTC companion sends unsupported signaling JSON.
- Remote video track is not received.

Diagnosis:
- Check `[Quest/WebRTC]` logs.
- Check `M_QuestSignalingHostTcp` connection/rejection events.
- Confirm `M_QuestLanAdvertiserUdp` broadcast settings.
- Confirm the phone-side flow sends `hello`, `offer`, and `ice` messages in the expected framed format.

Resolution:
- Enter the current pairing code.
- Move both devices to the same peer-to-peer Wi-Fi network.
- Restart the signaling host scene.
- Verify phone-side signaling payloads against `M_PhoneMirrorQuestWebRTC.SigMsg`.

## Problem: Passthrough toggle does not work

Symptoms:
- The Quest user menu appears, but passthrough stays off.
- The passthrough label changes briefly or remains off while the camera stays opaque.

Likely Causes:
- `OVRManager` is missing.
- `OVRPassthroughLayer` is missing and runtime creation is disabled.
- Current device/runtime does not support passthrough.
- Camera reference is missing or not the active XR camera.
- The runtime has reported `OVRManager.IsInsightPassthroughSupported()` as false.

Diagnosis:
- Check `[M_PassthroughModeController]` warnings.
- Inspect `ovrManager`, `passthroughLayer`, and `xrCamera` references.
- Confirm `createLayerIfMissing` and `makeCameraTransparentWhileEnabled`.
- Confirm the scene is running on Quest hardware that supports passthrough.

Resolution:
- Add or assign `OVRManager`.
- Allow `M_PassthroughModeController` to create a passthrough layer or assign one in the scene.
- Assign the active XR camera.
- Test on Quest hardware rather than relying only on editor behavior.

## Escalation Notes

- Preserve scene and prefab serialized names when debugging; many references are Unity-serialized.
- Keep imported SDK and package files unchanged during project-specific fixes.
- Record the active scene, device, Unity version, package import status, and exact console logs when handing issues to a future maintainer.
