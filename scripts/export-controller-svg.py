#!/usr/bin/env python3
"""Export original Figma layers with Inkscape, preserving SVG filters and glyphs.
Usage: python3 scripts/export-controller-svg.py /path/to/Frame.svg
"""
import copy, gzip, json, pathlib, subprocess, sys, tempfile, xml.etree.ElementTree as ET
source = pathlib.Path(sys.argv[1])
root = ET.fromstring(gzip.decompress(source.read_bytes())) if source.suffix == '.gz' else ET.parse(source).getroot()
ns = 'http://www.w3.org/2000/svg'
ET.register_namespace('', ns); ET.register_namespace('xlink', 'http://www.w3.org/1999/xlink')
original = list(root[0]); defs = root.find('{'+ns+'}defs')
out = pathlib.Path('android/app/src/main/assets/controller/figma-dark'); out.mkdir(parents=True, exist_ok=True)
parts = {
 'background': ([0,17,31,32], [0,0,2400,1080]),
 'stick-base': ([1,2], [1226,506,548,548]),
 'stick-cap': ([3,4,5], [1292,572,416,416]),
 'stick-base-left': ([6,7], [51,266,548,548]),
 'stick-cap-left': ([8,9,10], [117,332,416,416]),
 'dpad': ([11,12,51,52,53,54,55], [626,506,548,548]),
 'LT': ([13,14], [14,14,422,222]), 'RT': ([15,16], [1964,14,422,222]),
 'LB': ([21,22], [414,14,422,222]), 'RB': ([19,20], [1564,14,422,222]),
 'BACK': ([23,24], [675,226,235.301,123]),
 'L3': ([25,26], [747,351,235.301,123]),
 'R3': ([27,28], [1417,351,235.301,123]),
 'GUIDE': ([29,30], [941,351,517.301,123]),
 'START': ([41,42], [1490,226,235.301,123]),
 'B': ([33,34], [2111,415,249.82,249.82]),
 'A': ([35,36], [1950,576,249.82,249.82]),
 'X': ([37,38], [1791,415,249.82,249.82]),
 'Y': ([39,40], [1950,254,249.82,249.82]),
}
strokes = [original[18].attrib['d'], original[31][1].attrib['d'], original[32][1].attrib['d']]
with tempfile.TemporaryDirectory() as temp:
 for name, (indices, bounds) in parts.items():
  svg = ET.Element(root.tag, root.attrib)
  group = ET.SubElement(svg, '{'+ns+'}g', {'clip-path':'url(#clip0_1_2)'})
  for i in indices:
   node = copy.deepcopy(original[i])
   if name == 'background' and i in (31,32): node.remove(node[1])
   group.append(node)
  svg.append(copy.deepcopy(defs))
  source = pathlib.Path(temp)/(name+'.svg'); ET.ElementTree(svg).write(source, encoding='utf-8', xml_declaration=True)
  x,y,w,h = bounds
  subprocess.run(['inkscape', str(source), '--export-type=png', '--export-area='+':'.join(map(str,[x,y,x+w,y+h])), '--export-filename='+str(out/(name+'.png'))], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
# Inkscape can exit successfully even when an export file is empty. Fail closed.
for name in parts:
 data = (out/(name+'.png')).read_bytes()
 if not data.startswith(b'\x89PNG\r\n\x1a\n') or not data.endswith(b'\x00\x00\x00\x00IEND\xaeB`\x82'):
  raise RuntimeError('Incomplete PNG export: '+name)
(out/'manifest.json').write_text(json.dumps({'viewport':[2400,1080], 'parts':{name:bounds for name,(_,bounds) in parts.items()}, 'strokes':strokes}, indent=2)+'\n')
print('Exported',len(parts),'original layer assets')
