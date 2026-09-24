# Rebuilds every optimized plant model from the supplied sources, in two stages:
#   1. <Name>_Lab.fbx   decimated from the original model (slow for the multi-million-triangle files)
#   2. <Name>_Farm.fbx  decimated from the Lab mesh (fast)
# Output: Tools/out/models/*.fbx and Tools/out/previews/*.png. Copy the FBX files into
# Assets/_Project/Models/Optimized once the previews look right.
# Run from anywhere:  powershell -ExecutionPolicy Bypass -File Tools/decimate_all.ps1 [-Only Apple,Tomato] [-FarmOnly]
param([string[]]$Only, [switch]$FarmOnly)

# 'Continue': Blender prints harmless warnings on stderr, which 'Stop' would treat as fatal.
$ErrorActionPreference = 'Continue'
$blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
$root = Split-Path $PSScriptRoot -Parent
$src = Join-Path $root 'Assets\Plant_Asstes'
$out = Join-Path $root 'Tools\out'
$log = Join-Path $root 'Tools\logs\decimate.log'
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null

# Heights are real-world metres. Budgets follow the Quest 2 plan: Lab = one close-up model,
# Farm = near LOD of a repeated plant (Unity adds a billboard LOD for distance).
# Bell Pepper (~1,200 topological handles) and Apple cannot collapse below ~14k / ~40k
# triangles without shredding, so their farm budgets sit above those floors. Only the
# nearest plants use these meshes; distant ones switch to the billboard LOD.
$plants = @(
    @{ Name = 'Strawberry'; Height = 0.3; Lab = 50000; Farm = 5000
       Src = 'Strawberry_1\Meshy_AI_strawberry_plant_0923200556_texture_fbx\Meshy_AI_strawberry_plant_0923200556_texture.fbx' }
    @{ Name = 'Potato'; Height = 0.5; Lab = 60000; Farm = 5000; Extra = '--pick Potato_03'
       Src = 'potato-plants-collection\source\Potato_plants_Collection.fbx' }
    @{ Name = 'Cherry'; Height = 3.5; Lab = 60000; Farm = 7000
       Src = 'Cherry\fbx\Cherry1.fbx' }
    @{ Name = 'Tomato'; Height = 1.0; Lab = 60000; Farm = 8000
       Src = 'Tomato\Meshy_AI_Tomato_Plant_0923194145_texture_fbx\Meshy_AI_Tomato_Plant_0923194145_texture.fbx' }
    @{ Name = 'TomatoSingleStem'; Height = 1.0; Lab = 70000; Farm = 0
       Src = 'Tomato_asset\Meshy_AI_Single_Stem_Tomato_Pl_0923211133_image-to-3d-texture_fbx\Meshy_AI_Single_Stem_Tomato_Pl_0923211133_image-to-3d-texture.fbx' }
    @{ Name = 'BellPepper'; Height = 0.7; Lab = 60000; Farm = 18000
       Src = 'Bell Pepper\Meshy_AI_Colorful_Bell_Pepper__0923184552_texture_fbx\Meshy_AI_Colorful_Bell_Pepper__0923184552_texture.fbx' }
    @{ Name = 'Apple'; Height = 3.5; Lab = 80000; Farm = 50000
       Src = 'Apple_new\Meshy_AI_apple_tree_3d_0923203849_image-to-3d-texture_fbx\Meshy_AI_apple_tree_3d_0923203849_image-to-3d-texture.fbx' }
)

function Invoke-Blender([string]$label, [string[]]$arguments) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    "=== $label" | Add-Content $log
    $argList = @('-b', '--factory-startup', '-P', (Join-Path $root 'Tools\blender_decimate.py'), '--') + $arguments
    & $blender @argList 2>&1 |
        Select-String -Pattern '^(BASE|LOD|EXPORT|PREVIEW|TOPOLOGY)|Error|Traceback|error:' |
        ForEach-Object { $_.Line } | Add-Content $log
    "--- $label exit=$LASTEXITCODE seconds=$([int]$sw.Elapsed.TotalSeconds)" | Add-Content $log
}

foreach ($p in $plants) {
    if ($Only -and ($Only -notcontains $p.Name)) { continue }
    $common = @('--out-dir', "$out\models", '--name', $p.Name, '--height', "$($p.Height)", '--preview-dir', "$out\previews")
    if (-not $FarmOnly) {
        $extra = if ($p.Extra) { $p.Extra -split ' ' } else { @() }
        Invoke-Blender "$($p.Name) Lab" (@('--src', (Join-Path $src $p.Src), '--skip-farm', '--lab-tris', "$($p.Lab)") + $common + $extra)
    }
    if ($p.Farm -gt 0) {
        Invoke-Blender "$($p.Name) Farm" (@('--src', "$out\models\$($p.Name)_Lab.fbx", '--skip-lab', '--farm-tris', "$($p.Farm)") + $common)
    }
}
'DONE' | Add-Content $log
