# Contributing to SceneBridge

Thanks for your interest! SceneBridge is early and there's plenty to build. This guide covers the
layout, how to run each piece, and the house rules that keep the tool robust.

## Architecture in one paragraph

Everything is a client of one editor-side HTTP bridge (`Assets/Editor/SpatialBridge/SpatialBridge.cs`).
The MCP server (`tools/mcp/scenebridge-mcp.js`) wraps the bridge as agent tools. The bake scripts
(`tools/*.ps1`) snapshot the live scene into the in-chat editor or a shareable Artifact. Studio
(`widget/studio.html`) is a browser editor that talks to the bridge directly. See
[docs/agent-protocol.md](docs/agent-protocol.md) for the routing model.

## Running the pieces

- **Bridge:** open the project (or your own Unity project with the two `Assets/` folders) in Unity.
  It auto-starts; `GET http://localhost:8787/ping` should answer. The bridge claims the first free
  port in `8787..8796` and writes it to `Library/scenebridge.port`.
- **MCP server:** `node tools/mcp/scenebridge-mcp.js` (Node 16+, zero dependencies). It auto-discovers
  the bridge port. `node -c tools/mcp/scenebridge-mcp.js` to syntax-check.
- **Studio:** `node widget/studio-server.js`, then open `http://localhost:8791`.
- **Bakes / viewer:** `tools/bake-inline-editor.ps1`, `tools/gen-artifact-viewer.ps1` (PowerShell,
  Windows). Each auto-discovers the bridge port and takes portable output-path parameters.

## House rules (hard-won - please keep them)

- **The bridge is local-only.** Never widen the CORS policy beyond local origins or add a route that
  mutates the scene without the `OriginOk` guard.
- **Reject non-finite input.** `JsonUtility` parses `NaN`/`Infinity`; one poisoned value makes `/scene`
  emit invalid JSON and breaks every client. Guard new numeric channels with `AllFinite(...)`.
- **Key on hierarchy path, not instance id** - ids reassign on every domain reload.
- **Coordinate conventions are exact:** Unity is left-handed Y-up +Z-forward; `Quaternion.Euler` ==
  three.js Euler order `YXZ`. Viewers apply a Z-mirror at the boundary (negate z, flip winding,
  conjugate rotation). Get this wrong and multi-axis rotations look mirrored.
- **Keep source ASCII.** Windows PowerShell 5.1 mangles non-ASCII in BOM-less files; write generated
  files as BOM-free UTF-8 (`[System.IO.File]::WriteAllText(path, text, (New-Object System.Text.UTF8Encoding($false)))`).
- **Never bake a big scene into the in-chat widget** - every vertex costs agent context tokens. Use
  a focused bake or Studio.
- **Don't commit third-party paid Asset Store content** or generated bake artifacts - see
  `.gitignore`.

## Before you open a PR

- `node -c` any changed `.js`.
- If you touched the bridge, recompile in Unity and confirm `/ping` still answers.
- Exercise the actual path you changed (drive the edit, don't just eyeball the diff).
- Keep comments load-bearing: explain the non-obvious *why*, drop the obvious.

## License

By contributing you agree your contributions are licensed under the [MIT License](LICENSE).
