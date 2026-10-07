#!/usr/bin/env python3
"""Generate original CodeBrix SVG designs. No fonts or raster images are embedded."""
from pathlib import Path
import json,hashlib,math,html
import xml.etree.ElementTree as ET
ROOT=Path(__file__).resolve().parents[2];DEST=ROOT/'src/CodeBrix.Platform.GameEngine.CardsAndDice.Assets';RES=DEST/'Resources'
entries=json.loads((DEST/'catalog.json').read_text())
def svg(body,w=250,h=400):return f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">{body}</svg>'
def write(key,body):
 p=RES/key;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(body+'\n');sha=hashlib.sha256(p.read_bytes()).hexdigest()
 entries[:]=[e for e in entries if e['Key']!=key]
 entries.append(dict(Key=key,Name=Path(key).stem.replace('-',' ').title(),Category=key.split('/')[0],Source='Original CodeBrix artwork; tools/cards-and-dice/generate-originals.py',License='MIT',Sha256=sha,SourceSha256=sha))
# Simple vector-stroke numerals and letters (no installed font dependency).
segments={'0':'abcdef','1':'bc','2':'abged','3':'abgcd','4':'fgbc','5':'afgcd','6':'afgecd','7':'abc','8':'abcdefg','9':'abfgcd'}
lines={'a':'2 1 12 1','b':'13 2 13 10','c':'13 12 13 20','d':'2 21 12 21','e':'1 12 1 20','f':'1 2 1 10','g':'2 11 12 11'}
letters={'A':'M0 22 L7 0 14 22 M3 14 H11','J':'M1 0 H14 V15 Q14 23 7 22 Q0 22 0 15','Q':'M13 6 Q13 0 7 0 Q0 0 0 6 V16 Q0 22 7 22 Q13 22 13 16 Z M8 16 L16 25','K':'M0 0 V22 M14 0 L0 12 14 22'}
def label(text,x,y,scale=1,color='#152634'):
 b=''
 for i,c in enumerate(text):
  paths=''
  if c in segments:
   for s in segments[c]:
    a=lines[s].split();paths+=f'<path d="M{a[0]} {a[1]} L{a[2]} {a[3]}"/>'
  elif c in letters:paths=f'<path d="{letters[c]}"/>'
  b+=f'<g transform="translate({i*20} 0)">{paths}</g>'
 return f'<g transform="translate({x} {y}) scale({scale})" fill="none" stroke="{color}" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round">{b}</g>'
suits={'H':'M0 8 C-30 -12 -18 -30 0 -14 C18 -30 30 -12 0 8Z','D':'M0 -26 L18 -8 0 10 -18 -8Z','S':'M0 -28 C-9 -14 -27 -7 -18 4 Q-10 13 -3 3 L-7 14 H7 L3 3 Q10 13 18 4 C27 -7 9 -14 0 -28Z','C':'M-4 -5 C-26 -24 26 -24 4 -5 C30 -16 29 21 3 4 L7 16 H-7 L-3 4 C-29 21 -30 -16 -4 -5Z'}
for s,path in suits.items():
 color='#b83749' if s in 'HD' else '#172b3e'
 write('symbols/original/suit-'+s.lower()+'.svg',svg(f'<path transform="translate(32 36)" fill="{color}" d="{path}"/>',64,64))
 for n in range(1,14):
  rank={1:'A',11:'J',12:'Q',13:'K'}.get(n,str(n))
  body='<rect x="2" y="2" width="246" height="396" rx="16" fill="#fbf5e8" stroke="#b5a68b" stroke-width="3"/>'
  corner=label(rank,18,20,1.4,color)+f'<path transform="translate(29 84) scale(.7)" fill="{color}" d="{path}"/>'
  body+=corner+f'<g transform="translate(250 400) rotate(180)">{corner}</g>'
  if n>10:body+=label(rank,94,150,4,color)
  else:
   for i in range(n):
    cols=1 if n<=3 else 2;rows=math.ceil(n/cols);x=125 if cols==1 else 84+(i%2)*82;y=200 if rows==1 else 118+(i//cols)*164/(rows-1)
    body+=f'<path transform="translate({x} {y}) scale(1.25)" fill="{color}" d="{path}"/>'
  write(f'playing/simple/{rank}{s}.svg',svg(body))
for i,color in enumerate(['#dfbb70','#bc5374'],1):
 write(f'playing/simple/joker{i}.svg',svg(f'<rect x="2" y="2" width="246" height="396" rx="16" fill="#182638" stroke="{color}" stroke-width="4"/><path d="M48 215 L35 123 95 161 125 100 155 161 215 123 202 215 Z" fill="{color}"/><circle cx="125" cy="100" r="10" fill="{color}"/><path d="M55 238 H195" stroke="{color}" stroke-width="10"/>'+label(str(i),112,283,2,color)))
for name,bg,fg in [('royal','#163a50','#dfbb70'),('celestial','#25203f','#e2c187'),('crimson','#651c35','#e9ce94')]:
 body=f'<rect x="2" y="2" width="246" height="396" rx="16" fill="{bg}" stroke="{fg}" stroke-width="3"/><rect x="14" y="14" width="222" height="372" rx="10" fill="none" stroke="{fg}" stroke-width="2"/>'
 for y in range(40,380,32):
  for x in range(30,235,32):body+=f'<path d="M{x} {y-8} l8 8 -8 8 -8 -8Z" fill="none" stroke="{fg}" opacity=".35"/>'
 body+=f'<circle cx="125" cy="200" r="82" fill="{bg}" stroke="{fg}" stroke-width="3"/>'
 # Keep the selected public-domain symbol's colors and paths; only scale/position it.
 star_key='symbols/historical/ishtar-star.svg'
 star_bytes=(RES/star_key).read_bytes()
 star=ET.fromstring(star_bytes)
 star.set('viewBox','0 0 800 800');star.set('x','48');star.set('y','123')
 star.set('width','154');star.set('height','154')
 body+=ET.tostring(star,encoding='unicode')
 key='backs/'+name+'.svg'
 write(key,svg(body))
 entry=entries[-1]
 entry['Source']='Original CodeBrix card-back frame; public-domain Ishtar star by Raphael 75; https://commons.wikimedia.org/wiki/File:Ishtar-star-symbol.svg; tools/cards-and-dice/generate-originals.py'
 entry['SourceSha256']=hashlib.sha256(star_bytes).hexdigest()
shapes={4:'50,4 96,88 4,88',6:'15,8 85,8 92,15 92,85 85,92 15,92 8,85 8,15',8:'50,4 94,50 50,96 6,50',10:'50,4 94,35 82,78 50,96 18,78 6,35',12:'30,4 70,4 96,30 96,70 70,96 30,96 4,70 4,30',20:'50,3 91,26 91,74 50,97 9,74 9,26'}
facets={
 4:'M50 4 L50 60 M50 60 L4 88 M50 60 L96 88',
 6:'M18 22 Q18 18 22 18 H78 Q82 18 82 22 V78 Q82 82 78 82 H22 Q18 82 18 78 Z',
 8:'M6 50 H94 M50 4 L34 50 50 96 M50 4 L66 50 50 96',
 10:'M50 4 V96 M6 35 L50 55 94 35 M18 78 L50 55 82 78',
 12:'M50 20 L78 40 67 73 H33 L22 40 Z M50 20 L30 4 M50 20 L70 4 M78 40 L96 30 M67 73 L70 96 M33 73 L30 96 M22 40 L4 30',
 20:'M50 3 L25 65 75 65 Z M25 65 L9 74 M75 65 L91 74 M25 65 L50 97 75 65 M9 26 L25 65 M91 26 L75 65'
}
for n,points in shapes.items():
 body=f'<polygon points="{points}" fill="#245469" stroke="#e1bc72" stroke-width="3"/><path d="{facets[n]}" fill="none" stroke="#71a3af" stroke-width="2"/>'
 write(f'dice/d{n}.svg',svg(body,100,100))
write('dice/numbered.svg',svg('<rect x="5" y="5" width="90" height="90" rx="18" fill="#245469" stroke="#e1bc72" stroke-width="3"/>',100,100))
# Original traditional die faces: rounded ivory cubes with recessed circular pips.
pips = {1:[(50,50)], 2:[(27,27),(73,73)], 3:[(27,27),(50,50),(73,73)],
        4:[(27,27),(73,27),(27,73),(73,73)],
        5:[(27,27),(73,27),(50,50),(27,73),(73,73)],
        6:[(27,27),(73,27),(27,50),(73,50),(27,73),(73,73)]}
for count, positions in pips.items():
 body='<rect x="5" y="5" width="90" height="90" rx="17" fill="#c7bda6" stroke="#8c816c" stroke-width="2"/><rect x="7" y="6" width="86" height="83" rx="15" fill="#fff5dd"/>'
 for x,y in positions:
  body+=f'<circle cx="{x}" cy="{y+1}" r="7.5" fill="#ffffff"/><circle cx="{x}" cy="{y}" r="7" fill="#26343c"/>'
 write(f'dice/pips/{count}.svg',svg(body,100,100))
# Traditional geometric glyphs redrawn from elementary geometry, not copied from font software.
glyphs={'fire':'M32 6 L58 54 H6Z','water':'M6 10 H58 L32 58Z','air':'M32 6 L58 54 H6Z M14 39 H50','earth':'M6 10 H58 L32 58Z M14 25 H50','salt':'M8 32 A24 24 0 1 0 56 32 A24 24 0 1 0 8 32 M8 32 H56','sulfur':'M32 5 L52 34 H12Z M32 34 V60 M20 49 H44','sun':'M12 32 A20 20 0 1 0 52 32 A20 20 0 1 0 12 32 M30 32 H34','moon':'M40 5 A27 27 0 1 0 40 59 A29 29 0 0 1 40 5','mercury':'M20 4 Q32 20 44 4 M18 27 A14 14 0 1 0 46 27 A14 14 0 1 0 18 27 M32 41 V61 M22 51 H42','venus':'M14 22 A18 18 0 1 0 50 22 A18 18 0 1 0 14 22 M32 40 V61 M20 51 H44','mars':'M8 39 A17 17 0 1 0 42 39 A17 17 0 1 0 8 39 M37 27 L57 7 H42 M57 7 V22','spirit':'M32 3 V61 M3 32 H61 M11 11 L53 53 M53 11 L11 53','infinity':'M32 32 C5 -5 -8 67 32 32 C72 -5 77 67 32 32','pentagram':'M32 4 L49 57 5 24 H59 L15 57 Z','shield':'M8 8 H56 V32 Q56 50 32 60 Q8 50 8 32 Z','diamond':'M18 8 H46 L60 24 32 59 4 24Z M4 24 H60 M18 8 L32 59 46 8','wand':'M9 56 L45 20 M46 2 V14 M55 11 H62 M50 17 L59 26','eye':'M3 32 Q32 1 61 32 Q32 63 3 32Z M21 32 A11 11 0 1 0 43 32 A11 11 0 1 0 21 32','key':'M6 20 A13 13 0 1 0 32 20 A13 13 0 1 0 6 20 M29 29 L57 57 M42 42 L49 35 M50 50 L57 43','chalice':'M12 5 H52 V17 Q52 37 32 37 Q12 37 12 17Z M32 37 V56 M18 58 H46','sword':'M32 3 L40 15 V41 H24 V15Z M15 42 H49 M32 42 V61','hourglass':'M12 5 H52 M12 59 H52 M17 5 Q17 24 32 32 Q47 40 47 59 M47 5 Q47 24 32 32 Q17 40 17 59'}
for name,d in glyphs.items():write('symbols/original/'+name+'.svg',svg(f'<path d="{d}" fill="none" stroke="#dfbb70" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>',64,64))
for i in range(1,21):write(f'symbols/values/value-{i}.svg',svg('<circle cx="32" cy="32" r="29" fill="#182638" stroke="#dfbb70" stroke-width="2"/>'+label(str(i),19 if i<10 else 9,17,1.25,'#dfbb70'),64,64))
(DEST/'catalog.json').write_text(json.dumps(sorted(entries,key=lambda e:e['Key']),indent=2)+'\n')
print('Catalog:',len(entries),'assets')
