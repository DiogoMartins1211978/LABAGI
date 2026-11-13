# Events And Metrics

Channels connect publishers to listeners. Create an asset, assign that same asset to both sides, and publish after an action succeeds. Classes use `Tools.Events`.

## Create And Connect

- **Create > Events > Signal**: a notification without a payload.
- **Create > Events > Metric**: a subject ID and optional numeric value.
- Ready-made examples are in `Channels/`.

```csharp
using Tools.Events;

[SerializeField] private SignalChannel panelOpened;
[SerializeField] private MetricChannel roomEntered;

// Inside the methods that complete these actions:
panelOpened.Raise();
roomEntered.Raise(new MetricEvent(roomId));
```

Add a `SignalListener` to connect a Signal to Inspector responses. Code listeners subscribe to `channel.Raised` in `OnEnable` and unsubscribe in `OnDisable`. Delivery is synchronous on Unity's main thread, to current listeners only. Avoid throwing exceptions from listeners.

An asset does not detect an action. Its publisher supplies the data, and listeners decide what to do with it. Create another asset to add a new event with the same payload format.

## Record

Add one `MetricsRecorder` to an object that lives for the session. Preserve that object across scenes when needed. In its Metrics or Signals list, assign a channel and the exported event name. For example, bind `RoomEntered` to `room.entered` or `PanelOpened` to `panel.opened`.

```csharp
recorder.StartRun(runId); // New ID per attempt; retain it when resuming.
roomEntered.Raise(new MetricEvent(roomId));
combatEnded.Raise(new MetricEvent(combatId, victory ? 1 : 0));
recorder.EndRun();
```

The recorder generates a session ID on enable. The selected recording folder represents one local history. Gameplay still needs to call these APIs; channel assets alone do not instrument it.

| Event Name | Subject | Value |
|---|---|---|
| `room.entered` | Room ID | None; include revisits |
| `room.completed` | Room ID | None |
| `combat.ended` | Combat ID, unique within attempt | 1 for victory, 0 for defeat |
| `boss.reached` | Boss ID | None |
| `damage.dealt` | Combat ID | Actual applied damage after mitigation |
| `damage.received` | Combat ID | Actual applied damage after mitigation |

Publish each applied damage amount once. Signals have no event-specific payload; the recorder still adds session/run metadata and tags them with `eventKind = signal`.

## Decision Time

```csharp
// Begin after the choices become usable, before the player decides.
string id = recorder.BeginDecision("reward");
recorder.PauseDecision(id);  // Only if this interval should be excluded.
recorder.ResumeDecision(id);
recorder.EndDecision(id, chosenRewardId);
// Or: recorder.EndDecision(id, cancelled: true);
```

Use any context, such as `path`, `card`, `target` or `reward`. Timing requires active recording. Pause/resume is explicit and independent of `timeScale`. End before the resulting animation. Ending a run cancels pending decisions; interrupted sessions may leave incomplete decisions. Only confirmed decisions contribute to mean and median durations.

## Files And Analysis

Recordings are JSONL files in `Application.persistentDataPath/Metrics`, one per session. Use **Print metrics file path** on the recorder in Play Mode. Writes flush periodically and on pause/close; abrupt termination can lose buffered rows. Disk errors stop recording and appear in the Console; `IsRecording` reports availability.

Open `Tools/Metrics/MetricsAnalysis.ipynb` and select **Run All** to inspect the included test data. Change `DATA_DIR` for actual recordings. The tools README explains setup, tests and command-line CSV export.

Analysis counts unique rooms per attempt, boolean combat outcomes, cumulative victories, applied damage, decision durations and Signal occurrences. Attempts to boss require the full local history from its beginning. Results are separated by game version, and missing measurements stay unavailable.
