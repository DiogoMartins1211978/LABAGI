# Unity Tools

Each tool has its own folder, namespace and short setup guide.

- `Events/`: reusable channels, Inspector listeners and optional metrics recording.
- `Georeferencing/`: region authoring, coordinate tests, inherited content and location sources.

Keep asset creation menus direct: `Create > Events > Signal`, for example. Tools own their data and configuration. Gameplay components connect to them through public methods or channels; tools should not depend on a specific player, room or UI implementation.

New authoring tools follow the same structure: a dedicated folder under `Assets/Tools`, their own asset types and an Editor subfolder for Inspector or window code when needed.
