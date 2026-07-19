using UnityEngine;

// Draws a live "ghost" of this object's mesh via the editor gizmo pass while external
// edits stream in (SceneBridge Studio / in-chat editor). Unity serves the scene camera
// image from a cache while the editor app is unfocused - meshes look frozen - but the
// gizmo overlay still repaints. IMPORTANT: Gizmos.DrawMesh defers into the (cached)
// camera queue and freezes too; only IMMEDIATE drawing survives in the background -
// Graphics.DrawMeshNow and gizmo line primitives. The bridge toggles Active during bursts.
public class SpatialLiveGhost : MonoBehaviour
{
    public static bool Active; // set by SpatialBridge during edit bursts

#if UNITY_EDITOR
    static Material s_Mat;
    static Mesh s_Bake; // reused scratch mesh for skinned pose bakes (OnDrawGizmos runs many times/sec)

    void OnDrawGizmos()
    {
        if (!Active) return;

        if (s_Mat == null)
        {
            var sh = Shader.Find("Hidden/Internal-Colored");
            if (sh == null) return;
            s_Mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            s_Mat.SetInt("_ZWrite", 0);
            s_Mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            s_Mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        }

        // Ghost this object AND its children - dragging a prefab root should ghost the
        // whole vehicle (body + wheels), not just the root's own mesh (roots are often empty).
        var filters = GetComponentsInChildren<MeshFilter>();
        int drawn = 0;
        s_Mat.SetColor("_Color", new Color(0.30f, 0.64f, 1f, 0.30f));
        s_Mat.SetPass(0);
        foreach (var mf in filters)
        {
            if (mf == null || mf.sharedMesh == null) continue;
            if (drawn++ > 60) break; // perf guard for huge hierarchies
            Graphics.DrawMeshNow(mf.sharedMesh, Matrix4x4.TRS(mf.transform.position, mf.transform.rotation, mf.transform.lossyScale));
        }
        // Skinned characters: bake the live pose so bone edits ghost too.
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (smr == null || smr.sharedMesh == null) continue;
            if (drawn++ > 60) break;
            if (s_Bake == null) s_Bake = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            smr.BakeMesh(s_Bake, false); // overwrites s_Bake; drawn immediately before the next bake
            Graphics.DrawMeshNow(s_Bake, Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, smr.transform.lossyScale));
        }
        if (drawn == 0) return;

        // Seed the wire bounds from the first renderer, not transform.position: roots are often
        // empty and sit far from the meshes, which would stretch the box to include the pivot.
        Bounds wb = default; bool have = false;
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (r == null) continue;
            if (!have) { wb = r.bounds; have = true; }
            else wb.Encapsulate(r.bounds);
        }
        if (!have) return;
        Gizmos.color = new Color(0.30f, 0.64f, 1f, 0.95f);
        Gizmos.DrawWireCube(wb.center, wb.size);
    }
#endif
}
