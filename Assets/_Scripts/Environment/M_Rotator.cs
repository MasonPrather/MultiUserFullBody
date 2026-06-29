/*
 * Script Name: M_Rotator.cs
 * Author: Mason Prather
 * Description: Rotates assigned scene objects at a configured speed for ambient environment motion cues.
 * Project Role: Scene utility used by MultiUserFullBody environments that need steady, deterministic object rotation.
 * Key Inputs: Unity Transform state and the serialized rotationSpeed value.
 * Key Outputs: Per-frame transform rotation on the attached GameObject.
 */

using UnityEngine;

public class M_Rotator : MonoBehaviour
{
    [Header("Rotation Settings")]
    [Tooltip("Rotation speed in degrees per second")]
    [SerializeField] public float rotationSpeed = 90f;

    void Update()
    {
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
    }
}
