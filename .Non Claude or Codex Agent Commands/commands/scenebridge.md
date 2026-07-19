---
description: Do something in the live Unity scene via SceneBridge
argument-hint: [what to change, create, or mark]
---
You can read and edit the user's **live Unity scene** through SceneBridge - the `scenebridge_*`
MCP tools if loaded, otherwise the HTTP bridge at `http://localhost:8787` (POST bodies are JSON
with `Content-Type: text/plain`).

**Task:** $ARGUMENTS

How to approach it:

1. **Confirm the bridge is up** if you haven't this session - `scenebridge_scene_map` (with a
   `find`), or `curl -s http://localhost:8787/ping`. If it's down, the user's Unity isn't open -
   say so and stop.
2. **Use the smallest action that does it.** A change you can specify is a direct tool call -
   `scenebridge_apply` (move/rotate/scale), `scenebridge_build` (create primitives),
   `scenebridge_spawn_prefab` (place an asset), `scenebridge_delete`, `scenebridge_rename`,
   `scenebridge_create_marker` (typed spatial markers). No visual editor needed for specifiable work.
3. **Find ids/paths** with `scenebridge_scene_map`. On a big scene pass `find:"<name>"` so you
   pull only what you need. Ids change on recompile - key on path.
4. **When a position is genuinely ambiguous**, make your best guess, apply it, and ask the user to
   confirm or nudge. For large real-time editing or hands-on dragging, point them to **Studio**
   (`http://localhost:8791`).
5. **Report** what you changed and the ids involved. Confirm hard-to-undo actions (bulk deletes)
   before running them.

Unity conventions: left-handed, **Y-up, +Z forward**; euler order **YXZ**; **metres**; colours are
`color:[r,g,b]` 0..1 or `colorHex:"#RRGGBB"`.

If `$ARGUMENTS` is empty, ask the user what they'd like to do, or run `/scenebridge-help`.
