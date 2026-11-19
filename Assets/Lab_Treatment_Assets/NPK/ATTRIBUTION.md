# NPK treatment assets: sources and licences

| Asset | Files in this folder | Source | Author | Licence |
|---|---|---|---|---|
| Compost Bag 02 (open woven sack, 1k FBX + 1k diffuse / normal (GL) / ARM textures) | `sack/model/compost_bag_02_1k.fbx`, `sack/textures/compost_bag_02_*_1k.jpg` | https://polyhaven.com/a/compost_bag_02 (files: https://dl.polyhaven.org/file/ph-assets/Models/...) | Satyaki Mandal (Poly Haven) | CC0 1.0 (public domain) |
| Plaster 001 (micro surface normal (GL) + roughness, used as the granules' coating detail) | `granule/textures/Plaster001_1K-JPG_NormalGL.jpg`, `granule/textures/Plaster001_1K-JPG_Roughness.jpg` | https://ambientcg.com/a/Plaster001 | ambientCG | CC0 1.0 (public domain) |
| Fertilizer bag (original packet model, kept but no longer used by the guided config) | `fertilizer/` | supplied with the project | — | as supplied |
| Grain scoop | `grain-scoop/` | supplied with the project | — | as supplied |

CC0 needs no attribution; it is recorded here so the origin of every file is clear.

Generated in the project (no third-party content): the fertiliser granule meshes (`GranuleMeshes`, noise-displaced
icospheres), the per-product printed sack labels and granule-bed textures (`BTP/Setup/NPK Treatment Assets` writes them to
`Assets/_Project/Textures/Treatment/Npk/`). The sack's original print ("Yoki's Vermicompost", a fictional brand) is
painted over by the per-product label.
