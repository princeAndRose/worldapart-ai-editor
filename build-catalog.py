"""Build a base-table directory, exposing only fields present in game tables."""
import argparse, json, hashlib
from pathlib import Path
root = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument('--tables', type=Path, required=True)
parser.add_argument('--version', required=True)
parser.add_argument('--game', type=Path, default=root.parent)
args = parser.parse_args()
def read(name):
    return json.loads((args.tables / (name+'.json')).read_text('utf-8-sig'))
base_rows = read('tbnpcbasecfg')
base = {r['id']: r for r in base_rows}
personas = read('tbnpcaipersona')
prompts = read('tbaiprompttemplate')
if not base or len(base) != len(base_rows) or not personas or not prompts or len({p['npcId'] for p in personas}) != len(personas):
    raise ValueError('Empty or duplicate reference data')
persona_by_id = {p['npcId']: p for p in personas}
if set(persona_by_id) - set(base):
    raise ValueError('Persona references an unknown base ID')
catalog = {'MetadataHash': hashlib.sha256((args.game/'WorldApart_Data/il2cpp_data/Metadata/global-metadata.dat').read_bytes()).hexdigest(), 'Version': args.version, 'Npcs': [], 'Prompts': []}
for npc_id, b in base.items():
    # Non-positive IDs are placeholder records, not editable character identities.
    if npc_id <= 0:
        continue
    p = persona_by_id.get(npc_id)
    fields = {k:p[k] for k in ('hiddenPersona','hiddenTrigger','overDrive')} if p else {}
    fields['propPlot'] = b.get('propPlot',{})
    if p:
        fields['usePersonalIntimacyAttitude'] = {'value':str(p['usePersonalIntimacyAttitude']).lower()}
        for field in ('attitudeByIntimacy','daoCompanionAttitudeByIntimacy'):
            for tier, text in p[field]: fields[field+':'+str(tier)] = text
    catalog['Npcs'].append({'Id':npc_id, 'Name':b.get('npcName',{}).get('zh-Hans','未命名配置'), 'Title':b.get('npcTitle',{}).get('zh-Hans',''), 'Style':p['speakStyle'] if p else {}, 'Portrait':b.get('portraitIcon','').lower(), 'Fields':fields, 'BackgroundOnly':p is None, 'SearchNames':' / '.join(dict.fromkeys(b.get('npcName',{}).values()))})
catalog['Prompts'] = [{'Key':r['templateKey'], 'Text':r['templateText']} for r in prompts]
# Keep verified preset compatibility reproducible in a clean source checkout.
# Both metadata identities and the full editor reference content must match.
verified_versions = {
    'f91213826ea04a4ec18eed6659ff8c588a6cc961bd72f477b336f5e9e7a00a45': '0.35.7370692',
    'b500dd2432b1d866f40a921c7e5200fc1c24bed45d967fa0ec6fa746a5caae5f': '0.38.7394395',
}
reference_hash = hashlib.sha256(json.dumps(
    {k: catalog[k] for k in ('Npcs', 'Prompts')}, ensure_ascii=False,
    sort_keys=True, separators=(',', ':')).encode('utf-8')).hexdigest()
if (reference_hash == '0d5f94a628a72f951126b2a9004704d6d53a65e4e68a440022f2c7947a9d4dfe'
        and verified_versions.get(catalog['MetadataHash']) == catalog['Version']):
    catalog['CompatibleVersions'] = {k: v for k, v in verified_versions.items()
                                     if k != catalog['MetadataHash']}
out = root/'catalog.json'
if out.exists():
    previous = json.loads(out.read_text('utf-8'))
    if previous.get('Npcs') == catalog['Npcs'] and previous.get('Prompts') == catalog['Prompts']:
        compatible = catalog.setdefault('CompatibleVersions', {})
        compatible.update({k: v for k, v in previous.get('CompatibleVersions', {}).items()
                           if k != catalog['MetadataHash']})
        if previous['MetadataHash'] != catalog['MetadataHash']:
            compatible[previous['MetadataHash']] = previous['Version']
out.write_text(json.dumps(catalog, ensure_ascii=False, separators=(',',':')), 'utf-8')
print(f'{len(catalog["Npcs"])} NPC configurations; {len(catalog["Prompts"])} templates; game {args.version}')
