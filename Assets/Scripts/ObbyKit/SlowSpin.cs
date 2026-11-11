// =====================================================================================
//  SlowSpin.cs  —  ObbyKit
// -------------------------------------------------------------------------------------
//  Turns this object slowly around the world's up axis. The environment builder uses it
//  to make the background clouds drift around the course. Children orbit with it.
// =====================================================================================

using UnityEngine;

namespace ObbyKit
{
    public class SlowSpin : MonoBehaviour
    {
        [Tooltip("Degrees per second. Negative spins the other way.")]
        [SerializeField] private float degreesPerSecond = 0.5f;

        private void Update()
        {
            transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.World);
        }
    }
}
