#!/usr/bin/env python3
"""Resolve a manual or successful Windows release event without executing event text."""
import json
import os
from pathlib import Path
import re


def main():
    event = json.loads(Path(os.environ['GITHUB_EVENT_PATH']).read_text())
    automatic = os.environ['GITHUB_EVENT_NAME'] == 'workflow_run'
    if automatic:
        run = event['workflow_run']
        if run['conclusion'] != 'success' or run['head_branch'] != 'main':
            raise ValueError('Only successful main-branch release builds can publish automatically')
        match = re.search(r'\[release (v\d+\.\d+\.\d+(?:[.-][A-Za-z0-9.-]+)?)\]', run['head_commit']['message'])
        if not match:
            raise ValueError('Missing release marker')
        version = match[1][1:]
        source_sha = run['head_sha']
        dry_run = False
    else:
        version = os.environ['MANUAL_VERSION']
        source_sha = ''
        dry_run = os.environ.get('MANUAL_DRY_RUN', 'true').lower() == 'true'
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:[.-][A-Za-z0-9.-]+)?', version):
        raise ValueError('Invalid release version')
    notes_file = Path('docs') / f'RELEASE_v{version}.md'
    notes = notes_file.read_text(encoding='utf-8')
    notes = '\n'.join(line.lstrip('# ').replace('**', '').replace('`', '')
                      for line in notes.splitlines() if not line.startswith('Validation:'))
    delimiter = 'SOLASTA_RELEASE_NOTES_END'
    if delimiter in notes:
        raise ValueError('Invalid release notes delimiter')
    with open(os.environ['GITHUB_OUTPUT'], 'a', encoding='utf-8') as output:
        output.write(f'version={version}\ndry_run={str(dry_run).lower()}\nsource_sha={source_sha}\n')
        output.write(f'notes<<{delimiter}\n{notes}\n{delimiter}\n')
    print(f'Release {version}; validation-only={dry_run}; source={source_sha or "manual selection"}')


if __name__ == '__main__':
    main()
