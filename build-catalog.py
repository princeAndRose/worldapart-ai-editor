"""Build the complete 0.3 reference catalog; retain fingerprints only for equal data."""
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
base = {r['id']: r for r in read('tbnpcbasecfg')}
personas = read('tbnpcaipersona')
prompts = read('tbaiprompttemplate')
if not personas or not prompts or len({p['npcId'] for p in personas}) != len(personas):
    raise ValueError('Empty or duplicate reference data')
catalog = {'MetadataHash': hashlib.sha256((args.game/'WorldApart_Data/il2cpp_data/Metadata/global-metadata.dat').read_bytes()).hexdigest(), 'Version': args.version, 'Npcs': [], 'Prompts': []}
for p in personas:
    b = base[p['npcId']]
    fields = {k:p[k] for k in ('hiddenPersona','hiddenTrigger','overDrive')}
    fields['propPlot'] = b.get('propPlot',{})
    fields['usePersonalIntimacyAttitude'] = {'value':str(p['usePersonalIntimacyAttitude']).lower()}
    for field in ('attitudeByIntimacy','daoCompanionAttitudeByIntimacy'):
        for tier, text in p[field]: fields[field+':'+str(tier)] = text
    catalog['Npcs'].append({'Id':p['npcId'], 'Name':b.get('npcName',{}).get('zh-Hans','未命名配置'), 'Title':b.get('npcTitle',{}).get('zh-Hans',''), 'Style':p['speakStyle'], 'Portrait':b.get('portraitIcon','').lower(), 'Fields':fields})
catalog['Prompts'] = [{'Key':r['templateKey'], 'Text':r['templateText']} for r in prompts]
out = root/'catalog.json'
if out.exists():
    previous = json.loads(out.read_text('utf-8'))
    if previous.get('Npcs') == catalog['Npcs'] and previous.get('Prompts') == catalog['Prompts']:
        catalog['CompatibleVersions'] = dict(previous.get('CompatibleVersions', {}))
        catalog['CompatibleVersions'][previous['MetadataHash']] = previous['Version']
out.write_text(json.dumps(catalog, ensure_ascii=False, separators=(',',':')), 'utf-8')
print(f'{len(catalog["Npcs"])} NPC configurations; {len(catalog["Prompts"])} templates; game {args.version}')
