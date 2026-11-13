# Regression tests for the offline analysis and CSV export.

# Run from Tools/Metrics: python -m unittest discover -s tests -v
# These tests use dummy sessions and temporary files; they do not start Unity.

import csv
import json
import sys
import tempfile
import unittest
from copy import deepcopy
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from metrics_analysis import analyze, load_records
from export_metrics import export

ROOT = Path(__file__).resolve().parents[1]


class MetricsAnalysisTests(unittest.TestCase):
    # Load a fresh copy of the shared sample sessions before each test.
    def setUp(self):
        self.records, self.warnings = load_records(ROOT / 'test_data')

    # Compare all four gameplay metrics against known sample results.
    def test_reference_measurements(self):
        result = analyze(self.records, self.warnings)
        self.assertEqual(result['warnings'], [])
        self.assertEqual(len(result['rooms']), 5)
        self.assertEqual(sum(r['visited'] for r in result['rooms']), 11)
        self.assertEqual(sum(r['completed'] for r in result['rooms']), 9)
        self.assertEqual(sum(r['entries'] for r in result['rooms']), 12)
        self.assertEqual(len(result['combats']), 7)
        self.assertEqual(sum(r['victory'] for r in result['combats']), 4)
        self.assertEqual(result['attempts'][0]['attemptsToBoss'], 3)
        confirmed = [r['seconds'] for r in result['decisions'] if r['outcome'] == 'confirmed']
        self.assertEqual(len(confirmed), 12)
        self.assertAlmostEqual(sum(confirmed) / len(confirmed), 6.475)
        self.assertEqual(sum(r['cancelled'] for r in result['decision_summary']), 1)
        self.assertEqual(sum(r['incomplete'] for r in result['decision_summary']), 1)

    # Replayed events and resumed attempts must not count as new results.
    def test_resume_and_repeated_completion_do_not_inflate_counts(self):
        extra = [deepcopy(r) for r in self.records if r['eventName'] in
                 ('run.started', 'room.completed', 'combat.ended', 'boss.reached', 'decision.ended')]
        result = analyze(self.records + extra)
        self.assertEqual(len(result['rooms']), 5)
        self.assertEqual(len(result['combats']), 7)
        self.assertEqual(sum(r['completed'] for r in result['rooms']), 9)
        self.assertEqual(result['attempts'][0]['attemptsToBoss'], 3)
        self.assertEqual(sum(r['confirmed'] for r in result['decision_summary']), 12)

    # Missing results stay unavailable rather than becoming zero-duration choices.
    def test_no_completed_combats_or_decisions(self):
        rows = [r for r in self.records if r['eventName'] not in ('combat.ended', 'decision.ended')]
        result = analyze(rows)
        self.assertEqual(result['combats'], [])
        self.assertTrue(all(r['meanSeconds'] is None for r in result['decision_summary']))
        self.assertTrue(all(r['confirmed'] == 0 for r in result['decision_summary']))

    # Reject unsupported combat results and decisions without a recorded start.
    def test_orphan_and_invalid_outcomes_are_reported(self):
        rows = deepcopy(self.records)
        next(r for r in rows if r['eventName'] == 'combat.ended')['value'] = 2
        rows = [r for r in rows if r['eventName'] != 'decision.started']
        result = analyze(rows)
        self.assertEqual(len(result['combats']), 6)
        self.assertEqual(result['decisions'], [])
        self.assertTrue(result['warnings'])

    def test_reader_and_csv_handle_duplicates_broken_tail_and_formula_text(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            rows = deepcopy(self.records)
            rows[0]['subjectId'] = '=1+1'
            (path / 'sample.jsonl').write_text('\n'.join(json.dumps(r) for r in rows + [rows[0]]) +
                                              '\n{broken', encoding='utf-8')
            count, warnings = export(path, path / 'output')
            self.assertEqual(count, len(self.records))
            self.assertEqual(len(warnings), 2)
            with (path / 'output/events.csv').open(encoding='utf-8-sig') as stream:
                first = next(csv.DictReader(stream))
            self.assertEqual(first['subjectId'], "'=1+1")
            self.assertTrue((path / 'output/attempts.csv').exists())

    # An empty selection is valid; a folder without recordings gives a clear error.
    def test_damage_and_victories_per_attempt(self):
        # Compare applied damage totals and preserve missing measurements as unavailable.
        result = analyze(self.records)
        first = result['progression'][0]
        self.assertTrue(all(type(r['victory']) is bool for r in result['combats']))
        totals = [r['cumulativeVictories'] for r in result['progression']]
        self.assertEqual(totals, [1, 1, 3, 3, 4])
        self.assertEqual(first['damageDealt'], 45)
        self.assertEqual(first['damageReceived'], 33)
        without_damage = [r for r in self.records if not r['eventName'].startswith('damage.')]
        self.assertTrue(all(r['damageDealt'] is None for r in analyze(without_damage)['progression']))

    def test_signal_analysis_uses_explicit_kind(self):
        # Empty metric payloads must not be mistaken for Signals.
        result = analyze(self.records)
        expected = json.loads((ROOT / 'test_data/expected.json').read_text())
        self.assertEqual(len(result['signals']), expected['signals'])
        from collections import Counter
        counts = Counter(row['eventName'] for row in result['signals'])
        self.assertEqual(dict(counts), expected['signalCounts'])
        self.assertEqual(len({row['sessionId'] for row in result['signals']}), 3)
        self.assertEqual(sum(row['count'] for row in result['signal_summary']), expected['signals'])
        self.assertTrue(all(not row['hasValue'] and not row['subjectId'] for row in result['signals']))

    def test_decision_contexts_have_varied_distributions(self):
        # Verify every context, not just the overall mean.
        expected = json.loads((ROOT / 'test_data/expected.json').read_text())['decisionContexts']
        for row in analyze(self.records)['decision_summary']:
            reference = expected[row['context']]
            self.assertEqual(row['confirmed'], reference['count'])
            self.assertAlmostEqual(row['meanSeconds'], reference['mean'])
            self.assertAlmostEqual(row['medianSeconds'], reference['median'])
            self.assertNotAlmostEqual(row['meanSeconds'], row['medianSeconds'])

    def test_signals_do_not_modify_gameplay_measurements(self):
        # A configured Signal name must not create a fake gameplay result.
        rows = deepcopy(self.records)
        signal = deepcopy(next(row for row in rows if row['eventName'] == 'combat.ended'))
        signal.update(eventKind='signal', subjectId='fake-combat', value=1)
        result = analyze(rows + [signal])
        self.assertEqual(len(result['combats']), 7)
        self.assertEqual(result['progression'][-1]['cumulativeVictories'], 4)

    def test_empty_input(self):
        result = analyze([])
        self.assertEqual(result['rooms'], [])
        self.assertEqual(result['attempts'], [])
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(ValueError):
                load_records(directory)


if __name__ == '__main__':
    unittest.main()

