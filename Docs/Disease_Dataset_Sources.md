# Disease Dataset Sources: 68 Classes Across 20 Crops

Gap analysis between the Unity project and the deployed model, and the public datasets that supply
each missing class.

- **Deployed model** (`Backend/services/api/model/labels.json`): 68 leaf classes (48 diseases + 20 healthy,
  one per crop), plus `Not_a_leaf`.
- **Unity** (`Assets/_Project/Scripts/Runtime/Plants/DiseaseClasses.cs`): 9 crops and 24 disease classes.
  Healthy is left out on purpose; each existing plant model stands for its own healthy class.
- **PlantVillage**: 38 classes across 14 crops (26 diseases + 12 healthy), 54,303 images. It has **no** Rice,
  Wheat, Cotton, Sugarcane, Chilli or Mango.

## Gap summary

| Compared against | Crops missing | Disease classes missing | Healthy classes missing | Total to add |
|---|---|---|---|---|
| Full 68-class model (20 crops) | 11 | 24 | 11 | **35** |
| PlantVillage only (14 crops, 38 classes) | 5 | 2 | 3 | 5 |

How the 68 adds up: 38 PlantVillage classes, plus Orange healthy and Squash healthy (2), plus Rice 6,
Wheat 4, Cotton 4, Sugarcane 5, Chilli 4 and Mango 5 (28).

**Already in Unity (9 crops, 24 diseases):** Apple (3), Bell Pepper (1), Cherry (1), Corn (3), Grape (3),
Peach (1), Potato (2), Strawberry (1), Tomato (9).

## A. Crops covered by PlantVillage (7 classes)

| Crop | Class to add | Dataset |
|---|---|---|
| Orange | Huanglongbing (Citrus Greening) | **PlantVillage**, folder `Orange___Haunglongbing_(Citrus_greening)` |
| Orange | healthy | [Citrus Fruits & Leaves (Rauf et al., 2019)](https://data.mendeley.com/datasets/3f83gxmv57/2): 58 healthy leaves, CC BY 4.0 |
| Squash | Powdery Mildew | **PlantVillage**, folder `Squash___Powdery_mildew` |
| Squash | healthy | Pumpkin Leaf Diseases (400 healthy images; one paper says Kaggle, another says Mendeley, so the original host and link are unconfirmed). Another option is the healthy pumpkin images in [Mendeley 2svdj3yyrk](https://data.mendeley.com/datasets/2svdj3yyrk/2). Pumpkin is the same genus as squash (*Cucurbita*). |
| Blueberry | healthy | **PlantVillage**, folder `Blueberry___healthy` |
| Raspberry | healthy | **PlantVillage**, folder `Raspberry___healthy` |
| Soybean | healthy | **PlantVillage**, folder `Soybean___healthy` |

## B. Crops not in PlantVillage (28 classes)

| Crop | Classes | Dataset(s) |
|---|---|---|
| **Rice** (6) | Bacterial Leaf Blight, Leaf Blast, Brown Spot, Tungro | [Rice Leaf Disease Image Samples (Sethy et al., 2020)](https://data.mendeley.com/datasets/fwcj7stb8r): 5,932 images |
| | Sheath Blight, healthy (and more Blast, Brown Spot, BLB) | [Rice Leaf Bacterial & Fungal Disease Dataset](https://data.mendeley.com/datasets/hx6f852hw4): 8 classes, 1,701 originals |
| **Wheat** (4) | Leaf Blight, Blast, healthy | [Disease Dataset of Wheat (Bangladesh)](https://data.mendeley.com/datasets/5gc7hwydwg): 1,603 originals |
| | Rust | [Wheat Leaf Dataset](https://data.mendeley.com/datasets/wgd66f8n6h) (stripe rust), or [IARI leaf rust set](https://data.mendeley.com/datasets/th422bg4yd) (leaf rust, India) |
| **Cotton** (4) | Bacterial Blight, Leaf Curl Virus, Fusarium Wilt, healthy | [Cotton Leaf Disease Dataset with Severity Levels](https://data.mendeley.com/datasets/74jsdxtmx2): all 4 classes match exactly |
| **Sugarcane** (5) | Rust, Yellow Leaf, Red Rot, Mosaic, healthy | [Sugarcane Leaf Disease Dataset](https://data.mendeley.com/datasets/9424skmnrk): about 2,569 images (about 500 per class), Maharashtra, India; all 5 match exactly |
| **Chilli** (4) | Leaf Curl, Cercospora Leaf Spot, Anthracnose, healthy | [Chilli leaf dataset wzc6r6w5w5](https://data.mendeley.com/datasets/wzc6r6w5w5): about 1,544 images; all 4 match exactly |
| **Mango** (5) | Anthracnose, Gall Midge, Bacterial Canker, Die Back, healthy | [MangoLeafBD](https://data.mendeley.com/datasets/hxsnvwty3r/1): 4,000 images, 500 per class |

## Totals

- **PlantVillage:** 5 classes (2 diseases + 3 healthy)
- **Other public datasets:** 30 classes from 10 datasets
- **All 35 missing classes have a source.**

## Things to check

- **Licences.** Only the citrus set's CC BY 4.0 has been confirmed (from the TensorFlow catalogue). Check each
  licence on the dataset's page before using its images, and add every source to the `Credit` string in
  `DiseaseClasses.cs`, which currently credits PlantVillage only.
- **Wheat "Rust" is a mix.** One source is stripe rust and the other is leaf rust. Pick one so the class has one
  meaning, or rename it.
- **Some datasets have extra classes.** MangoLeafBD also has Powdery Mildew, Sooty Mould and Cutting Weevil. The
  rice dataset also has Leaf Scald, Hispa and Narrow Brown Spot. Leave these out to stay at 68.
- **Lab photos vs field photos.** PlantVillage images are lab photos on plain backgrounds, while most of these
  datasets are field photos. That matters if the gallery should look consistent.
- **Wrong datasets with similar names.** The rice set `zfwkkdj6dk` reads like generated text, so it is left out.
  The 3-class "Sugarcane Disease Image Dataset" (`69s3nh72rr`) is a different dataset from the 5-class one above.
- **Docs conflict.** `Backend/README.md` line 265 still says "38 classes, limited crops", but line 25 says
  68 classes across 20 crops.

## Appendix: PlantVillage's 38 classes

Checked against the dataset's GitHub folders.

- **Apple:** Apple scab, Black rot, Cedar apple rust, healthy
- **Blueberry:** healthy
- **Cherry:** Powdery mildew, healthy
- **Corn:** Cercospora / Gray leaf spot, Common rust, Northern leaf blight, healthy
- **Grape:** Black rot, Esca (Black Measles), Leaf blight (Isariopsis leaf spot), healthy
- **Orange:** Huanglongbing (the dataset spells it "Haunglongbing")
- **Peach:** Bacterial spot, healthy
- **Bell Pepper:** Bacterial spot, healthy
- **Potato:** Early blight, Late blight, healthy
- **Raspberry:** healthy
- **Soybean:** healthy
- **Squash:** Powdery mildew
- **Strawberry:** Leaf scorch, healthy
- **Tomato:** Bacterial spot, Early blight, Late blight, Leaf mold, Septoria leaf spot, Spider mites (two-spotted),
  Target spot, Yellow leaf curl virus, Mosaic virus, healthy

## Sources

- [PlantVillage-Dataset (GitHub)](https://github.com/spMohanty/PlantVillage-Dataset/tree/master/raw/color)
- [TFDS plant_village](https://www.tensorflow.org/datasets/catalog/plant_village)
- [TFDS citrus_leaves](https://www.tensorflow.org/datasets/catalog/citrus_leaves)
- [Citrus Fruits & Leaves, Mendeley](https://data.mendeley.com/datasets/3f83gxmv57/2)
- [Rice Leaf Disease Image Samples, Mendeley](https://data.mendeley.com/datasets/fwcj7stb8r)
- [Rice Leaf Bacterial & Fungal Disease Dataset, Mendeley](https://data.mendeley.com/datasets/hx6f852hw4)
- [Disease Dataset of Wheat, Mendeley](https://data.mendeley.com/datasets/5gc7hwydwg)
- [Wheat Leaf Dataset, Mendeley](https://data.mendeley.com/datasets/wgd66f8n6h)
- [Wheat leaf rust (IARI), Mendeley](https://data.mendeley.com/datasets/th422bg4yd)
- [Cotton Leaf Disease with Severity Levels, Mendeley](https://data.mendeley.com/datasets/74jsdxtmx2)
- [Sugarcane Leaf Disease Dataset, Mendeley](https://data.mendeley.com/datasets/9424skmnrk)
- [Chilli leaf disease dataset, Mendeley](https://data.mendeley.com/datasets/wzc6r6w5w5)
- [MangoLeafBD, Mendeley](https://data.mendeley.com/datasets/hxsnvwty3r/1)
- [MangoLeafBD paper (PMC)](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC9932726/)
- [Pumpkin leaf dataset, Mendeley](https://data.mendeley.com/datasets/2svdj3yyrk/2)
- [Pumpkin leaf disease paper (arXiv)](https://arxiv.org/pdf/2501.05449)
