/*
 * Script Name: M_LocalMediaNotificationAudio.cs
 * Description: Plays local-only notification sounds when new shared media is applied to the local scene display.
 * Project Role: Non-networked local feedback for image and video uploads/receives.
 */

using UnityEngine;

[DisallowMultipleComponent]
public class M_LocalMediaNotificationAudio : MonoBehaviour
{
    [Header("Notification Clips")]
    [SerializeField] private AudioClip newImageClip;
    [SerializeField] private AudioClip newVideoClip;

    [Header("Playback")]
    [SerializeField, Range(0f, 1f)] private float volume = 1f;
    [SerializeField] private float duplicateWindowSeconds = 1.5f;
    [SerializeField] private AudioSource notificationAudioSource;

    private string _lastNotificationSignature;
    private float _lastNotificationTime = -999f;

    private void Awake()
    {
        EnsureAudioSource();
    }

    public void ConfigureClips(AudioClip imageClip, AudioClip videoClip)
    {
        if (newImageClip == null)
            newImageClip = imageClip;

        if (newVideoClip == null)
            newVideoClip = videoClip;
    }

    public void PlayNewImage(string mediaName = null)
    {
        PlayNotification(newImageClip, "image", mediaName);
    }

    public void PlayNewVideo(string mediaName = null)
    {
        PlayNotification(newVideoClip, "video", mediaName);
    }

    private void PlayNotification(AudioClip clip, string kind, string mediaName)
    {
        if (!isActiveAndEnabled || clip == null)
            return;

        string signature = $"{kind}:{mediaName ?? string.Empty}";
        if (IsDuplicate(signature))
            return;

        AudioSource audioSource = EnsureAudioSource();
        if (audioSource == null)
            return;

        _lastNotificationSignature = signature;
        _lastNotificationTime = Time.unscaledTime;
        audioSource.PlayOneShot(clip, volume);
    }

    private bool IsDuplicate(string signature)
    {
        if (duplicateWindowSeconds <= 0f)
            return false;

        return string.Equals(signature, _lastNotificationSignature, System.StringComparison.Ordinal) &&
               Time.unscaledTime - _lastNotificationTime <= duplicateWindowSeconds;
    }

    private AudioSource EnsureAudioSource()
    {
        if (notificationAudioSource != null)
        {
            ConfigureAudioSource(notificationAudioSource);
            return notificationAudioSource;
        }

        Transform child = transform.Find("LocalMediaNotificationAudio");
        if (child == null)
        {
            GameObject childObject = new GameObject("LocalMediaNotificationAudio");
            childObject.transform.SetParent(transform, worldPositionStays: false);
            child = childObject.transform;
        }

        notificationAudioSource = child.GetComponent<AudioSource>();
        if (notificationAudioSource == null)
            notificationAudioSource = child.gameObject.AddComponent<AudioSource>();

        ConfigureAudioSource(notificationAudioSource);
        return notificationAudioSource;
    }

    private static void ConfigureAudioSource(AudioSource audioSource)
    {
        if (audioSource == null)
            return;

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        audioSource.dopplerLevel = 0f;
        audioSource.priority = 64;
    }
}
