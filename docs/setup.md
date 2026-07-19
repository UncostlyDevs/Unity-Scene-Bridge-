# Set up SceneBridge with your agent

SceneBridge has two halves: the **Unity side** (identical for every agent) and the **agent side** (a
standard MCP server you register with whatever client you use). Do the Unity side once, then follow
the section for your agent - Claude Code, Codex, or anything else that speaks MCP.

---

## 1. Unity side (once per project, same for every agent)

In Unity, open **Package Manager -> Add package from git URL** and paste:

```
https://github.com/UncostlyDevs/Unity-Scene-Bridge-.git?path=/Packages/com.uncostlydevs.scenebridge
```

Open a scene. The bridge starts on its own and logs `[SpatialBridge] listening on http://localhost:8787/`.
Check it:

```
curl http://localhost:8787/ping
```

If you keep several Unity projects open, each one takes the next free port in **8787-8796** and writes
its choice to `<project>/Library/scenebridge.port`.

---

## 2. Agent side

The agent side is a single file - `tools/mcp/scenebridge-mcp.js` - a zero-dependency MCP server that
speaks stdio. Every client launches it the same way: `node <path>/scenebridge-mcp.js`. You need
**Node 16+**.

Two things to get right, whatever the client:

- **Which project.** The server figures out the project from its launch working directory (it walks up
  looking for `Library/scenebridge.port`, then `Assets/`). So start your agent from inside the Unity
  project - or pin a bridge explicitly with `SCENEBRIDGE_URL=http://localhost:<port>`.
- **Absolute path.** Use the full path to `scenebridge-mcp.js` unless the client resolves paths
  relative to your project (Claude Code does).

### Claude Code

Register it once, for every session:

```
claude mcp add scenebridge --scope user -- node /absolute/path/to/Unity-Scene-Bridge-/tools/mcp/scenebridge-mcp.js
```

Or per-project: the repo ships a `.mcp.json` at its root, so opening the project in Claude Code picks
it up (approve the server when prompted). Confirm with `/mcp`. Claude Code gets the full experience,
including the in-chat 3D editor and the `/scenebridge-*` slash commands.

### Codex CLI

Add the server to `~/.codex/config.toml`:

```toml
[mcp_servers.scenebridge]
command = "node"
args = ["/absolute/path/to/Unity-Scene-Bridge-/tools/mcp/scenebridge-mcp.js"]
# optional - pin one project's bridge instead of auto-discovering:
# env = { SCENEBRIDGE_URL = "http://localhost:8787" }
```

(Recent Codex builds also accept `codex mcp add scenebridge -- node /abs/.../scenebridge-mcp.js`.)

Start Codex from inside your Unity project folder so it targets that project's bridge, or set
`SCENEBRIDGE_URL`. Then just talk to it - the `scenebridge_*` tools show up automatically. For
hands-on placement, use **Studio** (below); the in-chat 3D editor is Claude Code specific.

### Any other MCP client (Cursor, Windsurf, Cline, VS Code, a custom loop, ...)

They all take the same shape - a `command` and `args`. Drop this into the client's MCP config file
(`mcp.json`, `claude_desktop_config.json`, or the client's settings UI):

```json
{
  "mcpServers": {
    "scenebridge": {
      "command": "node",
      "args": ["/absolute/path/to/Unity-Scene-Bridge-/tools/mcp/scenebridge-mcp.js"],
      "env": { "SCENEBRIDGE_URL": "http://localhost:8787" }
    }
  }
}
```

Leave out `env` to auto-discover the project's port; include it to pin a specific bridge.

Not sure your client is wired up? Drive the server by hand and watch it answer:

```
printf '%s\n%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05"}}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"scenebridge_scene_map","arguments":{}}}' \
  | node tools/mcp/scenebridge-mcp.js
```

You should see the initialize result, then a `content` block with the live scene map.

---

## What every agent gets, and the one thing it doesn't

Every MCP client gets the full tool set: read the scene, edit transforms, build primitives, spawn
prefabs, place the five marker types, rename, delete, undo. Plus two surfaces that run in any browser
and are driven by you, the human:

- **Studio** - run `node widget/studio-server.js`, then open `http://localhost:8791`. A full editor
  that talks to the bridge directly. On any agent, this is where you do hands-on, real-time placement.
- **The viewer** - `tools/gen-artifact-viewer.ps1` writes a self-contained HTML snapshot you can open
  or share.

**Claude Code only:** the in-chat baked 3D editor and the `/scenebridge-*` slash commands, because
they rely on Claude's inline widget rendering and prompt relay. On Codex or any other agent, the same
"correct it by hand" job happens in Studio instead.

---

## Troubleshooting

- **`ping` fails or a tool times out** - Unity isn't open, or it's mid-compile. Open the project; the
  bridge starts itself.
- **It's editing the wrong project's scene** - the client launched from the wrong directory. Start it
  inside the Unity project, or set `SCENEBRIDGE_URL` to that project's port.
- **`node: command not found`** - install Node 16+ and make sure it's on `PATH`.

For the "which surface when" decision guide the agent itself follows, see
[agent-protocol.md](agent-protocol.md).
