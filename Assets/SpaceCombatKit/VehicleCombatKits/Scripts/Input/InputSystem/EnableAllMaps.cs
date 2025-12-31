using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VSX.Utilities
{
    /// <summary>
    /// Enable all the maps on a Player Input component.
    /// </summary>
    [DefaultExecutionOrder(5)]
    [RequireComponent(typeof(PlayerInput))]
    public class EnableAllMaps : MonoBehaviour
    {
        protected PlayerInput playerInput;

        protected virtual void Awake()
        {
            playerInput = GetComponent<PlayerInput>();
        }


        protected virtual void OnEnable()
        {
            foreach (InputActionMap map in playerInput.actions.actionMaps)
            {
                map.Enable();
            }
        }
    }
}
