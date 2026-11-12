# engineering/

The HOW of SCFD: one .NET solution (`SCFD.BackendServices.slnx`) for the NuGet family, the source readers and the
hosts, one pnpm workspace for `fe-harvest` and the console, one image. Rules:
wow-two-ws/conventions/development/repo/structure/domain-structure.md

- [Architecture](architecture/architecture.md): forms, package graph, the source host and its readers.
- [Contract](architecture/contract.md): the broker contract, engine seams, Ocharo setup and marketplace sources.
- [Packages](architecture/package-registry.md): the NuGet family, npm package and the image-only source readers.
- [Composition](architecture/composition.md): standalone UI, capability discovery, the artifact boundary.
- [Decisions](architecture/decisions/decisions.md) · [Flows](architecture/flows/flows.md)
- [Codebase](codebase/codebase.md): what lives where, test tiers.
- [Development](development/development.md): guidelines, rules, repository setup.
- [Deployment](deployment/deployment.md): local Compose, service access, Ocharo provisioning, integrations, Wheelhouse.
- [Research](research/research.md): source and provider evidence.
- [Scripts](scripts/scripts.md): release helpers.
- [Backlog](planning/backlog.md) · active version: newest `planning/version-track/v{X.Y}/`.
