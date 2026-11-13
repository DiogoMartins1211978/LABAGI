# Metrics Analysis

1. Install Python 3.10+ and run `python -m pip install -r requirements.txt`.
2. Open `MetricsAnalysis.ipynb` in JupyterLab or VS Code with Jupyter support.
3. Select **Run All** to analyze the included dummy sessions.
4. Set `DATA_DIR` in the configuration cell to analyze real recordings.

The notebook explains and charts rooms visited/completed, victories and damage per attempt, decision times by context and attempts until the first boss encounter.

```text
Tools/Metrics/
  MetricsAnalysis.ipynb
  metrics_analysis.py       # shared calculations
  export_metrics.py         # optional command-line export
  tests/
    test_metrics_analysis.py
  test_data/
    demo-session-01.jsonl   # one JSONL per session, matching the recorder
    expected.json          # known results for validating the analysis
```

Run `python -m unittest discover -s tests -v` from this folder to check the calculations, duplicate handling and export. Tests use dummy data and temporary output folders; they do not require Unity or the notebook packages.

Command-line export also uses only Python's standard library: `python export_metrics.py INPUT_DIRECTORY OUTPUT_DIRECTORY`. Existing CSV exports in the destination are replaced; choose a dedicated results folder.

The Unity integration contract is in `Assets/Tools/Events/README.md`.

`tests/` contains executable checks. `test_data/` contains input recordings and expected results; the notebook reads those same recordings.
