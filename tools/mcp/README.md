# SceneBridge MCP server

Exposes a live Unity scene as first-class MCP tools, so **any** MCP-capable agent (Claude Code,
Claude Desktop, Cursor, ...) can read and edit the scene without knowing any of the internals. It's
a thin, zero-dependency wrapper over the SpatialBridge HTTP API - each tool's description carries
the routing rule, so the agent knows *when* to use it.

## What you get

Once loaded, the agent has these tools (prefix `scenebridge_`):

| Tool | Use it for |
|---|---|
| `scene_map` | **Call first.** Cheap map: every object's id, path, kind, transform - no geometry. |
| `scene_full` | Full dump incl. mesh geometry (only when you truly need verts). |
| `apply` | Set position / rotation / scale on existing objects (precise changes). |
| `build` | Batch-create primitives (cubes, planes, cylinders...) - buildings, roads, props. |
| `spawn_prefab` | Instantiate an existing `.prefab` / `.fbx` asset. |
| `delete` | `rename` | `undo` | Object lifecycle. |
| `create_marker` | `list_markers` | Typed spatial markers (Point/Axis/Plane/Volume/Path). |
| `help` | The routing protocol + Unity conventions, on demand. |

The guiding rule baked into every description: **use the smallest action for the job - a change
you can specify precisely is a tool call, not a visual editor.** For large real-time editing, the
agent points you to Studio (`http://localhost:8791`).

## Requirements

- **Node.js 16+** on `PATH`.
- **Unity open** with this project - the `SpatialBridge` editor script auto-starts the HTTP
  server on `localhost:8787` (`InitializeOnLoad`; survives domain reloads).
- Verify it's up: `curl http://localhost:8787/ping` -> `{"ok":true,...}`. (If several Unity projects
  are open, each claims the next free port in **8787-8796**; the server auto-discovers the right
  one for its project via `Library/scenebridge.port`.)

## Load it

### Claude Code
A ready-to-use `.mcp.json` is at the **repo root**. Opening the project in Claude Code picks it up
(approve the server when prompted). Or add it globally:

```bash
claude mcp add scenebridge -- node /absolute/path/to/tools/mcp/scenebridge-mcp.js
```

Restart Claude Code after adding, then confirm with `/mcp` - you should see `scenebridge` with 12
tools.

### Codex CLI
Add to `~/.codex/config.toml`:

```toml
[mcp_servers.scenebridge]
command = "node"
args = ["/absolute/path/to/tools/mcp/scenebridge-mcp.js"]
# env = { SCENEBRIDGE_URL = "http://localhost:8787" }   # optional: pin one project's bridge
```

Start Codex from inside the Unity project (so it targets that project's bridge), or set
`SCENEBRIDGE_URL`. The in-chat 3D editor is Claude Code specific; on Codex, do hands-on placement in
Studio (`http://localhost:8791`).

### Claude Desktop / Cursor / other MCP clients
Add to the client's MCP config (`claude_desktop_config.json`, `mcp.json`, etc.):

```json
{
  "mcpServers": {
    "scenebridge": {
      "command": "node",
      "args": ["/absolute/path/to/tools/mcp/scenebridge-mcp.js"],
      "env": { "SCENEBRIDGE_URL": "http://localhost:8787" }
    }
  }
}
```

Use an **absolute path** unless the client resolves relative to the project (Claude Code does).

## Configuration

| Env var | Default | Meaning |
|---|---|---|
| `SCENEBRIDGE_URL` | *(auto-discover)* | Pin a specific bridge URL. Leave unset to auto-discover this project's port (reads `Library/scenebridge.port`, else scans 8787-8796). |

## Test it without a client

The server speaks MCP over stdio (newline-delimited JSON-RPC). You can drive it by hand:

```bash
printf '%s\n%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05"}}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"scenebridge_scene_map","arguments":{}}}' \
  | node tools/mcp/scenebridge-mcp.js
```

You should see the initialize result, then a `content` block with the live scene map.

## Notes

- All mutations are **Undo-grouped** in Unity (Ctrl+Z reverts a whole tool call).
- Instance ids are **reassigned on every Unity recompile** - the agent should key on hierarchy
  path, not cache ids across a reload.
- If a tool times out, Unity isn't open (or is mid-compile) - the error says so.
- Full per-agent setup (Claude Code, Codex, Cursor, any MCP client), plus troubleshooting, is in
  [`docs/setup.md`](../../docs/setup.md).
- For the full "which surface when" decision guide, see [`docs/agent-protocol.md`](../../docs/agent-protocol.md).
