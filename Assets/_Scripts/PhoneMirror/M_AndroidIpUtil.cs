/*
 * Script Name: M_AndroidIpUtil.cs
 * Author: Mason Prather
 * Description: Reads the Android Wi-Fi IP address used by local phone mirroring and signaling flows.
 * Project Role: Platform utility for Quest-hosted LAN services that need a reachable headset address.
 * Key Inputs: Android Wi-Fi manager state when running on device.
 * Key Outputs: Local IPv4 address string or 0.0.0.0 fallback.
 */

using UnityEngine;

public static class M_AndroidIpUtil
{
#if UNITY_ANDROID && !UNITY_EDITOR
    public static string GetLocalWifiIp()
    {
        try
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var wifiManager = activity.Call<AndroidJavaObject>("getSystemService", "wifi");
            using var wifiInfo = wifiManager.Call<AndroidJavaObject>("getConnectionInfo");
            int ipInt = wifiInfo.Call<int>("getIpAddress");

            return string.Format("{0}.{1}.{2}.{3}",
                ipInt & 0xff,
                (ipInt >> 8) & 0xff,
                (ipInt >> 16) & 0xff,
                (ipInt >> 24) & 0xff
            );
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[M_AndroidIpUtil] Failed to fetch WiFi IP: {e.Message}");
            return "0.0.0.0";
        }
    }
#else
    public static string GetLocalWifiIp() => "0.0.0.0";
#endif
}
