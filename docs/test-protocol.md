# SceneBridge - Test Protocol (current function set)

*A step-by-step QA pass over everything built so far. Goal: confirm the whole basics layer is rock-solid before we build more.*

**How this works:** you drive the widget and Unity; I verify the Unity side through the bridge. Two kinds of check:
-  **You confirm** (visual/interaction) - you just look and say pass/fail.
-  **I verify** (round-trip) - when you hit Create/Apply, the payload reaches me and I read `/scene` + `/markers` to confirm it landed with the right data. You don't have to check these by hand.

Mark each `[ ]` -> `[x]` pass or `[!]` fail (note what happened). Work top to bottom; report a section at a time (or all at once) and I'll verify the  items.

> **Known caveat (not a failure):** marker *positions* are exact; marker *orientations* (Plane normal, Axis direction) may look rotated in Unity - that's the deferred handedness fix. Note it if you see it, but it's expected.

---

## 0. Setup
- [ ] **0.1**  Bridge is live - *I confirm `/ping`.*
- [ ] **0.2**  Clean slate - *I run `/reset_demo`: fresh `SB_TestRig`, zero markers, sidecar wiped.*
- [ ] **0.3**  The widget below shows the clean truck (grey chassis, blue cab, 4 dark wheels), no orange markers.
- [ ] **0.4**  Your Unity **Scene view** is visible on `SB_TestRig` so you can watch changes land.
- [ ] **0.5**  No red (!) line under the viewport.

## A. Navigation (Select tool active)
- [ ] **A1**  **Orbit** - drag empty space -> camera orbits smoothly.
- [ ] **A2**  **Zoom** - mouse wheel -> dollies in/out.
- [ ] **A3**  **Pan** - right-drag (or middle-drag) -> camera pans.
- [ ] **A4**  **View presets** - 3/4, Front, Under each snap the camera.
- [ ] **A5**  **No keyboard leak** - with the viewport focused, pressing W/A/S/D does **not** type into the chat box.

## B. Select tool - transform + apply
- [ ] **B1**  Click a **wheel** -> it highlights and a move gizmo appears on it.
- [ ] **B2**  Click empty space -> deselects (gizmo goes away).
- [ ] **B3**  Select the **Cab**, drag a gizmo arrow -> Cab moves in the widget; drag empty space still orbits (no fighting).
- [ ] **B4**  **Move / Rotate / Scale** buttons switch the gizmo type; **Local/World** and **Snap** respond.
- [ ] **B5**  With the Cab moved+rotated+scaled, hit **Apply to Unity ->**. *I verify the real Cab matches your pos/rot/scale, and you watch it change in the Unity Scene view.*
- [ ] **B6**  In Unity, **Ctrl+Z** -> the Cab reverts (Apply was one undo step).
- [ ] **B7**  **Manual value edit** - with a part selected, type exact numbers into the **Pos / Rotdeg / Scale** fields -> the part moves precisely in the widget, and the gizmo follows; dragging the gizmo updates the fields live (two-way). Then **Apply** -> *I verify the exact typed values land in Unity.*

## C. Point tool
- [ ] **C1**  Pick **Point**, click the Cab -> orange sphere + normal arrow lands on the surface; panel opens.
- [ ] **C2**  Toggle **has facing** on; name it `Muzzle`; tick **Mark Visually**.
- [ ] **C3**  **Create in Unity ->**. *I verify `Cab/AIAnchors/Muzzle` exists, sidecar has it as type Point with `visual:true`.*
- [ ] **C4**  In Unity, `Muzzle` shows an **orange gizmo sphere** in the Scene view.

## D. Axis tool
- [ ] **D1**  Pick **Axis**, click a **start** point on the Chassis, then an **end** point -> an axis line with an arrow appears between them.
- [ ] **D2**  Tick **hinge limits**, set min -30 / max 90; name it `Hinge`; **Mark Visually**.
- [ ] **D3**  **Create**. *I verify `Chassis/AIAnchors/Hinge` type Axis, `hasLimits:true`, limits -30..90 in the sidecar.*

## E. Plane tool
- [ ] **E1**  Pick **Plane**, click the Chassis -> a translucent plane oriented to the surface appears.
- [ ] **E2**  Name it `CouplePlate`, **Mark Visually**, **Create**. *I verify type Plane persisted with its normal/rotation.*

## F. Volume tool
- [ ] **F1**  Pick **Volume**, click the Chassis top -> a wireframe box appears.
- [ ] **F2**  Change the **X/Y/Z half-extents** -> the box resizes live in the viewport.
- [ ] **F3**  Name it `CargoBay`, **Mark Visually**, **Create**. *I verify a real **trigger BoxCollider** on `Chassis/AIAnchors/CargoBay` sized 2xhalf-extents, params in sidecar.*

## G. Path tool
- [ ] **G1**  Pick **Path**, click 4-5 points along the chassis -> spheres + a polyline connect them.
- [ ] **G2**  **Remove last point** drops the last knot; **closed loop** connects end->start.
- [ ] **G3**  Name it `Cable`, **Mark Visually**, **Create**. *I verify N child `Knot_*` transforms under `AIAnchors/Cable` and the knot list in the sidecar.*

## H. Persistence & cross-cutting
- [ ] **H1**  *I read `/markers` and confirm every marker you made is present with correct type + params + `visual` flags.*
- [ ] **H2**  In Unity, all markers sit under the right `Host/AIAnchors/` group, and visual ones show orange spheres.
- [ ] **H3**  `Assets/SpatialAnchors/SampleScene.spatialmeta.json` exists and lists your markers.
- [ ] **H4**  In Unity, **Ctrl+Z** a couple times -> the last markers undo cleanly (no errors).

## I. Robustness / edge cases
- [ ] **I1**  In a marker tool, click **empty sky** (miss) -> no crash, info line says "missed", no marker.
- [ ] **I2**  Start an **Axis** (one click), then switch to **Point** -> the half-finished axis clears, no leftovers.
- [ ] **I3**  Create a Point named `Muzzle` **again** on the Cab -> *I verify it **replaces** the old one (not a duplicate).* 
- [ ] **I4**  Place markers on **three different hosts** (Cab, Chassis, a Wheel) -> each gets its own `AIAnchors` group.
- [ ] **I5**  Through the entire run, the red (!) line **never** appeared.

---

### Result summary (fill in as you go)
| Section | Pass | Notes |
|---|---|---|
| 0 Setup | | |
| A Navigation | | |
| B Select/transform | | |
| C Point | | |
| D Axis | | |
| E Plane | | |
| F Volume | | |
| G Path | | |
| H Persistence | | |
| I Robustness | | |

*Anything in the "fail" or "weird" column becomes the fix list before we build more. Orientation-looks-rotated on D/E is expected (handedness), not a fail.*
