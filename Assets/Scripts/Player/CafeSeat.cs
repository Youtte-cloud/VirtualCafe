using UnityEngine;
namespace VirtualCafe.Player
{
    public sealed class CafeSeat : MonoBehaviour
    {
        public Vector3 EyePosition { get; private set; }
        public Vector3 Forward { get; private set; }
        public bool Occupied { get; set; }
        public void Configure(Bounds bounds, Vector3 forward)
        {
            EyePosition = new Vector3(bounds.center.x, bounds.min.y + .45f + .7f, bounds.center.z);
            Forward = forward.sqrMagnitude > .01f ? forward.normalized : transform.forward;
        }
    }
}
