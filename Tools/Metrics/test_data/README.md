# Test Data

This folder matches the recorder output: one JSONL file per session, with the same schema and event names. All included data is fictional.

- 3 sessions, 5 attempts.
- Samples include a room revisit, a cancelled decision and an unfinished decision in an interrupted session.

Expected results are in `expected.json`: 11 rooms visited across attempts, 9 completed, 12 entries, 4 victories out of 7 combats and 12 confirmed decisions with an overall mean of 6.475 seconds and median of 5.20 seconds. Rooms count again in different attempts.

`panel.opened` demonstrates a recorded Signal with no event-specific payload. Its session metadata comes from the recorder.

## Signal Coverage

The three sessions include 41 Signals across five event names: `panel.opened`, `panel.closed`, `exploration.started`, `help.requested` and `feedback.played`. Repeated notifications have distinct timestamps; help requests vary by session. Signals carry no event-specific payload, while session/run metadata follows the recorder contract. Expected totals are listed in `expected.json`.

All sessions form one local history: five attempts and the first boss arrival on attempt three.
