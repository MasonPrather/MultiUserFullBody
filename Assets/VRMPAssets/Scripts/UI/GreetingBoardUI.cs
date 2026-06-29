/*
 * Script Name: GreetingBoardUI.cs
 * Author: Mason Prather
 * Description: Greeting Board UI controls local menu, tooltip, popout, greeting, and world-canvas UI behavior.
 * Project Role: User interface layer for shared VR scenes.
 */

using TMPro;
using UnityEngine;
using XRMultiplayer;

public class GreetingBoardUI : MonoBehaviour
{
    [SerializeField]
    TMP_Text m_RoomNameText;

    [SerializeField]
    TMP_Text m_RoomCodeText;

    [SerializeField]
    TMP_Text m_RoomCodeLabelText;

    private void OnEnable()
    {
        XRINetworkGameManager.Connected.Subscribe(ConnectedToGame);
        XRINetworkGameManager.ConnectedRoomName.Subscribe(UpdateRoomName);
    }

    void Start()
    {
        if (XRINetworkGameManager.CurrentSessionType == SessionType.LocalOnly)
            m_RoomCodeLabelText.text = "Connected IP";
    }

    private void OnDisable()
    {
        XRINetworkGameManager.Connected.Unsubscribe(ConnectedToGame);
        XRINetworkGameManager.ConnectedRoomName.Unsubscribe(UpdateRoomName);
    }

    void ConnectedToGame(bool connected)
    {
        if (connected)
        {
            m_RoomNameText.text = XRINetworkGameManager.ConnectedRoomName.Value;
            m_RoomCodeText.text = XRINetworkGameManager.ConnectedRoomCode;
        }
    }

    void UpdateRoomName(string roomName)
    {
        m_RoomNameText.text = roomName;
    }
}
