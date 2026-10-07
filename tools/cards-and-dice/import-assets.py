#!/usr/bin/env python3
"""Rebuild embedded assets. Network imports are explicit; normal builds are offline.
Requires Python 3, Pillow 12.3.0 and vtracer 0.6.15 for --tarot.
"""
from pathlib import Path
import argparse, hashlib, json, shutil, tempfile, urllib.request, urllib.parse, time, re, io, zipfile
ROOT=Path(__file__).resolve().parents[2]
DEST=ROOT/'src/CodeBrix.Platform.GameEngine.CardsAndDice.Assets'
RES=DEST/'Resources'
CAT=DEST/'catalog.json'
entries=json.loads(CAT.read_text()) if CAT.exists() else []
def add(key,data,source,license,source_hash=None):
 p=RES/key;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(data)
 entries[:]=[e for e in entries if e['Key']!=key]
 entries.append(dict(Key=key,Name=Path(key).stem.replace('-',' ').replace('_',' ').title(),Category=key.split('/')[0],Source=source,License=license,Sha256=hashlib.sha256(data).hexdigest(),SourceSha256=source_hash or hashlib.sha256(data).hexdigest()))
def save():
 CAT.write_text(json.dumps(sorted(entries,key=lambda e:e['Key']),indent=2)+'\n')
def get(url):
 for attempt in range(5):
  try:
   return urllib.request.urlopen(urllib.request.Request(url,headers={'User-Agent':'CodeBrixCardsAndDice/1.0 (public domain asset import; github.com/ellisnet/CodeBrix.Platform.GameEngine)'}),timeout=60).read()
  except Exception:
   if attempt==4:raise
   time.sleep(2+attempt*2)
def api(**kw):return json.loads(get('https://commons.wikimedia.org/w/api.php?'+urllib.parse.urlencode(dict(format='json',action='query',**kw))))
def local(folder):
 base=Path(folder)
 for foldername,pattern,prefix in [('Icons/Board Game Icons','Vector/Icons/*.svg','symbols/kenney'),('Icons/Board Game Info','Vector/Fantasy/*.svg','symbols/info')]:
  for p in sorted((base/foldername).glob(pattern)):
   add(prefix+'/'+p.name,p.read_bytes(),'Kenney All-in-1 3.6.0/'+str(p.relative_to(base)),'CC0-1.0')
 for filename in ['card-shuffle.ogg','card-slide-1.ogg','card-place-1.ogg','dice-shake-1.ogg','die-throw-1.ogg']:
  p=base/'Audio/Casino Audio/Audio'/filename
  if not p.exists():p=next((base/'Audio/Casino Audio').rglob(filename))
  add('sounds/'+filename,p.read_bytes(),'Kenney All-in-1 3.6.0/'+str(p.relative_to(base)),'CC0-1.0')
 save()
def ishtar():
 url='https://upload.wikimedia.org/wikipedia/commons/f/f9/Ishtar-star-symbol.svg'
 add('symbols/historical/ishtar-star.svg',get(url),'https://commons.wikimedia.org/wiki/File:Ishtar-star-symbol.svg | '+url,'Public-Domain')
 save()
def playing():
 url='https://www.me.uk/cards/makeadeck.cgi?view&ace=Plain&back=Diamond'
 html=get(url).decode()
 svgs=re.findall(r'<svg\b.*?</svg>',html,re.S)
 count=0
 for svg in svgs:
  m=re.search(r'face="([^"]+)"',svg)
  if not m:continue
  name=m.group(1)
  if not re.fullmatch(r'(?:[2-9TAJQK]|10)[SHCD]',name):continue
  name=name.replace('T','10')
  add('playing/traditional/'+name+'.svg',svg.encode(),url,'CC0-1.0');count+=1
 assert count==52,count
 save()
def tarot(limit):
 import vtracer
 from PIL import Image,ImageFilter
 # Scratch scans live in the system temp folder (honours TMPDIR); they are never shipped.
 cache=Path(tempfile.gettempdir())/'cardsdice-tarot';cache.mkdir(exist_ok=True)
 metadata=cache/'metadata.json'
 if metadata.exists():result=json.loads(metadata.read_text())
 else:
  result=api(generator='categorymembers',gcmtitle='Category:Rider-Waite-Smith tarot deck (TaionWC)',gcmlimit=100,prop='imageinfo',iiprop='url|extmetadata|sha1')
  metadata.write_text(json.dumps(result))
 pages=list(result['query']['pages'].values())
 assert len(pages)==78,len(pages)
 for i,p in enumerate(sorted(pages,key=lambda p:p['title'])):
  if limit and i>=limit:break
  title=p['title']; key='tarot/'+title[5:-4].lower().replace(' ','-')+'.svg'
  if (RES/key).exists() and any(e['Key']==key for e in entries):continue
  info=p['imageinfo'][0]
  meta=info['extmetadata'];lic=meta.get('LicenseShortName',{}).get('value','')
  if 'public domain' not in lic.lower():raise ValueError((title,lic))
  # Prefer Wikimedia's cached 960px rendition if the original is rate limited.
  source_url=info['url']
  try:raw=get(source_url)
  except urllib.error.HTTPError as error:
   if error.code!=429:raise
   renditions=api(titles=title,prop='imageinfo',iiprop='url',iiurlwidth=960)
   rendition=next(iter(renditions['query']['pages'].values()))['imageinfo'][0]
   source_url=rendition['thumburl'];raw=get(source_url)
  sha=hashlib.sha256(raw).hexdigest()
  (cache/(title[5:])).write_bytes(raw)
  # Reduce scanner texture while preserving line work. Fixed size/palette parameters.
  im=Image.open(io.BytesIO(raw)).convert('RGB');im.thumbnail((620,1000))
  png=cache/'trace.png';im.save(png)
  out=cache/'trace.svg'
  vtracer.convert_image_to_svg_py(str(png),str(out),colormode='color',hierarchical='stacked',mode='spline',filter_speckle=8,color_precision=5,layer_difference=24,corner_threshold=60,length_threshold=4.0,max_iterations=10,splice_threshold=45,path_precision=2)
  add(key,out.read_bytes(),info['descriptionurl']+' | '+source_url,'Public-Domain',sha)
  records=DEST/'tarot-provenance.json'
  evidence=json.loads(records.read_text()) if records.exists() else {}
  evidence[key]=dict(title=title,url=source_url,originalUrl=info['url'],sha1=info['sha1'],sourceSha256=sha,metadata={k:v for k,v in meta.items() if k in {'LicenseShortName','LicenseUrl','Copyrighted','AttributionRequired','DateTimeOriginal'}},trace='vtracer 0.6.15; max 620x1000; color_precision=5; layer_difference=24; filter_speckle=8; spline')
  records.write_text(json.dumps(evidence,indent=2)+'\n');save()
  print(f'{i+1}/78 {key} {out.stat().st_size}',flush=True)
  time.sleep(12)
p=argparse.ArgumentParser();p.add_argument('--ishtar',action='store_true');p.add_argument('--kenney');p.add_argument('--playing',action='store_true');p.add_argument('--tarot',action='store_true');p.add_argument('--limit',type=int,default=0)
a=p.parse_args()
if a.kenney:local(a.kenney)
if a.playing:playing()
if a.tarot:tarot(a.limit)

if a.ishtar:ishtar()
