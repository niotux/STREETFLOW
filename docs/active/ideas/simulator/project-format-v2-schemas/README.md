# Version 2 structural schemas

These JSON Schema Draft 2020-12 files describe the [revised model-weights format](../project-format-v2.md):

- [experiment.schema.json](experiment.schema.json): manifest.
- [terminals.schema.json](terminals.schema.json): terminal array.
- [journey-model.schema.json](journey-model.schema.json): architecture, axes, and evidence.
- [weights.schema.json](weights.schema.json): three numerical parameter tensors.

They enforce required fields, supported identifiers, exact tensor ranks/axis orders, basic ranges, and unknown-field rejection. The server implements equivalent structural checks alongside package validation in C#; it does not execute these schema files.

Package validation also needs strict duplicate-field and finite-number parsing, axis uniqueness/coverage, tensor shape and value-count consistency, daily continuity, evidence references, network permissions, and feasibility checks. Those requirements are defined in the specification and implemented by the version 2 loader. The example was checked against both structural schemas and these relevant package constraints.
