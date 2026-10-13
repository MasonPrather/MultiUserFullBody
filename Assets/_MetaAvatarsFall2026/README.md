# MUFBAvatars_Fall2026

Open `Assets/_Scenes/MUFBAvatars_Fall2026.unity` in Unity 6000.3.8f1. This uses the complete `PhonePhotoUpload` scene with an independent Meta Avatars SDK 40.0.1 avatar implementation.

## Avatar setup

- The XR origin, locomotion, interaction input, networking, user menus, voice, and phone-upload dependencies are copied from `PhonePhotoUpload`. `M_MetaAvatarRig` binds its camera and controller transforms to Meta Avatars in floor coordinates. The complete PhonePhotoUpload environment, gameplay objects, lighting, spawn/reset positions and object placements replace the previous conference room. The old environment and its menus are removed from this scene. The first-person camera uses a 2 cm near clip plane. The source scene itself is unchanged; shared XR Multiplayer scripts include the connection fixes described below.
- `LocalMetaAvatar` loads bundled Meta preset 7 at Light quality, with compute skinning and Meta's recommended shader configuration. Both Quest and desktop variants are in `StreamingAssets/MUFBAvatars_Fall2026`. No RPM URL or Meta account-avatar download is required. All participants currently use this same preset.
- `M_MetaAvatarInput` supplies head/controller poses relative to the moving player origin, controller buttons, and Meta's hand-tracking provider. Face and eye providers use supported hardware and granted permissions. The existing Vivox microphone capture drives Meta lip sync while joined to a voice channel.
- `OvrAvatarAnimationBehavior` blends tracked upper-body input with the SDK's default `RTR00003` legs rig and Mecanim controller. Full-body manifestation and local leg playback are enabled. Headset anchoring keeps the avatar aligned to the user’s view, and the SDK’s dynamic crouching uses both critical foot joints to bend the legs at floor contact. Legs are procedurally animated from movement/headset height; they are not independently measured foot tracking.
- Stand upright when tracking first becomes available to establish the sample controller's standing-height baseline. Sustained crouches are allowed; the sample's five-second automatic stand-up timeout is disabled.
- Interaction hand renderers are hidden once the Meta avatar is ready. Their data sources, joints, colliders, grab/poke/ray interactions, controller models, and cursor visuals stay available. The SDK's first-person view handles the local head visibility.

## Multiplayer

`MetaXriNetworkPlayer` is the player prefab for this scene. It keeps PhonePhotoUpload's `XRINetworkPlayer`, synchronized head/hand origins, player names, voice identity and media integration. The template mesh, template IK, hand animator and mesh-color scripts are removed. `M_MetaXriPlayerBridge` binds the tracked hand origins. `M_MetaNetworkAvatar` creates a third-person Meta avatar for each remote owner and sends the local Meta animation snapshot at 15 Hz through an owner-only peer RPC. Each packet includes the moving origin transform. Remote avatars retain the newest packet while their skeleton loads, then apply it with a 120 ms playback delay.

The source scene's Unity Multiplayer Services distributed-authority session setup replaces the previous Building Blocks matchmaking. **Host** creates a public room immediately. **Join** finds an open room with the same scene and application version; if no room exists it asks the colleague to Host, without silently creating a separate room. Both devices must use the new APK (protocol `2027`). The cloned scene stores regenerated NGO scene-object IDs, including prefab overrides; this prevents the shared control panel from failing scene synchronization on a joining client. Menus start at the connection panel, skipping the template avatar customization preview. The copied player list, mute/voice/settings and phone-upload menus remain available.

Authentication is coalesced across simultaneous requests. Session success waits for NGO connection and the local player object. Cancellation rejects and leaves late session results. Voice initialization/login/channel joining are serialized; Meta's microphone tap activates only once a Vivox channel exists. Lobby and voice still require internet and the project's configured Unity Services.

## Build and check

Use **Tools > MUFB > Use Quest Demo Profile**, then **Tools > MUFB > Build Quest Demo APK**. The `Quest Demo` Android profile overrides the shared scene list and includes only `MUFBAvatars_Fall2026`. The shared list retains the phone-upload selection. The build command validates the imported scene and NGO RPC generation before building a development APK at `Builds/MUFBAvatars_Fall2026.apk`; its build result is saved to `Builds/QuestDemoBuildReport.txt`.

Install that same APK on both standalone Quests. Launch it on both, stand upright while the avatars initialize, and grant the requested microphone permission for voice. Choose **Host** on one headset; after the room shows connected, choose **Join** on the other. Move apart with the sticks if the avatars initially overlap. No RPM service or personal Meta avatar download is needed. Keep the installed Meta Core, Interaction, Avatars 40.0.1, and Avatars Sample Assets packages; the SDK's import/build hooks prepare the core and behavior archives in StreamingAssets.

After Unity imports the new assets:

1. Open this scene and confirm the Console has no compilation, missing-script, or missing-avatar-resource errors.
2. On Quest, start upright, look down for the new body, move both hands/controllers, walk with the stick, turn, crouch, and hold the crouch for over five seconds. Confirm there are no overlapping hand meshes.
3. Verify the retained grab/poke/ray UI, locomotion, and media upload/display controls.
4. Host and join from two devices. Verify independent root movement, walking, sustained crouches, join-after-start, and disconnect/rejoin. Check remote mouth motion while speaking in Vivox. Check face/eye tracking separately on supported hardware.

Validation: serialized scene/prefab dependency checks; no new dangling scene references; Unity import and script compilation; generated NGO RPC handlers; imported scene and supporting prefabs checked for missing scripts; network player registration, rig references, authentication settings, and Quest profile checked in Unity. Use **Tools > MUFB > Validate Quest Demo** to repeat the Unity checks. Two-headset session behavior, voice, sustained crouching, and headset performance still require the device checks above.

Latest completed check (October 7, 2026, 10:51 PM EDT): the complete PhonePhotoUpload map is saved and the APK built with zero errors, then installed on the connected Quest Pro. Unity validation compared every Environment/Gameplay transform and active state, the XR origin, and offline/online reset positions against PhonePhotoUpload. Render, lightmap and navigation settings match the source. The source scene file remained unchanged. The user confirmed the accumulated floor correction keeps the feet above the floor while crouching. A Quest-host/editor-join test exchanged 896-byte avatar packets in both directions, initialized the remote Meta avatar on Quest, and the user confirmed that the separate avatar follows their movements. The reverse test also passed: Quest Quick Join connected to an editor-hosted room, sent its stream, and applied 240 returned poses with no scene-object synchronization errors in the captured logs. These tests use an opt-in editor echo of the Quest stream; independent tracking on two Quests remains unverified. The temporary session was stopped and the installed app was reopened normally, with no test copy or automatic connection.

The October 6 APK predates the PhonePhotoUpload migration and must not be used with the new prefab/protocol. Read `Builds/QuestDemoBuildReport.txt` for the latest completed build. Unity validation and device test results are recorded separately; a successful build alone does not establish two-headset behavior.

The Mac editor can test connections and packet exchange without the missing native Meta avatar framework; it logs that limitation and skips avatar rendering. Editor-only `join-echo`/`host-echo` test modes return a received Quest stream as the editor player so remote initialization can be tested on the headset. Normal sessions do not echo another player.

Development builds include an opt-in `mufb_test` Android intent extra (`loopback`, `host`, or `join`). Ordinary launches do not run these tests. Loopback puts a streamed copy two meters ahead to verify snapshot application without a second device. `[MetaAvatarHealth]`, `[MetaAvatarNet]`, and `[MetaAvatarTest]` logs distinguish tracked input, model loading, sent/received packets and successful remote playback.

Re-run the dependency check with `python3 tools/validate_meta_avatar_scene.py`. Both bundled `.glb.zst` files were also decompressed in memory and verified as complete GLB version 2 files.

## SDK support provenance

`SDKSupport` contains only the scripts and animation dependencies needed from Avatars SDK 40.0.1 `Samples~`, retaining their original GUIDs and license. Keep its `Oculus.AvatarSDK2.Samples` assembly name, which the SDK grants internal access. Do not import a second copy of these same sample scripts/GUIDs into Assets.

Local changes to the supplied support assets:

- Removed an unused `UnityEditor` import from `MecanimLegsAnimationController` so it compiles for Android.
- Measured leg locomotion using world displacement expressed in avatar space, while retaining avatar-local height for crouching. Removed an extra world-to-local conversion of the already-local head orientation and seeded the initial motion/height samples.
- Disabled `enableCrouchTimeout` in `RTR00003`.
- Replaced the sample crouch foot lookup by GameObject name with `GetSkeletonTransform(LeftFootBall)`. In Android builds monitored joints are named `AvatarCriticalJoint`, so the sample’s editor-only name lookup never found its foot reference. The local prefab includes both foot-ball critical joints. Floor correction accumulates against the lower support foot and releases as the user stands, instead of settling with persistent penetration. This remains the SDK’s procedural lower-body animation, not measured foot tracking.
- Removed four orphaned, unreferenced transitions in `RT_MecanimLegs.controller` whose destination asset is missing from the SDK distribution.

The service prefab derives from the sample manager, retaining compute-skinning resources, LOD budgets, and shader configuration. Unused sample input services and the optional debug-canvas reference were removed.
