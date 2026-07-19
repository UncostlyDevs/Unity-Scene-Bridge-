# SceneBridge (Unity side)

The Unity half of [SceneBridge](https://github.com/UncostlyDevs/Unity-Scene-Bridge-) - an editor HTTP
bridge that exposes the live scene to an AI agent, plus runtime ghost/marker helpers.

- `Editor/SpatialBridge.cs` - the localhost HTTP bridge (editor-only, auto-starts, local connections only).
- `Runtime/SpatialLiveGhost.cs` - live background "ghost" of edited meshes during external edits.
- `Runtime/SpatialMarkerGizmo.cs` - spatial marker visualization gizmo.

Pair this with the SceneBridge MCP server so an agent (Claude / any MCP client) can read and edit the
scene. The MCP server, Studio, in-chat editor, and full docs live in the main repo.

## Install

Package Manager > Add package from git URL:

    https://github.com/UncostlyDevs/Unity-Scene-Bridge-.git?path=/Packages/com.uncostlydevs.scenebridge

Open a scene; the bridge auto-starts and logs `[SpatialBridge] listening on http://localhost:8787/`.
It accepts local connections only. MIT licensed.
