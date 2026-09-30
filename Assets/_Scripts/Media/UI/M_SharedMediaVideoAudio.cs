/*
 * Script Name: M_SharedMediaVideoAudio.cs
 * Description: Configures spatial audio playback for videos rendered on a shared media display.
 * Project Role: Keeps video audio emitted from the same GameObject that owns the shared media display surface.
 */

using UnityEngine;
using UnityEngine.Video;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class M_SharedMediaVideoAudio : MonoBehaviour
{
    [Header("Audio Source Defaults")]
    [SerializeField] private bool spatializeAudio = true;
    [SerializeField, Range(0f, 1f)] private float spatialBlend = 1f;
    [SerializeField] private float minDistance = 1.25f;
    [SerializeField] private float maxDistance = 12f;
    [SerializeField] private AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;

    private AudioSource _audioSource;

    public AudioSource AudioSource
    {
        get
        {
            EnsureAudioSource();
            return _audioSource;
        }
    }

    private void Awake()
    {
        EnsureAudioSource();
    }

    public AudioSource ConfigureForVideoPlayer(VideoPlayer videoPlayer)
    {
        EnsureAudioSource();
        ApplyDefaults();

        if (videoPlayer == null)
            return _audioSource;

        videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        videoPlayer.controlledAudioTrackCount = 1;
        videoPlayer.EnableAudioTrack(0, true);
        videoPlayer.SetTargetAudioSource(0, _audioSource);

        return _audioSource;
    }

    public void Stop()
    {
        EnsureAudioSource();
        _audioSource.Stop();
    }

    private void EnsureAudioSource()
    {
        if (_audioSource == null)
            _audioSource = GetComponent<AudioSource>();

        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();
    }

    private void ApplyDefaults()
    {
        _audioSource.playOnAwake = false;
        _audioSource.loop = false;
        _audioSource.spatialize = spatializeAudio;
        _audioSource.spatialBlend = spatialBlend;
        _audioSource.minDistance = Mathf.Max(0.01f, minDistance);
        _audioSource.maxDistance = Mathf.Max(_audioSource.minDistance, maxDistance);
        _audioSource.rolloffMode = rolloffMode;
        _audioSource.dopplerLevel = 0f;
    }
}
