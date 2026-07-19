// SceneBridge - localhost HTTP bridge exposing the active Unity scene to an AI agent and
// applying edits back (Undo-recorded). Editor-only; drop under any "Editor" folder.
// Survives domain reloads. Accepts local (localhost/127.0.0.1) connections only.
// GET /help returns the full endpoint map + agent routing protocol; /ping reports the
// running version. Endpoints: /ping /help /scene /apply /build /spawn_prefab /delete
// /rename /undo /marker /markers /refresh (+ /spawn_demo /reset_demo sandbox helpers).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpatialBridge
{
    [InitializeOnLoad]
    internal static class SpatialBridgeServer
    {
        // Port range so multiple Unity projects (or a team) can each run a bridge without
        // colliding. Each editor claims the first free port; clients discover it via the
        // project-local port file (Library/scenebridge.port) or by scanning this range.
        const int PortStart = 8787;
        const int PortCount = 10; // 8787..8796
        const int VertCapPer = 20000;
        const int VertBudget = 250000;
        static HttpListener _listener;
        static int _port = PortStart;
        static readonly ConcurrentQueue<HttpListenerContext> _queue = new ConcurrentQueue<HttpListenerContext>();

        static SpatialBridgeServer()
        {
            // Asset-import worker processes load editor assemblies too - without this guard
            // a worker can win the race for port 8787 during a domain reload and the real
            // editor's listener fails with "address already in use".
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload -= StopListener;
            AssemblyReloadEvents.beforeAssemblyReload += StopListener;
            EditorApplication.focusChanged -= OnEditorFocusChanged;
            EditorApplication.focusChanged += OnEditorFocusChanged;
            EditorApplication.quitting -= StopListener;
            EditorApplication.quitting += StopListener;
            StartListener();
        }

        static void StartListener()
        {
            StopListener();
            for (int i = 0; i < PortCount; i++)
            {
                int port = PortStart + i;
                try
                {
                    var l = new HttpListener();
                    l.Prefixes.Add("http://localhost:" + port + "/");
                    // Mono binds "localhost" to [::1] only - add IPv4 loopback explicitly so
                    // clients on 127.0.0.1 (node, curl) also reach us.
                    l.Prefixes.Add("http://127.0.0.1:" + port + "/");
                    l.Start();
                    _listener = l;
                    _port = port;
                    l.BeginGetContext(OnContext, null);
                    WritePortFile(port);
                    Debug.Log("[SpatialBridge] listening on http://localhost:" + port + "/  (project: " + Application.productName + ")");
                    return;
                }
                catch { /* port busy - try the next one */ }
            }
            Debug.LogWarning("[SpatialBridge] no free port in " + PortStart + ".." + (PortStart + PortCount - 1) + " - close another Unity editor or free a port.");
            _listener = null;
        }

        // Publish the chosen port to a project-local file so this project's MCP server / Studio
        // connect to THIS project's bridge even when several are running on different ports.
        static void WritePortFile(int port)
        {
            try
            {
                var root = Directory.GetParent(Application.dataPath).FullName; // <project>/ (dataPath = <project>/Assets)
                File.WriteAllText(Path.Combine(root, "Library", "scenebridge.port"), port.ToString());
            }
            catch { }
        }

        static void StopListener()
        {
            try { if (_listener != null) { _listener.Stop(); _listener.Close(); } }
            catch { }
            _listener = null;
        }

        static void OnContext(IAsyncResult ar)
        {
            var l = _listener;
            if (l == null) return;
            try { _queue.Enqueue(l.EndGetContext(ar)); }
            catch { }
            try { if (_listener != null) _listener.BeginGetContext(OnContext, null); }
            catch { }
        }

        static void Pump()
        {
            PumpRepaint(); // sustained repaint pressure after mutations (see RepaintViews)
            while (_queue.TryDequeue(out var ctx))
            {
                try { Handle(ctx); }
                catch (Exception e) { try { Write(ctx, 500, "{\"error\":\"" + Esc(e.Message) + "\"}"); } catch { } }
            }
        }

        static void Handle(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath;
            string method = ctx.Request.HttpMethod;
            if (method == "OPTIONS") { Write(ctx, 200, "{}"); return; }
            if (!OriginOk(ctx)) { Write(ctx, 403, "{\"error\":\"cross-origin request blocked; SceneBridge only accepts local connections\"}"); return; }
            if (path == "/" || path == "/ping")
            {
                Write(ctx, 200, "{\"ok\":true,\"bridge\":\"SceneBridge\",\"version\":\"0.9.5\",\"port\":" + _port + ",\"project\":\"" + Esc(Application.productName)
                    + "\",\"scene\":\"" + Esc(SceneManager.GetActiveScene().name) + "\",\"help\":\"GET /help for the agent routing protocol\"}");
                return;
            }
            if (path == "/help")
            {
                // Self-describing routing protocol - an agent that discovers the bridge learns
                // which surface to use without external docs. Full guide: docs/agent-protocol.md.
                Write(ctx, 200,
                  "{\"bridge\":\"SceneBridge\",\"port\":" + _port + ",\"multi_project\":\"Each Unity editor claims the first free port in 8787..8796 and writes it to <project>/Library/scenebridge.port. Clients read that file (or scan the range) to reach THIS project's bridge - several can run at once.\",\"principle\":\"Use the smallest surface that does the job. A change you can specify precisely is an HTTP call, not a visual.\","
                  + "\"first_moves\":[\"GET /ping (is Unity open?)\",\"GET /scene?light=1 (cheap map: transforms+kinds+paths, no geometry - orient here)\"],"
                  + "\"surfaces\":{"
                  + "\"http\":\"Direct edits you can specify. Zero cost. Default for single/few-object changes and creation.\","
                  + "\"in_chat_widget\":\"Focused 3D editor for 1-few models when a position is AMBIGUOUS and a human should point/approve, or to place markers. Bake ONLY the focus object(s) at full geometry (every vertex = agent tokens); neighbors as boxes; rest as names.\","
                  + "\"studio\":\"http://localhost:8791 - full live editor for BIG scenes and REAL-TIME dragging/posing. Browser<->bridge direct, ~0 cost.\","
                  + "\"artifact\":\"Full-fidelity VIEW-ONLY snapshot to share. Generated disk->disk, ~0 cost even for millions of verts.\"},"
                  + "\"endpoints\":{"
                  + "\"GET /scene?light=1\":\"structural map, no geometry. Add &find=<substr> to return only matching nodes on a big scene\",\"GET /scene\":\"full incl. meshes+skinning\","
                  + "\"POST /apply\":\"{edits:[{id,position?[3],rotationEuler?[3],scale?[3]}]} - omit a channel to leave it; non-finite rejected\","
                  + "\"POST /build\":\"{root,items:[{shape,name,parent,position,rotationEuler,scale,color:[r,g,b] 0..1 OR colorHex:'#RRGGBB'}]} - batch primitives, returns name->id\","
                  + "\"POST /spawn_prefab\":\"{path,name?,position?[3]}\",\"POST /delete\":\"{id}\",\"POST /rename\":\"{id,newName}\",\"POST /undo\":\"{}\","
                  + "\"POST /marker\":\"{parentId,name,type:Point|Axis|Plane|Volume|Path,worldPosition[3],...}\",\"GET /markers\":\"live (orphans filtered)\"},"
                  + "\"conventions\":\"Unity left-handed, Y-up, +Z forward. Rotations: Unity Quaternion.Euler == three.js euler order YXZ. Metres. Instance ids reassign on recompile - key on hierarchy path.\","
                  + "\"post_bodies\":\"JSON with Content-Type: text/plain (skips CORS preflight)\"}");
                return;
            }
            if (path == "/undo" && method == "POST") { Undo.PerformUndo(); Write(ctx, 200, "{\"undone\":true}"); RepaintViews(); return; }
            if (path == "/rename" && method == "POST") { Write(ctx, 200, RenameObject(ReadBody(ctx))); RepaintViews(); return; }
            if (path == "/scene" && method == "GET") { Write(ctx, 200, DumpScene(ctx.Request.QueryString["light"] == "1", ctx.Request.QueryString["find"])); return; }
            if (path == "/apply" && method == "POST") { Write(ctx, 200, ApplyEdits(ReadBody(ctx))); RepaintViews(); return; }
            if (path == "/spawn_demo" && method == "POST") { Write(ctx, 200, SpawnDemo()); RepaintViews(); return; }
            if (path == "/spawn_prefab" && method == "POST") { Write(ctx, 200, SpawnPrefab(ReadBody(ctx))); RepaintViews(); return; }
            if (path == "/delete" && method == "POST") { Write(ctx, 200, DeleteObject(ReadBody(ctx))); RepaintViews(); return; }
            if (path == "/build" && method == "POST") { Write(ctx, 200, Build(ReadBody(ctx))); RepaintViews(); return; }
            if (path == "/reset_demo" && method == "POST") { Write(ctx, 200, ResetDemo()); RepaintViews(); return; }
            if ((path == "/anchor" || path == "/marker") && method == "POST") { Write(ctx, 200, CreateMarker(ReadBody(ctx))); RepaintViews(); return; }
            if (path == "/markers" && method == "GET") { Write(ctx, 200, GetMarkers()); return; }
            if (path == "/refresh" && method == "POST") { Write(ctx, 200, RefreshAssets()); return; }
            Write(ctx, 404, "{\"error\":\"no route " + Esc(path) + "\"}");
        }

        static string DumpScene(bool light = false, string find = null)
        {
            if (string.IsNullOrEmpty(find)) find = null;
            var sb = new StringBuilder(1 << 16);
            var scene = SceneManager.GetActiveScene();
            sb.Append("{\"project\":\"").Append(Esc(Application.productName)).Append("\",\"scene\":\"").Append(Esc(scene.name)).Append("\",\"nodes\":[");
            int nodeCount = 0, meshCount = 0, vertTotal = 0, dropped = 0;
            bool first = true;
            var roots = scene.GetRootGameObjects();
            var stack = new Stack<Transform>();
            for (int i = roots.Length - 1; i >= 0; i--) stack.Push(roots[i].transform);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                for (int i = t.childCount - 1; i >= 0; i--) stack.Push(t.GetChild(i));
                var go = t.gameObject;
                // Targeted query: ?find=<substr> returns only matching nodes (name or path),
                // so agents can orient in a big scene without pulling the whole hierarchy.
                // (Children are already pushed above, so descendants still get matched.)
                if (find != null &&
                    go.name.IndexOf(find, StringComparison.OrdinalIgnoreCase) < 0 &&
                    GetPath(t).IndexOf(find, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!first) sb.Append(',');
                first = false;
                nodeCount++;
                Vector3 p = t.position, e = t.eulerAngles, s = t.lossyScale;
                sb.Append("{\"id\":").Append(go.GetInstanceID());
                sb.Append(",\"name\":\"").Append(Esc(go.name)).Append("\"");
                sb.Append(",\"path\":\"").Append(Esc(GetPath(t))).Append("\"");
                sb.Append(",\"active\":").Append(go.activeInHierarchy ? "true" : "false");
                sb.Append(",\"t\":{\"p\":[").Append(F(p.x)).Append(',').Append(F(p.y)).Append(',').Append(F(p.z))
                  .Append("],\"r\":[").Append(F(e.x)).Append(',').Append(F(e.y)).Append(',').Append(F(e.z))
                  .Append("],\"s\":[").Append(F(s.x)).Append(',').Append(F(s.y)).Append(',').Append(F(s.z)).Append("]}");
                // Exact LOCAL transform - viewers with a real hierarchy mirror these verbatim.
                // (World lossyScale reconstruction drifts under rotated chains; locals cannot.)
                Vector3 lp = t.localPosition, le = t.localEulerAngles, ls = t.localScale;
                sb.Append(",\"l\":{\"p\":[").Append(F(lp.x)).Append(',').Append(F(lp.y)).Append(',').Append(F(lp.z))
                  .Append("],\"r\":[").Append(F(le.x)).Append(',').Append(F(le.y)).Append(',').Append(F(le.z))
                  .Append("],\"s\":[").Append(F(ls.x)).Append(',').Append(F(ls.y)).Append(',').Append(F(ls.z)).Append("]}");

                var mf = go.GetComponent<MeshFilter>();
                var mr = go.GetComponent<MeshRenderer>();
                var smr = go.GetComponent<SkinnedMeshRenderer>();
                bool wroteMesh = false;
                // Light mode: transforms + kinds only, no geometry. Viewers poll this fast
                // and fetch the full dump only when the node set actually changes.
                if (light)
                {
                    bool isMesh = (mf != null && mf.sharedMesh != null && mr != null) || (smr != null && smr.sharedMesh != null);
                    if (isMesh)
                    {
                        if (smr != null) sb.Append(",\"skinned\":true");
                        sb.Append(",\"kind\":\"mesh\",\"movable\":true");
                        meshCount++;
                    }
                    else if (mr != null || smr != null)
                    {
                        var lb = mr != null ? mr.bounds : smr.bounds;
                        sb.Append(",\"box\":{\"c\":[").Append(F(lb.center.x)).Append(',').Append(F(lb.center.y)).Append(',').Append(F(lb.center.z))
                          .Append("],\"size\":[").Append(F(lb.size.x)).Append(',').Append(F(lb.size.y)).Append(',').Append(F(lb.size.z)).Append("]}");
                        sb.Append(",\"kind\":\"box\",\"movable\":false");
                    }
                    else
                    {
                        string lk = go.GetComponent<Camera>() != null ? "camera" : (go.GetComponent<Light>() != null ? "light" : "empty");
                        sb.Append(",\"kind\":\"").Append(lk).Append("\",\"movable\":true");
                    }
                    sb.Append('}');
                    continue;
                }
                if (mf != null && mf.sharedMesh != null && mr != null
                    && mf.sharedMesh.vertexCount <= VertCapPer && vertTotal + mf.sharedMesh.vertexCount <= VertBudget)
                {
                    // isReadable is a runtime restriction - in the editor the import data is
                    // present, so real meshes export even with Read/Write disabled on the asset.
                    // Roll the buffer back cleanly if a read still fails.
                    int mark = sb.Length;
                    try
                    {
                        AppendMesh(sb, mf.sharedMesh);
                        AppendTint(sb, mr);
                        sb.Append(",\"kind\":\"mesh\",\"movable\":true");
                        meshCount++; vertTotal += mf.sharedMesh.vertexCount;
                        wroteMesh = true;
                    }
                    catch { sb.Length = mark; wroteMesh = false; }
                }
                // Skinned meshes (characters): export the BIND-POSE mesh plus skin weights,
                // bind matrices and bone node ids - the viewer runs its own skinning, so
                // bone edits deform the character instantly client-side (no re-bake round trip).
                if (!wroteMesh && smr != null && smr.sharedMesh != null
                    && smr.sharedMesh.vertexCount <= VertCapPer && vertTotal + smr.sharedMesh.vertexCount <= VertBudget)
                {
                    int mark = sb.Length;
                    try
                    {
                        var mesh = smr.sharedMesh;
                        AppendMesh(sb, mesh);
                        AppendTint(sb, smr);
                        var bws = mesh.boneWeights;
                        var bones = smr.bones;
                        var binds = mesh.bindposes;
                        if (bws != null && bws.Length == mesh.vertexCount && bones != null && binds != null
                            && binds.Length == bones.Length && bones.Length > 0)
                        {
                            sb.Append(",\"bi\":[");
                            for (int k = 0; k < bws.Length; k++) { if (k > 0) sb.Append(','); sb.Append(bws[k].boneIndex0).Append(',').Append(bws[k].boneIndex1).Append(',').Append(bws[k].boneIndex2).Append(',').Append(bws[k].boneIndex3); }
                            sb.Append("],\"bw\":[");
                            for (int k = 0; k < bws.Length; k++) { if (k > 0) sb.Append(','); sb.Append(F(bws[k].weight0)).Append(',').Append(F(bws[k].weight1)).Append(',').Append(F(bws[k].weight2)).Append(',').Append(F(bws[k].weight3)); }
                            sb.Append("],\"bp\":[");
                            bool first16 = true;
                            foreach (var bpm in binds)
                                for (int c = 0; c < 4; c++)
                                    for (int r = 0; r < 4; r++)
                                    { if (!first16) sb.Append(','); first16 = false; sb.Append(F(bpm[r, c])); }
                            sb.Append("],\"bids\":[");
                            for (int k = 0; k < bones.Length; k++) { if (k > 0) sb.Append(','); sb.Append(bones[k] != null ? bones[k].gameObject.GetInstanceID() : 0); }
                            sb.Append(']');
                        }
                        sb.Append(",\"skinned\":true,\"kind\":\"mesh\",\"movable\":true");
                        meshCount++; vertTotal += mesh.vertexCount;
                        wroteMesh = true;
                    }
                    catch { sb.Length = mark; wroteMesh = false; }
                }
                if (!wroteMesh && (mr != null || smr != null))
                {
                    var b = mr != null ? mr.bounds : smr.bounds;
                    sb.Append(",\"box\":{\"c\":[").Append(F(b.center.x)).Append(',').Append(F(b.center.y)).Append(',').Append(F(b.center.z))
                      .Append("],\"size\":[").Append(F(b.size.x)).Append(',').Append(F(b.size.y)).Append(',').Append(F(b.size.z)).Append("]}");
                    sb.Append(",\"kind\":\"box\",\"movable\":false");
                    if (mf != null && mf.sharedMesh != null) dropped++;
                }
                else if (!wroteMesh)
                {
                    string kind = go.GetComponent<Camera>() != null ? "camera" : (go.GetComponent<Light>() != null ? "light" : "empty");
                    sb.Append(",\"kind\":\"").Append(kind).Append("\",\"movable\":true");
                }
                sb.Append('}');
            }
            sb.Append("],\"stats\":{\"nodes\":").Append(nodeCount).Append(",\"meshes\":").Append(meshCount)
              .Append(",\"verts\":").Append(vertTotal).Append(",\"boundsFallback\":").Append(dropped).Append("}}");
            return sb.ToString();
        }

        static void AppendTint(StringBuilder sb, Renderer mr)
        {
            var m = mr.sharedMaterial;
            if (m == null) return;
            Color c;
            if (m.HasProperty("_BaseColor")) c = m.GetColor("_BaseColor");
            else if (m.HasProperty("_Color")) c = m.GetColor("_Color");
            else return;
            sb.Append(",\"tint\":[").Append(F(c.r)).Append(',').Append(F(c.g)).Append(',').Append(F(c.b)).Append(']');
        }

        static void AppendMesh(StringBuilder sb, Mesh mesh)
        {
            var verts = mesh.vertices; var tris = mesh.triangles;
            sb.Append(",\"mesh\":{\"v\":[");
            for (int i = 0; i < verts.Length; i++) { if (i > 0) sb.Append(','); var v = verts[i]; sb.Append(F(v.x)).Append(',').Append(F(v.y)).Append(',').Append(F(v.z)); }
            sb.Append("],\"i\":[");
            for (int i = 0; i < tris.Length; i++) { if (i > 0) sb.Append(','); sb.Append(tris[i]); }
            sb.Append("]}");
        }

        [Serializable] class Edit { public int id; public float[] position; public float[] rotationEuler; public float[] scale; }
        [Serializable] class ApplyBody { public Edit[] edits; }

        static bool AllFinite(float[] a)
        {
            for (int i = 0; i < a.Length; i++) if (float.IsNaN(a[i]) || float.IsInfinity(a[i])) return false;
            return true;
        }

        // Unity doesn't repaint editor views while backgrounded, so external edits land
        // invisibly until the window regains focus. A single RepaintAll issued while the
        // app is inactive gets swallowed, so instead we hold repaint PRESSURE: for a few
        // seconds after any mutation, every EditorApplication.update tick re-issues the
        // repaint. Requires Preferences > General > Interaction Mode = "No Throttling"
        // so the update loop keeps running at full rate in the background.
        static double _repaintUntil;
        static bool _alwaysRefreshOn;
        // When the editor regains focus the real render snaps into place immediately -
        // collapse the ghost window to a split second so the user sees the ghost and the
        // real mesh line up, then the ghost gets out of the way.
        static void OnEditorFocusChanged(bool focused)
        {
            if (focused && _repaintUntil > EditorApplication.timeSinceStartup + 0.125)
                _repaintUntil = EditorApplication.timeSinceStartup + 0.125;
        }
        static void RepaintViews()
        {
            _repaintUntil = EditorApplication.timeSinceStartup + 1.5;
            PumpRepaint();
        }
        static void PumpRepaint()
        {
            bool active = EditorApplication.timeSinceStartup < _repaintUntil;
            // Repaints alone re-blit a CACHED camera image while the editor is backgrounded:
            // overlays (gizmos, selection wireframes) redraw live but meshes stay stale.
            // Two-part fix: (a) "Always Refresh" (+ its FX master switch) upgrades repaints to
            // full renders; (b) an imperceptible camera-pivot jiggle (+/-1e-5, alternating)
            // invalidates any camera-keyed render cache every tick during an edit burst.
            if (active != _alwaysRefreshOn)
            {
                _alwaysRefreshOn = active;
                try
                {
                    foreach (SceneView sv in SceneView.sceneViews)
                    {
                        sv.sceneViewState.fxEnabled = true;
                        sv.sceneViewState.alwaysRefresh = active;
                    }
                }
                catch (Exception e) { Debug.LogWarning("[SpatialBridge] alwaysRefresh toggle failed: " + e.Message); }
            }
            SpatialLiveGhost.Active = active;
            if (!active) return;
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        static string ApplyEdits(string body)
        {
            ApplyBody data;
            try { data = JsonUtility.FromJson<ApplyBody>(body); }
            catch (Exception e) { return "{\"error\":\"bad json: " + Esc(e.Message) + "\"}"; }
            if (data == null || data.edits == null) return "{\"error\":\"expected {edits:[...]}\"}";
            int applied = 0; var missing = new List<int>();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            foreach (var ed in data.edits)
            {
                var obj = EditorUtility.InstanceIDToObject(ed.id) as GameObject;
                if (obj == null) { missing.Add(ed.id); continue; }
                Undo.RecordObject(obj.transform, "SpatialBridge Apply");
                // Reject non-finite values: JsonUtility parses NaN/Infinity happily, and one
                // poisoned transform breaks physics AND makes /scene emit invalid JSON.
                if (ed.position != null && ed.position.Length == 3 && AllFinite(ed.position)) obj.transform.position = new Vector3(ed.position[0], ed.position[1], ed.position[2]);
                if (ed.rotationEuler != null && ed.rotationEuler.Length == 3 && AllFinite(ed.rotationEuler)) obj.transform.rotation = Quaternion.Euler(ed.rotationEuler[0], ed.rotationEuler[1], ed.rotationEuler[2]);
                if (ed.scale != null && ed.scale.Length == 3 && AllFinite(ed.scale)) obj.transform.localScale = new Vector3(ed.scale[0], ed.scale[1], ed.scale[2]);
                EditorUtility.SetDirty(obj.transform);
                // Live-ghost: gizmos render in the background while the camera image is cached,
                // so edited objects ghost their mesh through the gizmo pass (see SpatialLiveGhost).
                if (obj.GetComponent<SpatialLiveGhost>() == null)
                {
                    var ghost = obj.AddComponent<SpatialLiveGhost>();
                    ghost.hideFlags = HideFlags.DontSave | HideFlags.HideInInspector;
                }
                applied++;
            }
            Undo.CollapseUndoOperations(group);
            if (applied > 0) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            var sb = new StringBuilder();
            sb.Append("{\"applied\":").Append(applied).Append(",\"missing\":[");
            for (int i = 0; i < missing.Count; i++) { if (i > 0) sb.Append(','); sb.Append(missing[i]); }
            sb.Append("]}");
            return sb.ToString();
        }

        [Serializable] class BuildItem { public string shape; public string name; public string parent; public float[] position; public float[] rotationEuler; public float[] scale; public float[] color; public string colorHex; }

        // Be liberal in what we accept: agents reach for hex color strings ("#3CB043") as often
        // as [r,g,b] arrays. Resolve either into linear-ish RGB; return null if neither is valid.
        static float[] ResolveColor(BuildItem it)
        {
            if (it.color != null && it.color.Length >= 3) return it.color;
            string h = it.colorHex;
            // A hex string may also arrive mis-parsed into the `color` field name; JsonUtility
            // drops it, so we only see it via colorHex - but tolerate a leading '#' either way.
            if (string.IsNullOrEmpty(h)) return null;
            h = h.Trim().TrimStart('#');
            if (h.Length == 3) h = "" + h[0] + h[0] + h[1] + h[1] + h[2] + h[2];
            if (h.Length != 6) return null;
            try
            {
                float r = Convert.ToInt32(h.Substring(0, 2), 16) / 255f;
                float g = Convert.ToInt32(h.Substring(2, 2), 16) / 255f;
                float b = Convert.ToInt32(h.Substring(4, 2), 16) / 255f;
                return new[] { r, g, b };
            }
            catch { return null; }
        }
        [Serializable] class BuildBody { public string root; public BuildItem[] items; }

        // Batch primitive builder - the AI's tool for constructing scenes (buildings, roads,
        // props) from primitives in one Undo-grouped call. Groups nest under root/parent paths
        // which are auto-created. Returns name->id so the caller can address what it built.
        static string Build(string body)
        {
            BuildBody data;
            try { data = JsonUtility.FromJson<BuildBody>(body); }
            catch (Exception e) { return "{\"error\":\"bad json: " + Esc(e.Message) + "\"}"; }
            if (data == null || data.items == null || data.items.Length == 0) return "{\"error\":\"expected {root,items:[...]}\"}";

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            var rootName = string.IsNullOrEmpty(data.root) ? "Built" : data.root;
            var rootGo = GameObject.Find("/" + rootName);
            if (rootGo == null) { rootGo = new GameObject(rootName); Undo.RegisterCreatedObjectUndo(rootGo, "SpatialBridge Build"); }

            var containers = new Dictionary<string, Transform>();
            var mats = new Dictionary<int, Material>();
            var sb = new StringBuilder();
            sb.Append("{\"root\":\"").Append(Esc(rootName)).Append("\",\"built\":");
            int built = 0; var idPairs = new List<string>();

            foreach (var it in data.items)
            {
                if (it == null || string.IsNullOrEmpty(it.shape)) continue;
                PrimitiveType pt;
                switch (it.shape.ToLowerInvariant())
                {
                    case "cube": pt = PrimitiveType.Cube; break;
                    case "plane": pt = PrimitiveType.Plane; break;
                    case "cylinder": pt = PrimitiveType.Cylinder; break;
                    case "sphere": pt = PrimitiveType.Sphere; break;
                    case "capsule": pt = PrimitiveType.Capsule; break;
                    case "quad": pt = PrimitiveType.Quad; break;
                    default: continue;
                }
                var g = GameObject.CreatePrimitive(pt);
                g.name = string.IsNullOrEmpty(it.name) ? it.shape : it.name;

                Transform parent = rootGo.transform;
                if (!string.IsNullOrEmpty(it.parent))
                {
                    if (!containers.TryGetValue(it.parent, out parent))
                    {
                        var cgo = new GameObject(it.parent);
                        Undo.RegisterCreatedObjectUndo(cgo, "SpatialBridge Build");
                        cgo.transform.SetParent(rootGo.transform, false);
                        parent = cgo.transform;
                        containers[it.parent] = parent;
                    }
                }
                g.transform.SetParent(parent, false);
                if (it.position != null && it.position.Length == 3 && AllFinite(it.position)) g.transform.localPosition = new Vector3(it.position[0], it.position[1], it.position[2]);
                if (it.rotationEuler != null && it.rotationEuler.Length == 3 && AllFinite(it.rotationEuler)) g.transform.localRotation = Quaternion.Euler(it.rotationEuler[0], it.rotationEuler[1], it.rotationEuler[2]);
                if (it.scale != null && it.scale.Length == 3 && AllFinite(it.scale)) g.transform.localScale = new Vector3(it.scale[0], it.scale[1], it.scale[2]);

                var rgb = ResolveColor(it);
                if (rgb != null)
                {
                    var mr = g.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        int key = Mathf.RoundToInt(rgb[0] * 255) << 16 | Mathf.RoundToInt(rgb[1] * 255) << 8 | Mathf.RoundToInt(rgb[2] * 255);
                        if (!mats.TryGetValue(key, out var mat))
                        {
                            mat = new Material(mr.sharedMaterial) { hideFlags = HideFlags.None };
                            var c = new Color(rgb[0], rgb[1], rgb[2]);
                            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
                            mats[key] = mat;
                        }
                        mr.sharedMaterial = mat;
                    }
                }
                Undo.RegisterCreatedObjectUndo(g, "SpatialBridge Build");
                idPairs.Add("\"" + Esc(g.name) + "\":" + g.GetInstanceID());
                built++;
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            sb.Append(built).Append(",\"ids\":{").Append(string.Join(",", idPairs)).Append("}}");
            return sb.ToString();
        }

        [Serializable] class DeleteBody { public int id; }
        static string DeleteObject(string body)
        {
            DeleteBody data;
            try { data = JsonUtility.FromJson<DeleteBody>(body); }
            catch (Exception e) { return "{\"error\":\"bad json: " + Esc(e.Message) + "\"}"; }
            if (data == null || data.id == 0) return "{\"error\":\"expected {id}\"}";
            var go = EditorUtility.InstanceIDToObject(data.id) as GameObject;
            if (go == null) return "{\"error\":\"object " + data.id + " not found\"}";
            string name = go.name;
            Undo.DestroyObjectImmediate(go);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return "{\"deleted\":\"" + Esc(name) + "\"}";
        }

        [Serializable] class SpawnBody { public string path; public string name; public float[] position; }
        static string SpawnPrefab(string body)
        {
            SpawnBody data;
            try { data = JsonUtility.FromJson<SpawnBody>(body); }
            catch (Exception e) { return "{\"error\":\"bad json: " + Esc(e.Message) + "\"}"; }
            if (data == null || string.IsNullOrEmpty(data.path)) return "{\"error\":\"expected {path:'Assets/...prefab'}\"}";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(data.path);
            if (prefab == null) return "{\"error\":\"no prefab at " + Esc(data.path) + "\"}";
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (!string.IsNullOrEmpty(data.name)) go.name = data.name;
            if (data.position != null && data.position.Length == 3 && AllFinite(data.position))
                go.transform.position = new Vector3(data.position[0], data.position[1], data.position[2]);
            Undo.RegisterCreatedObjectUndo(go, "SpatialBridge Spawn Prefab");
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            return "{\"spawned\":\"" + Esc(go.name) + "\",\"id\":" + go.GetInstanceID()
                + ",\"bounds\":{\"c\":[" + F(b.center.x) + "," + F(b.center.y) + "," + F(b.center.z)
                + "],\"size\":[" + F(b.size.x) + "," + F(b.size.y) + "," + F(b.size.z) + "]}}";
        }

        static string SpawnDemo()
        {
            var root = new GameObject("SB_TestRig");
            Undo.RegisterCreatedObjectUndo(root, "SpatialBridge Spawn Demo");
            Action<string, Vector3, Vector3, Transform> cube = (n, pos, scl, parent) =>
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                g.name = n; g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = scl;
                Undo.RegisterCreatedObjectUndo(g, "SpatialBridge Spawn Demo");
            };
            Action<string, Vector3, Transform> cyl = (n, pos, parent) =>
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                g.name = n; g.transform.SetParent(parent, false); g.transform.localPosition = pos;
                g.transform.localRotation = Quaternion.Euler(0, 0, 90); g.transform.localScale = new Vector3(0.6f, 0.2f, 0.6f);
                Undo.RegisterCreatedObjectUndo(g, "SpatialBridge Spawn Demo");
            };
            cube("Chassis", new Vector3(0, 0.9f, 0), new Vector3(2.2f, 0.6f, 5.4f), root.transform);
            cube("Cab", new Vector3(0, 2.2f, 1.9f), new Vector3(2.4f, 2.2f, 2.0f), root.transform);
            cyl("Wheel_FL", new Vector3(1.2f, 0.6f, 1.4f), root.transform);
            cyl("Wheel_FR", new Vector3(-1.2f, 0.6f, 1.4f), root.transform);
            cyl("Wheel_RL", new Vector3(1.2f, 0.6f, -1.6f), root.transform);
            cyl("Wheel_RR", new Vector3(-1.2f, 0.6f, -1.6f), root.transform);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return "{\"spawned\":\"SB_TestRig\"}";
        }

        static string ResetDemo()
        {
            var existing = GameObject.Find("SB_TestRig");
            if (existing != null) Undo.DestroyObjectImmediate(existing);
            var sp = SidecarPath();
            if (File.Exists(sp)) AssetDatabase.DeleteAsset(sp);
            SpawnDemo();
            return "{\"reset\":true}";
        }

        // ---- Spatial Marker system: one envelope, five types (Point|Axis|Plane|Volume|Path) ----
        [Serializable] class MarkerReq {
            public int parentId; public string id; public string name; public string label; public string type;
            public float[] worldPosition; public float[] worldRotation; public float[] normal; public bool hasFacing;
            public float length; public bool hasLimits; public float limitMin; public float limitMax;
            public bool infinitePlane; public float[] planeExtents2D;
            public string volumeShape; public float[] halfExtents; public float radius; public float height; public int capsuleAxis;
            public bool closed; public string interp; public float[] knots; public bool visual;
        }
        [Serializable] class MarkerRecord {
            public string id; public string name; public string label; public string type; public string host;
            public float[] local; public float[] world; public float[] rotation;
            public float length; public bool hasLimits; public float limitMin; public float limitMax;
            public bool infinitePlane; public float[] planeExtents2D;
            public string volumeShape; public float[] halfExtents; public float radius; public float height; public int capsuleAxis;
            public bool closed; public string interp; public float[] knots; public bool hasFacing; public bool visual; public string created;
        }
        [Serializable] class Sidecar { public string scene; public string generated; public MarkerRecord[] anchors; }

        static Vector3 V3(float[] a) { return new Vector3(a[0], a[1], a[2]); }

        static string CreateMarker(string body)
        {
            MarkerReq req;
            try { req = JsonUtility.FromJson<MarkerReq>(body); }
            catch (Exception e) { return "{\"error\":\"bad json: " + Esc(e.Message) + "\"}"; }
            if (req == null || string.IsNullOrEmpty(req.name) || req.worldPosition == null || req.worldPosition.Length != 3)
                return "{\"error\":\"expected {parentId,name,type,worldPosition[3]}\"}";
            // Reject non-finite input on every geometry channel: JsonUtility parses NaN/Infinity,
            // and one poisoned value makes the response and /markers emit invalid JSON.
            if (!AllFinite(req.worldPosition)
                || (req.worldRotation != null && !AllFinite(req.worldRotation))
                || (req.normal != null && !AllFinite(req.normal))
                || (req.knots != null && !AllFinite(req.knots)))
                return "{\"error\":\"non-finite value in marker (NaN/Infinity)\"}";
            string type = string.IsNullOrEmpty(req.type) ? "Point" : req.type;
            var parent = EditorUtility.InstanceIDToObject(req.parentId) as GameObject;
            // A marker without a valid host is a phantom - refuse rather than silently
            // creating orphans at scene root (stress test finding).
            if (parent == null) return "{\"error\":\"parent id " + req.parentId + " not found in scene\"}";
            Vector3 wp = V3(req.worldPosition);
            Quaternion rot = Quaternion.identity;
            if (req.worldRotation != null && req.worldRotation.Length == 4)
                rot = new Quaternion(req.worldRotation[0], req.worldRotation[1], req.worldRotation[2], req.worldRotation[3]);
            else if (req.normal != null && req.normal.Length == 3) { var n = V3(req.normal); if (n.sqrMagnitude > 1e-4f) rot = Quaternion.FromToRotation(Vector3.up, n.normalized); }

            Transform group = null;
            if (parent != null)
            {
                var gt = parent.transform.Find("AIAnchors");
                if (gt == null)
                {
                    var g = new GameObject("AIAnchors");
                    Undo.RegisterCreatedObjectUndo(g, "Create AIAnchors");
                    g.transform.SetParent(parent.transform, false);
                    g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity; g.transform.localScale = Vector3.one;
                    group = g.transform;
                }
                else { group = gt; var ex = gt.Find(req.name); if (ex != null) Undo.DestroyObjectImmediate(ex.gameObject); }
            }
            var anchor = new GameObject(req.name);
            Undo.RegisterCreatedObjectUndo(anchor, "Create Marker " + req.name);
            if (group != null) anchor.transform.SetParent(group, true);
            anchor.transform.position = wp;
            anchor.transform.rotation = rot;

            if (type == "Volume")
            {
                string shape = string.IsNullOrEmpty(req.volumeShape) ? "box" : req.volumeShape;
                if (shape == "sphere") { var c = anchor.AddComponent<SphereCollider>(); c.isTrigger = true; c.radius = req.radius > 0 ? req.radius : 0.5f; }
                else if (shape == "capsule") { var c = anchor.AddComponent<CapsuleCollider>(); c.isTrigger = true; c.radius = req.radius > 0 ? req.radius : 0.5f; c.height = req.height > 0 ? req.height : 2f; c.direction = req.capsuleAxis; }
                else { var c = anchor.AddComponent<BoxCollider>(); c.isTrigger = true; if (req.halfExtents != null && req.halfExtents.Length == 3) c.size = new Vector3(req.halfExtents[0] * 2f, req.halfExtents[1] * 2f, req.halfExtents[2] * 2f); }
            }
            else if (type == "Path" && req.knots != null && req.knots.Length >= 6)
            {
                int kn = req.knots.Length / 3;
                for (int k = 0; k < kn; k++)
                {
                    var kg = new GameObject("Knot_" + k);
                    Undo.RegisterCreatedObjectUndo(kg, "Create Knot");
                    kg.transform.SetParent(anchor.transform, true);
                    kg.transform.position = new Vector3(req.knots[k * 3], req.knots[k * 3 + 1], req.knots[k * 3 + 2]);
                }
            }

            if (req.visual) anchor.AddComponent<SpatialMarkerGizmo>();

            Vector3 local = parent != null ? parent.transform.InverseTransformPoint(wp) : wp;
            var rec = new MarkerRecord {
                id = string.IsNullOrEmpty(req.id) ? req.name : req.id,
                name = req.name, label = string.IsNullOrEmpty(req.label) ? req.name : req.label, type = type,
                host = parent != null ? GetPath(parent.transform) : "",
                local = new float[] { local.x, local.y, local.z },
                world = new float[] { wp.x, wp.y, wp.z },
                rotation = new float[] { rot.x, rot.y, rot.z, rot.w },
                length = req.length, hasLimits = req.hasLimits, limitMin = req.limitMin, limitMax = req.limitMax,
                infinitePlane = req.infinitePlane, planeExtents2D = req.planeExtents2D,
                volumeShape = req.volumeShape, halfExtents = req.halfExtents, radius = req.radius, height = req.height, capsuleAxis = req.capsuleAxis,
                closed = req.closed, interp = req.interp, knots = req.knots, hasFacing = req.hasFacing, visual = req.visual,
                created = DateTime.UtcNow.ToString("o")
            };
            UpsertSidecar(rec);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return "{\"created\":{\"id\":" + anchor.GetInstanceID() + ",\"name\":\"" + Esc(req.name) + "\",\"type\":\"" + Esc(type)
                + "\",\"path\":\"" + Esc(GetPath(anchor.transform)) + "\",\"localPosition\":[" + F(local.x) + "," + F(local.y) + "," + F(local.z) + "]}}";
        }

        static string GetMarkers()
        {
            // The sidecar keeps records even after their host objects are deleted from the
            // scene (it's the persistence layer). The LIVE feed only reports markers whose
            // host still exists, so viewers drop orphans instead of showing ghosts.
            var sc = LoadSidecar();
            if (sc.anchors != null && sc.anchors.Length > 0)
            {
                var live = new List<MarkerRecord>();
                foreach (var a in sc.anchors)
                    if (a != null && !string.IsNullOrEmpty(a.host) && GameObject.Find("/" + a.host) != null) live.Add(a);
                sc.anchors = live.ToArray();
            }
            return JsonUtility.ToJson(sc, false);
        }

        [Serializable] class RenameReq { public int id; public string newName; }

        static string RenameObject(string body)
        {
            RenameReq req;
            try { req = JsonUtility.FromJson<RenameReq>(body); }
            catch (Exception e) { return "{\"error\":\"bad json: " + Esc(e.Message) + "\"}"; }
            if (req == null || string.IsNullOrEmpty(req.newName)) return "{\"error\":\"expected {id,newName}\"}";
            var obj = EditorUtility.InstanceIDToObject(req.id) as GameObject;
            if (obj == null) return "{\"error\":\"object not found\"}";
            string oldName = obj.name;
            string newName = req.newName.Trim();
            Undo.RecordObject(obj, "Rename " + oldName);
            obj.name = newName;
            var p = obj.transform.parent;
            if (p != null && p.name == "AIAnchors" && p.parent != null)
            {
                var sc = LoadSidecar();
                string host = GetPath(p.parent);
                foreach (var m in sc.anchors)
                {
                    if (m != null && m.host == host && m.name == oldName)
                    {
                        m.name = newName;
                        if (string.IsNullOrEmpty(m.id) || m.id == oldName) m.id = newName;
                    }
                }
                sc.generated = DateTime.UtcNow.ToString("o");
                File.WriteAllText(SidecarPath(), JsonUtility.ToJson(sc, true));
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return "{\"renamed\":{\"id\":" + obj.GetInstanceID() + ",\"from\":\"" + Esc(oldName) + "\",\"to\":\"" + Esc(newName) + "\"}}";
        }

        static string SidecarPath() { return "Assets/SpatialAnchors/" + SceneManager.GetActiveScene().name + ".spatialmeta.json"; }

        static Sidecar LoadSidecar()
        {
            var path = SidecarPath();
            if (File.Exists(path))
            {
                try { var sc = JsonUtility.FromJson<Sidecar>(File.ReadAllText(path)); if (sc != null) { if (sc.anchors == null) sc.anchors = new MarkerRecord[0]; return sc; } } catch { }
            }
            return new Sidecar { scene = SceneManager.GetActiveScene().name, anchors = new MarkerRecord[0] };
        }

        static void UpsertSidecar(MarkerRecord rec)
        {
            var sc = LoadSidecar();
            var list = new List<MarkerRecord>(sc.anchors);
            list.RemoveAll(m => m != null && m.host == rec.host && m.name == rec.name);
            list.Add(rec);
            sc.anchors = list.ToArray();
            sc.scene = SceneManager.GetActiveScene().name;
            sc.generated = DateTime.UtcNow.ToString("o");
            var dir = "Assets/SpatialAnchors";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(SidecarPath(), JsonUtility.ToJson(sc, true));
            // No AssetDatabase.Refresh() here: the marker GameObject is already live in the scene;
            // the sidecar .json only needs importing for the Project window, which can wait for the
            // next focus/refresh. Skipping it keeps marker creation instant on the Unity side.
        }

        static string RefreshAssets()
        {
            AssetDatabase.Refresh();
            try { UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation(); } catch { }
            return "{\"refreshed\":true}";
        }

        const long MaxBodyBytes = 8 * 1024 * 1024; // reject absurd bodies before buffering them into a string
        static string ReadBody(HttpListenerContext ctx)
        {
            if (ctx.Request.ContentLength64 > MaxBodyBytes) return "";
            using (var r = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8))
                return r.ReadToEnd();
        }
        static string GetPath(Transform t)
        {
            var sbp = new StringBuilder(t.name);
            var p = t.parent;
            while (p != null) { sbp.Insert(0, p.name + "/"); p = p.parent; }
            return sbp.ToString();
        }
        static string F(float v) => v.ToString("0.######", CultureInfo.InvariantCulture);
        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.ToString();
        }
        // SceneBridge is a LOCAL tool. Browsers attach an Origin header on cross-site requests;
        // curl/node/the MCP server omit it, and Studio (a localhost page) sends a localhost
        // origin. Accepting only local or absent origins stops a malicious web page the user
        // visits from driving the bridge (CSRF) or reading the scene -- this also covers DNS
        // rebinding, since the page's origin stays remote even when its host resolves to 127.0.0.1.
        static bool OriginOk(HttpListenerContext ctx)
        {
            string origin = ctx.Request.Headers["Origin"];
            return string.IsNullOrEmpty(origin) || IsLocalOrigin(origin);
        }
        static bool IsLocalOrigin(string origin)
        {
            try
            {
                var h = new Uri(origin).Host;
                return h == "localhost" || h == "127.0.0.1" || h == "::1" || h == "[::1]";
            }
            catch { return false; }
        }
        static void Write(HttpListenerContext ctx, int status, string json)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json";
                // Reflect the caller's origin only when it is local; never wildcard. A remote page
                // therefore cannot read a response cross-origin, and OriginOk() (in Handle) has
                // already rejected any remote request outright.
                string origin = ctx.Request.Headers["Origin"];
                if (!string.IsNullOrEmpty(origin) && IsLocalOrigin(origin))
                {
                    ctx.Response.Headers["Access-Control-Allow-Origin"] = origin;
                    ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET,POST,DELETE,OPTIONS";
                    ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
                }
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }
    }
}
