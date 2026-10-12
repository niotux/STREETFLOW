# Project documentation

The simulator is the current design focus. Streetflow and the detector have component boundaries; their full product specifications will follow their product decisions.

| Read | Purpose |
| --- | --- |
| [Component architecture](architecture.md) | Roles, process boundaries, and simulation versus real-world operation |
| [Simulator](simulator/README.md) | SUMO wrapper design and implementation sequence |
| [Streetflow app](streetflow/README.md) | Decision-making product and unresolved navigation/control direction |
| [Detector](detector/README.md) | Camera inference product and observation generation |
| [Traffic observations](contracts/traffic-observations.md) | Shared semantics for simulator and real detector output |
| [Research](research/README.md) | Literature and Iași evidence |
| [Archive](archive/README.md) | Superseded proposals |

Images live in [assets/images](../assets/images/), and the original application lives in [legacy/simulator](../legacy/simulator/README.md).
