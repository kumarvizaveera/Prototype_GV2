using UnityEngine;

namespace GV
{
    public class FaceCamera : MonoBehaviour
    {
        private Camera mainCamera;

        void Update()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }

            if (mainCamera != null)
            {
                // Align the object's rotation with the camera's rotation so that the object faces the camera.
                // This assumes the text/content is on the forward-facing plane of the object.
                transform.LookAt(transform.position + mainCamera.transform.rotation * Vector3.forward,
                                 mainCamera.transform.rotation * Vector3.up);
            }
        }
    }
}
