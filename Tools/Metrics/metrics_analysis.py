"""Read recorder JSONL files and derive gameplay measurements."""
import json
import math
from collections import Counter, defaultdict
from datetime import datetime
from pathlib import Path
from statistics import mean, median

# --- RECORD CONTRACT ---

FIELDS = ('schemaVersion', 'eventKind', 'sessionId', 'runId', 'gameVersion',
          'sequence', 'utc', 'elapsedSeconds', 'eventName', 'subjectId',
          'decisionId', 'context', 'outcome', 'hasValue', 'value')
TEXT_FIELDS = set(FIELDS) - {'schemaVersion', 'sequence', 'elapsedSeconds', 'hasValue', 'value'}


def finite_number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def timestamp(row):
    return datetime.fromisoformat(row['utc'].replace('Z', '+00:00')).timestamp()


# --- FILE INPUT ---

def load_records(source):
    source = Path(source)
    files = [source] if source.is_file() else sorted(source.glob('*.jsonl'))
    if not files:
        raise ValueError(f'No JSONL files found in {source}')
    records, warnings, seen = [], [], set()
    # Read sessions independently so a damaged recording can be traced to its file.
    for file in files:
        for number, line in enumerate(file.read_text(encoding='utf-8-sig').splitlines(), 1):
            if not line.strip():
                continue
            try:
                row = json.loads(line)
                if not isinstance(row, dict) or row.get('schemaVersion') != 1:
                    raise ValueError('unsupported schema')
                if not row.get('sessionId') or not row.get('eventName'):
                    raise ValueError('missing session/event name')
                if type(row.get('sequence')) is not int or row['sequence'] < 1:
                    raise ValueError('invalid sequence')
                for field in TEXT_FIELDS:
                    if row.get(field) is None:
                        row[field] = ''
                    if not isinstance(row.get(field, ''), str):
                        raise ValueError(f'invalid text field: {field}')
                # Require timezone-aware timestamps before ordering different sessions.
                parsed = datetime.fromisoformat(row['utc'].replace('Z', '+00:00'))
                if parsed.tzinfo is None:
                    raise ValueError('UTC timestamp needs a timezone')
                if not finite_number(row.get('elapsedSeconds')) or row['elapsedSeconds'] < 0:
                    raise ValueError('invalid elapsed time')
                if type(row.get('hasValue')) is not bool:
                    raise ValueError('invalid hasValue')
                if row['hasValue'] and not finite_number(row.get('value')):
                    raise ValueError('invalid numeric value')
                key = (row['sessionId'], row['sequence'])
                if key in seen:
                    raise ValueError('duplicate session/sequence')
                seen.add(key)
                records.append(row)
            except (ValueError, KeyError, TypeError) as error:
                # A partial final line must not discard the rest of a session
                warnings.append(f'{file.name}:{number}: skipped ({error})')
    # UTC orders sessions; sequence resolves records captured at the same instant.
    records.sort(key=lambda row: (timestamp(row), row['sessionId'], row['sequence']))
    return records, warnings


# --- GROUPING ---

def run_key(row):
    # Run IDs stay stable when a saved attempt resumes in another session.
    return (row.get('gameVersion', ''), row.get('runId', ''))


def identity(key):
    return dict(zip(('gameVersion', 'runId'), key))


def analyze(records, warnings=()):
    warnings = list(warnings)
    runs = {}
    for row in records:
        if row.get('eventKind') == 'signal':
            continue
        if row['eventName'] == 'run.started':
            if not row.get('runId'):
                warnings.append('run.started without runId ignored')
                continue
            key = run_key(row)
            # Reopening a saved run does not create another attempt
            runs.setdefault(key, row)

    # Entries preserve revisits; sets count each completed room once per attempt.
    visits, completed = defaultdict(list), defaultdict(set)
    damage = defaultdict(lambda: {'dealt': [], 'received': []})
    combat_results, boss_times = {}, {}
    starts, ends = {}, {}
    for row in records:
        # Signals are counted separately, even when their configured name resembles a metric.
        if row.get('eventKind') == 'signal':
            continue
        event = row['eventName']
        key = run_key(row)
        subject = row.get('subjectId', '')
        if event in ('room.entered', 'room.completed', 'combat.ended', 'boss.reached', 'damage.dealt', 'damage.received'):
            if key not in runs or timestamp(row) < timestamp(runs[key]):
                warnings.append(f'{event}: missing preceding run.started for {key}')
                continue
            if not subject:
                warnings.append(f'{event}: missing subjectId for {key}')
                continue
            if event in ('damage.dealt', 'damage.received'):
                # The subject identifies the combat; values are applied damage amounts.
                if not row.get('hasValue') or not finite_number(row.get('value')) or row['value'] < 0:
                    warnings.append(f'{event}: expected non-negative damage for {subject}')
                    continue
                damage[key][event.split('.')[1]].append(row['value'])
            elif event == 'room.entered':
                visits[key].append(subject)
            elif event == 'room.completed':
                completed[key].add(subject)
            elif event == 'boss.reached':
                boss_times.setdefault(key, timestamp(row))
            else:
                if not row.get('hasValue') or row.get('value') not in (0, 1):
                    warnings.append(f'combat.ended: expected value 0 or 1 for {subject}')
                    continue
                combat_key = key + (subject,)
                if combat_key in combat_results:
                    warnings.append(f'combat.ended: repeated combat result ignored for {subject}')
                    continue
                combat_results[combat_key] = row
        elif event in ('decision.started', 'decision.ended'):
            decision = (row['sessionId'], row.get('decisionId', ''))
            if not decision[1]:
                warnings.append(f'{event}: missing decisionId')
                continue
            target = starts if event == 'decision.started' else ends
            if decision in target:
                warnings.append(f'{event}: repeated decision ignored for {decision}')
            else:
                target[decision] = row

    # --- ROOM AND COMBAT TABLES ---

    rooms = []
    for key, start in sorted(runs.items(), key=lambda item: timestamp(item[1])):
        visited = set(visits[key])
        if completed[key] - visited:
            warnings.append(f'room.completed without matching room.entered for {key}')
        rooms.append(dict(identity(key), entries=len(visits[key]), visited=len(visited),
                          completed=len(completed[key])))
    combats = [dict(identity(key[:2]), combatId=key[2], victory=bool(row['value']))
               for key, row in combat_results.items()]

    # Preserve attempt order and show missing damage as unavailable, rather than zero.
    progression = []
    ordinals = Counter()
    cumulative_victories = Counter()
    for key, start in sorted(runs.items(), key=lambda item: timestamp(item[1])):
        ordinals[key[:1]] += 1
        # Count each boolean combat outcome once; never accumulate an existing total.
        results = [combat['victory'] for combat in combats if run_key(combat) == key]
        cumulative_victories[key[:1]] += results.count(True)
        progression.append(dict(identity(key), attempt=ordinals[key[:1]],
            cumulativeVictories=cumulative_victories[key[:1]],
            defeats=results.count(False), combats=len(results),
            damageDealt=sum(damage[key]['dealt']) if damage[key]['dealt'] else None,
            damageReceived=sum(damage[key]['received']) if damage[key]['received'] else None))

    # --- DECISION TABLES ---

    decisions = []
    groups = defaultdict(lambda: {'times': [], 'cancelled': 0, 'incomplete': 0})
    # Pair starts and ends by session and decision ID, keeping unfinished choices visible.
    for key, start in starts.items():
        end = ends.get(key)
        context = start.get('context', '')
        group = groups[(start.get('gameVersion', ''), context)]
        outcome, seconds, selected = 'incomplete', None, ''
        if end:
            # A valid completion must belong to the same attempt and follow its start.
            valid = (end.get('outcome') in ('confirmed', 'cancelled') and
                     end.get('hasValue') and finite_number(end.get('value')) and
                     end['value'] >= 0 and end.get('context', '') == context and
                     run_key(end) == run_key(start) and end['sequence'] > start['sequence'])
            if valid:
                outcome, seconds = end['outcome'], end['value']
                selected = end.get('subjectId', '')
            else:
                warnings.append(f'invalid decision completion for {key}; treated as incomplete')
        if outcome == 'confirmed':
            group['times'].append(seconds)
        else:
            group[outcome] += 1
        decisions.append(dict(identity(run_key(start)), sessionId=key[0], decisionId=key[1],
                              context=context, outcome=outcome, seconds=seconds, selectedId=selected))
    for key in ends.keys() - starts.keys():
        warnings.append(f'decision.ended without matching start ignored for {key}')
    decision_summary = []
    for (version, context), group in sorted(groups.items()):
        times = group['times']
        decision_summary.append(dict(gameVersion=version, context=context, confirmed=len(times),
            cancelled=group['cancelled'], incomplete=group['incomplete'],
            meanSeconds=mean(times) if times else None, medianSeconds=median(times) if times else None))

    # --- ATTEMPTS UNTIL FIRST BOSS ENCOUNTER ---

    # Group attempts across sessions within the selected local recordings, separated by game version.
    versions = defaultdict(list)
    for key, start in runs.items():
        versions[key[:1]].append((key, timestamp(start)))
    attempts = []
    for group, items in sorted(versions.items()):
        items.sort(key=lambda item: (item[1], item[0][1]))
        reached = [boss_times[key] for key, _ in items if key in boss_times]
        first_boss = min(reached) if reached else None
        # Include the arrival attempt, but leave selections without an arrival unmeasured.
        count = sum(time <= first_boss for _, time in items) if first_boss is not None else None
        attempts.append(dict(zip(('gameVersion',), group),
            attemptsObserved=len(items), reachedBoss=first_boss is not None, attemptsToBoss=count))

    # Signal identity is explicit; an empty metric payload is not enough to infer it.
    signals = [row for row in records if row.get('eventKind') == 'signal']
    signal_counts = Counter((row.get('gameVersion', ''), row['sessionId'], row['eventName']) for row in signals)
    signal_summary = [dict(gameVersion=key[0], sessionId=key[1], eventName=key[2], count=count)
                      for key, count in sorted(signal_counts.items())]

    return dict(events=records, signals=signals, signal_summary=signal_summary, rooms=rooms, combats=combats, progression=progression, decisions=decisions,
                decision_summary=decision_summary, attempts=attempts, warnings=warnings,
                coverage=dict(Counter(row['eventName'] for row in records)))
