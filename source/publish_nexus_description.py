#!/usr/bin/env python3
"""Update the authorized Nexus mod page via the documented v3 author API."""
import argparse
import json
import os
from pathlib import Path
import urllib.request

BASE = 'https://api.nexusmods.com/v3'
MOD_PATH = '/games/solastacrownofthemagister/mods/301'
SUMMARY = ('Co-op quality-of-life improvements: shared loot and travel, teammate panels, '
           'auto-follow, actual combat and exploration movement speed, empty-loot cleanup, '
           'dialogue voting and pre-dialogue autosaves.')


def request(path, method='GET', body=None):
    headers = {'apikey': os.environ['NEXUS_API_KEY'], 'Accept': 'application/json'}
    data = None
    if body is not None:
        headers['Content-Type'] = 'application/json'
        data = json.dumps(body).encode('utf-8')
    req = urllib.request.Request(BASE + path, data=data, headers=headers, method=method)
    with urllib.request.urlopen(req, timeout=45) as response:
        raw = response.read()
        return json.loads(raw) if raw else None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--resolve-only', action='store_true')
    args = parser.parse_args()
    mod = request(MOD_PATH)['data']
    if str(mod['game_scoped_id']) != '301' or mod['name'] != 'Solasta Coop Mod':
        raise ValueError('Unexpected Nexus mod identity')
    mod_id = str(mod['id'])
    if not mod_id.isdecimal():
        raise ValueError('Unexpected Nexus global mod ID')
    with open(os.environ['GITHUB_OUTPUT'], 'a', encoding='utf-8') as output:
        output.write(f'mod_id={mod_id}\n')
    if args.resolve_only:
        print('Verified the existing Solasta Coop Mod page and resolved its API identity.')
        return
    version = os.environ['RELEASE_VERSION']
    if mod['version'] != version:
        raise ValueError(f'Nexus page version is {mod["version"]}, expected {version}; description was not changed')
    description = Path('docs/NEXUS_DESCRIPTION.bbcode').read_text(encoding='utf-8')
    request('/mods/' + mod_id, 'PATCH', {'summary': SUMMARY, 'description': description})
    updated = request('/mods/' + mod_id)['data']
    if updated['description'] != description or updated['version'] != version:
        raise ValueError('Nexus description/version readback did not match the published release')
    print(f'Verified Nexus version {version} and exact description readback.')


if __name__ == '__main__':
    main()
