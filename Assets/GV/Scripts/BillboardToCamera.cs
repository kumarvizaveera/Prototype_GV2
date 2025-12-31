using UnityEngine;

[ExecuteAlways]
public class BillboardToCamera : MonoBehaviour
{
    public Camera targetCamera;
    public bool keepUpright = true;

    void LateUpdate()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null) return;

        if (keepUpright)
        {
            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }
        else
        {
            transform.LookAt(transform.position + cam.transform.rotation * Vector3.forward,
                             cam.transform.rotation * Vector3.up);
        }
    }
}
