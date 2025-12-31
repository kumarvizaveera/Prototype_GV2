using UnityEngine;

public class TeleportCooldown : MonoBehaviour
{
    float _nextAllowedTime = 0f;

    public bool CanTeleport() => Time.time >= _nextAllowedTime;

    public void SetCooldown(float seconds)
    {
        _nextAllowedTime = Time.time + Mathf.Max(0f, seconds);
    }
}
