---
description: Verify and set up SceneBridge (live Unity scene editing) for this session
argument-hint: (no args)
---
You are setting up **SceneBridge** - a tool that lets you read and edit the user's **live Unity
scene** over a local HTTP bridge (and, when its MCP server is loaded, as tools named
`scenebridge_*`). Run these checks in order, take the fix action where one is needed, and finish
with a short status. Don't ask the user anything unless a step needs their hands.

1. **Is the Unity bridge running, and on which port?** The bridge claims the first free port in
   **8787-8796** (so multiple projects can run at once) and writes it to `Library/scenebridge.port`.
   Find it:
   - Read `Library/scenebridge.port` if it exists -> that's the port for THIS project. Verify with
     `curl -s http://localhost:<port>/ping`.
   - Otherwise probe the range: `curl -s http://localhost:8787/ping`, 8788, ... up to 8796, and use
     the first that returns `{"ok":true,...}`.
   - On success, note `port`, `version`, `project`, `scene`. **Remember this port for every later
     call** (or just use the MCP tools, which auto-discover it).
   - If nothing answers -> Unity isn't open (or is mid-compile). Tell the user: *"Open the Unity
     project that has the SceneBridge package (`com.uncostlydevs.scenebridge`) installed; the bridge
     auto-starts when Unity loads the scripts. Then run `/scenebridge-setup` again."* Stop here.

2. **Are the MCP tools loaded?** Check whether tools named `mcp__scenebridge__*` are available to you.
   - Yes -> you'll call them directly. Good.
   - No -> look for a `.mcp.json` at the project root with a `scenebridge` server. If it's missing,
     create it with exactly:
     ```json
     { "mcpServers": { "scenebridge": { "command": "node", "args": ["tools/mcp/scenebridge-mcp.js"], "env": { "SCENEBRIDGE_URL": "http://localhost:8787" } } } }
     ```
     Then tell the user: *"Reload Claude Code (or approve the `scenebridge` MCP server when
     prompted) so the tools register."* You can still drive the bridge via `curl` until then.

3. **Node present?** (the MCP server needs it) `node --version`. If missing, tell the user to
   install Node 16+.

4. **Learn the protocol.** Call `scenebridge_help`, or `curl -s http://localhost:<port>/help`. It
   returns the routing rule + endpoint map + Unity conventions. Read it - it governs how you use
   every tool.

5. **Smoke test (end-to-end read).** `curl -s "http://localhost:<port>/scene?light=1&find=Camera"`
   (or `scenebridge_scene_map` with `find:"Camera"`). You should get one-or-few nodes back.

6. **Recommend snappy background editing (optional).** If bridge calls feel slow (a few seconds),
   the Unity editor is throttling itself while unfocused. Tell the user: *"In Unity -> Edit ->
   Preferences -> General -> set **Interaction Mode = No Throttling** so the scene updates instantly
   while you work in Claude."* (Not required - just faster.)

**Report, concise:** bridge up? (version / port / project / scene) | MCP tools loaded? | Node ok? |
smoke test passed? | then one line: *"Ready - describe a change or ask me to build something, and
I'll use the smallest action for the job. Type `/scenebridge-help` for the routing guide."*
