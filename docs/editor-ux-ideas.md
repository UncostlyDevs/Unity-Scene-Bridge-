# SceneBridge editor - UX / visual / efficiency idea catalog

*Output of a 4-lens research team (desktop pro editors | web/embedded 3D editors | visual-UI design | our-tool-specific gaps) + synthesis, 2026-07-16. 63 raw ideas distilled. Every idea checked against the hard constraints: in-chat sandboxed widget (~680px), mouse-only (no keyboard), Claude-as-bridge, re-rendered each turn. Tags: [impact/effort], (mouse-ok) or (needs-thought).*

## The 30-second read
The highest-leverage moves cluster in three places: **replacing the text preset buttons with a live clickable orientation gizmo** (surfaced four separate ways - it declutters the toolbar *and* cures mouse-only disorientation in one control), **a hover->select->dim feedback chain** (pre-highlight with name tag, crisp outline, gentle scene-dim) that kills "did I grab the right part?" guesswork, and **one disciplined visual restyle** (icon toolbar + neutral dark chrome + single accent + X/Y/Z=RGB color language + progressive disclosure) that is the actual line between "prototype" and "product." Two constraint-specific wins punch above their weight: **scrubbable numeric fields** (the only precise mouse-only way to dial values since chat owns the keyboard) and **honest state signaling** (sync-freshness chip + persist camera/selection across the turn re-render), which fix pain unique to this widget.

## Quick wins (high impact / low effort)
| Idea | What it is | Inspired by |
|---|---|---|
| Corner orientation gizmo | Clickable X/Y/Z axis triad, top-right, spins with camera; click a ball to snap to that view - absorbs the 3-4/Front/Under buttons | three.js ViewHelper, Blender nav gizmo, Maya ViewCube |
| Hover pre-highlight + name tag | Raycast on mouse-move; faint outline + floating object name previews what a click will grab | Unity/Blender/Sketchfab hover highlight |
| Double-click to frame-selected | Double-click a mesh to dolly-fit *and* recenter the orbit pivot; double-click sky to frame all | Maya/Blender/Unity "F" focus |
| Pinned selection HUD | Fixed corner overlay: name + real bounding-box WxHxD in meters + world position | Blender N-panel Dimensions, Maya HUD |
| Damped, eased camera | OrbitControls damping + one shared easing curve for all programmatic moves | Sketchfab / model-viewer / Spline |
| Sync-freshness chip | Green "Synced | this turn" flips to amber "May be stale" after edits, + hatch overlay + Sync button | Figma "All changes saved", git "behind by N" |
| Scrubbable numeric fields | Click-drag the X/Y/Z label to scrub value; plain click still types - the keyboard-free precision path | Blender/After Effects/Figma scrubby sliders |
| Consistent axis color language | X=red/Y=green/Z=blue across gizmo, orientation cube, value fields; color always paired with a letter | Blender/Unity/Maya RGB axis convention |
| Mode-aware cursors | Crosshair when a marker tool is armed, grab while orbiting, resize over scrub labels, not-allowed over sky | Photoshop/Figma/Blender cursor-as-mode |
| Per-tool contextual hint strip | Slim line stating the exact next click ("Click to set origin" -> "Click or drag to aim") | Blender header hints, Illustrator |

## By theme

### Viewport & navigation
- Corner orientation gizmo (clickable axis triad) replacing preset buttons **[high/low]** (mouse-ok)
- Double-click to frame-selected with orbit-pivot recenter + Fit-all on empty double-click **[high/low]** (mouse-ok)
- Perspective / Orthographic toggle (toolbar switch or center-of-gizmo click) for distortion-free elevations **[medium/medium]** (mouse-ok)
- Inertial, damped camera with one shared easing curve for all moves **[high/low]** (mouse-ok)
- Viewport HUD: zoom/distance pill, one-click Home reset, persistent persp/ortho toggle, faint orbit-pivot dot **[medium/low]** (mouse-ok)

### Selection & hover feedback
- Hover pre-highlight (in Select *and* every marker tool) with floating name tag **[high/low]** (mouse-ok)
- Crisp selection outline (Fresnel/inverted-hull, cheaper than OutlinePass) + gentle scene-dim for focus **[high/medium]** (mouse-ok)
- Visible snap-target indicators (diamond=vertex, tick=edge, dot=face) with named confirmation + snap-mode chips **[high/medium]** (mouse-ok)
- Live transform ghost + running delta chip ("Z +2.57 m | RotY +15deg") previewing the pending Apply **[medium/medium]** (mouse-ok)
- Marquee multi-select with a choosable shared pivot (bbox-center/median/last-picked) for group edits **[medium/high]** (needs-thought - replaces the shift-click the no-keyboard rule forbids)
- Isolate / dim-others toggle on selection to click within crowded geometry, plus depth-aware see-through markers so buried gizmos never vanish **[medium/medium]** (mouse-ok)

### Information design / HUD
- Pinned selection HUD: name + real bounding-box WxHxD (meters) + world position **[high/low]** (mouse-ok)
- Viewport HUD with attach-target readout: cursor world coords, unit note, marker count, live "Attach -> Chassis" chip (red when no valid host) **[high/medium]** (mouse-ok)
- On-geometry CAD dimension lines for Volumes, Paths, and selections (per-edge lengths + Path total) **[medium/medium]** (mouse-ok)
- Selection bounding-box with live dimension labels (BoxHelper + HTML overlays), fading while camera moves **[medium/medium]** (mouse-ok)
- Status bar split: context breadcrumb left ("Plane | click a surface"), live readout right (coords, snap, units badge); red (!) reserved for hard errors only **[medium/low]** (mouse-ok)

### Professional visual restyle
- Icon-first contextual toolbar: monochrome icons w/ tooltips, filled active state, marker tools collapsed to a "Marker v" flyout, Move/Rotate/Scale + Local/World as segmented controls **[high/medium]** (mouse-ok)
- Progressive disclosure into ONE docked contextual panel: nothing selected -> scene hint; mesh -> Transform+Apply; armed tool -> that marker's params only **[high/high]** (mouse-ok)
- One-accent color discipline: ~7 CSS tokens, neutral surfaces, single hot accent for active/selected, red only for errors/destructive, amber only for drift **[high/medium]** (mouse-ok)
- Panel elevation system: 1px hairlines, 6-8px radius, 8px padding rhythm, shared 28px control height, aligned label columns **[high/medium]** (mouse-ok)
- Studio env map (RoomEnvironment/PMREM, no asset download) + ACES filmic tone mapping + subtle vignette **[medium/low]** (mouse-ok)
- Render-quality pass: MSAA/SMAA anti-aliasing + soft grounded contact-shadow + consistent hover/active micro-states **[medium/low]** (mouse-ok)
- Unified inline-SVG icon family at one 1.5px stroke weight (tools, gizmo modes, snap magnet, eye, -> apply) **[medium/medium]** (mouse-ok)
- Pill switches + segmented gizmo-mode controls replacing native checkboxes (Mark Visually, facing, closed loop, Snap) **[medium/medium]** (mouse-ok)
- Consistent, colorblind-safe X/Y/Z=RGB axis language everywhere, color always paired with a letter **[medium/low]** (mouse-ok)
- Subtle motion discipline: ~130-150ms panel cross-fades, one-shot selection pulse, value-change flash on Unity refresh, gated by prefers-reduced-motion **[medium/medium]** (mouse-ok)

### Marker workflow
- Markers as numbered/typed pins that billboard at constant screen size, with hover cards (type, params, AI confidence) and a linked cross-highlighting list **[high/medium]** (mouse-ok)
- Marker legend / outliner overlay: rows of glyph+name+host, hover to highlight, click to select+frame, eye to toggle visibility, empty state **[high/medium]** (mouse-ok)
- Typed marker glyphs + one color system (Point=pin, Axis=arrow, Plane=quad+normal, Volume=wire box, Path=polyline) reused across viewport/legend/panel/Unity **[medium/medium]** (mouse-ok)
- AI-proposed marker preview: dashed semi-transparent "guess | 0.62" glyph with confidence ring; check to accept, one drag to correct, flips to solid on commit **[high/medium]** (mouse-ok)
- Inline rename in the legend (double-click name -> field -> Enter relays rename to Unity + sidecar) **[medium/low]** (mouse-ok)
- Per-tool step-aware hint strip for the multi-click marker flows **[medium/low]** (mouse-ok)

### Trust & state honesty
- Sync-freshness chip with honest staleness signaling + Sync-from-Unity button **[high/low]** (mouse-ok)
- Persist camera pose + selection + tool across the turn re-render via a tiny state token round-tripped through Claude; tween into the restored pose **[high/medium]** (mouse-ok)
- In-widget undo: prominent "(undo) Revert last change" + clickable history chips relaying grouped-undo to Unity **[high/medium]** (mouse-ok)
- Apply/Create success toast + button success state ("Pushed to Cab [x]") replacing the silent-on-success status string **[medium/low]** (mouse-ok)
- Professional loading/streaming/empty/reconnect states: progress ring over dimmed last-frame poster, calm idle hint, tidy bridge-unreachable state **[high/medium]** (mouse-ok)

### Discoverability & mouse-only efficiency
- Mode-aware contextual cursors as the primary state indicator **[medium/low]** (mouse-ok)
- First-touch nav prompt ("drag to orbit | scroll to zoom | right-drag to pan") that wiggles once then self-dismisses **[medium/low]** (mouse-ok)
- Viewport empty state + fading gesture legend (orbit/zoom/pan icons) that reappears on fresh render until first interaction **[medium/low]** (mouse-ok)
- Transform-gizmo polish: enable plane handles (XY/YZ/XZ), add a screen-space/view handle, live rotate-angle arc with degree count, active-axis emphasis **[medium/medium]** (mouse-ok - plane/view handles recover the axis-constrain the keyboard can't)

## Deliberately-not-now
- **Right-click radial / marking menu** - right-click is already right-drag pan; the click-vs-drag threshold is fiddly and high-risk for a first pass. *(needs-thought)*
- **Marquee multi-select + shared choosable pivot** - genuinely useful but [high] effort; park until single-object editing feels polished.
- **Full progressive-disclosure single docked panel** - the right end state, but [high] effort touching every panel at once; stage it after the cheaper contextual-reveal and icon-toolbar wins land.
- **In-widget undo transaction relay** - high value, but correctness depends on a Unity grouped-undo bridge that isn't built yet; ship the history *display* first, wire revert when the bridge supports atomic undo.
- **On-geometry CAD dimension lines everywhere** - strong for precision but label overlap at 680px is real work; start with cheaper pinned-HUD dimensions, add on-geometry lines only for the active Volume/Path.
