using UnityEngine;
using Fusion;
using TMPro;

namespace GV.Network
{
    /// <summary>
    /// Controls the 3D TextMeshPro label placed inside the ship prefab.
    /// Shows the player's role (Host / Client / Client 2, etc.) to opponents only.
    /// The label is hidden on your own ship — only opponents see it.
    ///
    /// Self-contained NetworkBehaviour — no NetworkManager.cs changes needed.
    ///
    /// Usage:
    ///   1. Place a 3D TextMeshPro inside your ship prefab wherever you want it.
    ///   2. Add this script to the player ship prefab (same object that has NetworkObject).
    ///   3. Drag your 3D TextMeshPro onto the "Label Text" field in the Inspector.
    /// </summary>
    public class PlayerLabelController : NetworkBehaviour
    {
        [Header("Label")]
        [Tooltip("The 3D TextMeshPro placed inside the ship prefab.")]
        [SerializeField] private TextMeshPro labelText;

        [Tooltip("If true, the label always faces the camera (billboard effect).")]
        [SerializeField] private bool faceCamera = true;

        /// <summary>
        /// Networked player number set by the host:
        ///   0 = Host
        ///   1 = Client (first client)
        ///   2 = Client 2, etc.
        /// </summary>
        [Networked] public int PlayerNumber { get; set; } = -1;

        private Camera mainCam;
        private bool isLocalPlayer;

        public override void Spawned()
        {
            // Host assigns the player number for this ship
            if (Object.HasStateAuthority)
            {
                AssignPlayerNumber();
            }

            // Determine if this is the local player's ship
            isLocalPlayer = Object.HasInputAuthority;

            if (labelText != null)
            {
                if (isLocalPlayer)
                {
                    // Hide label on our own ship
                    labelText.gameObject.SetActive(false);
                    Debug.Log($"[PlayerLabel] Local player ship — label hidden on {gameObject.name}");
                }
                else
                {
                    // Show label on opponent ships
                    labelText.gameObject.SetActive(true);
                    UpdateLabelText();
                    Debug.Log($"[PlayerLabel] Opponent ship — label visible on {gameObject.name}");
                }
            }

            mainCam = Camera.main;
        }

        private void AssignPlayerNumber()
        {
            PlayerRef owner = Object.InputAuthority;

            if (owner == Runner.LocalPlayer)
            {
                PlayerNumber = 0;
                Debug.Log($"[PlayerLabel] Assigned PlayerNumber=0 (Host) to {gameObject.name}");
            }
            else
            {
                int clientNumber = owner.PlayerId - 1;
                if (clientNumber < 1) clientNumber = 1;
                PlayerNumber = clientNumber;
                Debug.Log($"[PlayerLabel] Assigned PlayerNumber={clientNumber} (Client) to {gameObject.name}, PlayerId={owner.PlayerId}");
            }
        }

        private void UpdateLabelText()
        {
            if (labelText == null) return;

            if (PlayerNumber < 0)
            {
                labelText.text = "...";
                return;
            }

            if (PlayerNumber == 0)
            {
                labelText.text = "Host";
            }
            else if (PlayerNumber == 1)
            {
                labelText.text = "Client";
            }
            else
            {
                labelText.text = $"Client {PlayerNumber}";
            }
        }

        public override void Render()
        {
            if (isLocalPlayer || labelText == null) return;

            // Update text in case the networked value just arrived
            UpdateLabelText();

            // Billboard: face the camera so text is always readable
            if (faceCamera)
            {
                if (mainCam == null) mainCam = Camera.main;
                if (mainCam != null)
                {
                    labelText.transform.rotation = mainCam.transform.rotation;
                }
            }
        }
    }
}
