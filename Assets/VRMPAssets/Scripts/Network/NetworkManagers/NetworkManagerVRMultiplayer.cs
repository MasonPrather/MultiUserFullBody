/*
 * Script Name: NetworkManagerVRMultiplayer.cs
 * Author: Mason Prather
 * Description: Applies project NetworkManager settings for log level, background execution, and Netcode configuration at scene startup.
 * Project Role: Netcode configuration component for shared VR scenes.
 * Key Inputs: Serialized LogLevel, RunInBackground, and NetworkConfig fields.
 * Key Outputs: Configured NetworkManager state and project logging level.
 */

using Unity.Netcode;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace XRMultiplayer
{
    /// <summary>
    /// Manages the network functionality for VR multiplayer.
    /// </summary>
    public class NetworkManagerVRMultiplayer : NetworkManager
    {
        [SerializeField, Tooltip("Controls Netcode logging detail for project sessions.")]
        LogLevel m_LogLevel;

        [SerializeField, Tooltip("Keeps the session active while the application is not focused.")]
        bool m_RunInBackground = true;

        [SerializeField]
        NetworkConfig m_NetworkConfig;

        ///<inheritdoc/>
        void Awake()
        {
            LogLevel = m_LogLevel;
            RunInBackground = m_RunInBackground;
            NetworkConfig = m_NetworkConfig;
            Utils.s_LogLevel = LogLevel;
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(NetworkManagerVRMultiplayer))]
    class VRMultiplayerNetworkManagerEditor : Editor
    {
        /// <summary>
        /// This function is called when the inspector is drawn.
        /// </summary>
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (Application.isPlaying)
            {
                switch (XRINetworkGameManager.CurrentConnectionState.Value)
                {
                    case XRINetworkGameManager.ConnectionState.None:
                        GUILayout.Box("Authenticating");
                        break;
                    case XRINetworkGameManager.ConnectionState.Authenticating:
                        GUILayout.Box("Authenticating");
                        break;
                    case XRINetworkGameManager.ConnectionState.Authenticated:
                        if (GUILayout.Button("Connect"))
                        {
                            XRINetworkGameManager.Instance.QuickJoinLobby();
                        }
                        break;
                    case XRINetworkGameManager.ConnectionState.Connecting:
                        GUILayout.Box("Connecting");
                        break;
                    case XRINetworkGameManager.ConnectionState.Connected:
                        if (GUILayout.Button("Disconnect"))
                        {
                            XRINetworkGameManager.Instance.Disconnect();
                        }
                        break;
                }
            }
            else
            {
                GUILayout.Box("Game not running.");
            }
        }
    }
#endif
}
