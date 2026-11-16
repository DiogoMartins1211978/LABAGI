# Metrics Analysis

Two notebooks use the same calculations in `metrics_analysis.py`:

- **MetricsAnalysis.ipynb** opens the included `test_data` recordings and checks known totals.
- **RecordedMetricsAnalysis.ipynb** analyzes actual recordings. Set `DATA_DIR` to the folder printed by `MetricsRecorder`, or provide `METRICS_DATA_DIR` before starting Jupyter. Missing recordings produce a configuration error; the notebook does not substitute test data.

Both show rooms visited/completed, combat victories and damage, decision times, attempts to reach the boss and Signal activity. Keep each local recording history in its own folder, including its earlier sessions when counting attempts to reach the boss.

## Run A Notebook

From `Tools/Metrics`, install Python 3.10+ dependencies with `python -m pip install -r requirements.txt`. Open either notebook in JupyterLab or VS Code with Jupyter support and select **Run All**. For Android recordings, copy the session files to a local folder first.

```text
Tools/Metrics/
  MetricsAnalysis.ipynb          # test recordings and expected totals
  RecordedMetricsAnalysis.ipynb  # actual recordings
  metrics_analysis.py           # shared calculations
  export_metrics.py             # command-line CSV export
  tests/                        # calculation and export checks
  test_data/                    # JSONL inputs and expected.json
```

## Tests And Export

Run `python -m unittest discover -s tests -v` from this folder. Tests use the included recordings and temporary output directories; they do not require Unity or notebook dependencies.

For CSV output, run `python export_metrics.py INPUT_DIRECTORY OUTPUT_DIRECTORY`. The exporter uses Python's standard library and preserves the source recordings. Existing CSV exports in the destination are replaced, so choose a dedicated results folder. Notebook export is optional and uses the full input folder; `GAME_VERSION` filters the displayed analysis only.

See [Events And Metrics](../../Assets/Tools/Events/README.md) for the Unity channels, recorder APIs and event names.
