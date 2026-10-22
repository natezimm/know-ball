"""Compare the 48 NFL/MLB option values against separately retrieved source files.

Usage: python docs/prototypes/numbers-dont-lie/verify-source-values.py /path/to/snapshots
Expected names: nfl22.txt, nfl23.txt (CSV), mlb22.txt, mlb23.txt (JSON).
Does not download sources or claim to verify NBA/NHL PDFs or publication rights.
"""
import csv
import json
import sys
from collections import defaultdict
from pathlib import Path
import unicodedata


def normalized(value):
    return ''.join(c for c in unicodedata.normalize('NFKD', value) if not unicodedata.combining(c))


def main():
    snapshots = Path(sys.argv[1])
    questions = json.loads(Path(__file__).with_name('questions.json').read_text())['questions']
    checked = 0
    for year in (2022, 2023):
        with (snapshots / f'nfl{str(year)[2:]}.txt').open() as file:
            rows = list(csv.DictReader(file))
        totals = defaultdict(lambda: defaultdict(int))
        names = {}
        seen = set()
        for row in rows:
            if row['season_type'] != 'REG':
                continue
            assert int(row['season']) == year
            key = (row['player_id'], row['week'])
            assert key not in seen, f'Duplicate weekly row: {key}'
            seen.add(key)
            names[normalized(row['player_display_name'])] = row['player_id']
            for metric in ('passing', 'rushing', 'receiving'):
                totals[row['player_id']][metric] += int(float(row[f'{metric}_yards'] or 0))
        mlb = json.loads((snapshots / f'mlb{str(year)[2:]}.txt').read_text())
        assert mlb['stats'][0]['type']['displayName'] == 'season'
        splits = mlb['stats'][0]['splits']
        # Comparisons only use these four named players, never infer league leaders from a page.
        for q in questions:
            if q['season'] != str(year) or q['league'] not in ('nfl', 'mlb'):
                continue
            for option in q['options']:
                if q['league'] == 'nfl':
                    actual = totals[names[normalized(option['label'])]][q['metric']]
                else:
                    matches = [r for r in splits if normalized(r['player']['fullName']) == normalized(option['label'])]
                    assert len(matches) == 1, f'Ambiguous/missing MLB player: {option["label"]}'
                    assert matches[0]['season'] == str(year)
                    actual = matches[0]['stat'][q['metric']]
                assert actual == option['value'], (q['id'], option['label'], actual, option['value'])
                checked += 1
    assert checked == 48
    print('PASS: all 48 NFL/MLB option values match source snapshots; NFL uses REG rows grouped by player ID.')


if __name__ == '__main__':
    main()
