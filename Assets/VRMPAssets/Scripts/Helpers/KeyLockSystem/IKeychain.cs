/*
 * Script Name: IKeychain.cs
 * Author: Mason Prather
 * Description: IKeychain provides key/keychain/lock data structures for gated interactable behavior.
 * Project Role: Interaction helper used by keyed scene objects.
 */

namespace UnityEngine.XR.Content.Interaction
{
    /// <summary>
    /// Interface to implement for objects that hold a set of <c>Key</c>s
    /// </summary>
    public interface IKeychain
    {
        /// <summary>
        /// This callback is used to check if this keychain has a specific <c>Key</c>
        /// <see cref="Lock"/>
        /// </summary>
        /// <param name="key">the key to be checked</param>
        /// <returns>True if this keychain has the supplied key; false otherwise</returns>
        bool Contains(Key key);
    }
}
