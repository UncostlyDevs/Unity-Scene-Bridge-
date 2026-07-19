# SceneBridge Stress Test Protocol

Purpose: deliberately hunt bugs across the whole pipeline - bridge API, sync loops, hierarchy,
skinning, markers, and recovery paths - instead of waiting for them to find us. First executed
2026-07-16 (v0.8.2): found and fixed 2 shipping bugs on its first run (see Findings Log).

## Prerequisites

- Unity open with the project; bridge answering (`GET /ping` -> `"ok":true`).
- Preferences -> General -> **Interaction Mode = "No Throttling"** (required for background operation).
- **Save the scene first** (Ctrl+S). The suite cleans up after itself, but a crash mid-run shouldn't cost work.
- A rigged character in the scene enables the bone-drift sweep (skipped otherwise).
- Close other bridge clients you care about - the storms will make their views churn.

## Part A - Automated suite

Run from a PowerShell prompt, in order:

```powershell
.\tools\stress-run1.ps1   # fuzz, throughput, integrity, batch
.\tools\stress-run2.ps1   # markers, spawn/delete, bone drift, cleanup, reload drill
```

What they cover:

| Area | Tests |
|---|---|
| API robustness | malformed JSON, empty bodies/edits, unknown ids/routes, NaN injection, bogus marker parents, empty renames, unknown deletes - bridge must answer everything with JSON and survive |
| Throughput | 100 rapid `/apply` calls; p50/p95/max reported (PowerShell client adds ~50ms/call vs a browser's keep-alive fetch - Studio's real latency is lower) |
| Integrity | 20 random multi-axis transforms, exact round-trip verification |
| Batch | one `/apply` with 120 edits, all applied |
| Marker storm | 12 rapid mixed-type creates, live-feed growth, weird-name renames |
| Lifecycle | 3 prefab spawns (distinct ids) -> `light`/`full` node-count equality -> 3 deletes -> count restored |
| Bone drift | 8 random bone rotations -> **every bone in the skeleton must read localScale exactly (1,1,1)** -> batch restore |
| Cleanup | all `SB_TestRig` instances destroyed by id; orphaned markers must vanish from the live feed |
| Reload drill | `/refresh` -> bridge must be answering again within 3 minutes (LAST test - see gotchas) |

### Hard invariants (any violation is a bug)

1. The bridge answers every request with JSON (or HTTP 404) - it never hangs outside a reload blackout.
2. `/scene` output is always valid JSON - no `NaN`/`Infinity` can ever reach a transform (guarded since v0.8.2).
3. `/scene?light=1` and `/scene` always report identical node counts.
4. A rotation-only editing session leaves every localScale untouched (the v0.8.1 feedback-loop regression guard).
5. Every marker in the live `/markers` feed has a resolvable host in the scene.
6. `/marker` with an unknown parent is an error, never a phantom object (v0.8.2 regression guard).

## Part B - Manual protocol (things only eyes can judge)

Setup: Unity and Studio (Edge, `localhost:8791/studio.html`) side by side.

1. **Drag latency feel** - drag a vehicle in Studio; Unity's ghost should track within ~100ms, no rubber-banding.
2. **Ghost lifecycle** - blue ghost appears on drag, lingers <=1.5s after you stop, vanishes 0.125s after you refocus Unity - with a visible moment of ghost/mesh alignment.
3. **Solo violence test** - Solo on, fast wiggly drags + rapid scrubs + direction reversals on a parent; children must never move. Log line must read "SOLO, N kids held" on every push.
4. **Mirror parity** - orbit Studio to roughly Unity's camera angle: left is left, right is right, raised arm on the same side.
5. **Pose parity** - a rigged character must match Unity's pose exactly (arms, feet). Multi-axis bone rotations round-trip: type (20,30,40) on a bone, Unity Inspector must show ~(20,30,40).
6. **Instant skinning** - rotating a bone deforms the character in the same frame as the drag; Unity follows ~80ms later.
7. **Tree navigation** - click rows (frame + select at part-relative distance), drag the panel edge to resize (persists), `F` frames selection, `Sel: Root/Part` behaves.
8. **Marker suite** - place all five types on real geometry; verify Unity children + gizmos + sidecar.
9. **Unity -> Studio** - move/pose things in Unity; Studio follows within ~1s (0.5s pulls), including re-baked poses.
10. **Multi-editor awareness** - two Studio tabs editing the same object is last-write-wins; that's current expected behavior, not a bug (until multi-editor coordination lands).

## Part C - Recovery drills

- **Domain reload:** any `.cs` change or `/refresh` can black the bridge out. Expected: clients show
  "bridge unreachable" then self-heal without a page refresh; instance IDs are REASSIGNED after every
  reload, so clients must re-key on structure (node counts/paths), never cache ids across a blackout.
- **Unity restart:** unsaved scenes lose spawned objects/markers (learned the hard way). Sidecar markers
  re-materialize via `/marker` posts. Verify port 8787 is owned by the *main* editor after restart -
  never a title-less worker process (v0.5.6 guard).
- **Ctrl+S discipline:** the tool mutates live scenes at high frequency. Save before and after sessions.

## Part D - Findings log

### 2026-07-16 - first execution (v0.8.1 -> v0.8.2)
- **BUG (fixed): phantom markers.** `/marker` with an unknown `parentId` silently created an orphan
  object at scene root. Now returns an error.
- **BUG (fixed): NaN poisoning.** JsonUtility parses `NaN` happily; one poisoned transform breaks physics
  and makes `/scene` emit invalid JSON (killing every client's pull). All apply channels now reject
  non-finite values.
- **GAP (fixed): no `/delete` endpoint.** Added (Undo-recorded), needed by both cleanup and product.
- **BEHAVIOR: `/refresh` blackout is asynchronous.** The compile can start ~30-60s after the response
  returns. Never schedule anything timing-sensitive after a refresh; the drill runs LAST.
- **BEHAVIOR: `reset_demo` destroys only the first rig found by name.** Cleanup deletes rigs by id.
- **PERF: p50 ~ 120ms via PowerShell client** (~70ms of it is client overhead; browser fetch ~ 60-80ms
  total). Occasional 1-2s spikes when the editor is busy (import/GC). Verification code must POLL for
  expected values, never read once after a fixed wait - two contaminated verifications taught us this.
- **NOTE:** `F()` number formatting confirmed InvariantCulture - no EU-locale decimal-comma corruption.
