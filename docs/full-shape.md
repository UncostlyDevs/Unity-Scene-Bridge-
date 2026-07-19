# SceneBridge - the full shape

*Working name. A two-way visual collaboration channel between AI coding agents (Claude Code, Codex, Cursor...) and a human developer working in Unity. This document is the "shape" we agreed to nail before planning and building. It merges two research passes run on 2026-07-14:*

- *A **deep-research** sweep of real developer complaints (Unity Discussions, r/Unity3D, GitHub issues on the unity-mcp repos, Hacker News, practitioner blogs) - 44 sources, 213 extracted claims.*
- *An **edge-case ideation** pass - 8 discipline lenses + 3 gap critics -> 156 concrete scenarios, distilled into a use-case catalog, a primitive vocabulary, and an edge-case register.*

> **Evidence honesty note.** The complaint quotes below were extracted by agents fetching the real pages; the adversarial-verification stage was cut short by session restarts, so treat them as *documented developer testimony* (accurately sourced) rather than independently fact-checked claims. Citations are given so any one can be re-opened and confirmed. The design conclusions don't rest on any single quote - they rest on the pattern, which is overwhelming.

---

## 1. The root cause, and why this is the right problem

One blog put the core diagnosis more cleanly than I could:

> "There is a structural representation mismatch between LLMs and game engines: engines consist of scene hierarchies, node graphs, binary assets, and drag-and-drop wiring, while LLMs can only emit text." - *Chier Hu, "AI Coding Tools for Video Game Development: A First-Principles Analysis"*

Everything else is a symptom of that. The agent is fluent in the half of Unity that is text (C#, shaders, ScriptableObjects) and blind in the half that is spatial and visual (scenes, prefabs, rigs, physics, framing). Developers report this as a *phase shift*, not a speedup:

> "In Unity specifically, Claude Code is strong at C# scripting, ShaderLab/HLSL, and editor tooling, but .scene/.prefab YAML editing, the GUI-centric editor workflow, and asset-pipeline hookup remain manual human work - shifting Unity development *from coding-heavy to design-heavy* rather than automating it end-to-end." - *Chier Hu, "Claude Code for Game Development: A Comprehensive Survey"*

That sentence is the market. The agent handed the design-heavy remainder *back to the human*, with no channel to collaborate on it. SceneBridge is that channel.

Crucially, the pain is **not** "the agent can't touch the editor" - that's largely solved (see Sec.3). The unsolved pain is: the agent can touch the editor but **can't see what it's doing, can't show you what it's about to do, and you can't point at what you mean.** Text is the only pipe, and 3D intent doesn't fit through it.

---

## 2. Complaint taxonomy (grounded)

Seven clusters, each with representative sourced testimony and a severity read.

### C1 - Spatial/visual blindness: agents can't see, and can't be pointed at things
**Severity: defining. Frequency: constant.**
- "Are game developers vibe coding with agents? It's such a visual and experiential [medium]..." - the framing of an entire *HN* thread; the OP argues game dev's visual/experiential nature makes it nearly impossible to write ahead-of-time machine-checkable success criteria an agent could iterate against. There is no automated visual eval loop for games. *(HN 46772079)*
- Documented failure mode where **all automated checks pass yet the game is broken to play**: green tests, compiling code, launches fine - but zero damage dealt in 60 seconds and level-ups every 3.9s instead of the intended 10-30s. Agents verify correctness, not feel. *(yurukusa, via Chier Hu survey)*
- A developer resorted to building a **dev-only grid overlay** just to give Claude and themselves a shared coordinate system, because natural-language spatial nudges ("move the panel left a bit") were too imprecise to converge. *("Your AI Agent Is Blind", Medium)*
- Nested-prefab UI layout "defeats every AI" - a novice reports *all* major text AIs fail at it. *(r/Unity3D 1icbqkm)*

### C2 - Scene/prefab/asset manipulation friction (the YAML problem)
**Severity: high. Frequency: daily.**
- Practitioners independently converge on a workaround: **have the AI write Unity Editor scripts to build scenes/prefabs** rather than emit `.prefab`/`.unity` YAML directly, because direct YAML is "unreliable and token-expensive." They treat editor scripts as "the only deterministic interface Unity reliably accepts." *(r/Unity3D "Game development with Unity MCP"; echoed across the MCP threads)*
- Godot's plain-text `.tscn` is cited as a decisive LLM advantage; a commenter calls Unity's YAML "very unfriendly for human consumption" and Unreal's binary assets "inaccessible to LLMs." *(HN 47146712)* - but even Godot's text format isn't enough: LLMs generate non-unique/placeholder UIDs (`aaaaa1`, `foobar`) and needed a custom duplicate-UID linter. Text access != semantic competence. *(same thread)*
- In CoplayDev/unity-mcp, editing *existing* scene objects (vs. creating new ones) is called out by the tool's own builder as **the key missing capability**, blamed on the sheer parameter count of Unity objects. *(Unity Discussions; issue #97)*

### C3 - Compile / domain-reload latency tax on the agent loop
**Severity: high. Frequency: every script edit.**
- Every C# change triggers a three-stage pipeline - asset import -> compilation -> domain reload - and the agent pays it on **every** edit. *(s-schoener blog; Chier Hu)*
- unity-mcp issue **#814 "Agents sleep after script changes"**: no primitive to await compilation, so agents insert fixed sleeps; ~**12 seconds of dead sleep** measured in a single play/read-console/fix/re-verify iteration. Requests a `wait_for_compilation` capability. *(GitHub CoplayDev/unity-mcp #814)*
- Issue **#657**: the 20-second reload wait is a *blind, non-deterministic timeout*, not actual reload-state detection - "causes unnecessary delays specifically in agentic workflows because agents trigger domain reloads frequently." *(#657)*

### C4 - Bridge fragility & context blowout (the plumbing breaks)
**Severity: high where adopted. Frequency: recurring.**
- Issue **#1173**: a `TcpListener` leak triggered by domain reload makes scene-inspection tools (`find_gameobjects`, `get_hierarchy`, `execute_code`) hang indefinitely **from the second play-mode cycle onward** - the agent goes scene-blind mid-session. *(#1173)*
- Issue **#317**: `get_hierarchy` returned **29,320 tokens against Claude's 25,000-token cap**, hard-failing the call - the agent literally cannot read the scene. Reporter says it's recurring, worst during semantic scene search. *(#317)*
- Issue **#1055**: `save_prefab_stage` fails - agents can open and modify prefabs but **can't save** them. *(#1055)*
- Issue **#1254**: `manage_camera` with `include_image=true` **pauses Play Mode** - an agent can't visually observe a running game without disrupting it. *(#1254)*
- Modal editor dialogs stall agent workflows indefinitely - a script-reload popup hangs the MCP until a human clicks it. *(reported in #891)*

### C5 - Human-in-the-loop gaps: no steering, no gate, destructive autonomy
**Severity: high. Frequency: per session.**
- Unity's own in-editor AI offers **no mid-execution steering or stop**: once it starts a task plan you can't intervene. One dev reports it hung **40 minutes on task 3 of 10**, forcing a Unity shutdown. No timeout/checkpoint/recovery. *(Unity Discussions "AI agent for Unity - become hard to continue"; Darko Unity review)*
- Unity AI resolves compile errors **destructively** - deleting or gutting working functionality to make code compile. *(Darko Unity review)*
- Once agents get MCP write access, the dominant failure mode shifts to **"persuasive wrongness"**: locally coherent, globally harmful edits - e.g. adjusting physics settings in ways that break determinism. *(Chier Hu)*
- Verification overhead is the core cost: multiple commenters say verifying/fixing AI-generated Unity code takes **longer than writing it themselves** beyond basic tasks. *(r/Unity3D "How do you use AI for coding", 45 comments)*

### C6 - The wished-for workflow (what people are literally asking for)
**This is the product brief, in users' words.**
- The wished-for division of labor: **the human builds UI visually in the prefab editor and the AI does semantic cleanup** - inspecting the GameObject tree, finding elements by displayed text (e.g. a `TextMeshProUGUI` showing "10"), renaming generic objects to meaningful names ("CountdownText"), and wiring them into serialized fields. *(r/Unity3D MCP thread)*
- Screenshot feedback is already valued as a differentiator - Google Antigravity's agent screenshots the scene and iterates on it, which a poster preferred over Cursor. *(r/Unity3D)* But it's one-way and lossy - see Sec.3.
- The whole class of tool is explicitly positioned **against the copy-paste-from-chatbot workflow**, treating direct spatial placement as the desired model. *("Your AI Agent Is Blind")*

### C7 - First-party disappointment & solo-project mortality (the white space is real)
**Severity: strategic. Frequency: n/a.**
- Unity's Muse judged "dramatically worse than other AI systems and not worth $30/month." Its successor, Unity AI beta, drew public backlash over usefulness and focus. On a 6-7 layer procedural-world benchmark, Unity AI completed only layer 1 and stalled at layer 2. *(Unity Discussions "Muse is disappointing"; Yahoo/MSN backlash coverage; Chier Hu benchmark)*
- **ProtoTip.AI**, a Unity-native agentic plugin by a solo dev, reached working-prototype then stalled - "the scope of making an agent operate inside Unity kept expanding beyond what one person could sustain." *(r/Unity3D)* - the cautionary tale for us: the space kills solo efforts by **surface-area sprawl**, not lack of demand. Our answer to that is the primitive vocabulary (Sec.4): build ~35 primitives, not 500 features.

---

## 3. The landscape: what exists, and exactly where it stops

| Existing solution | What it genuinely solves | Where it stops (our opening) |
|---|---|---|
| **Unity MCP servers** (CoplayDev/unity-mcp - 47 tools, 2021.3->6.x; IvanMurzak/Unity-MCP; HuntNight/unity-mcp-advanced) | The "AI can't touch the editor" problem: create/query GameObjects, edit scripts, manage assets, run tests, take screenshots - via **API calls**. | Command-based, not visual. No shared workspace, no preview-before-apply, no way for the human to *point*. Suffers C3/C4 directly (token blowout, socket leaks, save failures). |
| **Screenshot loops** (Antigravity, unity-mcp-advanced, `manage_camera include_image`) | One-way sight: agent renders the scene and iterates. | Lossy and one-directional. Human still describes corrections in words. Pauses Play Mode (#1254). No structured return - the agent guesses coordinates off pixels. |
| **Unity first-party AI** (Muse -> Unity AI beta) | In-editor presence, first-party asset access. | No scene-view awareness, no mid-run steering/stop, destructive compile-fixes, stalls on multi-step spatial work. Public backlash (C7). |
| **Editor-script workaround** (agent writes C# to build scenes) | Deterministic writes Unity actually accepts; dodges YAML unreliability. | Pays the full compile+reload tax **every action** (C3). No preview, no gate, no visual. It's a symptom of the gap, not a fix. |
| **JSON scene bridges** (`vibe-gamedev`) | Cleaner text interface than raw YAML. | Self-documented gaps: no prefab support, single active scene only, brittle to agent JSON formatting mistakes. |
| **Godot's `.tscn` text format** | LLMs can read/edit scenes directly. | Different engine; and even there, ID discipline fails without custom linters. Text access alone doesn't confer spatial competence. |
| **`grid overlay` / `Code Mode` / dev hacks** | Point solutions to specific pains (shared coords; token-lean tool-calling via JS against TS defs). | Each solves one facet. Nobody has composed them into a general two-way channel. |

**The white space, stated precisely:** no shipping tool provides a **two-way** visual channel where the agent *shows* (preview/diff/variants/overlays) **and** the human *answers with structured spatial data* (marks/picks/drags that return coordinates, not pixels), flowing through a **previewed, gated, journaled transaction**, with corrections **persisted as reusable semantic metadata.** Every existing tool has one or two of those; none has the loop.

---

## 4. The shape of the answer

### 4.1 Thesis
> Don't force the AI to infer exact 3D intent from language or screenshots when a human can supply it with one click - and don't make the human leave the AI workflow to do it. Put a small interactive surface in the chat where the agent shows its best guess and the human corrects only the spatial ambiguity, returning **structured data**.

### 4.2 The architectural bet (validated by the catalog)
The 156 scenarios span vehicles, level design, rigging, cinematics, asset pipeline, gameplay wiring, trust, and platform - endlessly many *scenarios*. But they decompose into **~35 core interaction primitives**. The scenarios are infinite; the primitives are few. **The primitives are the product.** This is also our defense against the surface-area death that killed ProtoTip.AI (C7): we ship a vocabulary, not a feature list, and third parties compose the long tail.

### 4.3 The primitive vocabulary (the core set)

**Rendering surfaces (AI -> human) - what the agent can *show*:**
- `proxy_viewport_3d` - streamed, decimated, orbitable scene/prefab view (the canvas, ~45 scenarios)
- `debug_overlay` - colliders, joint axes/cones, rays, NavMesh, bounds, contacts, layer badges, skeletons (the single biggest surface, ~50 scenarios; **must render in the correct local frame or it lies**)
- `side_by_side_variants` - orbit-synced candidate galleries (~28)
- `visual_diff` - before/after ghosts, wipe sliders, rebased diffs (~30)
- `capture_replay + time_scrub` - record play-mode truth, replay as scrubbable ghosts/filmstrips (~20; **data playback, never re-simulation**)
- `thumbnail_triage_grid`, `table/matrix`, `heatmap`, `graph/curve`, `ghost_preflight`, `live_metric_badge`, `node_wire_graph`, `filmstrip_pose_scrub`, `editor_render_stream` (the shader-safe fallback)

**Input primitives (human -> AI) - every one returns structured data, never pixels:**
- `pick_one_of_N (+ none-of-these)` - the cheapest, highest-value human act (~35)
- `point_on_geometry` - surface pick with vertex/edge/plane/feature snapping; re-projects onto full-res mesh; resolves bone-local when skinned (~28)
- `parametric_handle_drag` - radius/height/arc/cone/falloff/threshold/curve handles, constrained to valid states at input time (~30)
- `transform_drag` - TRS gizmo / constrained rotation / 6DOF, returned in a declared frame (~25)
- `per_item_triage` - accept/reject/flag grids with **stable candidate ids across regeneration** (~25)
- `lasso / region / paint` - encodes strokes+falloff, not per-vertex arrays (~20)
- `axis/plane/frame mark` - "a point without axes is half an answer" (~18)
- `object/reference pick` - click -> GlobalObjectId/bone/collider-pair (~18)
- `volume_rough_block`, `toggle/waive (with reason)`, `timeline/frame markers`, `path/spline draw`, `annotate_render` (the "more headroom" scribble), `label/classify`, `numeric_field_commit` (float-exact escape hatch on every drag), `drag_to_pair/rewire`, `mirror/symmetry`

**Transaction & persistence primitives (the connective tissue):**
- `approve_reject_transaction` - the ambient contract: **nothing writes without a card**, with per-property/per-item granularity (~55 - the most-used primitive of all)
- `semantic_anchor_persist` - named child transforms + metadata (axis conventions, per-mesh, per-variant); **the product's memory**; paired with geometry-hash drift detection (~22)
- `undo_grouped_atomic_apply` - collapsed multi-object undo + compensating actions for asset writes (partial undo that orphans a joint is a *correctness bug*)
- `stable_object_refs` - GlobalObjectId + GUID round-trip (names and instance IDs are always wrong eventually)
- `audit/provenance log`, `exemption/waiver persist`, `deferred/trial apply + revert`, `live_revalidation` (rules re-run as the human drags), `confidence_routing` (escalate below threshold, log above)

**Platform primitives (open-source, day-one):**
- `capability_handshake + render tiers + degradation notice` - every widget degrades: live viewport -> hotspot stills -> numbered text, all returning the *same* structured decision
- `frame_tagged_coordinate contract` - the bridge **rejects untagged coordinates**
- `widget_manifest + scoped access + payload schema validation` - the third-party trust boundary
- `host data-provider API`, `session auto-resume across domain reload`, cross-host adapters/sidecars

**In one sentence:** a streamed proxy viewport with truthful overlays; galleries, diffs, replays, grids, tables to *show*; point/axis/frame/volume/path marks, drags, picks, lassos, paints, and triage to *answer*; all flowing through frame-tagged structured payloads into previewed, undo-grouped, journaled transactions with persistent semantic anchors.

---

## 5. Gap analysis - documented pain -> the primitive that kills it

This is the money table: it proves the vocabulary was reverse-engineered from real complaints, not imagined.

| Documented pain (from Sec.2) | Primitive(s) that address it | Anything existing do this? |
|---|---|---|
| Spatial nudges too vague; grid-overlay hack (C1) | `point_on_geometry`, `transform_drag`, `parametric_handle_drag` -> structured coords | No - screenshots return pixels, not coords |
| Green tests, broken *feel* (C1) | `capture_replay + time_scrub`, telemetry `graph`, A/B `variants` | No |
| YAML unreliable; editor-script workaround (C2) | `approve_reject_transaction` + `undo_grouped_atomic_apply` via editor API (no per-action recompile) | Partially - MCP writes, but no preview/gate |
| Editing *existing* objects is the missing capability (C2) | `transform_drag`, `parametric_handle_drag`, `visual_diff` on live state | No - named as unsolved by the tool's own builder |
| 12s dead sleep; blind 20s reload wait (C3) | transactions apply via editor API, not generated C# -> **no recompile for spatial edits** | No |
| `get_hierarchy` 29k-token hard-fail; scene blindness (C4) | streamed **decimated** `proxy_viewport` + `object pick` (visual, not a token dump) | No - this *is* the token dump |
| No mid-run steering; 40-min hang; destructive fixes (C5) | `approve_reject_transaction`, `confidence_routing`, `failure ledger`, `live_revalidation` | No |
| "Persuasive wrongness," determinism-breaking physics (C5) | `ghost_preflight` + `visual_diff` + `blast-radius gate` before write | No |
| Wished-for: human builds UI, AI does semantic cleanup + wiring (C6) | `object pick`, `label/classify`, `node_wire_graph`, `drag_to_pair` | No - this is the exact unbuilt loop |
| Re-asking the same spatial question every session (C1/C6) | `semantic_anchor_persist` + drift detection | **No - nobody persists corrections. This is our flywheel.** |

The bottom two rows are the ones with no competitor at all.

---

## 6. What SceneBridge uniquely owns

1. **The closed two-way loop.** Show *and* answer *and* apply, in one surface, without leaving chat. Everyone else has a fragment.
2. **Structured return, not pixels.** A click becomes a GlobalObjectId + a frame-tagged coordinate, so the agent acts on data it can verify - not a guess off a screenshot.
3. **The anchor-persistence flywheel.** Mark `MuzzlePoint`/`FifthWheelPivot`/`DoorHinge` **once**; it persists as named metadata and is never re-asked. Every correction makes the asset library permanently more AI-legible. This is compounding capital no competitor is accruing.
4. **The transaction/trust layer as a first-class product.** Preview -> gate -> atomic undo -> journal -> provenance -> confidence-routing. This directly answers the loudest C5 complaints (no steering, destructive autonomy, verification overhead) and is the precondition for *unattended* agent runs.
5. **A vocabulary, not a feature pile.** ~35 primitives cover ~95% of 156 scenarios; the rest are compositions. This is both the design elegance and the survival strategy against surface-area death.

---

## 7. Non-negotiable constraints the edge-cases impose

The 156-scenario pass produced a brutal edge-case register. These are the ones that must be designed in from line one, because retrofitting them is a rewrite:

- **Frame-tagged coordinates, enforced by contract.** Every payload carries space (world/prefab-local/asset-source) + units + up-axis + handedness. The bridge rejects untagged data. Skip this and every downstream number is a lie waiting to happen (negative scale, Z-up DCC, -90deg import compensation, PhysX slip-units-aren't-degrees).
- **Overlays render in the subject's true local frame** - a joint cone drawn around the wrong axis bakes garbage at full confidence. Validate the frame *before* enabling a handle.
- **Domain-reload survival.** Bridge state, queues, and journals live *outside* the AppDomain; sessions auto-resume and rebind via GlobalObjectId. (This is literally the #1173 bug class - we treat it as an architectural given, not a bug to patch.)
- **Preview honesty under concurrent edits.** The human never stops working while a card is open: stale-badge, diff-rebase, and revalidate-on-apply, applying only the still-valid subset.
- **Undo atomicity is a safety property.** 200-object creations collapse to one step; asset/prefab writes (off the scene undo stack) need compensating actions.
- **Data playback, never re-simulation** for A/B - seeded, fixed-timestep, identical solver, or the "winning" variant is noise.
- **Accessibility is not optional** - pass/fail needs redundant channels (hatch/icon/outline); red/green hue alone excludes ~8% of male developers. Fallback tiers need keyboard + screen-reader traversal.
- **Graceful degradation tiers** - live WebGL viewport -> pre-rendered hotspot stills -> captioned numbered text, each returning the *same* structured decision, with the agent *told* which tier it got so it never over-trusts a quantized point.
- **Stable candidate ids across regeneration** - a rejected loot-spawn must never resurrect on the next run.

Full register (asset weirdness, state sync, precision/UX, trust/safety, scale/perf, extensibility) lives in [research/edge-case-catalog.md](research/edge-case-catalog.md), and the raw sourced complaint claims in [research/complaint-claims-raw.jsonl](research/complaint-claims-raw.jsonl).

---

## 8. Scope posture for the design phase

**The v1 spine (the daily top-5, in dependency order):**
1. `proxy_viewport_3d` + `debug_overlay` (the canvas + truthful gizmos)
2. `approve_reject_transaction` + `undo_grouped_atomic_apply` (the gate - nothing else is safe without it)
3. `point_on_geometry` + `axis/frame mark` + `semantic_anchor_persist` (the anchor loop - the thesis in one feature)
4. `transform_drag` + `parametric_handle_drag` (drag-fit corrections)
5. `pick_one_of_N` via `editor_render_stream` (the universal, shader-safe disambiguator)

That set alone answers C1, C2 (existing-object edits), C5 (the gate), and C6 (the anchor flywheel) - the highest-severity clusters.

**Platform-from-day-one (thin but present):** the `capability_handshake`, the `frame_tagged_coordinate` contract, and the `widget_manifest` schema. These are cheap to stub and impossible to retrofit - the whole open-source extensibility story hinges on them existing before the first widget ships.

**Architecture, unchanged from our earlier sketch but now evidence-backed:** chat-side widget (proven - the coupling demo already round-tripped structured JSON via `sendPrompt`) <-> local bridge server <-> Unity Editor plugin that reads scene/prefab state and applies transactions through editor APIs (Undo-recorded, no per-action recompile). Unity stays authoritative; the widget is a temporary spatial surface.

**Open questions to settle before/at the design doc:**
- **Host reality check.** Which agent surfaces do *you* actually drive day-to-day (this app, Codex CLI, Cursor)? The widget-rendering capabilities differ, and it decides whether v1 targets in-chat widgets, a synced EditorWindow, or a side panel. This is the one external constraint that can move the whole plan.
- **Anchor storage:** child transforms under an `AIAnchors` group vs. a `.spatialmeta.json` sidecar vs. both? (Sidecar survives FBX re-export and cross-engine; child transforms are native but stripped by *Optimize Game Objects* - an edge-case landmine.)
- **Provenance/journal location:** in-repo (commits, shareable, diffable) vs. local-only (private, no repo noise)?
- **First real target:** stay in this clean Unity-Scene-Bridge- project, or point v1 at TruckSwarm's actual truck/trailer prefabs for a real-stakes round-trip?

---

*Next step is the design doc (architecture, data contracts, the v1 primitive specs, the widget SDK manifest). This shape is the input to that. Poke holes in it first - that's what it's for.*
