# PhoneBox: agent context

This repo works spec-first. Read this before any task, then follow `openspec/` for the change you are working on.

- **Rules that bind every change**: `CONTRIBUTING.md` (the security model, privacy, off-by-default outbound
  connections, how to test) and `docs/DEVELOPMENT.md` (commands, source layout).
- **Spec-first workflow**: OpenSpec, schema `spec-driven-contracts`. `openspec/config.yaml` holds the context and
  the rules. A change lives in `openspec/changes/<name>/` and is built in this order:
  proposal → specs → design → contracts → tasks → apply → archive.
  Generate each artifact with `openspec instructions <artifact> --change <name>` and follow the injected rules:
  they are requirements, not advice.
- **Before resuming a change**: read `openspec/config.yaml`, [`docs/GLOSSARY.md`](docs/GLOSSARY.md), the change's
  proposal, design, contracts and specs, then `tasks.md` from the first unchecked task. Checked boxes are the only
  record of what is done.
- **Red-green**: every implementation task is preceded by its failing test. A test that never ran red is not a
  contract.
- **Domain glossary**: [`docs/GLOSSARY.md`](docs/GLOSSARY.md). Use its terms exactly; one term per concept;
  append-only.
