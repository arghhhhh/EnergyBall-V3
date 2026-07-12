---
name: unity-vfx-graph
description: Drive Unity Visual Effect Graph authoring and runtime control in EnergyBall-V3 via the forked unity-cli bridge (vfx_* commands). Use when the user wants to read, author, or mutate a .vfx graph asset (add/remove contexts, operators, blocks, parameters, link slots, set values, templates, sticky notes), control a live VisualEffect component at runtime, read/write VFX project settings, or bake a mesh into an SDF Texture3D. The global unity-cli skills do NOT cover VFX Graph — use this one.
---

# Unity VFX Graph Control (EnergyBall-V3)

This project embeds a **fork** of the unity-cli bridge that adds VFX Graph control:
`Packages/unity-cli-bridge/Editor/Handlers/VfxGraphHandler.cs` (fork branch
`arghhhhh/unity-cli@vfx-graph-control-pr`). It drives VFX Graph *authoring* through
reflection over Unity's internal `UnityEditor.VFX` model API, plus runtime control of
`VisualEffect` via the public `UnityEngine.VFX` API.

## Important: invoke via `raw`, not `tool call`

The installed `unity-cli` binary (`~/.local/bin/unity-cli.exe`, v0.12.0) is the **upstream**
build — its compiled tool catalog does NOT list the `vfx_*` tools, so `unity-cli tool call vfx_*`
and `unity-cli tool schema vfx_*` will fail. The bridge (C#) DOES have them.

Always call VFX commands through `raw`, which forwards an arbitrary command name straight to the
bridge over TCP and bypasses the catalog:

```bash
unity-cli raw <tool_name> --json '{ ...params... }'
```

(This matches how every other unity-cli skill in this setup already calls the bridge.)

## Prerequisites

- Unity Editor **open** on this project with the bridge package compiled (the bridge runs a TCP
  server in-editor). If a command times out, the editor isn't running / bridge not listening.
- `com.unity.visualeffectgraph` is installed (17.3.0) — required; the handler resolves
  `UnityEditor.VFX` / `UnityEngine.VFX` types at runtime and errors clearly if the package is absent.
- Multiple editors: target one with `--host`/`--port`, or select via the `instances` subcommand.

## The six commands

| Command | Purpose | Required params |
|---|---|---|
| `vfx_describe_graph` | Read a `.vfx` asset: contexts (settings, blocks, slots), operators, exposed parameters, slot + flow links. **The read-back oracle — call this first.** | `assetPath` |
| `vfx_list_library` | Discover available descriptor names (`kind`: `block`, `operator`, `context`, `parameter`, `template`). Use to find exact names for `vfx_apply`. | — |
| `vfx_apply` | Authoring mutation on a `.vfx` asset (~50 ops, see below). | `op` (+ `assetPath` for all ops except `create_subgraph_asset`) |
| `vfx_runtime` | Control a live `VisualEffect` component in Play Mode via its public API. | `op`, `gameObject` |
| `vfx_settings` | Read/write VFX environment settings. | `op` |
| `vfx_bake_sdf` | Bake a Mesh asset into an SDF `Texture3D` asset (public `MeshToSDFBaker`, needs compute support). | `meshPath`, `outputPath` |

## Core workflow (authoring)

Author blind and you'll pass wrong indices/names. Always:

1. **`vfx_describe_graph`** → get current contexts/operators/parameters and their **indices** (ops
   address contexts/operators/parameters/blocks by the indices this returns).
2. **`vfx_list_library`** → get the **exact descriptor name** for the context/operator/block/parameter
   you want to add (matching is exact-first, then substring — exact names are safest).
3. **`vfx_apply`** → make the change.
4. **`vfx_describe_graph`** again → confirm the mutation landed (read-back verification).

## `vfx_apply` ops

Grouped; see the description string in `src/tooling/tool_catalog.rs` / the handler for the
authoritative list. `assetPath` is required for every op except `create_subgraph_asset`.

- **Contexts / systems**: `add_context`, `remove_context`, `set_context_setting`, `delete_system`,
  `set_system_name`, `set_bounds`, `set_initial_event_name`
- **Blocks**: `add_block`, `remove_block`, `set_block_setting`, `set_block_enabled`, `reorder_block`,
  `move_block`, `duplicate_block`
- **Operators**: `add_operator`, `remove_operator`, `duplicate_operator`, `set_operator_setting`,
  `add_operator_input`, `remove_operator_input`, `set_operator_operand_type`, `rename_operator_input`,
  `reorder_operator_input`
- **Parameters (exposed properties)**: `add_parameter`, `remove_parameter`, `rename_parameter`,
  `duplicate_parameter`, `set_parameter_category`, `reorder_parameter`, `convert_to_property`,
  `convert_to_inline`
- **Categories**: `rename_category`, `reorder_category`
- **Slots / links**: `link_slots`, `unlink_slots`, `set_slot_value`, `set_slot_space`
- **Flow**: `link_flow`, `unlink_flow`
- **Attributes**: `add_custom_attribute`
- **Instancing**: `set_instancing`
- **Sticky notes**: `add_sticky_note`, `update_sticky_note`, `remove_sticky_note`, `reorder_sticky_note`
- **Templates / subgraphs**: `create_subgraph_asset`, `create_from_template`, `insert_template`,
  `designate_template`

### Common `vfx_apply` params

`op`, `assetPath`, `contextType`, `contextIndex`, `blockName`, `blockIndex`, `settings` (object),
`enabled`, `operatorIndex`, `operatorName`, `parameterIndex`, `parameterName`, `index`, `setting`,
`value`, `type`, `exposed`, `space`, `category`, `newCategory`, `min`, `max`, `from`/`to`/`target`
(objects, for slot/flow links), `subPath` (string[], to address a nested slot), `name`,
`attributeName`, `attributeType`, `eventName`, `operandType`, `template`/`targetPath`, `title`/
`contents`/`position`/`colorTheme`/`textSize` (sticky notes), `capacity`, `center`/`size`/`padding`
(bounds), `subgraphPath`, `kind`, `order`.

## Examples

```bash
# 1. Inspect a graph (do this first — gives you indices + names)
unity-cli raw vfx_describe_graph --json '{"assetPath":"Assets/VFX/EnergyBall.vfx","includeErrors":true}'

# 2. Find exact block descriptor names
unity-cli raw vfx_list_library --json '{"kind":"block","filter":"Force"}'

# 3. Add a block to the Update context (index from describe_graph), with settings
unity-cli raw vfx_apply --json '{"op":"add_block","assetPath":"Assets/VFX/EnergyBall.vfx","contextType":"Update","contextIndex":0,"blockName":"Turbulence"}'

# 4. Set a slot value (address the target slot via subPath)
unity-cli raw vfx_apply --json '{"op":"set_slot_value","assetPath":"Assets/VFX/EnergyBall.vfx","target":{"kind":"block","contextIndex":0,"blockIndex":1},"subPath":["Intensity"],"value":2.5}'

# 5. Expose a new float parameter
unity-cli raw vfx_apply --json '{"op":"add_parameter","assetPath":"Assets/VFX/EnergyBall.vfx","type":"float","name":"Radius","value":1.0,"exposed":true,"category":"Ball"}'

# 6. Runtime: set an exposed float on a live VisualEffect during Play Mode
unity-cli raw vfx_runtime --json '{"op":"set_float","gameObject":"Player/EnergyBallVFX","name":"Radius","value":3.0}'

# 7. Runtime: send an event / read state
unity-cli raw vfx_runtime --json '{"op":"send_event","gameObject":"Player/EnergyBallVFX","eventName":"OnBurst"}'
unity-cli raw vfx_runtime --json '{"op":"get_state","gameObject":"Player/EnergyBallVFX"}'

# 8. Read all VFX project settings, then write one
unity-cli raw vfx_settings --json '{"op":"get","scope":"project"}'
unity-cli raw vfx_settings --json '{"op":"set","scope":"project","setting":"fixedTimeStep","value":0.0166667}'

# 9. Bake a mesh into an SDF Texture3D
unity-cli raw vfx_bake_sdf --json '{"meshPath":"Assets/Models/Ball.fbx","outputPath":"Assets/VFX/BallSDF.asset","maxResolution":64,"overwrite":true}'
```

## `vfx_runtime` ops

`set_asset`, `set_float`, `set_int`, `set_bool`, `set_vector2`/`3`/`4`, `set_texture`, `set_mesh`,
`send_event` (optional `attributes`), `set_initial_event_name`, `reinit`, `simulate`
(`deltaTime` default 0.05, `steps` default 1), `get_state`. Requires Play Mode with the target
`VisualEffect` active. `gameObject` is a scene path/name.

## `vfx_settings`

- `op`: `get` (read all) or `set` (write one `setting` = `value`).
- `scope`: `project` (default → `ProjectSettings/VFXManager.asset`: `fixedTimeStep`, `maxDeltaTime`,
  `maxCapacity`, and Object-ref plumbing like `m_RuntimeResources` set by asset path) or `preferences`
  (per-machine EditorPrefs via `UnityEditor.VFX.VFXViewPreference`: `instancingEnabled`,
  `displayExperimentalOperator`, `multithreadUpdateEnabled`, `allowShaderExternalization`).

## Troubleshooting

- **Command times out / connection refused** → Unity Editor isn't open on this project, or the
  bridge failed to compile. Check the Unity Console for `UnityCliBridge` compile errors.
- **"VFX type not found … Is com.unity.visualeffectgraph installed?"** → the VFX Graph package is
  missing or the editor hasn't recompiled; it's present in this project at 17.3.0.
- **"No block/operator descriptor matching '…'"** → run `vfx_list_library` for the exact name.
- **Wrong context/operator hit** → your index is stale; re-run `vfx_describe_graph` (indices shift
  after add/remove ops).
- **Bridge won't compile after install** → the Editor asmdef references Input System + Newtonsoft.Json
  (both in `Packages/manifest.json`). The Recorder + Addressables handlers were trimmed out for this
  project: `VideoCaptureHandler` and the `capture_video_*` router entries are guarded behind
  `#if UNITY_RECORDER`, and `AddressablesHandler` + `addressables_*` behind `#if UNITY_ADDRESSABLES` —
  both defines are OFF because those packages aren't installed, so `capture_video_*` and
  `addressables_*` commands are unavailable here (by design). Re-add `com.unity.recorder` /
  `com.unity.addressables` to the manifest to re-enable.

## Provenance

Fork: https://github.com/arghhhhh/unity-cli/tree/vfx-graph-control-pr — bridge embedded as a
local package under `Packages/unity-cli-bridge/` (Tests/ omitted). To update, re-copy that package
from the fork branch. The upstream binary is kept as-is; VFX is reached via `raw`.
