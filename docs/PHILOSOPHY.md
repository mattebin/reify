# Philosophy: structured state over screenshots

## The thesis in one paragraph

LLMs are language-native. They reason about JSON, hierarchies, numbers, code
identifiers, and names dramatically better than they reason about pixels. Every
Unity MCP plugin on the market today treats an LLM like a junior human Unity
developer — "take a screenshot, eyeball the game view, click around in the
Inspector" — and then wonders why the LLM is slow, expensive, and often wrong.
This project starts from the opposite premise: **the LLM is reading code and
data, not looking at pictures**. Every tool should expose rich structured state
so the model can `grep`, diff, and reason, not squint.

## Where this comes from

The concrete experience that keeps surfacing the same friction:

1. **Scale guessing.** Ask an LLM to place a prefab in a scene. It doesn't know
   the prefab's native mesh bounds, so it guesses scale. The cube turns out to
   be a 20-meter-wide monolith, or a 2-centimeter speck. The LLM then has to
   screenshot, squint, iterate. The fix is trivial: expose each prefab's
   `Renderer.bounds` / `MeshFilter.sharedMesh.bounds` in meters before
   placement. The model never needed the screenshot — it needed numbers.

2. **Theme / material debugging.** "Why is the skybox not rendering in URP?"
   The LLM can guess, or it can screenshot a black viewport and confirm it's
   black. Neither helps. The actual signal lives in the URP pipeline asset: is
   `PostProcessing` enabled, what's the render scale, which renderer feature
   list, is the skybox shader using the correct render queue. All readable.

3. **"Which material on which renderer."** `MaterialPropertyBlock` vs
   `sharedMaterial` vs per-instance `material` vs asset-backed material is a
   constant source of "I set the color but it didn't change" bugs. Screenshots
   show the wrong color; structured state shows exactly which override is
   active and where the read path lands.

4. **Animator / state machine inspection.** What state is the animator in
   right now, what parameters does it have, what transitions fire from here?
   The screenshot shows the animation frame. The structured state shows the
   whole graph.

5. **Render queue / sorting / depth conflicts.** Two transparent objects in
   the wrong order? A screenshot shows the wrong pixels. Structured state
   shows the two render queues and the two sorting layers and the fact that
   they're equal. Diagnosable in one tool call.

In every case, screenshots were a costly workaround for missing structured
state. The plugin should provide the state directly.

## What "structured state first" means in practice

### Tool design rules

1. **Every tool that reads state returns JSON with code identifiers.**
   Component names, property names, shader keyword names, asset paths, GUIDs,
   layer indices, render queue numbers. Not "a screenshot of the inspector."

2. **Read tools are cheap and composable.** An LLM should be able to call
   five read tools in parallel without worrying about rendering cost. Any tool
   that blocks on a frame render (screenshot, game view capture) is a last
   resort, not a first-class path.

3. **Write tools verify.** After a `set_material_color`, the tool returns the
   actual resulting color from the same code path the engine uses. No "did
   this work?" screenshot round-trip.

4. **Diagnostics, not just CRUD.** Any property the engine uses at render
   time — render queue, sorting layer, depth test mode, culling mask, light
   culling, post-process override state — must be readable. A "CRUD plugin"
   that lets the LLM set these but not read them forces the LLM to guess.

5. **Batch reads over single reads.** "Give me every Renderer in the scene
   with render queue > 3000 and their material and their shader keyword set"
   is one tool call, not N. The LLM can then filter on its side.

### Screenshot tools: opt-in, not default

Screenshots remain available — sometimes you actually want to show a human a
picture, or sanity-check a final composition. But they are not the first
reach. The default tool surface should be strong enough that a competent LLM
can solve 90% of scene/asset debugging without taking one.

### Naming and shapes

- Tool names are nouns and noun phrases, not verbs. `mesh_bounds`, not
  `get_mesh_bounds`. `material_properties`, not `inspect_material_props`.
- Return shapes are flat-ish JSON with clear keys. Nested objects only where
  the domain is nested (a mesh has bounds which has a center and a size).
- Units in the key name when ambiguous: `size_meters`, not `size`. Angles in
  `_degrees` or `_radians`. Colors with `_rgba` or `_srgb_rgba`.
- Every state read returns a `stale_at` / `read_at` timestamp or frame number
  so the LLM can reason about whether the state has changed since it last
  looked. (Unity state mutates constantly in play mode.)

## Philosophy features to build (non-exhaustive)

These are the tools neither upstream exposes cleanly today. Building them is
the reason this fork exists.

- `mesh_native_bounds(prefab | gameobject) → {center, size, min, max}` in meters,
  from the mesh asset, before any Transform scale is applied. This solves the
  scale-guessing problem in one call.
- `material_inspect(renderer)` → full property table: every `_Color`,
  `_Float`, `_Texture`, keyword state, render queue, shader variant. Flags
  whether each value comes from asset, instance material, or
  `MaterialPropertyBlock`.
- `urp_pipeline_state()` → URP asset summary: renderer features,
  post-processing volumes active, render scale, HDR, depth priming, shadow
  settings.
- `render_queue_audit()` → every renderer in scene, bucketed by render queue /
  sorting layer / sorting order, flagged where two are tied or overlap.
- `animator_state(gameobject)` → current state per layer, parameter table,
  active transitions, clip playback time.
- `theme_audit()` (project-specific, needs a convention) → which prefabs /
  materials are theme-conditional, what theme is active, which assets are
  currently loaded vs swapped out.
- `shader_variant_state(material | renderer)` → which shader variant is
  actually compiled and used, what keywords are on/off.
- `scene_structural_diff(before_snapshot, after_snapshot)` → what changed in
  the scene hierarchy, cheap enough to run after every edit.

## What we're not building

- A Unity clone inside the plugin.
- Visual tooling for humans. The UI surface is "open Unity to look at it";
  the LLM surface is "read the tools."
- A photoshop-style screenshot-and-annotate pipeline. Screenshots exist as an
  escape hatch, not a workflow.
- Anything that requires a persistent daemon beyond the MCP server itself.

## Decision heuristic

When in doubt, ask: **"Could the LLM answer this question by reading a
well-shaped JSON return, instead of looking at a picture?"** If yes, build
that JSON return. If the only way to answer the question is visually, think
harder — there's almost always a structured-state path that's cheaper and
more reliable.
