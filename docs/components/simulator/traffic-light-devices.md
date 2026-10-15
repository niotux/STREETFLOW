# Physical traffic-light devices

**Edge Population** (project version 1), **Transformer Routes** (project version 2), and **Neural Routes** (project version 3) accept the same optional `traffic_lights_file` in `experiment.json`:

```json
"traffic_lights_file": "traffic-lights.json"
```

The file defines the configured physical control devices. It is independent of arrival or routing weights. The simulator translates a command for one device into a single SUMO controller-state write covering all its mapped movement indices.

## File format

```json
{
  "format_version": 1,
  "world_id": "four-way",
  "description": "Synthetic mapping: one shared signal device per approach.",
  "devices": [
    {
      "id": "north",
      "label": "North approach",
      "x": 198.4,
      "y": 207.2,
      "lane_ids": ["north_in_0"],
      "signal_id": "center",
      "link_indices": [0, 1, 2]
    }
  ]
}
```

`description` is optional; all other fields shown are required. `devices` may be empty. Each device represents lights commanded together, potentially covering several lanes and movements. Give independently operated turn arrows separate device records, even if they share an incoming lane.

Device IDs must be unique, nonempty identifiers of at most 128 characters. Labels are nonempty. Coordinates are finite map coordinates. `lane_ids` must be unique ordinary lane IDs and match all incoming lanes of the mapped movements. `link_indices` must be unique, nonnegative, present in the named SUMO controller's connections, and within every loaded program's state length. Two device records cannot claim the same controller/link pair.

A device currently binds to one SUMO controller. Its movements change together in one write, serialized with simulation steps. Other devices' movements retain their current states. `off` restores each mapped movement's original priority state; differing `o`/`O` values still represent one physical device being off.

The bindings are simulator-side adapter data. They do not appear in the public tlcapi map or device states. The schema is [traffic-lights.schema.json](../../active/ideas/simulator/project-format-v2-schemas/traffic-lights.schema.json); additional network and reference checks run in the project loader.

## Physical inventory and compatibility

SUMO movements are not a surveyed physical-device inventory. The loader does not invent physical devices from lane count, movement count, or SUMO controller IDs. A project without this file still loads and runs but exposes zero physical devices through tlcapi. Its road geometry remains available. Technical simulator signal APIs remain for compatibility and diagnostics; Logistics uses the physical-device API.

The synthetic four-way has four explicitly authored devices, one per approach, each covering three turning movements. The mapping belongs to the map and is shared by all three model formats. Existing `*-devices` packages remain as compatibility copies; the canonical four-way packages now include the same mapping:

- [tiny-graph](../../../experiments/drafts/tiny-graph/README.md): Edge Population.
- [four-way-transformer-routes](../../../experiments/drafts/four-way-transformer-routes/README.md): Transformer Routes baseline.
- [four-way-north-east](../../../experiments/drafts/four-way-north-east/README.md): the tuned north-to-east experiment.
- [four-way-neural-routes](../../../experiments/drafts/four-way-neural-routes/README.md): Neural Routes.

These are synthetic physical layouts, not measurements. Podu Roș requires an authored or surveyed inventory before physical devices can be published. Its existing 81 movement markers do not become 81 claimed physical devices.

## Project readiness and migration

The packages currently have these authored device inventories:

| Package | Physical devices | UI behavior |
| --- | --- | --- |
| `tiny-graph`, `tiny-graph-devices` | 4 | One marker per approach |
| `four-way-transformer-routes`, `four-way-transformer-routes-devices` | 4 | One marker per approach |
| `four-way-north-east`, `four-way-north-east-devices` | 4 | One marker per approach |
| `four-way-neural-routes` | 4 | One marker per approach |
| `podu-ros`, `podu-ros-commute`, `iasi-central-day` | 0 | Roads load; physical inventory still needs authoring |

To inspect working lights, register any canonical four-way folder or retained `-devices` folder in Balthasar's Loader, then create/load a simulation from that package. A project preview does not create the active session that Melchior discovers. A paused simulation is sufficient for discovery and manual commands; simulation stepping is required to observe vehicles responding.

For a package that needs a new inventory:

1. Edit the registered project folder directly. Use a separate folder and project ID only if you want to keep both variants in the list. Saved runs already preserve their original input snapshots.
2. Author `traffic-lights.json` using the format above and reference it with `traffic_lights_file`. Keep a world's identity stable when it represents the same infrastructure; the infrastructure revision changes when its mapping changes.
3. Click **Refresh from disk** in Loader, or register the folder if it is not yet listed. Validation includes lane coverage and controller/link ownership. Fix any reported mapping errors in the same folder and refresh again.
4. Create/load a new simulation from the new project. Check its map's `traffic_devices` array, then the active TLC session map's `devices` array. Each device's `lane_ids` must resolve to the incoming lane geometry in that map.
5. Select a device in Logistics, command a state, and verify readback and the simulator marker. All mapped movements change together. Restarting an earlier run uses its archived package, so it does not pick up this migration.

### Podu Roș inventory requirements

The imported network provides ten SUMO controllers and 81 movement markers. Those are conversion data, not evidence of how many independently operated physical devices exist. Before creating a mapped package, record:

- A stable device ID and human-readable label for each independently commanded signal group. Decide whether repeated heads operate together, and identify separately operated turn arrows.
- A marker position in the network's SUMO local coordinates. Geographic or survey positions need conversion into that coordinate system; a position alone does not identify the movements controlled.
- Every incoming ordinary lane controlled by the device, using lane IDs from this exact network revision.
- The legal turns served by that group, mapped to the network's `signal_id` and `link_indices`. Incoming and outgoing lanes are needed while authoring to distinguish turn arrows that share an incoming lane. One current device can bind to only one SUMO controller.
- The source of the grouping and position information, and any remaining assumptions. Keep survey evidence or explicit synthetic assumptions alongside the package.

The loader checks that declared incoming lanes exactly match the mapped movements and rejects overlapping controller/link assignments. It does not require every network movement to have a device, so an inventory review must separately account for unmapped movements. Neither valid references nor a successful import establishes a surveyed or conflict-free control plan. Timing plans and automatic phase control are separate from this mapping.

The public TLC map exposes incoming `lane_ids`, enabling one marker to be linked to several controlled lanes. It does not expose outgoing lanes, turn geometry, road names, or the private SUMO bindings. Turn-specific UI highlighting would require an extended public contract.

## World identity and replay

The device file supplies `world_id`. An unmapped legacy project uses its project ID as the world ID. The infrastructure revision covers the network, additional signal programs, device file, and attribution. It excludes traffic-generation weights and run settings. Changing arrival weights creates a new project-input revision without invalidating the same infrastructure map.

All four-way projects share the same world ID and infrastructure revision when they use the shared device mapping. Session IDs change whenever a new simulation is loaded. Cached geometry can be reused across those sessions and traffic formats; physical-device state is read again for the new session.

The device file participates in project revisions and is copied into saved run snapshots. To use a new mapping, edit the registered folder, refresh from disk, and load a new simulation. A new project ID is not required for edits to the same folder. Restart reuses the saved mapping and starts signals off.

Both maps draw one marker per configured physical device. The simulator map remains read-only for light control. Logistics offers one set of controls per device and does not expose per-lane or per-movement commands.

See [tlcapi](../contracts/tlcapi.md) for discovery, readback, commands, and automatic switching.
