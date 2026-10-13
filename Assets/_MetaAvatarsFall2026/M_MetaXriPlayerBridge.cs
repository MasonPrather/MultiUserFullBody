using MUFB.Avatars;
using Unity.Netcode;
using UnityEngine;
using XRMultiplayer;

// Keep the template's names, player list, voice and media RPCs, while Meta owns the visible body.
public sealed class M_MetaXriPlayerBridge : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;
        var rig = M_MetaAvatarRig.Instance;
        if (!rig) return;
        GetComponent<XRINetworkPlayer>().SetHandOrigins(rig.LeftHand, rig.RightHand);
    }
}
