# Quest User Menu and Passthrough

## Summary

`Assets/_Scenes/PhonePhotoUpload.unity` now uses a Quest user menu instead of scattered camera-mounted or static world-space controls. The menu opens automatically on launch, can be reopened from the configured OVR menu input, appears in front of the current headset pose, and contains the passthrough toggle, phone pairing instructions, recenter action, and close button.

## Root Cause Fixed

The previous passthrough UX did not reliably expose real Quest passthrough because the visible virtual scene geometry was not owned by the passthrough controller. The toggle could change camera background or skybox behavior while the room meshes still rendered in front of the Meta passthrough underlay. The scene also had split controls: a camera-following passthrough button, generated phone pairing show/hide controls, and a generated recenter button. That made the workflow uncomfortable and left important actions in different places.

## What Changed

- `M_PassthroughModeController` now controls the Meta XR passthrough stack directly through `OVRManager.isInsightPassthroughEnabled`, an `OVRPassthroughLayer` configured as an underlay, transparent camera clear behavior, scene renderer visibility, and optional hand/controller visual hiding.
- `M_QuestUserMenu` creates one compact world-space menu that is summoned in front of the user and remains stable until dismissed or recentered.
- `M_PassthroughMountedToggleUI` remains in the project as a legacy helper but is disabled in `PhonePhotoUpload.unity`.
- `M_PhoneImportHeadsetMode` no longer auto-opens a separate pairing prompt in `PhonePhotoUpload.unity`; the menu displays the current phone upload URLs and pairing code.
- `M_PhonePairingUiToggle`, `M_WorldSpaceUiRecenter`, and `M_PairingUiPresenter` no longer generate runtime buttons by default.

## Modified Scene and Scripts

- `Assets/_Scenes/PhonePhotoUpload.unity`
  - Adds `M_QuestUserMenu` to `MetaXRPassthroughRuntime`.
  - Adds `M_PassthroughHandVisualController` to the same runtime object.
  - Opens the Quest user menu at startup so the URL/code and passthrough controls are visible immediately.
  - Keeps `OVRPassthroughLayer` present and hidden at startup.
  - Sets `OVRManager.isInsightPassthroughEnabled` off at startup.
  - Disables the old `M_PassthroughMountedToggleUI` component.
  - Disables the old automatic phone import prompt startup.
- `Assets/_Scripts/Menus/M_QuestUserMenu.cs`
  - New summonable VR menu.
  - Uses Input System-backed editor keyboard checks so Quest builds do not call Unity's legacy `Input` API.
- `Assets/_Scripts/MediaUpload/M_PassthroughModeController.cs`
  - Central passthrough, camera clear, scene visibility, and hand visual coordination.
- `Assets/_Scripts/MediaUpload/M_PhoneImportHeadsetMode.cs`
  - Exposes phone pairing instructions for the menu and changes generated controls to opt-in.
- `Assets/_Scripts/PhoneMirror/M_PhonePairingUiToggle.cs`
- `Assets/_Scripts/PhoneMirror/M_WorldSpaceUiRecenter.cs`
- `Assets/_Scripts/PhoneMirror/M_PairingUiPresenter.cs`
  - Generated controls are now opt-in so they do not create competing UI by default.

## Passthrough Control

Passthrough is controlled by `M_PassthroughModeController`.

When enabled, it:

- Sets `OVRManager.isInsightPassthroughEnabled` to `true`.
- Ensures an `OVRPassthroughLayer` exists, is enabled, is not hidden, uses `OVROverlay.OverlayType.Underlay`, and has full texture opacity.
- Sets the XR camera to a transparent solid clear color while passthrough is visible.
- Hides scene `MeshRenderer` and `SkinnedMeshRenderer` components that would otherwise block the underlay.
- Keeps canvas UI and controller/ray line renderers available for interaction.
- Hides visual-only hand/controller meshes through `M_PassthroughHandVisualController` where possible.

When disabled, it restores the previous camera state, renderer states, assigned object states, and hand/controller visual states.

## Menu Behavior

`M_QuestUserMenu` is attached to `MetaXRPassthroughRuntime`.

- Opens automatically in `PhonePhotoUpload.unity`.
- Reopens from `OVRInput.Button.Start` by default after it is closed. In the editor, `M` also toggles the menu.
- On open, places the menu in front of the current headset position using headset yaw, a comfortable distance, and a small vertical offset.
- Does not follow the camera continuously.
- Recenter moves the menu back in front of the user and calls `XRMultiplayer.CharacterResetter.ResetPlayer()` if a player resetter is present.
- Close hides the menu cleanly.
- The passthrough button label reflects the current controller state.
- Phone instructions are pulled from `M_ServerBootstrap`/`M_PhoneImportHeadsetMode`, so the displayed URL and pairing code stay current.

## Required Meta XR / OVR Setup

The active scene must include:

- An enabled `OVRManager`.
- An enabled `OVRPassthroughLayer` configured as an underlay, hidden at startup.
- A valid XR camera reference.
- Meta XR SDK / Oculus VR package support from `com.meta.xr.sdk.core`.
- Android/Quest deployment to a headset that supports passthrough. Editor play mode cannot prove real headset passthrough.

## Quest Pro Manual Test Checklist

1. Launch `Assets/_Scenes/PhonePhotoUpload.unity` on Quest Pro.
2. Confirm the Quest user menu opens in front of the headset and no old phone prompt, recenter button, or passthrough button is awkwardly stuck to the camera or static room.
3. Close and reopen the menu with the configured OVR menu input.
4. Confirm the menu appears in front of the current headset pose at a comfortable distance.
5. Confirm the menu is reachable with the existing controller/ray UI interaction.
6. Confirm phone pairing instructions show local URL options and the current pairing code.
7. Toggle passthrough on.
8. Confirm real camera passthrough is visible, not just a skybox/background change.
9. Confirm virtual room geometry no longer blocks the passthrough feed while the menu remains usable.
10. Toggle passthrough off.
11. Confirm the virtual scene and hand/controller visuals restore correctly.
12. Upload a photo from a phone on the same Wi-Fi network.
13. Confirm the uploaded photo appears in the scene display.
14. Open the menu again and press Recenter.
15. Confirm the player reset behavior runs if the scene has a `CharacterResetter`, and the menu moves back in front of the user.
16. Press Close.
17. Confirm the menu hides and no orphaned runtime UI remains visible.

## Current Limitations

- This was statically edited and must still be validated on Quest Pro hardware. Unity editor play mode cannot verify real headset passthrough.
- If runtime-spawned mesh renderers appear after passthrough is already enabled, they may need explicit assignment or a refresh pass if they block the underlay.
- The default summon input is serialized and can be changed on `M_QuestUserMenu` if it conflicts with another scene input.
