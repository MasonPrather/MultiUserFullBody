/*
 * Script Name: PlayerListInitializer.cs
 * Author: Mason Prather
 * Description: Player List Initializer displays connected player entries and per-player list rows.
 * Project Role: Menu UI support for multiplayer participant awareness.
 */

using UnityEngine;

namespace XRMultiplayer
{
    public class PlayerListInitializer : MonoBehaviour
    {
        [SerializeField] PlayerListUI[] m_PlayerListUIs;

        void Start()
        {
            foreach (var l in m_PlayerListUIs)
            {
                l.InitializeCallbacks();
            }
        }
    }
}
