---
description: How to use SceneBridge - the routing protocol, tools, and examples
argument-hint: (no args)
---
Explain how to use **SceneBridge** to edit the live Unity scene, for yourself and the user. If you
can, first pull the live endpoint map - call `scenebridge_help` or
`curl -s http://localhost:8787/help` - and fold anything version-specific into the guidance below.

**The one rule: use the smallest surface that does the job.** A change you can specify precisely is
a direct tool call, not a visual editor.

| The user wants... | Do this |
|---|---|
| A precise change to 1-few objects (move / rotate / scale / delete / rename) | `scenebridge_apply` | `scenebridge_delete` | `scenebridge_rename` directly |
| You to create 1-few objects | `scenebridge_build` (primitives: cube/plane/cylinder/sphere/capsule/quad) or `scenebridge_spawn_prefab` (existing `.prefab`/`.fbx`) |
| A change where the position is ambiguous / needs a human eye, **or the user wants to see / hand-edit a model** | Make your best guess, apply it, ask the user to confirm or nudge. To show an in-chat 3D editor, **don't hand-roll one** -- call **`scenebridge_inline_editor`** (optional `focus:["name",...]`) and pass the HTML it returns straight to `show_widget`. When the user drags + clicks Apply, you get `SPATIAL_APPLY {mirroredZ:true,edits:[...]}` back - un-mirror (negate pos z; negate rot x,y) and `scenebridge_apply`. *(No MCP? the manual path is `tools/bake-inline-editor.ps1` -> read `widget/inline-editor.ready.html` -> `show_widget`.)* |
| Semantic geometry a mesh doesn't encode (hitch / grip point, hinge axis, trigger volume, driving path) | `scenebridge_create_marker` (Point/Axis/Plane/Volume/Path) |
| To build/edit a **big scene** or drag things in **real time** | Tell them to open **Studio**: http://localhost:8791 |
| Just to **see / share** the scene | Generate a full-fidelity **Artifact viewer** (view-only) |

**Always:** call `scenebridge_scene_map` first to find ids/paths. On a big scene pass
`find:"<name>"` so you get only matching nodes, not the whole hierarchy. **Ids change on every
Unity recompile - key on path, never cache ids across a reload.**

**Unity conventions:** left-handed, **Y-up, +Z forward**; rotations use **euler order YXZ**
(Unity `Quaternion.Euler`); units are **metres**; POST bodies are JSON with
`Content-Type: text/plain`. Colours: `color:[r,g,b]` (0..1) **or** `colorHex:"#RRGGBB"`.

**Examples**
- *"Move the cab up 1 m."* -> `scene_map find:"Cab"` -> `apply {edits:[{id, position:[x, y+1, z]}]}`.
- *"Add a red bench at [4,0,2]."* -> `build {items:[{shape:"cube", name:"Bench", position:[4,0.3,2], scale:[1.6,0.4,0.5], colorHex:"#b23b3b"}]}`.
- *"Mark where the trailer hooks onto the truck."* -> `create_marker` a Point on the truck at your
  best-guess hitch position, then ask the user to nudge it - that human correction is the point.

To do a task now, use `/scenebridge <what you want>`, or just describe it.
