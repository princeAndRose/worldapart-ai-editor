"""Export the editor's four reference tables from a local game installation.

Reads packaged TextAssets only. Never reads saves, settings or model credentials.
"""
import argparse
import json
from pathlib import Path
import UnityPy

parser = argparse.ArgumentParser()
parser.add_argument('--game', type=Path, required=True)
parser.add_argument('--out', type=Path, required=True)
args = parser.parse_args()
package = args.game / 'WorldApart_Data/StreamingAssets/yoo/DefaultPackage'
manifest = json.loads((package / 'DefaultPackage_1.0.json').read_text('utf-8-sig'))
names = ('tbconstants', 'tbnpcbasecfg', 'tbnpcaipersona', 'tbaiprompttemplate')
expected = {('assets/luban/data/' + name + '.json'): name for name in names}
bundles = {}
for asset in manifest['AssetList']:
    path = asset['AssetPath'].lower()
    if path in expected:
        bundles.setdefault(asset['BundleID'], {})[path] = expected[path]
found = {}
for bundle_id, paths in bundles.items():
    data = (package / (manifest['BundleList'][bundle_id]['FileHash'] + '.bundle')).read_bytes()
    environment = UnityPy.load(data[24:] if data.startswith(b'A1BNDLHP') else data)
    for path, obj in environment.container.items():
        if path.lower() in paths:
            raw = obj.read().m_Script
            text = raw.decode('utf-8-sig') if isinstance(raw, bytes) else raw
            rows = json.loads(text)
            if not isinstance(rows, list) or not rows:
                raise ValueError('Empty or invalid table: ' + path)
            found[paths[path.lower()]] = rows
if set(found) != set(names):
    raise ValueError('Missing tables: ' + ', '.join(set(names) - set(found)))
args.out.mkdir(parents=True, exist_ok=True)
for name, rows in found.items():
    (args.out / (name + '.json')).write_text(json.dumps(rows, ensure_ascii=False), 'utf-8')
    print(name, len(rows))
