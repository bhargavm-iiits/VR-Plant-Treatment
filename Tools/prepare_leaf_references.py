"""Lab reference images for the Lab-only conditions that have no PlantVillage photo.

The Lab's reference card shows a PlantVillage sample photo where one exists
(Assets/_Project/Textures/DiseaseLeaves/<plant>/<disease>.jpg: the 24 Farm classes, plus
orange/huanglongbing and squash/powdery_mildew taken from the same CC BY-SA 3.0 dataset).
Every other Lab-only condition gets <plant>/<disease>.png: a 256 px front render of the supplied
3D specimen (Tools/blender_leaf_reference.py), rotated so the stem points down. DiseaseGalleryBuilder
labels these "Specimen render, not a field photo". Existing images are never overwritten.
Run: python Tools/prepare_leaf_references.py
"""
import json
import subprocess
import tempfile
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
BLENDER = Path(r'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe')
FARM = {'apple', 'bell_pepper', 'cherry', 'corn', 'grape', 'peach', 'potato', 'strawberry', 'tomato'}
OUT = ROOT / 'Assets/_Project/Textures/DiseaseLeaves'
entries = json.loads((ROOT / 'Tools/leaf_manifest.json').read_text(encoding='utf-8'))['entries']
orientations = json.loads((ROOT / 'Tools/leaf_orientations.json').read_text(encoding='utf-8'))
made = 0
for e in entries:
    if e['plantId'] in FARM:
        continue
    folder = OUT / e['plantId']
    photo, render = folder / (e['diseaseId'] + '.jpg'), folder / (e['diseaseId'] + '.png')
    if photo.exists() or render.exists():
        continue
    folder.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as temp:
        raw = Path(temp) / 'raw.png'
        subprocess.run([str(BLENDER), '-b', '--factory-startup', '-P', str(ROOT / 'Tools/blender_leaf_reference.py'), '--',
                        str(ROOT / e['model']), str(ROOT / e['albedo']), str(raw)], check=True, capture_output=True)
        image = Image.open(raw).convert('RGB')
        # Same correction as the Lab prefab: Unity Z rotation of -90 turns a leaf whose stem points left upright.
        angle = -orientations.get(e['plantId'] + '_' + e['diseaseId'], [0, 0, 0])[2]
        if angle:
            image = image.rotate(angle, resample=Image.Resampling.BICUBIC, fillcolor=image.getpixel((0, 0)))
        image.resize((256, 256), Image.Resampling.LANCZOS).save(render)
    made += 1
    print('REFERENCE ' + str(render.relative_to(ROOT)), flush=True)
print(f'DONE: {made} specimen renders', flush=True)
