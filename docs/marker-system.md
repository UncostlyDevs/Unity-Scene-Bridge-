# SceneBridge - Spatial Marker System

*The spec we build against. Supersedes the point-only `/anchor` first cut (2026-07-15), generalizing it into a typed marker system. Grounded in how Unreal, Unity, Godot, glTF, and FBX actually represent attachment points, plus the edge-case register in [full-shape.md](full-shape.md).*

---

## 0. Why this exists (one paragraph)

A "spatial marker" is how a human hands the AI a piece of spatial truth it can't infer from geometry or names - the kingpin's pivot, the muzzle's forward, the cargo bay's volume. The research finding that shaped this: **every engine represents an attachment point as a named node carrying a full orientation *frame* (position + which-way-it-faces), never a bare XYZ point.** A position answers *where*; almost every real use needs *which way*. So the humble "anchor point" becomes a **named, typed, oriented frame** - and that single model serves all four uses at once: attach/parent, drive physics/joints, align/measure, and mark logic points.

---

## 1. The unified model

**One envelope for every marker.** Point, Axis, Plane, Volume, and Path are the *same structure* - they differ only in `params`. This is what makes it easy for the AI to read/write and what maps cleanly onto real Unity components.

```jsonc
Marker = {
  "id":     "muzzle_01",            // stable, unique within the host asset
  "type":   "Point",                // Point | Axis | Plane | Volume | Path
  "label":  "muzzle",               // semantic tag (may repeat across assets)
  "space": {
    "parent":     "Turret/Barrel",  // host Transform path AND GlobalObjectId (below); null = root
    "parentGid":  "GlobalObjectId_V3-...",
    "relativeTo": "Local"           // Local (default, robust) | World (output only)
  },
  "frame": {
    "position": [x, y, z],          // meters, in parent-local space
    "rotation": [x, y, z, w]        // unit quaternion, (x,y,z,w) order (matches Unity + glTF)
    // scale is intentionally NOT stored here - see Sec.3 rule 2
  },
  "params": { /* discriminated by type - see Sec.2 */ },
  "provenance": {
    "placedBy":   "human" | "ai",   // who set it
    "confidence":  0.0..1.0,        // AI's confidence if ai-placed (see Sec.5)
    "geomHash":    "sha1:...",      // hash of host mesh at placement time (drift detection, Sec.4)
    "created":     "2026-07-15T..."
  }
}
```

The `frame` is the heart. A **Point** uses its position (and optionally forward/up). An **Axis** uses `frame.forward` (`rotation * (0,0,1)`). A **Plane** uses `frame.up` as its normal. A **Volume** box uses the whole frame + half-extents. Everything else is `params`.

---

## 2. The five types

| Type | Beyond the frame it stores | Frame DOF that matter | Unity component it becomes |
|---|---|---|---|
| **Point** | `hasFacing: bool` | position always; rotation iff `hasFacing` | empty GameObject |
| **Axis** | `kind: line\|ray\|segment`, `length?`, `limits?: {min,max}deg` | position + **forward**; roll matters iff a reference "up" is needed | `HingeJoint.axis` + `JointLimits`, or `transform.forward` |
| **Plane** | `infinite: bool`, `halfExtents2D?: {u,v}` | position (a point on it) + **up = normal**; right/forward = U/V when finite | `UnityEngine.Plane(normal, point)`, or a thin `BoxCollider`/Quad |
| **Volume** | `shape: box\|sphere\|capsule`, `halfExtents?`, `radius?`, `height?`, `axis?` | box: full frame; sphere: position only; capsule: position + swept axis | `BoxCollider`/`SphereCollider`/`CapsuleCollider`, `isTrigger` for zones |
| **Path** | `closed: bool`, `interp: linear\|bezier\|catmullrom`, `knots[]` | container origin; knots are container-local | `SplineContainer` + `Spline` of `BezierKnot` |

**Per-type notes that keep them unambiguous:**

- **Point** - `hasFacing:false` = a bare location (a hit-spark origin). `hasFacing:true` = a seat/spawn/muzzle where orientation is real; the quaternion captures both forward and up with no ambiguity.
- **Axis** - always read the direction off a canonical basis vector (**forward**), so every consumer agrees which axis is "the" one. `segment` adds `length` (suspension travel, finite axle). A hinge adds `limits` in degrees, measured about the axis from the frame's reference up.
- **Plane** - store **point + normal**, not Unity's `normal + distance` (point+normal is reparent-stable and portable; convert at runtime). A finite coupling plate adds in-plane half-extents (U/V from the frame's right/forward). A symmetry plane needs nothing extra - the normal is the reflection axis.
- **Volume** - the oriented box (OBB) is the general case and is exactly a `BoxCollider` on a rotated Transform. Put the center in `frame.position` and set the collider's local `center = 0` (one source of truth). An axis-aligned box is just a box with identity world rotation - not a separate type.
- **Path** - a polyline cable/route = knot positions only, `interp:"linear"`. A smooth spline mirrors Unity's `BezierKnot` exactly; **tangents are stored knot-local** (so a knot can rotate without rewriting tangents). Per-knot `rotation` gives an oriented sweep (pipe cross-section, banked track).

### Two worked examples

```jsonc
// Kingpin pivot: an Axis on the trailer's underside, swinging about local Y, +/-45deg
{ "id":"kingpin","type":"Axis","label":"KingPin",
  "space":{"parent":"Trailer/Body","relativeTo":"Local"},
  "frame":{"position":[0,-1.36,4.28],"rotation":[0,0,0,1]},
  "params":{"kind":"line","limits":{"min":-45,"max":45}} }

// Cargo bay: an oriented trigger box on the chassis
{ "id":"cargo_bay","type":"Volume","label":"cargo_bay",
  "space":{"parent":"Chassis","relativeTo":"Local"},
  "frame":{"position":[0,1.2,-1.5],"rotation":[0,0,0,1]},
  "params":{"shape":"box","halfExtents":[1.1,0.8,2.3]} }
```

---

## 3. Conventions (load-bearing - state at the top of every payload)

1. **Frame = position + quaternion.** Orientation is a quaternion `(x,y,z,w)`, never Euler (avoids rotation-order/gimbal ambiguity). Axis convention: **+Z = forward/outward, +Y = up** (matches Unity XR). World basis: **Unity left-handed, Y-up, Z-forward, meters.**
2. **Never store size in `scale`.** Box half-extents, radius, height, angle limits, knot lists all live in `params`; the marker's `scale` is fixed at 1. Unity scale compounds down the hierarchy and corrupts child gizmos/normals.
3. **Store local, not world.** A pose is relative to a named `parent` (Transform path *and* `GlobalObjectId` - names go stale, GIDs don't). World coords are an output convenience only; consumers resolve `world = parent.localToWorldMatrix * localPose`.
4. **Handedness is converted at the boundary, by contract.** The in-chat widget renders in three.js (right-handed); Unity is left-handed. The bridge is the single place that converts, and every coordinate payload is frame-tagged (space/units/up-axis/handedness); untagged coordinates are rejected. (This is the same frame-tag contract from full-shape.md - and the fix for the "looks mirrored" issue.)
5. **Validate on write:** quaternions/normals are unit length; `halfExtents`/`radius`/`height >= 0`; `scale == 1`.

---

## 4. Persistence - "both", at the asset level

Per the locked decision: markers persist **two ways at once**, and target the **source asset/prefab**, not the scene instance (asset-level is what makes it *mark once, inherited by every instance/scene/agent*; a scene-only mark would be re-asked every time). For the current sandbox test objects (no source asset), a per-scene sidecar is the fallback.

- **Native child transforms** - `Host/AIAnchors/<Name>` empty GameObjects, so they're visible/selectable in the Unity hierarchy and travel with the object. Axis/Plane/Volume also get the matching component (HingeJoint helper / Quad / trigger Collider) when useful.
- **Sidecar** - `<asset>.spatialmeta.json` (or `Assets/SpatialAnchors/<scene>.spatialmeta.json` for scene-only), the portable, git-diffable, engine-neutral record. Regenerated from actual scene/asset state on every write so it never drifts from reality.

**Naming:** namespaced + typed + indexed - `KingPin`, `MuzzlePoint_0`, `Wheel.Axle.FL`. Unique within the host.

**Robustness rules (from the edge-case register):**
- **Drift detection** - store `geomHash` of the host mesh at placement time; on reimport, if the hash changed, flag the marker as *possibly drifted* and lower its confidence (never silently trust it).
- **Strip-on-reimport guard** - Unity's *Optimize Game Objects* strips bone-child transforms on model reimport; the sidecar is the safety net (it survives), and the importer post-process re-creates the child transforms from the sidecar.
- **Never rely on names or instance IDs alone** - GlobalObjectId is the durable reference.

---

## 5. Placement - AI proposes, human corrects, confidence routes

Per the locked decision (**both, by confidence**):

1. The AI computes a **candidate** marker from cheap signals - `Renderer.bounds` math (kingpin ~ front-underside centerline), object/bone naming, mesh features, symmetry - and attaches a **confidence** `0..1`.
2. **Confidence >= threshold ->** the AI places it directly and reports it (logged in `provenance`, revertible). No interruption.
3. **Confidence < threshold ->** the AI opens the marker widget **pre-loaded with its best guess**, and the human *corrects only the part that's wrong* (nudge the point, spin the axis, resize the box). Placing entirely from scratch is available but is the fallback, not the default.
4. Thresholds are per-marker-type and adjustable; overturn rate feeds back (a marker the human always moves -> the AI's guesses for that type were bad -> ask more often).

The widget's job is **correction, not blank-slate placement** - that's what scales the human effort down.

---

## 6. Bridge API (generalize `/anchor` -> `/marker`)

- `GET  /markers?scene=<name>` (and later `?asset=<guid>`) -> list existing markers (from sidecar + live scene).
- `POST /marker` -> body = the Sec.1 envelope (minus provenance the bridge fills). Creates/updates the child transform(s) + type component, writes the sidecar, Undo-grouped. Returns the created id + resolved local frame.
- `DELETE /marker` -> `{host, id}` -> removes child + sidecar entry (Undo-grouped).
- Existing `/scene`, `/apply`, `/refresh` stay. The current `/anchor` (point-only) is the seed - it becomes the `type:"Point"` path of `/marker`.

Candidate *computation* lives on the AI side (it reads `/scene` and does the bounds/naming math); the bridge only persists and reflects truth.

---

## 7. Widget UX per type (all five ship together)

Common: mouse-only navigation (orbit-drag, wheel-zoom, right-drag look, middle/shift-drag pan - **no keyboard**, since chat owns the keyboard focus), surface raycast with vertex/edge/center snapping, and a type picker. Each tool emits the unified Sec.1 envelope via `sendPrompt` (`SPATIAL_MARKER {json}`), the AI POSTs it to `/marker`.

- **Point** - click surface -> position; a draggable forward arrow sets facing (toggle `hasFacing`).
- **Axis** - click origin, then drag a direction handle (or two-click along the axis); optional limit arcs for a hinge; length handle for a segment.
- **Plane** - click a point + use the surface normal (or 3-point placement); drag U/V handles for a finite plate.
- **Volume** - drop an oriented box and drag its face handles; switch to sphere/capsule; snap to bounds.
- **Path** - click a sequence of knots on surfaces/grid; toggle closed; drag tangents for smooth.

For each, the AI's guess arrives pre-placed (Sec.5); the handles are for *correcting* it.

---

## 8. Build plan

**Prerequisites (fix first, both already diagnosed):**
- Mouse-only navigation (drop keyboard fly - chat owns keyboard focus in the embedded widget).
- Handedness conversion at the bridge boundary (fixes "looks mirrored"; establishes the frame-tag contract markers depend on).

**Milestone M1 - the marker system (all five types, per the "all at once" decision):**
1. Bridge: generalize `/anchor` -> `/marker` with typed params + component creation + typed sidecar; add `GET /markers`, `DELETE /marker`.
2. Widget: type picker + the five placement/correction tools above, mouse-only nav, snapping.
3. AI side: candidate computation (bounds/naming/mesh) + confidence routing.
4. Round-trip test in the sandbox: AI proposes a kingpin Axis under the (moved) Cab -> human corrects -> real `Cab/AIAnchors/KingPin` HingeJoint-ready transform + sidecar, verified by read-back.

**Later:** asset/prefab-level persistence (vs scene sidecar), drift-detection on reimport, importer post-process that rebuilds stripped child transforms from the sidecar.

---

*Open question deferred to build time: exact confidence thresholds and the candidate heuristics per type - easier to tune against real placements than to guess now.*
