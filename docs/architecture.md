# SceneBridge - Architecture & Technical Reference

A developer-facing deep dive: how the pieces fit, the wire protocol, the coordinate math, and the
design decisions behind them. For the routing rules an agent follows, see
[agent-protocol.md](agent-protocol.md); for the marker envelope, [marker-system.md](marker-system.md).

---

## 1. System overview

Everything is a client of **one HTTP bridge that runs inside the Unity editor**. Three surfaces talk to
it; an MCP server exposes it to the agent.

```
   AI agent (Claude / any MCP client)
      |                          \
      |  stdio JSON-RPC 2.0       \  bakes + relays
      v                           v
  MCP server  --HTTP-->  SpatialBridge (Unity editor)  <--HTTP--  Studio (browser)
  (tools/mcp)            HttpListener, localhost:8787..8796        (widget/studio.html)
                                 ^
                                 | disk -> disk snapshot
                           Artifact viewer (self-contained HTML)
```

- **Bridge** - the substrate. Editor-only C#. Every mutation is Undo-grouped. Local connections only.
- **MCP server** - zero-dependency Node stdio server; turns bridge endpoints into agent tools.
- **In-chat editor** - a three.js editor *baked* from the live scene into a chat widget; edits relay
  back through the agent.
- **Studio** - a localhost browser page that talks to the bridge **directly** (no sandbox), for
  real-time editing.
- **Artifact viewer** - a self-contained HTML snapshot, generated disk-to-disk, for view-only sharing.

**Routing principle:** the agent uses the *smallest surface that does the job*. A specifiable change is
a direct API call; an ambiguous placement is a focused in-chat editor; a whole level is Studio; a
share is the Artifact viewer.

---

## 2. The bridge (`Packages/com.uncostlydevs.scenebridge/Editor/SpatialBridge.cs`)

An `[InitializeOnLoad]` static class hosting a `System.Net.HttpListener`.

- **Lifecycle.** Starts on editor load. Requests are enqueued on a background `BeginGetContext`
  callback and **drained on the main thread** via `EditorApplication.update` (Unity APIs are
  main-thread-only). It survives domain reloads: `AssemblyReloadEvents.beforeAssemblyReload` stops the
  listener, and `InitializeOnLoad` restarts it after the reload. An
  `AssetDatabase.IsAssetImportWorkerProcess()` guard stops import-worker processes from racing for the
  port.
- **Binding.** Adds prefixes for both `http://localhost:<port>/` **and** `http://127.0.0.1:<port>/` -
  Mono binds `localhost` to IPv6 `[::1]` only, so the explicit IPv4 prefix is needed for `127.0.0.1`
  clients (node, curl).
- **Multi-port discovery.** Claims the **first free port in 8787..8796** and writes it to
  `<project>/Library/scenebridge.port`. Clients read that file (or scan the range), so several projects
  can each run a bridge without colliding.
- **Security.** Accepts **local origins only**: requests carrying a non-local `Origin` header are
  rejected with `403`, and responses reflect a local origin instead of a wildcard `*`. This blocks a
  web page you happen to visit from driving the bridge (CSRF) or reading your scene - and covers DNS
  rebinding, since the page origin stays remote even when its host resolves to `127.0.0.1`. Request
  bodies are size-capped, and every numeric channel is guarded against `NaN`/`Infinity` (which
  `JsonUtility` parses happily and which would otherwise poison a transform and emit invalid JSON).
- **Mutations.** `/apply`, `/build`, `/spawn_prefab`, `/delete`, `/rename`, `/marker` are all
  `Undo`-recorded and collapsed into one undo group, then `MarkSceneDirty`. `/undo` performs a Unity
  undo.
- **Background visibility (live ghost).** Unity serves the Scene camera from a cache while the editor
  is unfocused, so meshes look frozen during external drags. The bridge attaches a runtime
  `SpatialLiveGhost` to edited objects that redraws their mesh via **immediate-mode**
  `Graphics.DrawMeshNow` in the gizmo pass (the only channel that repaints live in the background);
  the real render snaps in on refocus.
- **Mesh export.** Reads mesh data even when the asset's `Read/Write` is disabled (import data is
  present in the editor). Skinned meshes export the **bind-pose mesh + bone weights + bindposes + bone
  node ids** so a viewer can run its own skinning client-side.

`Runtime/` holds `SpatialLiveGhost.cs` (editor-only ghost drawing) and `SpatialMarkerGizmo.cs` (marker
gizmo). They're in the Runtime assembly so the Editor assembly can reference them; both are inert in
player builds.

---

## 3. The MCP server (`tools/mcp/scenebridge-mcp.js`)

A **zero-dependency** Node process implementing the MCP **stdio transport** (newline-delimited
JSON-RPC 2.0: `initialize`, `tools/list`, `tools/call`, `ping`). It never force-exits on stdin end -
it lets the event loop drain in-flight calls.

**Tools** (each description is the routing contract the agent reads): `scenebridge_help`,
`scenebridge_scene_map` (light structural map, optional `find` filter), `scenebridge_scene_full`,
`scenebridge_inline_editor`, `scenebridge_apply`, `scenebridge_build`, `scenebridge_spawn_prefab`,
`scenebridge_delete`, `scenebridge_rename`, `scenebridge_create_marker`, `scenebridge_list_markers`,
`scenebridge_undo`.

**Project discovery.** The server separates `INSTALL_ROOT` (where the tool lives - holds the bake
script and template) from the **discovered project root**. `findProjectRoot()` walks up from
`process.cwd()` (Claude launches the server with cwd = the opened project), preferring a directory with
`Library/scenebridge.port`, else one with `Assets/`, else falling back to `INSTALL_ROOT`. This lets a
single **user-scope (global)** server target whichever of several open projects the current chat
belongs to; a per-project install still works because `INSTALL_ROOT` is then the project itself.
`SCENEBRIDGE_URL` overrides discovery (host + port).

**`scenebridge_inline_editor`.** Spawns the PowerShell baker with the resolved `-Port` and an
`-OutFile` in the OS temp dir, then returns the baked HTML for the agent to hand to `show_widget`. On
baker failure it surfaces the error instead of returning a stale file.

---

## 4. HTTP API

Base `http://localhost:<port>`. POST bodies are JSON with `Content-Type: text/plain` (a CORS "simple"
request, so no preflight for the local Studio client).

| Method | Route | Purpose |
|---|---|---|
| GET | `/ping` | Liveness + bridge version + port + project/scene |
| GET | `/help` | Self-describing routing protocol + endpoint map (JSON) |
| GET | `/scene?light=1` | Structural map: transforms + kinds + hierarchy paths, no geometry. `&find=<substr>` filters |
| GET | `/scene` | Full dump incl. mesh geometry + skinning |
| POST | `/apply` | `{edits:[{id, position?[3], rotationEuler?[3], scale?[3]}]}` - omit a channel to leave it; non-finite rejected |
| POST | `/build` | `{root, items:[{shape, name, parent, position, rotationEuler, scale, color:[r,g,b] 0..1 OR colorHex:"#RRGGBB"}]}` - batch primitives; returns `name -> id` |
| POST | `/spawn_prefab` | `{path, name?, position?[3]}` - instantiate a `.prefab` / `.fbx` |
| POST | `/delete` / `/rename` | `{id}` / `{id, newName}` |
| POST | `/marker` | `{parentId, name, type, worldPosition[3], ...}` - typed spatial marker |
| GET | `/markers` | Live markers whose host still exists (orphans filtered) |
| POST | `/undo` | Undo the last bridge operation |

---

## 5. Coordinate conventions

Unity is **left-handed, Y-up, +Z forward**; units are **metres**.

- **Rotation.** `Quaternion.Euler(x,y,z)` is **term-for-term identical** to three.js
  `Euler(x,y,z,'YXZ')` with the same positive angles (proven by algebra and multi-axis experiment).
  Not `ZXY` (differs in the sign of the second term of each component - invisible on single-axis
  rotations, wrong on multi-axis bone chains).
- **Display Z-mirror.** A raw coordinate copy renders a Unity scene mirror-flipped in three.js. Viewers
  apply a full mirror at the boundary: negate `z` on positions **and** vertices, **flip triangle
  winding** (a mirror inverts orientation), conjugate rotations by the mirror (negate `qx, qy`; keep
  `qz, qw`), and conjugate skinned bindposes (`M * bindpose * M`). Numeric fields display Unity-space
  values. The bridge's `/apply` always takes **Unity-space** values - the mirror is a viewer concern.
- **Locals over reconstructed worlds.** The bridge emits exact **local** transforms alongside world
  ones; reconstructing local scale from world `lossyScale` drifts under rotated chains, so viewers
  mirror the locals verbatim.
- **Ids.** Instance ids are reassigned on every domain reload. **Key on hierarchy path**, never a
  cached id.

---

## 6. The three surfaces (technical)

### In-chat editor (`widget/inline-editor.tmpl.html` + `tools/bake-inline-editor.ps1`)
A template baked from the live scene, emitted via `show_widget`. **Token economics** drive the design:
all widget geometry passes through the agent's context, and base64 tokenizes at ~2.5 chars/token, so
Unity primitives are emitted as **shared-geometry references** (a `cube`/`cyl` tag, not vertices) and
custom meshes ship as **base64 uint16-quantized** buffers the template dequantizes. Never bake a big
scene here. The **Apply** button relays `SPATIAL_APPLY {mirroredZ:true, edits:[...]}` via `sendPrompt`;
the agent un-mirrors and calls `/apply`. The chat widget's sandbox CSP blocks `fetch`, WebSocket,
WebRTC and arbitrary HTTPS (see section 8), so the agent is its only wire.

### Studio (`widget/studio.html` + `widget/studio-server.js`)
Served from a tiny local static server and opened in a browser. Being a **localhost page (not
sandboxed)** it talks to the bridge directly: ~80 ms throttled push-on-drag, ~500 ms pull-back,
`/scene?light=1` for structure with a full fetch only when the node set changes. It builds a
`THREE.SkinnedMesh` bound to the existing scene-graph bone nodes for **instant client-side skinning**,
does **channel-aware pushes** (only the edited channel(s), so scale doesn't drift), and **solo-move**
(counter-transform children so a parent moves without disturbing them).

### Artifact viewer (`tools/gen-artifact-viewer.ps1`)
Snapshots `/scene` + `/markers` into a **self-contained** HTML with three.js inlined (the artifact CSP
blocks external hosts too). Generated **disk-to-disk**, so it costs ~0 agent tokens even for millions
of verts. Untrusted scene data is escaped before splicing so an object named `...</script>...` can't
break out.

---

## 7. Spatial markers

One unified envelope - `{id, type, label, space (parent path + GlobalObjectId), frame (position +
quaternion), params, provenance}` - where **Point / Axis / Plane / Volume / Path** differ only in
`params`. Each maps to a real Unity representation: an empty oriented transform (Point/Axis/Plane), a
trigger `BoxCollider`/`SphereCollider`/`CapsuleCollider` (Volume), or child knot transforms (Path).
Persistence is **both** a native `Host/AIAnchors/<name>` child **and** a `.spatialmeta.json` sidecar,
so a correction survives FBX re-export and is paid for once. Conventions: orientation is a full
**quaternion** (never a bare point); dimensions live in `params`, **never baked into scale**; store
**local**, not world.

---

## 8. Why the in-chat editor isn't fully live

The chat widget runs on a sandboxed origin (`*.claudemcpcontent.com`) whose CSP was tested exhaustively:
`fetch` to localhost/127.0.0.1 is blocked, WebSocket to a live local server is blocked, WebRTC never
completes ICE (the sandbox filters the UDP), and arbitrary public HTTPS is blocked. Only allow-listed
CDN static assets and `sendPrompt` get out. That is *why* there are three surfaces: the in-chat editor
is a **baked snapshot + relay**, Studio (a real localhost page) is the **live** surface, and the
Artifact viewer is **disk-to-disk**.

---

## 9. Install topology

- **Unity side** - a UPM package (`com.uncostlydevs.scenebridge`) with Editor + Runtime assembly
  definitions. Install via Package Manager git URL
  (`...Unity-Scene-Bridge-.git?path=/Packages/com.uncostlydevs.scenebridge`) or embed the folder under a
  project's `Packages/`.
- **Agent side** - register the MCP server once at **user scope**
  (`claude mcp add scenebridge --scope user -- node <path>/tools/mcp/scenebridge-mcp.js`) so it's in
  every chat, or drop `.mcp.json` into a project root for a per-project install.

## 10. Requirements & platform

- **Unity** 2021.3+ (developed on Unity 6). **Node** 16+ for the MCP server.
- The bridge, MCP server, and web surfaces are cross-platform. The bake/generate scripts
  (`tools/*.ps1`) are **Windows PowerShell** today - the bridge is unaffected; only the in-chat bake
  and the Artifact generator need them.
