/*
 * Script Name: Key.cs
 * Author: Mason Prather
 * Description: Key provides key/keychain/lock data structures for gated interactable behavior.
 * Project Role: Interaction helper used by keyed scene objects.
 */

namespace UnityEngine.XR.Content.Interaction
{
    /// <summary>
    /// An asset that represents a key. Used to check if an object can perform some action
    /// (<see cref="XRLockSocketInteractor"/> and <see cref="Keychain"/>)
    /// </summary>
    [CreateAssetMenuAttribute(menuName = "XR/Key Lock System/Key")]
    public class Key : ScriptableObject
    { }
}
