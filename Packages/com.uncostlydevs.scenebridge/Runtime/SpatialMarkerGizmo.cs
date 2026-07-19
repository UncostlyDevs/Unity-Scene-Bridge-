using UnityEngine;

// Runtime component (deliberately NOT under an Editor/ folder, so it can be attached to
// scene GameObjects). Draws an orange sphere in the Scene view so an otherwise-invisible
// spatial marker is easy to spot. Gizmos never render in the game or in a build, so this
// is purely an editor aid and does not affect the running game.
[DisallowMultipleComponent]
public class SpatialMarkerGizmo : MonoBehaviour
{
    public Color color = new Color(1f, 0.35f, 0.1f, 1f);
    public float radius = 0.12f;

    void OnDrawGizmos()
    {
        Gizmos.color = color;
        Gizmos.DrawSphere(transform.position, radius);
    }
}
