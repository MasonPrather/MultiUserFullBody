/*
 * Script Name: GameObjectToggle.cs
 * Author: Mason Prather
 * Description: Game Object Toggle provides reusable scene helpers for following, pooling, resetting, toggling, clamping, and XR affordance behavior.
 * Project Role: Utility layer used across shared VR scenes and prefabs.
 */

using UnityEngine;

namespace XRMultiplayer
{
    public class GameObjectToggle : MonoBehaviour
    {
        [SerializeField] GameObject[] objectsToToggle;

        public void ToggleObjects()
        {
            foreach (var obj in objectsToToggle)
            {
                obj.SetActive(!obj.activeSelf);
            }
        }
    }
}
