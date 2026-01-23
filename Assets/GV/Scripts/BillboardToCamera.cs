using UnityEngine;

namespace VSX.Engines3D
{
    public class BillboardToCamera : MonoBehaviour
    {
        public Camera targetCamera;
        private Camera mainCamera;

        void Start()
        {
            if (targetCamera == null) targetCamera = Camera.main;
        }

        void LateUpdate()
        {
            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            
            if (cam != null)
            {
                // Look at the camera, but reverse direction so the front of the UI faces the camera
                transform.LookAt(transform.position + cam.transform.rotation * Vector3.forward,
                                 cam.transform.rotation * Vector3.up);
            }
        }
    }
}
