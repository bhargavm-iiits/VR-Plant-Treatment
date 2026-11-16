from pathlib import Path
from PIL import Image
folder = Path(__file__).resolve().parent / 'out/previews/recovery_motion'
frames = [Image.open(p).convert('RGB') for p in sorted(folder.glob('frame_*.png'))]
frames[0].save(folder / 'recovery.gif',save_all=True,append_images=frames[1:]+frames[-2:0:-1],duration=100,loop=0)
print(folder / 'recovery.gif')
