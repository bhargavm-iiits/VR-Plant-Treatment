"""Process the 58 supplied leaf models using Blender; originals are read-only inputs.

The first 24 are the Farm crops' PlantVillage conditions; the other 34 are Lab and Treatment only
(see DiseaseClasses.LabOnly). Folder names are the intended labels; (plantId, diseaseId) keys are
stable lowercase IDs shared by Unity and the treatment SQLite database.
"""
import concurrent.futures
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parent.parent
BLENDER = Path(r'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe')
EXPECTED = 58
PLANTS = {'Apple': 'apple', 'Bell Pepper': 'bell_pepper', 'Cherry': 'cherry',
          'Corn(Maize)': 'corn', 'Grape': 'grape', 'Peach': 'peach',
          'Potato': 'potato', 'Strawberry': 'strawberry', 'Tomato': 'tomato',
          # Lab and Treatment only
          'Blueberry': 'blueberry', 'Chilli': 'chilli', 'Cotton': 'cotton', 'Mango': 'mango',
          'Orange': 'orange', 'Rice': 'rice', 'Soybean': 'soybean', 'Squash': 'squash',
          'Sugarcane': 'sugarcane', 'Wheat': 'wheat'}
# Condition folders shared by several crops map to one disease ID (e.g. Rust); the plant ID keeps keys unique.
DISEASES = {'Apple Black Rot': 'black_rot', 'Apple Scab': 'apple_scab',
    'Cedar Apple Rust': 'cedar_apple_rust', 'Bacterial Spot': 'bacterial_spot',
    'Powdery Mildew': 'powdery_mildew', 'Common Rust': 'common_rust',
    'Gray Leaf Spot (Cercospora)': 'gray_leaf_spot', 'Northern Leaf Blight': 'northern_leaf_blight',
    'Black Rot': 'black_rot', 'Esca (Black Measles)': 'esca',
    'Leaf Blight (Isariopsis Leaf Spot)': 'leaf_blight', 'Early Blight': 'early_blight',
    'Late Blight': 'late_blight', 'Leaf Scorch': 'leaf_scorch', 'Leaf Mold': 'leaf_mold',
    'Mosaic Virus': 'mosaic_virus', 'Septoria Leaf Spot': 'septoria_leaf_spot',
    'Target Spot': 'target_spot', 'Two-Spotted Spider Mite': 'spider_mites',
    'Yellow leaf Curl Virus': 'yellow_leaf_curl_virus',
    # Lab and Treatment only
    'Rust': 'rust', 'Anthracnose': 'anthracnose', 'Cercospora Leaf Spot': 'cercospora_leaf_spot',
    'Leaf Curl': 'leaf_curl', 'Bacterial Blight': 'bacterial_blight', 'Fusarium Wilt': 'fusarium_wilt',
    'Leaf Curl Virus': 'leaf_curl_virus', 'Bacterial Canker': 'bacterial_canker', 'Die Back': 'die_back',
    'Gall Midge': 'gall_midge', 'Orange Huanglongbing (Citrus Greening)': 'huanglongbing',
    'Bacterial Leaf Blight': 'bacterial_leaf_blight', 'Brown Spot': 'brown_spot', 'Leaf Blast': 'leaf_blast',
    'Sheath Blight': 'sheath_blight', 'Tungro': 'tungro', 'Cercospora Leaf Blight': 'cercospora_leaf_blight',
    'Downy Mildew': 'downy_mildew', 'Frogeye Leaf Spot': 'frogeye_leaf_spot',
    'Septoria Brown Spot': 'septoria_brown_spot', 'Soybean Rust': 'soybean_rust',
    'Sudden Death Syndrome': 'sudden_death_syndrome', 'Mosaic': 'mosaic', 'Red Rot': 'red_rot',
    'Yellow Leaf': 'yellow_leaf', 'Blast': 'blast', 'Leaf Blight': 'leaf_blight'}
out = ROOT / 'Assets/_Project/Models/Leaves'
logs = ROOT / 'Tools/logs/leaves'
out.mkdir(parents=True, exist_ok=True)
logs.mkdir(parents=True, exist_ok=True)
entries = []
for source in sorted((ROOT / 'Assets/Leaf_Assets').rglob('*.fbx')):
    folders = source.relative_to(ROOT / 'Assets/Leaf_Assets').parts
    plant, disease = PLANTS[folders[0]], DISEASES[folders[1]]
    name = plant + '_' + disease
    albedo = source.with_suffix('.png')
    if not albedo.exists():
        raise RuntimeError('Missing albedo: ' + str(albedo))
    entries.append({'plantId': plant, 'diseaseId': disease,
        'model': str((out / (name + '.fbx')).relative_to(ROOT)).replace('\\', '/'),
        'albedo': str(albedo.relative_to(ROOT)).replace('\\', '/'),
        'source': str(source), 'name': name})
assert len(entries) == EXPECTED, f'Expected {EXPECTED} leaf models, found {len(entries)}'
keys = [(e['plantId'], e['diseaseId']) for e in entries]
assert len(set(keys)) == len(keys), 'Duplicate (plantId, diseaseId) keys'
# The original 24 come first in their existing (folder) order, then the Lab-only 34 in folder order.
FARM = {'apple', 'bell_pepper', 'cherry', 'corn', 'grape', 'peach', 'potato', 'strawberry', 'tomato'}
entries.sort(key=lambda e: e['plantId'] not in FARM)   # stable: keeps folder order within each group

def process(entry):
    target = ROOT / entry['model']
    if target.exists() and target.stat().st_mtime >= Path(entry['source']).stat().st_mtime:
        return 'EXISTS ' + entry['name']
    command = [str(BLENDER), '-b', '--factory-startup', '-P', str(ROOT / 'Tools/blender_leaf.py'),
        '--', '--source', entry['source'], '--output', str(out), '--name', entry['name']]
    if entry['name'] == 'grape_leaf_blight':
        command += ['--triangles', '80000', '--weld', '0.00003']
    with (logs / (entry['name'] + '.log')).open('w', encoding='utf-8') as log:
        result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT)
    if result.returncode or not target.exists():
        raise RuntimeError('Leaf processing failed: ' + entry['name'])
    return 'READY ' + entry['name']

with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
    for task in concurrent.futures.as_completed([pool.submit(process, e) for e in entries]):
        print(task.result(), flush=True)
manifest = {'entries': [{k: v for k, v in e.items() if k not in ('source', 'name')} for e in entries]}
(ROOT / 'Tools/leaf_manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print(f'DONE: {len(entries)} leaf models', flush=True)
