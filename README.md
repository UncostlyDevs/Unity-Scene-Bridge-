<div align="center">

# 🌉 Unity SceneBridge

### Give your AI agent eyes and hands in a live Unity scene.

*It proposes, you correct, Unity updates.*

</div>

---

## Your AI can't see your scene

Coding assistants are great with text and lost in 3D. Yours will happily talk about your scene, but it can't tell where the cab sits on the chassis, where a hand should grip a rifle, or whether the mailbox it just spawned landed on the sidewalk or in the road. So it guesses, and you clean up after it.

SceneBridge fixes that. Your AI reads the live scene, makes real edits, and every change is a normal Unity undo away. When it isn't sure where something goes, it opens a small 3D editor right in the chat — you drag the object into place, hit Apply, and the exact coordinates land back in Unity. Its guess plus your correction becomes the answer, and you only pay for it once.

Built for [Claude Code](https://claude.com/claude-code), works with any MCP client. Unity is just the program it drives.

---

## What it does

- **👁 See and edit the live scene.** Reads transforms, hierarchy, and geometry, then moves, rotates, scales, creates, deletes, and renames real GameObjects — all undo-recorded.
- **✋ Correct placement by hand.** When a spot is ambiguous, it bakes a real three.js editor into the chat. Orbit around it, drag the gizmo, click Apply, and the numbers go straight into Unity.
- **💬 Just talk to it.** *Move the cab forward two metres. Add a red bench on the corner. Mark where the hand grips the rifle.* It works out the right action on its own.
- **⚡ Edit live, both ways.** Open Studio, a full browser editor, and drag things while Unity moves with you at about 80 ms. Edit in Unity and Studio catches up.
- **📍 Drop markers a mesh can't hold.** Points for grips and hitches, axes for hinges, planes for couplings, volumes for triggers, paths for routes. Each one becomes a real Unity component plus a sidecar file that survives a re-export.
- **🏗 Build in bulk.** Greybox a level, a road network, or a shelf of props from a single sentence.
- **🔗 Share the whole scene.** Snapshot it into one self-contained HTML file a teammate can open and spin around. No Unity, no size limit.
- **🦴 Pose characters live.** Move a skinned character's bones and watch the mesh deform in real time.
- **🔒 Stays on your machine.** The bridge only answers local connections and ignores anything a website tries to send it.

---

## See it in action

```
You:  Add a stop sign at the north intersection.
→ Done, placed straight away.

You:  Put a grip point where the character's hand holds the rifle.
→ It can't be sure in 3D, so it opens an editor in the chat with its best
  guess. You nudge the marker, hit Apply, and the exact local coordinates
  land in Unity.

You:  Lay out a city block — roads in a grid, four bus stops, a player spawn.
→ Built in one pass, ready to refine.

You:  Let me drag the whole level around.
→ Studio opens in the browser. You drag; Unity moves with you.
```

---

## Under the hood

- **It's a loop, not a one-shot generator.** The AI proposes, you fix the placement it couldn't judge, and your correction flows back as structured data. That exchange is the whole idea.
- **Studio holds sync both ways at around 80 ms**, and it survives a domain reload — Unity recompiling mid-session doesn't drop the connection.
- **Characters skin on the client.** A live `THREE.SkinnedMesh` binds to the real scene-graph bones, so a bone edit deforms the mesh the same frame, with no round trip.
- **Your edits show even when Unity is in the background.** Unity freezes its Scene view the moment it loses focus, so a drag would normally look stuck. SceneBridge redraws the moving mesh through the gizmo pass — the one thing that still paints in the background — so you watch it glide.
- **It never pours geometry into the chat.** Unity primitives ship as tiny shared-geometry references and real meshes as quantized base64, and the full-fidelity viewer is written straight to disk, so it costs almost nothing in tokens even at millions of verts.
- **Rotations come back exact.** Unity's `Quaternion.Euler` lines up with three.js Euler order `YXZ` — worked out on paper, then confirmed — with a full left/right-handed mirror at the viewer edge, so multi-axis rotations and skinned rigs land precisely.
- **The agent side is one Node file with no dependencies.** It speaks the MCP protocol directly.
- **Set it up once.** The server works out which project a chat belongs to from its working directory, so you can keep several projects and several chats going at once, each on its own port.
- **It's locked down.** Localhost only, cross-origin requests refused, NaN and Infinity rejected on every input, request sizes capped, every change grouped into one undo.

---

## What it can do

| | |
|---|---|
| **Edit** | Move, rotate, scale, delete, rename anything — precise or by hand, all undo-recorded |
| **Create** | Batch primitives (cube, plane, cylinder, sphere, capsule, quad) with color or hex; drop in existing prefabs and FBX models |
| **Mark up** | Point, Axis, Plane, Volume, and Path markers, each a real Unity component with a `.spatialmeta.json` sidecar |
| **Correct** | In-chat 3D editor with orbit, a transform gizmo, numeric fields, and Apply-back |
| **Edit live** | Studio: drag-to-Unity at ~80 ms, a scene tree, per-object visibility, solo-move, bone posing |
| **Inspect** | A cheap structural map of the scene, with name and path search for big hierarchies |
| **Share** | A self-contained, view-only 3D snapshot in one HTML file |
| **Scale** | Multiple ports (8787–8796), multiple projects, one global install |

---

## What people use it for

**Attach points and markers** — weapon muzzles and grips, vehicle hitches, door hinges, character grip and IK targets, modular sockets, trigger volumes, driving and patrol paths, AI spawns and cover points.

**Placement and cleanup** — fixing whatever the AI put in the wrong spot, snapping things flush to a surface, seating a helmet on a head or a sword in a scabbard, correcting a bad imported pivot.

**Building** — greyboxing levels and arenas, scattering props, laying out tracks with checkpoints, reshuffling a layout by voice.

**Live work and sharing** — dragging a scene around while the AI does the heavy lifting, posing characters and blocking cutscenes, setting up cameras and lights, sending a teammate something they can spin around.

---

## Where the editing happens

Everything runs on one small HTTP bridge inside the Unity editor, and the AI reaches for the lightest tool that fits:

- **The API** for anything it can state outright — no visual needed.
- **The in-chat editor** for one model up close, when a placement needs your eye.
- **Studio** for big scenes, real-time dragging, and posing.
- **The viewer** for sharing a scene read-only.

---

## Setup

Two halves: the Unity side (the same for every agent) and the agent side (whichever client you use).

### 1. Unity

In Unity, open **Package Manager → Add package from git URL** and paste:

```
https://github.com/UncostlyDevs/Unity-Scene-Bridge-.git?path=/Packages/com.uncostlydevs.scenebridge
```

The bridge starts on its own and logs `[SpatialBridge] listening on http://localhost:8787/`. For smooth background editing, set Edit → Preferences → General → Interaction Mode to No Throttling. If you keep several projects open, each takes the next free port in 8787–8796.

### 2. Your agent

The agent side is one zero-dependency MCP server — `tools/mcp/scenebridge-mcp.js` — launched with `node`. You'll need Node 16+. It finds the right project from where you start it, so launch your agent from inside the Unity project, or pin a bridge with `SCENEBRIDGE_URL=http://localhost:<port>`. Use an absolute path to the script unless your client resolves relative to the project.

**Claude Code** — register it once, for every session:

```
claude mcp add scenebridge --scope user -- node /path/to/Unity-Scene-Bridge-/tools/mcp/scenebridge-mcp.js
```

Or per-project: the repo ships a `.mcp.json`, so opening the project picks it up. Claude Code gets the full experience, including the in-chat 3D editor and the `/scenebridge-*` slash commands.

**Codex** — add it to `~/.codex/config.toml`:

```toml
[mcp_servers.scenebridge]
command = "node"
args = ["/absolute/path/to/Unity-Scene-Bridge-/tools/mcp/scenebridge-mcp.js"]
# env = { SCENEBRIDGE_URL = "http://localhost:8787" }   # optional: pin one project's bridge
```

Start Codex from inside your Unity project folder (or set `SCENEBRIDGE_URL`), then just talk to it.

**Any other MCP client** (Cursor, Windsurf, Cline, VS Code, a custom loop) — same shape, a command and args in the client's MCP config:

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

Leave out `env` to auto-discover the port; include it to pin a bridge. Open a chat in your project and start talking. On Claude Code, `/scenebridge-setup` checks everything's wired up.

> **For non-Claude agents:** the in-chat 3D editor and the `/scenebridge-*` slash commands are Claude Code only — they need its inline widget rendering. On Codex or anything else you still get every other tool, plus Studio and the shareable viewer, and you do hands-on placement in **Studio** (`http://localhost:8791`) instead of in the chat.

Full per-agent walkthrough and troubleshooting: [docs/setup.md](docs/setup.md).

---

## Good to know

- Every edit is undo-recorded, connections stay local, and nothing leaves your machine.
- Unity 2021.3+ (built on Unity 6) and Node 16+. The bridge, server, and web surfaces run anywhere; the bake and export scripts are Windows PowerShell for now.
- Pre-1.0, and working end to end.
- Docs: [setup with any agent](docs/setup.md), [architecture](docs/architecture.md), [agent protocol](docs/agent-protocol.md), [markers](docs/marker-system.md).
- [MIT](LICENSE) licensed. Contributions welcome — [CONTRIBUTING.md](CONTRIBUTING.md).

<div align="center">

**The spatial half of AI game dev, finally in the loop.**

</div>
