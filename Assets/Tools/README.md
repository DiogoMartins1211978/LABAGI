# Unity Tools

Each tool has its own folder, namespace and short setup guide.

- [Events And Metrics](Events/README.md): reusable channels, Inspector listeners and optional metrics recording.
- [Georeferencing](Georeferencing/README.md): region authoring, coordinate tests, inherited content and location sources.
- [Content Browser And Availability](Content/README.md): thumbnail browsing, dependency navigation and common or regional assets addressed by stable content IDs.
- [Android Logs](../../ANDROID.md): device logs through Unity's Android Logcat window.

Keep asset creation menus direct: `Create > Events > Signal`, for example. Tools own their data and configuration. Gameplay components connect to them through public methods or channels; tools should not depend on a specific player, room or UI implementation.

New authoring tools follow the same structure: a dedicated folder under `Assets/Tools`, their own asset types and an Editor subfolder for Inspector or window code when needed.
