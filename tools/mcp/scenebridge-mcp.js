#!/usr/bin/env node
/*
 * SceneBridge MCP server - exposes a live Unity scene (via the SpatialBridge HTTP API)
 * as first-class MCP tools. Zero dependencies: implements the MCP stdio transport
 * (newline-delimited JSON-RPC 2.0) directly. Point any MCP client at this file.
 *
 *   Requires:  Node 16+, and Unity open with the SpatialBridge editor script running.
 *   Discovery: finds the Unity project from the chat's working directory (cwd), reads its
 *              Library/scenebridge.port, else scans 8787..8796. Works installed per-project OR
 *              once globally (user scope) across several projects. Override with env SCENEBRIDGE_URL.
 */
const http = require('http');
const fs = require('fs');
const path = require('path');
const os = require('os');
const { execFile } = require('child_process');

// INSTALL_ROOT is where this tool lives: the server sits at <INSTALL_ROOT>/tools/mcp/, so the bake
// script and widget template ship alongside it. For a PER-PROJECT install this equals the Unity
// project; for a GLOBAL (user-scope) install it's the shared tool folder.
const INSTALL_ROOT = path.resolve(__dirname, '..', '..');
const PORT_START = 8787, PORT_COUNT = 10;

function safeExists(p) { try { return fs.existsSync(p); } catch { return false; } }

// Which Unity PROJECT is this chat about? Claude launches the MCP server with cwd = the folder you
// opened, so walk up from there for a Unity project: prefer one whose bridge is already running
// (has Library/scenebridge.port), else the nearest Assets/ folder, else fall back to INSTALL_ROOT
// (the per-project install, where the server lives inside the project). This lets ONE globally
// registered server target whichever of several open projects the current chat belongs to.
function findProjectRoot() {
  const chain = [];
  let dir = process.cwd();
  for (let i = 0; i < 40; i++) {
    chain.push(dir);
    const parent = path.dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }
  for (const d of chain) { if (safeExists(path.join(d, 'Library', 'scenebridge.port'))) return d; }
  for (const d of chain) { if (safeExists(path.join(d, 'Assets'))) return d; }
  return INSTALL_ROOT;
}

let cachedPort = null;
let cachedHost = 'localhost'; // overridden by SCENEBRIDGE_URL's hostname when that env var is set
let cachedRoot = null;        // the resolved Unity project root (where the port file lives)

function pingPort(port, host) {
  return new Promise((resolve, reject) => {
    const req = http.request({ host: host || 'localhost', port, path: '/ping', method: 'GET', timeout: 700 },
      r => { let d = ''; r.on('data', c => (d += c)); r.on('end', () => (r.statusCode === 200 ? resolve(port) : reject())); });
    req.on('error', reject); req.on('timeout', () => { req.destroy(); reject(); });
    req.end();
  });
}

async function resolvePort() {
  if (process.env.SCENEBRIDGE_URL) {
    const u = new URL(process.env.SCENEBRIDGE_URL);
    cachedHost = u.hostname || 'localhost';
    return parseInt(u.port, 10) || 8787;
  }
  cachedRoot = findProjectRoot();
  // 1) trust THIS project's port file, but verify it's actually answering
  try {
    const p = parseInt(fs.readFileSync(path.join(cachedRoot, 'Library', 'scenebridge.port'), 'utf8').trim(), 10);
    if (p) { await pingPort(p); return p; }
  } catch { /* missing/stale/unreachable - fall through to scan */ }
  // 2) scan the range and take the first responder
  for (let i = 0; i < PORT_COUNT; i++) {
    try { return await pingPort(PORT_START + i); } catch { /* next */ }
  }
  throw new Error('no SceneBridge found on ports ' + PORT_START + '..' + (PORT_START + PORT_COUNT - 1) + ' - is Unity open with the project?');
}

function request(port, method, p, body) {
  return new Promise((resolve, reject) => {
    const data = body != null ? JSON.stringify(body) : null;
    const req = http.request(
      { host: cachedHost, port, path: p, method, timeout: 30000, headers: { 'Content-Type': 'text/plain' } },
      r => { let d = ''; r.on('data', c => (d += c)); r.on('end', () => resolve({ status: r.statusCode, text: d })); }
    );
    req.on('error', e => reject(e));
    req.on('timeout', () => { req.destroy(); reject(new Error('bridge timeout - is Unity open with the project?')); });
    if (data) req.write(data);
    req.end();
  });
}

async function call(method, path, body) {
  if (cachedPort == null) cachedPort = await resolvePort();
  try {
    return await request(cachedPort, method, path, body);
  } catch (e) {
    // the bridge may have moved (recompile picked a different port) - re-resolve once
    cachedPort = await resolvePort();
    return await request(cachedPort, method, path, body);
  }
}

// Bake the polished in-chat editor (widget/inline-editor.tmpl.html) full of the LIVE scene by
// running the PowerShell baker, then return the ready HTML so the agent can hand it straight to
// show_widget - instead of hand-rolling an ugly widget. Windows/PowerShell for now. The baker and
// template ship next to this server (INSTALL_ROOT); we pass the discovered -Port so it targets the
// current project's bridge, and -OutFile a temp path so a global install never writes into a project.
const BAKE_SCRIPT = path.join(INSTALL_ROOT, 'tools', 'bake-inline-editor.ps1');
async function runBake(focus, maxVerts) {
  let port;
  try { if (cachedPort == null) cachedPort = await resolvePort(); port = cachedPort; }
  catch (e) { return { status: 500, text: 'inline editor bake failed: ' + e.message }; }
  const outFile = path.join(os.tmpdir(), 'scenebridge-inline-' + process.pid + '.html');
  const args = ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', BAKE_SCRIPT, '-Port', String(port), '-OutFile', outFile];
  if (focus && focus.length) args.push('-Focus', focus.join(','));
  if (maxVerts) args.push('-MaxVerts', String(maxVerts));
  return await new Promise(resolve => {
    execFile('powershell', args, { cwd: INSTALL_ROOT, timeout: 90000, maxBuffer: 32 * 1024 * 1024, windowsHide: true },
      (err, stdout, stderr) => {
        // If the baker itself failed (e.g. Unity closed), surface that -- never fall through to a
        // stale file. Reading the fresh temp -OutFile also avoids writing into any user's project.
        if (err) {
          resolve({ status: 500, text: 'inline editor bake failed: ' +
            ((stderr && stderr.trim()) || err.message) +
            ' -- is Unity open with the SpatialBridge running? Run tools/bake-inline-editor.ps1 by hand to debug.' });
          return;
        }
        try {
          const html = fs.readFileSync(outFile, 'utf8').replace(/^﻿/, '');
          if (!html || html.length < 200) throw new Error('baker produced an empty widget');
          try { fs.unlinkSync(outFile); } catch { /* best effort cleanup */ }
          resolve({ status: 200, text: html });
        } catch (e) {
          resolve({ status: 500, text: 'inline editor bake failed: ' + e.message });
        }
      });
  });
}

// Tool definitions - the description is the routing contract the agent reads.
const TOOLS = [
  { name: 'scenebridge_help',
    description: 'Return the routing protocol + endpoint map + Unity conventions. Call once at the start if unsure how to use these tools.',
    inputSchema: { type: 'object', properties: {} },
    run: () => call('GET', '/help') },

  { name: 'scenebridge_scene_map',
    description: 'CHEAP structural map of the live scene: object ids, names, hierarchy paths, kinds, transforms - NO geometry. Call this FIRST to find the id/path of what you want to change. On a BIG scene pass `find` (a name/path substring) to return only matching nodes instead of the whole hierarchy - do this whenever you know roughly what you\'re looking for. Ids change on Unity recompile; key on path.',
    inputSchema: { type: 'object', properties: { find: { type: 'string', description: 'optional: only return nodes whose name or path contains this (case-insensitive)' } } },
    run: a => call('GET', '/scene?light=1' + (a && a.find ? '&find=' + encodeURIComponent(a.find) : '')) },

  { name: 'scenebridge_scene_full',
    description: 'Full scene dump INCLUDING mesh geometry and skinning. Large. Only call when you actually need vertex data; use scenebridge_scene_map to browse.',
    inputSchema: { type: 'object', properties: {} },
    run: () => call('GET', '/scene') },

  { name: 'scenebridge_inline_editor',
    description: 'Bake a POLISHED in-chat 3D editor of the live scene (orbit + transform gizmo + numeric fields) and return it as ready HTML - pass the returned text STRAIGHT to show_widget. This is the "show a model / let the user edit it by hand" surface: reach for it whenever the user wants to SEE a model or place/nudge something where the exact numbers are ambiguous, instead of you specifying transforms. Optional `focus` = names of the object(s) to include; on a big scene bake ONLY those (every vertex costs context tokens). When the user drags and clicks Apply, the widget messages you `SPATIAL_APPLY {mirroredZ:true,edits:[...]}` via chat - un-mirror it (negate position z; negate rotation x and y) and call scenebridge_apply. Do NOT hand-write your own three.js widget, and for a whole/large live scene or continuous dragging point the user to Studio (http://localhost:8791) instead.',
    inputSchema: { type: 'object', properties: {
      focus: { type: 'array', items: { type: 'string' }, description: 'names of objects to include; omit to bake all mesh nodes' },
      maxVerts: { type: 'integer', description: 'total vertex budget (default 60000); meshes beyond it are skipped - use focus instead of raising this' } } },
    run: a => runBake(a && a.focus, a && a.maxVerts) },

  { name: 'scenebridge_apply',
    description: 'Set transforms on existing objects. Use this for any PRECISE change you can specify (move/rotate/scale) - no visual editor needed. Omit a channel to leave it untouched. Unity is left-handed, Y-up, +Z forward; rotationEuler uses Unity Quaternion.Euler order (three.js YXZ); metres.',
    inputSchema: { type: 'object', required: ['edits'], properties: {
      edits: { type: 'array', items: { type: 'object', required: ['id'], properties: {
        id: { type: 'integer' }, position: { type: 'array', items: { type: 'number' } },
        rotationEuler: { type: 'array', items: { type: 'number' } }, scale: { type: 'array', items: { type: 'number' } } } } } } },
    run: a => call('POST', '/apply', { edits: a.edits }) },

  { name: 'scenebridge_build',
    description: 'Batch-create primitive objects (buildings, roads, props). shape  in  cube|plane|cylinder|sphere|capsule|quad. Items nest under root/parent group paths (auto-created). Color: use either color:[r,g,b] with 0..1 floats, OR colorHex:"#RRGGBB". Returns name->id for what you built. Use to construct one or many models.',
    inputSchema: { type: 'object', required: ['items'], properties: {
      root: { type: 'string', description: 'top-level container name (e.g. "City")' },
      items: { type: 'array', items: { type: 'object', required: ['shape'], properties: {
        shape: { type: 'string' }, name: { type: 'string' }, parent: { type: 'string' },
        position: { type: 'array', items: { type: 'number' } }, rotationEuler: { type: 'array', items: { type: 'number' } },
        scale: { type: 'array', items: { type: 'number' } },
        color: { type: 'array', items: { type: 'number' }, description: '[r,g,b] 0..1' },
        colorHex: { type: 'string', description: '#RRGGBB (alternative to color)' } } } } } },
    run: a => call('POST', '/build', { root: a.root, items: a.items }) },

  { name: 'scenebridge_spawn_prefab',
    description: 'Instantiate an existing Unity asset (.prefab or model .fbx) into the scene. Returns the new object id and world bounds.',
    inputSchema: { type: 'object', required: ['path'], properties: {
      path: { type: 'string', description: 'e.g. "Assets/.../Pick Up_21.prefab"' },
      name: { type: 'string' }, position: { type: 'array', items: { type: 'number' } } } },
    run: a => call('POST', '/spawn_prefab', { path: a.path, name: a.name, position: a.position }) },

  { name: 'scenebridge_delete',
    description: 'Remove an object from the scene by id (Undo-recorded). Confirm with the user before bulk deletes.',
    inputSchema: { type: 'object', required: ['id'], properties: { id: { type: 'integer' } } },
    run: a => call('POST', '/delete', { id: a.id }) },

  { name: 'scenebridge_rename',
    description: 'Rename an object by id.',
    inputSchema: { type: 'object', required: ['id', 'newName'], properties: { id: { type: 'integer' }, newName: { type: 'string' } } },
    run: a => call('POST', '/rename', { id: a.id, newName: a.newName }) },

  { name: 'scenebridge_create_marker',
    description: 'Place a typed SPATIAL marker as a child of an existing object (native child transform + .spatialmeta.json sidecar). type  in  Point|Axis|Plane|Volume|Path. This is the tool for semantic geometry a mesh doesn\'t encode: hitch/grip points, hinge axes, trigger volumes, driving paths. If the exact position is ambiguous, propose your best guess and ask the user to confirm/correct. parentId must exist (from scene_map).',
    inputSchema: { type: 'object', required: ['parentId', 'name', 'type', 'worldPosition'], properties: {
      parentId: { type: 'integer' }, name: { type: 'string' }, type: { type: 'string' },
      worldPosition: { type: 'array', items: { type: 'number' } }, worldRotation: { type: 'array', items: { type: 'number' } },
      hasFacing: { type: 'boolean' }, normal: { type: 'array', items: { type: 'number' } },
      knots: { type: 'array', items: { type: 'number' } }, halfExtents: { type: 'array', items: { type: 'number' } },
      volumeShape: { type: 'string' }, visual: { type: 'boolean' } } },
    run: a => call('POST', '/marker', a) },

  { name: 'scenebridge_list_markers',
    description: 'List live spatial markers whose host object still exists.',
    inputSchema: { type: 'object', properties: {} },
    run: () => call('GET', '/markers') },

  { name: 'scenebridge_undo',
    description: 'Undo the last change made through the bridge, in Unity.',
    inputSchema: { type: 'object', properties: {} },
    run: () => call('POST', '/undo', {}) },
];

const SERVER_INFO = { name: 'scenebridge', version: '0.9.5' };
const INSTRUCTIONS =
  'Edit a live Unity scene. Use the SMALLEST surface for the job: for any change you can specify ' +
  'precisely (move/rotate/scale/create/delete/mark), just call the matching tool - no visual editor ' +
  'needed. When the user wants to SEE a model or edit it BY HAND (ambiguous position, "let me place it"), ' +
  'call scenebridge_inline_editor and pass the HTML it returns straight to show_widget. Always call ' +
  'scenebridge_scene_map first to find ids/paths (ids change on recompile - key on path). For a whole/large ' +
  'live scene or continuous dragging, tell the user to open Studio (http://localhost:8791). Unity is ' +
  'left-handed, Y-up, +Z forward; rotations use euler order YXZ; metres.';

function send(msg) { process.stdout.write(JSON.stringify(msg) + '\n'); }

async function handle(msg) {
  const { id, method, params } = msg;
  if (method === 'initialize') {
    return send({ jsonrpc: '2.0', id, result: {
      protocolVersion: (params && params.protocolVersion) || '2024-11-05',
      capabilities: { tools: {} }, serverInfo: SERVER_INFO, instructions: INSTRUCTIONS } });
  }
  if (method === 'notifications/initialized' || method === 'initialized') return; // notification, no reply
  if (method === 'ping') return send({ jsonrpc: '2.0', id, result: {} });
  if (method === 'tools/list') {
    return send({ jsonrpc: '2.0', id, result: {
      tools: TOOLS.map(t => ({ name: t.name, description: t.description, inputSchema: t.inputSchema })) } });
  }
  if (method === 'tools/call') {
    const tool = TOOLS.find(t => t.name === (params && params.name));
    if (!tool) return send({ jsonrpc: '2.0', id, error: { code: -32602, message: 'unknown tool: ' + (params && params.name) } });
    try {
      const r = await tool.run(params.arguments || {});
      const isErr = r.status >= 400;
      return send({ jsonrpc: '2.0', id, result: {
        content: [{ type: 'text', text: r.text }], isError: isErr } });
    } catch (e) {
      return send({ jsonrpc: '2.0', id, result: {
        content: [{ type: 'text', text: 'SceneBridge error: ' + (e.message || String(e)) }], isError: true } });
    }
  }
  if (id != null) send({ jsonrpc: '2.0', id, error: { code: -32601, message: 'method not found: ' + method } });
}

let buf = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', chunk => {
  buf += chunk;
  let nl;
  while ((nl = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, nl).trim();
    buf = buf.slice(nl + 1);
    if (!line) continue;
    let msg; try { msg = JSON.parse(line); } catch { continue; }
    handle(msg).catch(() => {});
  }
});
// Do NOT force-exit on stdin end: let the event loop drain any in-flight tool calls
// (their HTTP responses keep the process alive), then Node exits on its own.
process.stdin.on('end', () => {});
process.stderr.write('SceneBridge MCP server ready (auto-discovers bridge port 8787..8796 for this project)\n');
