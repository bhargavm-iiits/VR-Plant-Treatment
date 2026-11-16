from pathlib import Path
from PIL import Image, ImageDraw

folder = Path(__file__).resolve().parent / 'out/previews/leaf_audit'
files = sorted(folder.glob('*.png'))
sheet = Image.new('RGB', (6 * 300, 4 * 330), (18, 22, 26))
draw = ImageDraw.Draw(sheet)
for index, file in enumerate(files):
    x, y = index % 6 * 300, index // 6 * 330
    image = Image.open(file).resize((300,300))
    sheet.paste(image, (x,y))
    label = file.stem.replace('_',' ')
    draw.text((x+5,y+304), label[:43], fill='white')
sheet.save(folder.parent / 'leaf_orientation_sheet.png')
print(folder.parent / 'leaf_orientation_sheet.png')
