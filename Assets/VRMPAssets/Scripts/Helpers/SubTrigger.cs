/*
 * Script Name: SubTrigger.cs
 * Author: Mason Prather
 * Description: Sub Trigger provides reusable scene helpers for following, pooling, resetting, toggling, clamping, and XR affordance behavior.
 * Project Role: Utility layer used across shared VR scenes and prefabs.
 */

using System;
using UnityEngine;

namespace XRMultiplayer
{
    /// <summary>
    /// A simple class used for callbacks when OnTriggerEnter or OnTriggerExit is called.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SubTrigger : MonoBehaviour
    {
        public Action<Collider, bool> OnTriggerAction;
        public Collider subTriggerCollider;

        private void Awake()
        {
            if (subTriggerCollider == null)
                TryGetComponent(out subTriggerCollider);
        }

        private void OnTriggerEnter(Collider other)
        {
            OnTriggerAction?.Invoke(other, true);
        }

        private void OnTriggerExit(Collider other)
        {
            OnTriggerAction?.Invoke(other, false);
        }
    }
}
