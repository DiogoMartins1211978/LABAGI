"""Export JSONL measurements: python export_metrics.py INPUT OUTPUT_DIRECTORY."""
import argparse
import csv
from pathlib import Path
from metrics_analysis import FIELDS, analyze, load_records

# --- CSV OUTPUT ---

TABLES = {
    'signals': FIELDS,
    'signal_summary': ('gameVersion', 'sessionId', 'eventName', 'count'),
    'progression': ('gameVersion', 'runId', 'attempt', 'cumulativeVictories', 'defeats', 'combats', 'damageDealt', 'damageReceived'),
    'events': FIELDS,
    'rooms': ('gameVersion', 'runId', 'entries', 'visited', 'completed'),
    'combats': ('gameVersion', 'runId', 'combatId', 'victory'),
    'decisions': ('gameVersion', 'runId', 'sessionId', 'decisionId',
                  'context', 'outcome', 'seconds', 'selectedId'),
    'decision_summary': ('gameVersion', 'context', 'confirmed', 'cancelled', 'incomplete',
                         'meanSeconds', 'medianSeconds'),
    'attempts': ('gameVersion', 'attemptsObserved', 'reachedBoss', 'attemptsToBoss'),
}


def export(source, destination):
    records, warnings = load_records(source)
    result = analyze(records, warnings)
    destination = Path(destination)
    destination.mkdir(parents=True, exist_ok=True)
    for name, fields in TABLES.items():
        with (destination / (name + '.csv')).open('w', encoding='utf-8-sig', newline='') as stream:
            writer = csv.DictWriter(stream, fieldnames=fields, extrasaction='ignore')
            writer.writeheader()
            for row in result[name]:
                # Keep spreadsheet applications from treating text IDs as formulas
                writer.writerow({key: ("'" + value if isinstance(value, str) and
                    value.lstrip().startswith(('=', '+', '-', '@')) else value)
                    for key, value in row.items()})
    (destination / 'warnings.txt').write_text('\n'.join(result['warnings']), encoding='utf-8')
    return len(records), result['warnings']


# --- COMMAND LINE ---

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    count, warnings = export(args.input, args.output)
    print(f'Exported {count} records; {len(warnings)} warnings. Output: {args.output}')
