# Tiny graph with probabilistic arrivals · physical devices

This is the [tiny-graph](../tiny-graph/README.md) project with an explicitly authored synthetic physical-device mapping. Traffic parameters and SUMO geometry are unchanged.

Import as `tiny-graph-devices`. The simulator and Logistics show four devices: north, east, south, and west. Each controls all three turning movements from its approach. SUMO bindings are private provider data; tlcapi publishes only the device IDs, labels, positions, lanes, and states.

All three physical-device fixtures share world ID `four-way` and the same infrastructure revision. Switching between them changes the session and generation inputs, while Logistics can reuse the map. The existing baseline projects and saved imports are preserved. Projects lacking a mapping continue to run but expose no physical devices.

See [the infrastructure format](../../../docs/components/simulator/traffic-light-devices.md).
