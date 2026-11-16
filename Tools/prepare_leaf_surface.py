"""Bake material data from the supplied UV atlases, preserving UVs and normal-map detail.

The healthy endpoint is a visual reconstruction from healthy green areas, not measured recovery.

Leaves whose baked maps already exist are skipped, so adding leaves never rewrites existing textures;
pass --force (or --repair-only) to rebake every leaf. A supplied map that is missing (e.g. no roughness
texture) is not invented: no file is written and the Unity material keeps the leaf shader's default.
"""
import json
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter

root = Path(__file__).resolve().parent.parent
entries = json.loads((root / 'Tools/leaf_manifest.json').read_text())['entries']
out = root / 'Assets/_Project/Textures/Leaves'
orientations = json.loads((root / 'Tools/leaf_orientations.json').read_text())
manifest = []
repair_only = '--repair-only' in sys.argv
force = '--force' in sys.argv or repair_only
for entry in entries:
    name = entry['plantId'] + '_' + entry['diseaseId']
    source = root / entry['albedo']
    if not force and (out / (name + '_Healthy.png')).exists() and (out / (name + '_Recovery.png')).exists():
        manifest.append({'name': name, 'euler': orientations.get(name, [0, 0, 0])})
        print('EXISTS ' + name, flush=True)
        continue
    image = Image.open(source).convert('RGB')
    image.thumbnail((2048, 2048), Image.Resampling.LANCZOS)
    rgb = np.asarray(image, dtype=np.float32) / 255
    hsv = np.asarray(image.convert('HSV'), dtype=np.float32) / 255
    green = ((hsv[:,:,0] > .18) & (hsv[:,:,0] < .48) &
             (hsv[:,:,1] > .20) & (hsv[:,:,2] > .30))
    known = green.astype(np.float32)
    median = np.median(rgb[green], axis=0) if green.sum() > 100 else np.array([.32,.52,.14])
    healthy = np.broadcast_to(median, rgb.shape).copy()
    # Normalized convolution fills affected areas from neighbouring healthy foliage.
    for radius in (192, 48, 12):
        weights = np.asarray(Image.fromarray((known * 255).astype('uint8')).filter(ImageFilter.GaussianBlur(radius)), dtype=np.float32)/255
        weighted = np.asarray(Image.fromarray((rgb * known[:,:,None] * 255).astype('uint8')).filter(ImageFilter.GaussianBlur(radius)), dtype=np.float32)/255
        candidate = weighted / np.maximum(weights[:,:,None], .001)
        reliable = weights > .025
        healthy[reliable] = candidate[reliable]
    luminance = rgb @ np.array([.299,.587,.114])
    low = np.asarray(Image.fromarray((luminance*255).astype('uint8')).filter(ImageFilter.GaussianBlur(3)),dtype=np.float32)/255
    detail = np.clip((luminance+.02)/(low+.02), .88, 1.12)
    healthy *= detail[:,:,None]
    healthy[green] = rgb[green]
    # The final atlas must contain living-green foliage, including formerly dark patches.
    # Keep veins and colour variation, but exclude residual brown/black lesions and yellow areas.
    endpoint = np.asarray(Image.fromarray((np.clip(healthy,0,1)*255).astype('uint8')).convert('HSV')).copy()
    endpoint[:,:,0] = np.clip(endpoint[:,:,0], 57, 102)
    endpoint[:,:,1] = np.clip(endpoint[:,:,1], 90, 195)
    endpoint[:,:,2] = np.clip(endpoint[:,:,2], 108, 235)
    healthy = np.asarray(Image.fromarray(endpoint,mode='HSV').convert('RGB'),dtype=np.float32)/255
    Image.fromarray((np.clip(healthy,0,1)*255).astype('uint8')).save(out / (name+'_Healthy.png'))
    damaged = np.clip(np.linalg.norm(rgb-healthy,axis=2)*2.4,0,1)
    rng = np.random.default_rng(sum((i+1)*ord(c) for i,c in enumerate(name)))
    grain = Image.fromarray((rng.random((32,32))*255).astype('uint8')).resize(image.size,Image.Resampling.BICUBIC).filter(ImageFilter.GaussianBlur(6))
    delays = .12 + .55*np.asarray(grain,dtype=np.float32)/255 + .25*damaged
    packed = np.stack((damaged,delays,np.clip(low,0,1)),axis=2)
    Image.fromarray((np.clip(packed,0,1)*255).astype('uint8')).save(out / (name+'_Recovery.png'))
    for suffix in (() if repair_only else ('normal','roughness')):
        original = source.with_name(source.stem+'_'+suffix+'.png')
        if not original.exists():
            print('MISSING '+suffix+' map for '+name+': the material keeps its default', flush=True)
            continue
        surface = Image.open(original)
        surface.thumbnail((2048,2048),Image.Resampling.LANCZOS)
        surface.save(out / (name+'_'+suffix+'.png'))
    manifest.append({'name': name, 'euler': orientations.get(name,[0,0,0])})
    print('SURFACE '+name,flush=True)
(root/'Tools/leaf_surface_manifest.json').write_text(json.dumps({'entries':manifest},indent=2))
