# SceneBridge - Agent Protocol

How an AI agent decides **which surface to use** when a user asks for something involving the
Unity scene. The rule mirrors normal tool selection: **reach for the smallest surface that does
the job.** Don't open a whole-scene editor to nudge one cube; don't bake 50k vertices into chat
to confirm a delete.

There is no single "SceneBridge tool." There are **four surfaces** over one HTTP bridge
(`http://localhost:8787`). Pick per request.

---

## TL;DR routing table

| The user wants... | Do this | Surface |
|---|---|---|
| A precise change to 1-few objects ("move cab to z=4", "delete that sign") | `POST /apply` | `/delete` directly, confirm in text | **HTTP only** |
| You to create 1-few objects ("add a mailbox", "make a bench") | `POST /build` (primitives) or `/spawn_prefab` (assets) | **HTTP only** |
| A change where the *position is ambiguous* / needs a human eye ("put the grip where a hand holds it") | Bake a **focused** in-chat widget of just those objects; AI proposes, human corrects, Applies | **In-chat widget** |
| To place spatial/semantic markers (hitch point, axis, stop zone, path) | Propose via marker tools in a focused widget, or `POST /marker` if unambiguous | **In-chat widget** / HTTP |
| To build or edit a **big scene / whole level**, or work in **real time** | Point them to **Studio**; or batch it yourself with `/build` + `/apply` | **Studio** |
| Just to **see / review / share** the scene | Generate the **Artifact viewer** (full fidelity, free) | **Artifact** |
| To know what's *in* the scene (a question, no change) | `GET /scene?light=1`, answer in text | **HTTP only** |

**Cost ladder** (cheapest -> most expensive in *agent tokens*):
`HTTP call` ~ `Artifact` ~ `Studio` (all ~0)  <<  `focused widget` (tokens per baked vertex)  <<  **`full-scene widget` (never for big scenes)**.

---

## First moves (always)

Before acting on any scene request:

1. `GET /ping` - is the bridge up? If not, tell the user to open Unity with the project (the
   SpatialBridge editor script auto-starts the server). Don't guess.
2. `GET /scene?light=1` - cheap structural map (transforms + kinds + hierarchy paths, **no
   geometry**, ~17 KB even for hundreds of nodes). Use it to find target ids, understand the
   hierarchy, and decide scope. **Never** `GET /scene` (full geometry) just to look around.

> **Port:** `8787` is only the *default*. The bridge claims the first free port in **8787..8796**
> and writes it to `<project>/Library/scenebridge.port`; clients read that file (or scan the range)
> so multiple open projects don't collide. The `scenebridge_*` MCP tools and the bake scripts do
> this discovery for you - the `localhost:8787` URLs below are shorthand for "the discovered port".

Instance ids are reassigned on every Unity domain reload - re-key on **path**, never cache ids
across a recompile.

---

## Decision tree

```
Request about the Unity scene
│
├─ Just a question ("what's in the scene?", "how many wheels?")
│     -> GET /scene?light=1 -> answer in text. No visual.
│
├─ A CHANGE (move / rotate / scale / create / delete / mark)
│   │
│   ├─ Target + values are UNAMBIGUOUS (numeric, or obviously implied)
│   │     -> POST /apply | /build | /spawn_prefab | /delete | /marker directly.
│   │       Confirm in text. NO widget. (This is most single-object edits.)
│   │
│   ├─ Position is AMBIGUOUS / needs a human to point or approve
│   │     -> Bake a FOCUSED in-chat widget (just the relevant object(s) + a few
│   │       context boxes). Propose your best guess, let the human correct & Apply.
│   │       This is the founding use case: "the AI proposes, the human fixes spatial ambiguity."
│   │
│   └─ MANY objects / whole level / iterative REAL-TIME sculpting
│         -> Human-driven: point them to Studio (browser pane or Edge).
│           Agent-driven: batch it with /build + /apply yourself, then optionally
│           show an Artifact viewer of the result.
│
└─ A VIEW ("show me", "review", "share this")
      ├─ Quick look / shareable / whole scene -> Artifact viewer (full fidelity, free).
      ├─ Look at ONE model up close          -> focused in-chat widget.
      └─ Look AND then edit live             -> Studio.
```

**Overriding principle:** if you can express the change as an HTTP call and you're confident in
the values, *just make it*. Surface a visual only when a human eye genuinely reduces ambiguity
or when the user asked to see it.

---

## The four surfaces

### 1. HTTP bridge - the substrate (always available)
Direct REST at `http://localhost:8787`. Zero agent-token cost, no geometry through context.
Every other surface is a client of this. **Default to this for any change you can specify.**

### 2. In-chat widget - *single models & small groupings, close-up specific editing*
A `show_widget` three.js editor baked from the live scene. The human edits and clicks **Apply**,
which relays the change back through the agent (`sendPrompt`) to `POST /apply`.
- **DO NOT hand-write a three.js widget** - you'll produce an ugly, inconsistent one. It's a
  first-class tool: **call `scenebridge_inline_editor`** (optional `focus:["name1","name2"]`) and
  **pass the HTML it returns straight to `show_widget`.** One call bakes the live scene into the
  polished template (Unity primitives become tiny shared-geometry references; real meshes ship as
  base64). *(No MCP loaded? the same baker is `tools/bake-inline-editor.ps1` -> read
  `widget/inline-editor.ready.html` -> `show_widget`.)* The widget carries the correct conventions
  (Z-mirror + YXZ) and an Apply button that emits `SPATIAL_APPLY {mirroredZ:true,...}` - when you
  receive that, un-mirror before `/apply` / `scenebridge_apply` (negate position.z; negate rot x and y).
- **Use for:** one model or a small group; spatial correction; marker placement; before/after
  proposals with an info diff.
- **Cost:** every baked vertex passes through the agent's context (base64 ~ 2.5 chars/token). So
  **bake only the focus object(s)** at full geometry; render neighbors as bounding boxes; list
  the rest as names. See `tools/bake-widget.ps1` (Focus / Context / Hidden tiers, `-ClusterGrid`
  decimation). **Never bake a big scene into a widget.**
- **Cannot** reach the bridge directly (sandbox CSP blocks fetch/WS/WebRTC/public-HTTPS - all
  tested). Its only outbound wire is `sendPrompt` -> the agent relays.

### 3. Studio - *big live scenes, real-time editing*
A full editor served at `http://localhost:8791/studio.html`, opened in the app's **Browser pane**
or a normal browser tab. Talks to the bridge **directly** (it's a localhost page, not sandboxed):
live ~80 ms push-on-drag, 0.5 s pull-back, hierarchy tree with per-object **eye toggles**, Solo
move, bone posing / client-side skinning, five marker tools.
- **Use for:** whole scenes or large parts, real-time human editing, "let me just drag things",
  posing characters. Also the home of **"send to Claude"** - the user edits or describes, the
  agent makes the change via HTTP.
- **Cost:** ~0 agent tokens (browser <-> bridge). Heavy scenes make agent-driven pane
  screenshots / JS evals slow - verify with light queries, not big ones.

### 4. Artifact viewer - *full-fidelity, view-only*
`tools/gen-artifact-viewer.ps1` snapshots the live scene into a self-contained HTML (three.js
inlined) and publishes it as an Artifact. Full resolution, unlimited size, shareable link.
- **Use for:** "show me / review / share" - anything view-only. Generated disk->disk, so **~0
  agent tokens even for millions of verts.**
- **Cannot** edit. Changes come back as words -> the agent makes them via HTTP.

---

## HTTP API quick reference

Base: `http://localhost:8787` | POST bodies are JSON with `Content-Type: text/plain` (avoids
CORS preflight). All mutations are Undo-grouped in Unity.

| Method | Route | Purpose |
|---|---|---|
| GET | `/ping` | Liveness + bridge version + project/scene name |
| GET | `/scene?light=1` | Structural map: transforms, kinds, hierarchy paths (no geometry) - **use this to orient** |
| GET | `/scene` | Full dump incl. geometry (+ skinning data) - only when you truly need meshes |
| POST | `/apply` | `{edits:[{id, position?[3], rotationEuler?[3], scale?[3]}]}` - set transforms. Omit a channel to leave it untouched. Non-finite values rejected. |
| POST | `/build` | `{root, items:[{shape, name, parent, position, rotationEuler, scale, color:[r,g,b] (0..1 floats) OR colorHex:"#RRGGBB"}]}` - batch-create primitives (cube/plane/cylinder/sphere/capsule/quad). Auto-creates group containers. Returns `{built, ids:{name:id}}`. |
| POST | `/spawn_prefab` | `{path:"Assets/...prefab or .fbx", name?, position?[3]}` - instantiate an asset. Returns id + bounds. |
| POST | `/delete` | `{id}` - remove an object (Undo-recorded). |
| POST | `/marker` | `{parentId, name, type:"Point\|Axis\|Plane\|Volume\|Path", worldPosition[3], worldRotation?[4], hasFacing?, normal?[3], knots?[], halfExtents?[3], ...}` - create a typed spatial marker (native child + `.spatialmeta.json` sidecar). Parent must exist. |
| GET | `/markers` | Live markers whose host still exists (orphans filtered). |
| POST | `/rename` | `{id, newName}` |
| POST | `/undo` | Undo the last bridge operation in Unity. |
| POST | `/delete`,`/build` etc. force a Scene-view repaint so background edits are visible. |

Conventions Unity uses (mirror them exactly): **left-handed, Y-up, +Z forward**; rotations are
**Unity `Quaternion.Euler` == three.js Euler order `YXZ`**; positions are metres; never bake size
into scale - a marker's extent lives in its params. (Studio also applies a display Z-mirror so a
browser view matches Unity's orientation; that's a viewer concern, not an API one - `/apply`
takes Unity-space values.)

---

## Worked examples

**"Move the truck cab forward two metres."**
-> `GET /scene?light=1`, find `Cab` id + current pos, `POST /apply` with pos.z+2. Confirm in text.
*No widget - the change is precise.*

**"Add a stop sign at the north intersection."**
-> `POST /build` a red cube on a grey cylinder at the known corner. Confirm. *No widget - you can
place it from the map.*

**"Put a grip point where the character's hand would hold the rifle."**
-> Ambiguous in 3D. Bake a **focused** widget of the hand + rifle, drop your best-guess Point
marker, let the human nudge it and Apply. *This is what the visual channel is for.*

**"Build me a small city." / "Lay out this level."**
-> Big + iterative. Either drive it yourself with batched `/build` + `/apply` and show an Artifact
viewer of the result, or set the user up in **Studio** to arrange it live.

**"Show me the whole scene." / "Send my teammate a link."**
-> **Artifact viewer.** Full fidelity, free, shareable. Not a widget.

**"Let me just drag stuff around."**
-> **Studio** in the Browser pane. Real-time, no round-trips.

---

## Guardrails

- **Cheapest surface wins.** A specifiable change is an HTTP call, not a widget.
- **Never bake a big scene into an in-chat widget** - vertices are agent tokens. Focus + context
  boxes + hidden list, always (`tools/bake-widget.ps1`).
- **Never `GET /scene` (full) to browse** - use `?light=1`. Full dump only when you need meshes.
- **Re-key on path, not id**, across any recompile / domain reload.
- **Confirm side-effectful, hard-to-undo actions** (bulk deletes, scene-wide changes) before
  running them, and tell the user to save the scene (Ctrl+S) after a heavy session.
- **Reject nonsense before it reaches Unity** - the bridge guards non-finite values and bad
  parents, but don't send them.
- If the bridge is unreachable, the user's Unity isn't open - say so plainly; don't fabricate.

---

## Drop-in system-prompt block

Paste into an agent's instructions to teach the routing in ~150 words:

> You can edit a live Unity scene through the SceneBridge HTTP API at `http://localhost:8787`
> (GET `/scene?light=1` to map it; POST `/apply`, `/build`, `/spawn_prefab`, `/delete`, `/marker`
> to change it - JSON, `Content-Type: text/plain`, Unity is left-handed Y-up, euler order YXZ).
> Choose the smallest surface for the job: (1) **just call the API** for any change you can
> specify precisely - that's most single-object edits; (2) open a **focused in-chat 3D widget**
> (only the relevant object(s) at full geometry, neighbors as boxes) when a position is ambiguous
> and a human should point or approve; (3) send the user to **Studio** (`localhost:8791`, real
> Chrome) for big scenes or real-time dragging; (4) publish an **Artifact viewer** when they just
> want to see or share. Never bake a large scene into a chat widget - every vertex costs context
> tokens. Instance ids change on recompile; key on hierarchy path.
