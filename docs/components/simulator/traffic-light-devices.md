# Physical traffic-light devices

Both **Edge Population** (project version 1) and **Transformer Routes** (project version 2) accept an optional `traffic_lights_file` in `experiment.json`:

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

The synthetic four-way has four explicitly authored devices, one per approach, each covering three turning movements. Prepared copies preserve the original inputs and immutable imports:

- [tiny-graph-devices](../../../experiments/drafts/tiny-graph-devices/README.md): Edge Population.
- [four-way-transformer-routes-devices](../../../experiments/drafts/four-way-transformer-routes-devices/README.md): Transformer Routes baseline.
- [four-way-north-east-devices](../../../experiments/drafts/four-way-north-east-devices/README.md): the tuned north-to-east experiment.

These are synthetic physical layouts, not measurements. Podu Roș requires an authored or surveyed inventory before physical devices can be published. Its existing 81 movement markers do not become 81 claimed physical devices.

## World identity and replay

The device file supplies `world_id`. An unmapped legacy project uses its project ID as the world ID. The infrastructure revision covers the network, additional signal programs, device file, and attribution. It excludes traffic-generation weights and run settings. Changing arrival weights creates a new project-input revision without invalidating the same infrastructure map.

All three four-way device fixtures share the same world ID and infrastructure revision. Session IDs change whenever a new simulation is loaded. Cached geometry can be reused across those sessions and traffic formats; physical-device state is read again for the new session.

The device file is included in import revisions and copied into saved runs. Old imported packages and run archives are not rewritten. To use a new mapping, import a prepared package under a new project ID. Restart reuses the saved mapping and starts signals off.

Both maps draw one marker per configured physical device. The simulator map remains read-only for light control. Logistics offers one set of controls per device and does not expose per-lane or per-movement commands.

See [tlcapi](../contracts/tlcapi.md) for discovery, readback, commands, and automatic switching.
