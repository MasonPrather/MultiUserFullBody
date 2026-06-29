# Phone Photo Upload

This flow sends phone camera-roll photos directly to the Quest app over the local Wi-Fi network. It does not require a phone app install: the Quest hosts a small upload page, the participant opens the displayed local URL on the phone, enters the headset code, and chooses photos with the phone browser's native picker.

The `PhonePhotoUpload` scene now uses `M_QuestUserMenu` as the headset-side control surface. The menu is summoned in front of the current headset pose and contains the phone URL/code, passthrough toggle, recenter action, and close button. `M_PhoneImportHeadsetMode` still supplies pairing text for the menu, but it no longer opens a separate prompt by default in this scene.

## How To Try It

1. Open a scene that has `M_ServerBootstrap` enabled.
   - `Assets/FromCompanion/Scenes/ServerScene.unity`
   - `Assets/_Scenes/PhonePhotoUpload.unity`
2. Run the scene on Quest or in the editor.
3. In `Assets/_Scenes/PhonePhotoUpload.unity`, summon the Quest user menu and confirm it appears comfortably in front of the headset.
4. On the phone, join the same Wi-Fi network and open one of the displayed local URLs, such as `192.168.1.25:8080`.
5. Enter the separately displayed headset code, then choose one or more photos and upload.
6. Uploaded photos are saved under `Application.persistentDataPath/Uploads`.
7. `M_PhoneUploadToDisplay` shows the newest upload immediately.
8. `M_QuestGalleryController` refreshes the browseable gallery so imported phone photos can be selected again later.
9. In multiplayer scenes, the current shared image is relayed to connected users and replayed to later joiners after their player object spawns.

## Notes

- The phone page tries to convert selected photos to JPEG before upload, which helps with iPhone camera-roll formats.
- The headset can browse photos after they have been imported. The phone browser exposes only the files selected through the browser picker.
- The old raw endpoint still works: `POST /upload-photo` with image bytes. By default, legacy raw uploads can skip the pairing code so existing Unity companion clients do not break.
- Multipart form uploads also work for browser fallback.
- Imported phone photos are app-owned files, so they remain browseable even if the user denies Quest-wide media/gallery permission.
- The `Share Image` button routes through `M_QuestGalleryController.ShareSelectedImage`, so the selected grid image is applied to the local shared display and sent through `M_NetworkedPhotoSync`.
- Shared images are automatically distributed to connected multiplayer users when a spawned owning `XRINetworkPlayer` is available. If no local network player is spawned, the image displays locally and the sync layer logs a broadcast warning.
- `M_NetworkedPhotoSync` clears stale local image state when a client joins, then applies any latest shared image replayed by `XRINetworkPlayer`.
- Passthrough state is controlled by `M_PassthroughModeController`, which enables the Meta XR `OVRPassthroughLayer`, makes the XR camera transparent, and hides opaque scene renderers so real headset passthrough is visible.
- `M_PhoneImportHeadsetMode` remains available for explicit pairing prompts in scenes that opt into it; the active `PhonePhotoUpload` scene uses the summonable user menu instead.
- `M_PassthroughHandVisualController` hides visual-only hand/controller meshes during passthrough while leaving tracking, rays, and input components active.
- Detailed passthrough/menu validation steps are in `docs/QUEST_USER_MENU_AND_PASSTHROUGH.md`.
- This is a local-network prototype. If the router blocks peer-to-peer devices, the phone may not reach the Quest IP.
- Public domains such as `share.mufb.com` will not work unless DNS/local routing is configured to resolve that name to the current Quest IP. The headset should normally show local IP URLs.
- The current repository implements the local lab-network upload path. A remote upload backend is not present in this project.
