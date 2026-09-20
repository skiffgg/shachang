# Architecture

## The shape of the project

There is exactly one scene, and nothing in it is placed by hand. `Assets/Editor/Builder.cs` builds
`Assets/Scenes/Main.unity` from code: the camera, lighting rig, skybox, post-processing volume, the
shared materials under `Resources/Mats`, the FishNet `NetworkManager` with its transport, and the
spawnable network prefabs. Running `./build.ps1 -method Builder.Setup` regenerates all of it.

That choice pays for itself three times over: the scene can never drift away from what the scripts
expect, a code review shows the whole level setup as a diff, and the dedicated server can build the
same world without shipping authored content.

Everything else is plain `MonoBehaviour` code under `Assets/Scripts`, grouped by responsibility:

| Folder | Contents |
|---|---|
| `Core/` | `Game` (modes, HUD, minimap, weather, day/night, menus), `Tune` (live values from `tune.json`), `Mats` (null-safe material helper) |
| `World/` | `World` (terrain, roads, rivers, towns, props), `WaterFlow`, `Destructible`, `Loot`, `Capture` |
| `Characters/` | `Player` (first-person controller, health, inventory), `Enemy` (squad AI with cover, flanking, grenades) |
| `Combat/` | `Weapons` (definitions, view models, attachments), `Projectile` |
| `Vehicles/` | `Vehicle` (jeep, armed pickup, motorbike, helicopter, AI tank) |
| `Presentation/` | `Fx` (particles, tracers, generated sprite textures), `Sfx` (pooled audio) |
| `Net/` | See [multiplayer.md](multiplayer.md) |

## World generation

`World.Build()` produces the entire battlefield from the fixed seed `20260920`:

1. **Terrain.** A Unity `Terrain` with a sculpted heightmap: a base elevation of 20 m so valleys can be
   carved downward, peaks up to 120 m, a canyon, a river with side creeks, and lakes. Splat weights
   follow slope and height, so cliffs read as rock and valley floors as grass.
2. **Roads.** Four spokes from the centre plus a ring that links the zones, laid as slope-tilted slabs
   that follow the terrain instead of floating above it.
3. **Zones.** Six named areas — town, villages, an industrial yard, a jungle, the canyon and the
   lakeside — each with their own prop sets, loot density and enemy spawn weights, all navigable and
   listed on the compass and the map.
4. **Props.** Buildings (several of them enterable and multi-storey), walls, containers, barrels,
   vegetation and rocks, each grounded to the lowest corner of its own footprint so nothing hovers.
5. **Navigation.** A `NavMeshSurface` is baked from the physics colliders once the world exists.

Two traps cost real debugging time here and are worth remembering:

- `Terrain.drawInstanced = true` renders a **flat** terrain while the collider uses the sculpted
  heightmap. The map looked empty and the player appeared to stand under it. The fix is
  `drawInstanced = false` plus `TerrainData.SyncHeightmap()` after writing heights.
- Foliage imported as solid geometry cannot be decimated to a few hundred triangles; it turns into
  shards. The plants are re-exported at 7k–14k triangles and placed in smaller numbers instead.

## Rendering and performance

The Built-in pipeline keeps the shader set small enough to strip aggressively. Three quality presets
(`F3`) scale shadows, post-processing, particle counts and view distance. The minimap and the big map
are rendered by a dedicated overhead camera into a render texture: for that single frame fog is
disabled, the terrain base-map distance is raised and the sun is pointed straight down, otherwise the
map comes out as a grey square.

## Tuning

`Assets/StreamingAssets/tune.json` holds values that are worth changing without a rebuild — mouse
sensitivity, field of view, weapon and attachment offsets, vehicle gun positions. `F5` reloads it in a
running build, which is how the weapon attachment positions were dialled in.

## Dedicated-server considerations

A Unity dedicated-server build strips **every** shader. Any `new Material(Shader.Find(...))` therefore
returns a material with a null shader, or throws, while the world is being generated. `Mats.New` and
its chainable, null-safe setters exist for that reason, and `Game.Headless` (set when
`SystemInfo.graphicsDeviceType == Null`) skips particles, audio, weather visuals, the map render and
water animation. The server still builds the full terrain, colliders and navmesh, because it needs
them for hit detection and AI.
