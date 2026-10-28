# Changelog

## 0.1.1

- **Packaging fix:** the zip now ships the DLL under `plugins/` so Gale and r2modman keep the folder layout (they flatten anything outside `plugins/`). A build guard (`check-gale-layout.py`) fails the release if that regresses. No code or gameplay change.

## 0.1.0
- Initial release.
- Per-world console bookmarks (10 slots) to save and recall positions.
- Requires devcommands (soft dependency on Server Devcommands).
- Minimap markers for saved slots.
- Minimap pin code resolves `ZNet.GetUID` and `Minimap.AddPin` at runtime, so one build works with both the pre-1.0 and 1.0 Valheim APIs.
