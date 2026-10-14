# Documentation folders

The folder path shows the document's role in the project. Work starts under `active/`; settled decisions and how-to material live with the component they document; completed plans move to `archive/` when they no longer guide work.

## Active work

- [`active/ideas/`](active/ideas/) holds proposals and unresolved design questions, grouped by component.
- [`active/plans/`](active/plans/) holds work plans, review checklists, and open actions, grouped by component.

Current active simulator work is under [`active/ideas/simulator/`](active/ideas/simulator/) and [`active/plans/simulator/`](active/plans/simulator/).

## Component documentation

- [`components/`](components/) contains settled product boundaries, contracts, and implementation documentation.
- [`components/simulator/`](components/simulator/) covers the accepted project format, implemented backend, APIs, and operation.
- [`components/streetflow/`](components/streetflow/) and [`components/detector/`](components/detector/) describe product boundaries and decisions still open for those products.
- [`components/contracts/`](components/contracts/) contains shared data contracts.

When an idea is decided, move the resulting decision or specification into the relevant component folder and leave only unresolved follow-up work in `active/`. When a plan is complete, move enduring instructions into component documentation and move the plan itself to `archive/` if it is useful as a record.

## Research and history

- [`research/`](research/) contains literature, local infrastructure research, source data, and preparation scripts.
- [`archive/`](archive/) contains superseded proposals and completed plans kept for context.
- Product inputs live in [`../experiments/`](../experiments/); images live in [`../assets/images/`](../assets/images/); the preserved earlier simulator is in [`../legacy/simulator/`](../legacy/simulator/).
