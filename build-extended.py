"""Build portrait thumbnails after build-catalog.py; does not modify catalog data."""
from pathlib import Path
import json,sys,zipfile,io,hashlib,argparse
root=Path(__file__).resolve().parent
parser=argparse.ArgumentParser()
parser.add_argument('--game',type=Path,default=root.parent)
args=parser.parse_args()
c=json.loads((root/'catalog.json').read_text('utf-8'))
assert hashlib.sha256((args.game/'WorldApart_Data/il2cpp_data/Metadata/global-metadata.dat').read_bytes()).hexdigest()==c['MetadataHash']
import UnityPy
from PIL import Image
pkg=args.game/'WorldApart_Data/StreamingAssets/yoo/DefaultPackage'
m=json.loads((pkg/'DefaultPackage_1.0.json').read_text('utf-8-sig'))
wanted={n['Portrait'] for n in c['Npcs'] if n['Portrait']}
bybundle={}
for a in m['AssetList']:
    if a['AssetPath'].lower() in wanted: bybundle.setdefault(a['BundleID'],set()).add(a['AssetPath'].lower())
index={};count=0
with zipfile.ZipFile(root/'portraits.zip','w',zipfile.ZIP_DEFLATED) as z:
    for bid,paths in bybundle.items():
        b=(pkg/(m['BundleList'][bid]['FileHash']+'.bundle')).read_bytes()
        env=UnityPy.load(b[24:] if b.startswith(b'A1BNDLHP') else b)
        objects={}
        for key,obj in env.container.items():
            if key.lower() in paths and (key.lower() not in objects or obj.type.name=='Sprite'): objects[key.lower()]=obj
        for key,obj in objects.items():
            im=obj.read().image.convert('RGBA');im.thumbnail((240,280),Image.Resampling.LANCZOS)
            buf=io.BytesIO();im.save(buf,format='PNG',optimize=True)
            name=str(count)+'.png';count+=1;z.writestr(name,buf.getvalue());index[key]=name
    z.writestr('index.json',json.dumps(index))
print('NPCs',len(c['Npcs']),'templates',len(c['Prompts']),'portraits',count,'bytes',(root/'portraits.zip').stat().st_size)
