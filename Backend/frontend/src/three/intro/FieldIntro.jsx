import { Component, Suspense, useEffect, useLayoutEffect, useMemo, useRef } from 'react'
import { Canvas, useFrame, useLoader, useThree } from '@react-three/fiber'
import { Environment, MeshPortalMaterial, useGLTF, useProgress, useTexture } from '@react-three/drei'
import * as THREE from 'three'

import greenhousePoster from '../../assets/art/journey-greenhouse.webp'
import greenhouseSoft from '../../assets/art/journey-greenhouse-soft.webp'
import greenhouseDepth from '../../assets/art/journey-greenhouse-depth.webp'
import referenceArch from '../../assets/art/greenhouse-reference-arch.webp'
import plantAtlasUrl from '../../assets/art/intro-plant-growth.webp'
import plantMotion from '../../assets/art/intro-plant-growth.json'
import { APERTURE, BEAT, ease } from './timeline'
import GreenhouseProjection from './GreenhouseProjection'
import JourneyLeaf from './JourneyLeaf'

// IntroHero owns the loader clock and the scroll position. The camera stays
// on the original seedling until the aperture has opened into the timber arch.
// See public/3d/CREDITS.md for the loader's pot, soil, and nursery light assets.

const POT_URL = '/3d/planter_pot_clay/planter_pot_clay.gltf'
const PLANT_URL = '/3d/potted_plant_02/potted_plant_02.gltf'
const HDR_URL = '/3d/cloudy_netted_nursery_1k.hdr'

const FOV = 30
const CAM_POS = new THREE.Vector3(0, 0.98, 3.35)
const CAM_TARGET = new THREE.Vector3(0, 0.3, 0)
const PORTAL_DEPTH = 5.2
const CALIBRATION_DISTANCE = 5.75
const PHOTO_ASPECT = 1592 / 1080
const ARCH_ASPECT = 884 / 805
const PEACH = '#E9B591'   // reads as the page's #F5CEB0 once through the portal's render target

/**
 * A texture decoded off the main thread. The 4K plant atlas and greenhouse art
 * take seconds to decode as <img> on the main thread, which held the loader at
 * 0%. The bitmap is flipped at decode time, so UVs match a normal flipY texture.
 */
function useBitmapTexture(url) {
  const bitmap = useLoader(THREE.ImageBitmapLoader, url, (loader) => {
    loader.setOptions({ imageOrientation: 'flipY', premultiplyAlpha: 'none', colorSpaceConversion: 'none' })
  })
  const texture = useMemo(() => {
    const texture = new THREE.Texture(bitmap)
    texture.flipY = false
    texture.colorSpace = THREE.SRGBColorSpace
    texture.needsUpdate = true
    return texture
  }, [bitmap])
  useEffect(() => () => texture.dispose(), [texture])
  return texture
}

const clamp01 = (x) => Math.min(1, Math.max(0, x))
const smooth = (a, b, x) => {
  const u = clamp01((x - a) / (b - a))
  return u * u * (3 - 2 * u)
}
/* ─── Composition: reference pixels → hero fractions ────────────────────────
   Reference frame 1280 × 720 with a 60 px header, so the scene is 1280 × 660.
   pot base (695, 585), rim x 580–808  → (0.543, 0.795), rim 0.345 h
   doorway (olive-and-oak reference): outer x 633–1240, y 14–628 of the hero,
     607 × 614 px; frame 8.8 %, oak lining 4.9 %, outer corner radius 17.5 %
     of the door width; flat open threshold, no bottom rail
   aisle vanishing point: centred in the opening, 52.6 % of the way down it
   the seedling's aperture starts over the canopy at (0.56, 0.47)
   Lengths are in hero heights (h). */
const DOOR = { frame: 0.075, lining: 0.027, corner: 0.175, depth: 0.17, face: 0.023 }
function composition(aspect) {
  if (aspect >= 1.15) {
    // Reserve the outer columns for the landing copy and the chapter shortcuts.
    const photoWidth = Math.min(0.91, aspect * 0.47)
    const W = photoWidth * 0.52
    const bottom = 0.93
    const a = W / 2
    const top = bottom - photoWidth * 0.74 / ARCH_ASPECT
    const openTop = top + (DOOR.frame + DOOR.lining) * W
    return {
      pot: [0.543, 0.795], rim: 0.345,
      centre: [0.54, (top + bottom) / 2], a, b: (bottom - top) / 2, W, photoWidth,
      vp: [0.54, openTop + 0.526 * (bottom - openTop)],
      leaf: [0.56, 0.47],
    }
  }
  // Portrait: copy on top; the pot, then the doorway, in the lower part.
  const photoWidth = Math.min(0.97 * aspect, 0.61)
  const W = photoWidth * 0.52
  const b = photoWidth * 0.37 / ARCH_ASPECT
  const bottom = 0.87
  const openTop = bottom - 2 * b + (DOOR.frame + DOOR.lining) * W
  return {
    pot: [0.5, 0.86], rim: 0.42 * aspect,
    centre: [0.5, bottom - b], a: W / 2, b, W, photoWidth,
    vp: [0.5, openTop + 0.526 * (bottom - openTop)],
    leaf: [0.51, 0.7],
  }
}

const fovFor = (aspect) => (aspect >= 1.15 ? FOV : THREE.MathUtils.clamp(FOV / Math.max(aspect, 0.35), FOV, 60))
const viewHeight = (d, fov) => 2 * d * Math.tan(THREE.MathUtils.degToRad(fov / 2))

const baseCam = new THREE.PerspectiveCamera(FOV, 1, 0.05, 80)
const _v = new THREE.Vector3()
const _fwd = new THREE.Vector3()
function rayThrough(cam, fx, fy) {
  return _v.set(fx * 2 - 1, -(fy * 2 - 1), 0.5).unproject(cam).sub(cam.position).normalize().clone()
}
function atDepth(cam, fx, fy, d) {
  const dir = rayThrough(cam, fx, fy)
  cam.getWorldDirection(_fwd)
  return cam.position.clone().addScaledVector(dir, d / dir.dot(_fwd))
}
function onFloor(cam, fx, fy) {
  const dir = rayThrough(cam, fx, fy)
  return cam.position.clone().addScaledVector(dir, -cam.position.y / dir.y)
}

function useLayout() {
  const size = useThree((s) => s.size)
  return useMemo(() => {
    const aspect = size.width / Math.max(1, size.height)
    const fov = fovFor(aspect)
    baseCam.aspect = aspect
    baseCam.fov = fov
    baseCam.position.copy(CAM_POS)
    baseCam.lookAt(CAM_TARGET)
    baseCam.updateProjectionMatrix()
    baseCam.updateMatrixWorld()
    const c = composition(aspect)
    const hd = viewHeight(PORTAL_DEPTH, fov)
    const centre = atDepth(baseCam, c.centre[0], c.centre[1], PORTAL_DEPTH)
    const quat = baseCam.quaternion.clone()
    const toLocal = new THREE.Matrix4().compose(centre, quat, new THREE.Vector3(1, 1, 1)).invert()

    const aisleAxis = atDepth(baseCam, ...c.vp, 1).sub(CAM_POS)
    const aisleFacing = baseCam.clone()
    aisleFacing.lookAt(CAM_POS.clone().add(aisleAxis))
    const forward = new THREE.Vector3(aisleAxis.x, 0, aisleAxis.z).normalize()
    const floor = atDepth(baseCam, c.centre[0], c.centre[1] + c.b, PORTAL_DEPTH)
    floor.y -= 0.025
    const yaw = Math.atan2(-forward.x, -forward.z)
    const scale = c.photoWidth * hd / 2.35
    const interiorFacing = new THREE.Quaternion().setFromEuler(new THREE.Euler(-0.12, yaw, 0, 'YXZ'))

    // pot: base on the floor at the reference point; rim width from the reference
    const potPos = onFloor(baseCam, c.pot[0], c.pot[1])
    const potDist = potPos.clone().sub(CAM_POS).dot(baseCam.getWorldDirection(new THREE.Vector3()))
    const rimWorld = c.rim * viewHeight(potDist, fov)

    const leaf = atDepth(baseCam, c.leaf[0], c.leaf[1], PORTAL_DEPTH).applyMatrix4(toLocal)
    const door = {
      a: c.a * hd, b: c.b * hd, W: c.W * hd,
      frame: DOOR.frame * c.W * hd, lining: DOOR.lining * c.W * hd, corner: DOOR.corner * c.W * hd,
      depth: DOOR.depth * c.W * hd, face: DOOR.face * c.W * hd,
      photoWidth: c.photoWidth * hd,
    }
    return {
      aspect, fov, R: Math.max(door.a, door.b) * 1.2, door, leaf: [leaf.x, leaf.y],
      rig: { position: centre, quaternion: quat },
      greenhouse: { floor, scale, forward, facing: interiorFacing },
      aisleAxis, aisleFacing: aisleFacing.quaternion, potPos, rimWorld,
    }
  }, [size])
}

function placeCamera(camera, layout, distance, offsetX = 0, offsetY = 0) {
  camera.position.copy(CAM_POS)
  camera.position.x += offsetX
  camera.position.y += offsetY
  camera.lookAt(CAM_TARGET)
  camera.quaternion.slerp(layout.aisleFacing, smooth(0, 3.2, distance))
  camera.quaternion.slerp(layout.greenhouse.facing, smooth(1.22, 4.0, distance))
  camera.position.addScaledVector(layout.aisleAxis, Math.min(distance, PORTAL_DEPTH))
  camera.position.addScaledVector(layout.greenhouse.forward, Math.max(0, distance - PORTAL_DEPTH) * layout.greenhouse.scale)
  const eye = layout.greenhouse.floor.y + 1.34 * layout.greenhouse.scale
  camera.position.y = THREE.MathUtils.lerp(camera.position.y, eye, smooth(3.1, 6.9, distance))
  const insideFov = Math.max(43, Math.min(64, layout.fov * 1.05))
  const fov = THREE.MathUtils.lerp(layout.fov, insideFov, smooth(3.2, 6.05, distance))
  if (camera.fov !== fov) {
    camera.fov = fov
    camera.updateProjectionMatrix()
  }
  camera.updateMatrixWorld()
}

function CameraRig({ store, layout }) {
  const camera = useThree((s) => s.camera)
  const offset = useMemo(() => new THREE.Vector2(), [])
  useFrame((_, dt) => {
    const target = store.t >= BEAT.settled ? store.journey : 0
    store.journeyView = Math.abs(target - store.journeyView) < 0.0002
      ? target : THREE.MathUtils.damp(store.journeyView, target, 16, Math.min(dt, 0.08))
    const progress = store.journeyView
    const distance = 5.01 * smooth(0.068, 0.366, progress) + 2.14 * smooth(0.278, 0.741, progress)
    store.walkView = distance
    const allow = store.animate && store.t >= BEAT.settled ? 1 - smooth(0, 1.5, distance) : 0
    const k = 1 - Math.pow(0.002, Math.min(dt, 0.05))
    offset.x = THREE.MathUtils.lerp(offset.x, store.pointer.x * 0.1, k)
    offset.y = THREE.MathUtils.lerp(offset.y, -store.pointer.y * 0.06, k)
    placeCamera(camera, layout, distance, offset.x * allow, offset.y * allow)
  }, -1)
  return null
}

/* ─── Floor: soft leftward contact shadow and faint elliptical rings ─────── */
const ringsMaterial = () => new THREE.ShaderMaterial({
  transparent: true,
  depthWrite: false,
  uniforms: { ringOpacity: { value: 1 } },
  vertexShader: 'varying vec2 vP; void main(){ vP = position.xy; gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0); }',
  fragmentShader: [
    'varying vec2 vP; uniform float ringOpacity;',
    'void main(){',
    '  float d = length(vP);',
    '  float k = d / 0.08;',
    '  float edge = min(fract(k), 1.0 - fract(k));   // distance to the nearest ring',
    '  float line = 1.0 - smoothstep(0.0, 0.05, edge);',
    '  float band = smoothstep(0.36, 0.44, d) * (1.0 - smoothstep(0.66, 0.98, d));',
    '  gl_FragColor = vec4(0.62, 0.35, 0.2, line * band * 0.22 * ringOpacity);',
    '}',
  ].join('\n'),
})

/** Fade materials that were made transparent at creation. Never toggling
 *  `transparent` here matters: a toggle recompiles the shader, and doing that
 *  as the reveal starts froze a frame for ≈0.9 s. */
function setOpacity(mats, o) {
  for (const m of mats) {
    m.opacity = o
    m.depthWrite = o > 0.6
  }
}

function prepareFade(material) {
  material.transparent = true
  material.opacity = 1
  return material
}

// The intro camera is locked during growth. Play the isolated foliage from the
// reference in that camera plane, with its stem registered to the 3D soil.
function GrowingFoliage({ store, layout, soilTop }) {
  const texture = useBitmapTexture(plantAtlasUrl)
  const gl = useThree((s) => s.gl)
  const mesh = useRef()
  const lastFrame = useRef(-1)

  useLayoutEffect(() => {
    texture.colorSpace = THREE.SRGBColorSpace
    // 3584 × 4070: the footage atlas upscaled with Real-ESRGAN (colours matched
    // back to the footage). Mipmapped, so 1× screens downscale cleanly too.
    texture.generateMipmaps = true
    texture.minFilter = THREE.LinearMipmapLinearFilter
    texture.magFilter = THREE.LinearFilter
    texture.anisotropy = 8
    texture.needsUpdate = true
    // upload (and build mipmaps for) the 4K atlas now, while loading -- not on
    // the first visible frame of growth at 0.92 s
    gl.initTexture(texture)
    lastFrame.current = -1
  }, [texture, gl])

  useFrame(() => {
    const foliage = mesh.current
    if (!foliage) return
    const frame = Math.min(plantMotion.frames.length - 1,
      Math.floor(store.t * plantMotion.fps) - plantMotion.firstFrame)
    const opacity = ease(APERTURE.pot, store.t)
    foliage.visible = frame >= 0 && opacity > 0.001
    if (!foliage.visible) return
    foliage.material.opacity = opacity
    if (lastFrame.current === frame) return
    lastFrame.current = frame

    // Packed rectangle in source pixels, followed by the offset from the soil root.
    const [x, y, w, h, left, bottom] = plantMotion.frames[frame]
    // frame rectangles are in the original atlas's pixels; UVs are fractions of
    // it, so they hold at any texture resolution
    const [width, height] = plantMotion.atlasSize
    const uv = foliage.geometry.attributes.uv
    const u0 = x / width, u1 = (x + w) / width
    const v0 = 1 - (y + h) / height, v1 = 1 - y / height
    uv.setXY(0, u0, v1)
    uv.setXY(1, u1, v1)
    uv.setXY(2, u0, v0)
    uv.setXY(3, u1, v0)
    uv.needsUpdate = true
    foliage.position.set(left + w / 2, bottom + h / 2, 0)
    foliage.scale.set(w, h, 1)
  })

  return (
    // one footage pixel = one reference pixel; the reference rim spans 228 px (x 580–808)
    <group position={[0, soilTop, 0]} quaternion={layout.rig.quaternion} scale={layout.rimWorld / 228}>
      {/* Drawn over the pot, as in the footage: with depth testing the nearer soil
          and rim would hide the stem base and the leaves that hang over the rim. */}
      <mesh ref={mesh} castShadow visible={false} renderOrder={10}>
        <planeGeometry args={[1, 1]} />
        <meshBasicMaterial map={texture} transparent alphaTest={0.025} depthWrite={false} depthTest={false} toneMapped={false} />
      </mesh>
    </group>
  )
}

/* ─── Pot, soil and the growing shoots ──────────────────────────────────── */
function PottedPlant({ store, layout }) {
  const potGltf = useGLTF(POT_URL)
  const plantGltf = useGLTF(PLANT_URL)
  const root = useRef()

  const parts = useMemo(() => {
    // pot: the tapered terracotta planter, normalised so its rim matches the reference width
    const pot = potGltf.scene.clone(true)
    const potMats = []
    pot.traverse((o) => {
      if (!o.isMesh) return
      o.castShadow = true
      o.receiveShadow = true
      o.material = o.material.clone()
      // clean warm terracotta: keep the scan's surface relief, drop its grime colour
      o.material.map = null
      o.material.color = new THREE.Color('#C9804F')
      o.material.roughness = 0.9
      o.material.emissive = new THREE.Color('#5E2E14')
      o.material.emissiveIntensity = 0.25
      potMats.push(prepareFade(o.material))
    })
    const pb = new THREE.Box3().setFromObject(pot)
    const rimRadius = Math.max(pb.max.x - pb.min.x, pb.max.z - pb.min.z) / 2
    const potScale = layout.rimWorld / (rimRadius * 2)
    const POT_SQUASH = 0.82          // the reference pot is squatter than the model
    const potHeight = (pb.max.y - pb.min.y) * potScale * POT_SQUASH

    // soil: the scanned granular soil, recentred and fitted inside the rim
    const dirtSrc = plantGltf.scene.getObjectByName('potted_plant_02_dirt')
    const dirt = dirtSrc.clone(true)
    const dirtMats = []
    dirt.traverse((o) => {
      if (!o.isMesh) return
      o.receiveShadow = true
      o.material = o.material.clone()
      o.material.color = new THREE.Color('#6B4C38')   // dark, coarse potting soil
      dirtMats.push(prepareFade(o.material))
    })
    // The scanned soil is many stacked layers: under plain alpha they add up to
    // near-solid. A depth-only copy drawn first keeps just its top surface, so
    // the soil fades as one sheet with the pot.
    const dirtDepth = dirt.clone(true)
    const depthOnly = new THREE.MeshBasicMaterial({ colorWrite: false })
    dirtDepth.traverse((o) => { if (o.isMesh) { o.material = depthOnly; o.castShadow = false; o.receiveShadow = false } })
    const db = new THREE.Box3().setFromObject(dirt)
    const dirtCentre = db.getCenter(new THREE.Vector3())
    const dirtRadius = Math.max(db.max.x - db.min.x, db.max.z - db.min.z) / 2
    const soilWorldR = layout.rimWorld * 0.5 * 0.82
    const dirtScale = soilWorldR / dirtRadius
    const soilTop = potHeight * 0.9
    const soilY = db.max.y          // the scanned soil's surface, in its own units

    return {
      pot, potMats, potScale, potSquash: POT_SQUASH, dirt, dirtDepth, dirtMats, dirtCentre, dirtScale, soilY, soilTop,
    }
  }, [potGltf, plantGltf, layout.rimWorld])

  useEffect(() => {
    store.potReady = true
    // QA: ?intro=debug exposes the pot parts to the review script
    if (new URLSearchParams(window.location.search).get('intro') === 'debug') window.__cropscanPot = parts
  }, [store, parts])

  useFrame(() => {
    const t = store.t
    setOpacity(parts.potMats, ease(APERTURE.pot, t))
    setOpacity(parts.dirtMats, ease(APERTURE.pot, t))
    if (root.current) root.current.visible = ease(APERTURE.pot, t) > 0.001
  })

  const { potScale, dirtScale, dirtCentre, soilY, soilTop } = parts
  return (
    <group ref={root} position={layout.potPos}>
      <primitive object={parts.pot} scale={[potScale, potScale * parts.potSquash, potScale]} />
      {/* the scanned soil, recentred on the pot axis, its surface at soilTop */}
      <group position={[0, soilTop, 0]} scale={dirtScale}>
        <group position={[-dirtCentre.x, -soilY, -dirtCentre.z]}>
          <primitive object={parts.dirtDepth} />
          <primitive object={parts.dirt} />
        </group>
      </group>
      <GrowingFoliage store={store} layout={layout} soilTop={soilTop} />
    </group>
  )
}

function SoftFocus({ store, photo }) {
  const plane = useRef(null)
  const camera = useThree((state) => state.camera)

  useFrame(() => {
    const mesh = plane.current
    const opacity = smooth(0.68, 0.85, store.journeyView)
    mesh.visible = opacity > 0
    if (!mesh.visible) return

    const distance = 2.53
    const height = viewHeight(distance, camera.fov) * Math.max(1, camera.aspect / PHOTO_ASPECT)
    mesh.position.set(0, 0, -distance).applyQuaternion(camera.quaternion).add(camera.position)
    mesh.quaternion.copy(camera.quaternion)
    mesh.scale.set(height * PHOTO_ASPECT, height, 1)
    mesh.material.opacity = opacity
  })

  return (
    <mesh ref={plane} visible={false} frustumCulled={false} renderOrder={3}>
      <planeGeometry />
      <meshBasicMaterial map={photo} transparent depthWrite={false} toneMapped={false} />
    </mesh>
  )
}

function GreenhouseView({ store, layout, veil }) {
  const photo = useBitmapTexture(greenhousePoster)
  const soft = useBitmapTexture(greenhouseSoft)
  const depth = useLoader(THREE.TextureLoader, greenhouseDepth)
  const gl = useThree((state) => state.gl)
  const room = useRef(null)
  const calibration = useMemo(() => {
    const camera = new THREE.PerspectiveCamera(layout.fov, layout.aspect, 0.05, 80)
    placeCamera(camera, layout, CALIBRATION_DISTANCE)
    return camera
  }, [layout])

  useLayoutEffect(() => {
    photo.anisotropy = Math.min(8, gl.capabilities.getMaxAnisotropy())
    gl.initTexture(photo)
    gl.initTexture(soft)
  }, [photo, soft, gl])
  useEffect(() => { store.panoReady = true }, [store])
  useFrame(() => { room.current.visible = store.journeyView < 0.85 })

  return (
    <>
      <color attach="background" args={['#978360']} />
      <hemisphereLight args={['#fff2da', '#726851', 1.9]} />
      <directionalLight position={[-3, 5, 3]} color="#fff2cf" intensity={1.95} />
      <group ref={room}>
        <GreenhouseProjection camera={calibration} depth={depth} photo={photo} />
      </group>
      {/* This veil is tied to the aperture fade on the preserved loader clock. */}
      <mesh position={layout.rig.position} quaternion={layout.rig.quaternion} renderOrder={1}>
        <planeGeometry args={[layout.R * 4, layout.R * 4]} />
        <primitive object={veil} attach="material" />
      </mesh>
      <SoftFocus store={store} photo={soft} />
      <Suspense fallback={null}>
        <JourneyLeaf store={store} />
      </Suspense>
    </>
  )
}

/* ─── The aperture and the olive-and-oak doorway ─────────────────────────── */
const N = 168
function makeFan() {
  const geo = new THREE.BufferGeometry()
  geo.setAttribute('position', new THREE.BufferAttribute(new Float32Array((N + 1) * 3), 3))
  const idx = []
  for (let i = 1; i <= N; i++) idx.push(0, i, (i % N) + 1)
  geo.setIndex(idx)
  return geo
}

const FINAL_OPENING = 0.816   // the timeline's growth key: 1 at 5.4 s, this when settled

/**
 * Point i (0..N) of the doorway outline at inset d, counter-clockwise from the
 * bottom-right corner: right side up, top-right arc, lintel, top-left arc, left
 * side down, then the threshold. The inset is the true offset curve, except on
 * the threshold, which has no rail: there inner and outer edges coincide.
 * out = [x, y, onThreshold].
 */
function doorPoint(i, a, b, c, d, out) {
  c = Math.max(1e-6, Math.min(c, a, b))
  const side = 2 * b - c
  const arc = Math.PI * c / 2
  const top = 2 * a - 2 * c
  const bottom = 2 * a
  let s = (i / N) * (2 * side + 2 * arc + top + bottom)
  const ci = Math.max(c - d, 0)
  out[2] = 0
  if (s <= side) { out[0] = a - d; out[1] = -b + s; return out }
  s -= side
  if (s <= arc) {
    const th = s / c
    out[0] = a - c + Math.cos(th) * ci; out[1] = b - c + Math.sin(th) * ci; return out
  }
  s -= arc
  if (s <= top) { out[0] = a - c - s; out[1] = b - d; return out }
  s -= top
  if (s <= arc) {
    const th = Math.PI / 2 + s / c
    out[0] = -a + c + Math.cos(th) * ci; out[1] = b - c + Math.sin(th) * ci; return out
  }
  s -= arc
  if (s <= side) { out[0] = -a + d; out[1] = b - c - s; return out }
  s -= side
  out[2] = 1
  out[0] = THREE.MathUtils.clamp(-a + s, -a + d, a - d); out[1] = -b
  return out
}

/** The seedling's upright leaf (a lens through two tips, fuller on the right),
 *  sampled by angle so it morphs point-for-point into the doorway. */
function leafPoint(th, halfW, halfH, tilt, out) {
  const sy = Math.sin(th)
  const cx = Math.cos(th)
  const w = halfW * (cx > 0 ? 1.08 : 0.92)
  const rho = (halfH * halfH + w * w) / (2 * Math.max(w, 1e-6))
  const ly = sy * halfH
  const lx = Math.sign(cx) * Math.max(0, Math.sqrt(Math.max(0, rho * rho - ly * ly)) - (rho - w))
  const c = Math.cos(tilt)
  const s = Math.sin(tilt)
  out[0] = lx * c - ly * s
  out[1] = lx * s + ly * c
  return out
}

// Across the olive frame from its outer edge: [fraction of the frame width,
// face height]. Soft bevels on both edges, then a narrow groove to the oak.
const OLIVE_PROFILE = [[0, 0], [0.04, 0.45], [0.12, 0.85], [0.22, 1], [0.78, 1], [0.88, 0.85], [0.96, 0.4], [1, -0.25]]

function makeBand(rows) {
  const geo = new THREE.BufferGeometry()
  geo.setAttribute('position', new THREE.BufferAttribute(new Float32Array((N + 1) * rows * 3), 3))
  geo.setAttribute('uv', new THREE.BufferAttribute(new Float32Array((N + 1) * rows * 2), 2))
  const idx = []
  for (let i = 0; i < N; i++) {
    for (let j = 0; j < rows - 1; j++) {
      const a0 = i * rows + j
      const b0 = (i + 1) * rows + j
      idx.push(a0, b0, a0 + 1, a0 + 1, b0, b0 + 1)
    }
  }
  geo.setIndex(idx)
  return geo
}

/** Honey oak: a warm base with long grain that runs along the lining. */
function makeOakTexture() {
  const w = 2048
  const h = 256
  const cv = document.createElement('canvas')
  cv.width = w
  cv.height = h
  const g = cv.getContext('2d')
  g.fillStyle = '#C48A4F'
  g.fillRect(0, 0, w, h)
  let seed = 3
  const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647)
  for (let k = 0; k < 140; k++) {
    const y = rnd() * h
    const dark = rnd() < 0.55
    g.strokeStyle = dark ? `rgba(120,70,30,${0.08 + rnd() * 0.16})` : `rgba(235,185,120,${0.06 + rnd() * 0.12})`
    g.lineWidth = 0.6 + rnd() * 2.2
    g.beginPath()
    const amp = 1 + rnd() * 4
    const freq = 0.002 + rnd() * 0.006
    const ph = rnd() * 10
    for (let x = 0; x <= w; x += 16) {
      const yy = y + Math.sin(x * freq + ph) * amp
      if (x === 0) g.moveTo(x, yy)
      else g.lineTo(x, yy)
    }
    g.stroke()
  }
  const tex = new THREE.CanvasTexture(cv)
  tex.colorSpace = THREE.SRGBColorSpace
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping
  tex.anisotropy = 8
  return tex
}

const _m = new THREE.Matrix4()
const _q = new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(1, 0, 0), Math.PI / 2)
const _p = new THREE.Vector3()
const _s = new THREE.Vector3()

const ARCH_OPENING = (() => {
  const contour = new THREE.Path()
  contour.moveTo(0.739, 1)
  contour.lineTo(0.739, 0.911)
  contour.lineTo(0.701, 0.911)
  contour.lineTo(0.701, 0.469)
  contour.bezierCurveTo(0.701, 0.358, 0.622, 0.279, 0.494, 0.276)
  contour.bezierCurveTo(0.377, 0.279, 0.292, 0.362, 0.291, 0.47)
  contour.lineTo(0.291, 0.91)
  contour.lineTo(0.247, 0.91)
  contour.lineTo(0.247, 1)
  contour.closePath()
  return contour.getSpacedPoints(N)
})()

function ArchFace({ store, door }) {
  const mesh = useRef(null)
  const photo = useBitmapTexture(referenceArch)
  const gl = useThree((state) => state.gl)
  const height = door.photoWidth / ARCH_ASPECT

  useLayoutEffect(() => { gl.initTexture(photo) }, [gl, photo])
  useEffect(() => { store.archReady = true }, [store])

  useFrame(() => {
    const reveal = smooth(6.66, BEAT.settled, store.t)
    mesh.current.visible = reveal > 0
    mesh.current.material.opacity = reveal
  })

  return (
    <mesh
      ref={mesh}
      position={[0, height / 2 - door.b, 0.072]}
      scale={[door.photoWidth, height, 1]}
      visible={false}
      renderOrder={2}
    >
      <planeGeometry />
      <meshBasicMaterial map={photo} transparent alphaTest={0.18} depthWrite={false} toneMapped={false} />
    </mesh>
  )
}

function Portal({ store, layout }) {
  const rig = useRef()
  const portal = useRef()
  const sill = useRef()
  const bolts = useRef()
  const lastShapeTime = useRef(-1)
  const fan = useMemo(() => makeFan(), [])
  const olive = useMemo(() => makeBand(OLIVE_PROFILE.length), [])
  const oak = useMemo(() => makeBand(4), [])   // groove floor, oak face out, face in, reveal back

  const veil = useMemo(() => new THREE.MeshBasicMaterial({ color: PEACH, transparent: true, opacity: 1, depthWrite: false, toneMapped: false }), [])
  // Weathered oak and a darker, deeply recessed inner band.
  const oliveMat = useMemo(() => new THREE.MeshStandardMaterial({
    color: '#76543A', map: makeOakTexture(), roughness: 0.83, envMapIntensity: 0.55,
    transparent: true, opacity: 0, side: THREE.DoubleSide,
  }), [])
  const oakMat = useMemo(() => new THREE.MeshStandardMaterial({
    map: makeOakTexture(), roughness: 0.62, envMapIntensity: 0.7,
    transparent: true, opacity: 0, side: THREE.DoubleSide,
  }), [])
  const sillMat = useMemo(() => new THREE.MeshStandardMaterial({ color: '#A58460', roughness: 0.9, transparent: true, opacity: 0 }), [])
  const boltMat = useMemo(() => new THREE.MeshStandardMaterial({ color: '#3B4424', metalness: 0.5, roughness: 0.45, transparent: true, opacity: 0 }), [])

  useLayoutEffect(() => {
    rig.current.position.copy(layout.rig.position)
    rig.current.quaternion.copy(layout.rig.quaternion)
    lastShapeTime.current = -1
  }, [layout])

  const po = useMemo(() => [0, 0, 0], [])
  const pl = useMemo(() => [0, 0], [])
  useFrame(() => {
    // Give the portal scene the full viewport just before crossing its near plane.
    // (during warm-up the blend path runs once, so its buffers and shader exist
    // before the walk needs them: first use mid-scroll stalled ≈0.75 s)
    if (portal.current) portal.current.blend = store.warmBlend ?? smooth(0.84, 0.965, store.walkView / PORTAL_DEPTH)
    const t = Math.min(store.t, BEAT.settled)
    if (t === lastShapeTime.current) return
    lastShapeTime.current = t
    const D = layout.door
    const live = t >= BEAT.aperture - 0.05
    // the opening travels from over the seedling's canopy to the doorway
    const travel = ease(APERTURE.travel, t)
    const cx = layout.leaf[0] * travel
    const cy = layout.leaf[1] * travel
    const a = live ? ease(APERTURE.halfW, t) * D.a : 0
    const b = live ? ease(APERTURE.halfH, t) * D.b : 0
    const c = THREE.MathUtils.lerp(D.corner, Math.min(D.a, D.b), smooth(6.15, BEAT.settled, t)) * Math.min(1, a / D.a)
    const round = ease(APERTURE.round, t)
    const tilt = ease(APERTURE.tilt, t)
    const grow = clamp01((1 - ease(APERTURE.opening, t)) / (1 - FINAL_OPENING))   // 0 → 1
    const tFrame = D.frame * grow
    const tLining = D.lining * grow
    const face = D.face * grow
    const depth = D.depth * grow
    const reference = smooth(6.66, BEAT.settled, t)
    const archHeight = D.photoWidth / ARCH_ASPECT
    const fanZ = THREE.MathUtils.lerp(-depth, 0.067, reference)

    // a contour point at inset d, morphed from the leaf (same angle) to the door
    const at = (i, d, out) => {
      doorPoint(i, a, b, c, d, out)
      if (round < 1) {
        const th = Math.atan2(out[1] / Math.max(b, 1e-6), out[0] / Math.max(a, 1e-6))
        const k = Math.max(0, 1 - d / Math.max(a, 1e-6))
        leafPoint(th, a * k, b * k, tilt, pl)
        out[0] = THREE.MathUtils.lerp(pl[0], out[0], round)
        out[1] = THREE.MathUtils.lerp(pl[1], out[1], round)
      }
      out[0] += cx
      out[1] += cy
      return out
    }

    // the clear opening, inside the oak lining (the fan's back plane)
    const fp = fan.attributes.position
    fp.setXYZ(0, cx, cy, fanZ)
    for (let i = 0; i < N; i++) {
      at(i, tFrame + tLining, po)
      const artPoint = ARCH_OPENING[i]
      fp.setXYZ(i + 1,
        THREE.MathUtils.lerp(po[0], (artPoint.x - 0.5) * D.photoWidth, reference),
        THREE.MathUtils.lerp(po[1], (1 - artPoint.y) * archHeight - D.b, reference),
        fanZ,
      )
    }
    fp.needsUpdate = true
    fan.computeBoundingSphere()

    // olive frame: bevelled band from the outer edge to the groove
    const op = olive.attributes.position
    const ou = olive.attributes.uv
    const rowsO = OLIVE_PROFILE.length
    for (let i = 0; i <= N; i++) {
      for (let j = 0; j < rowsO; j++) {
        const [f, z] = OLIVE_PROFILE[j]
        at(i % N, tFrame * f, po)
        op.setXYZ(i * rowsO + j, po[0], po[1], po[2] ? 0 : z * face)
        ou.setXY(i * rowsO + j, i / N, f)
      }
    }
    op.needsUpdate = true
    olive.computeVertexNormals()
    olive.computeBoundingSphere()

    // oak lining: groove floor, its face, then the reveal running back
    const kp = oak.attributes.position
    const ku = oak.attributes.uv
    const rowsK = [[tFrame, -0.25 * face, 0], [tFrame + tLining * 0.08, 0.15 * face, 0.05], [tFrame + tLining, 0.15 * face, 0.3], [tFrame + tLining, -depth, 1]]
    const grain = 2 * (a + b) / Math.max(D.W, 1e-6) * 3
    for (let i = 0; i <= N; i++) {
      for (let j = 0; j < 4; j++) {
        const [d, z, v] = rowsK[j]
        at(i % N, d, po)
        kp.setXYZ(i * 4 + j, po[0], po[1], po[2] ? 0 : z)
        ku.setXY(i * 4 + j, (i / N) * grain, v)
      }
    }
    kp.needsUpdate = true
    oak.computeVertexNormals()
    oak.computeBoundingSphere()

    // the flat threshold sill, and two recessed fasteners near each foot
    if (sill.current) {
      sill.current.visible = grow > 0.01
      sill.current.position.set(cx, cy - b - D.W * 0.006, -depth / 2)
      sill.current.scale.set(Math.max(1e-4, 2 * a), D.W * 0.012, Math.max(1e-4, depth))
    }
    if (bolts.current) {
      const r = Math.max(1e-4, D.W * 0.0085 * grow)
      let n = 0
      for (const sx of [-1, 1]) {
        for (const hy of [0.05, 0.11]) {
          _p.set(cx + sx * (a - tFrame * 0.5), cy - b + D.W * hy, face * 0.9)
          _s.set(r, Math.max(1e-4, face * 0.25), r)
          bolts.current.setMatrixAt(n++, _m.compose(_p, _q, _s))
        }
      }
      bolts.current.instanceMatrix.needsUpdate = true
      bolts.current.visible = grow > 0.01 && round > 0.98
    }

    veil.opacity = live ? 1 - ease(APERTURE.photo, t) : 1
    const fade = smooth(0, 0.35, grow)
    oliveMat.opacity = fade * (1 - reference)
    oakMat.opacity = fade * (1 - reference)
    sillMat.opacity = fade * round * (1 - reference)
    boltMat.opacity = fade * (1 - reference)
  })

  return (
    <group ref={rig}>
      {/* always drawn (degenerate before 5.4 s) so the greenhouse uploads and
          compiles during loading, never mid-transition */}
      <mesh geometry={fan} renderOrder={-1} frustumCulled={false}>
        <MeshPortalMaterial ref={portal} resolution={1024} blur={0} worldUnits>
          <Suspense fallback={null}>
            <GreenhouseView store={store} layout={layout} veil={veil} />
          </Suspense>
        </MeshPortalMaterial>
      </mesh>
      <mesh geometry={olive} material={oliveMat} receiveShadow castShadow frustumCulled={false} />
      <mesh geometry={oak} material={oakMat} receiveShadow frustumCulled={false} />
      <Suspense fallback={null}>
        <ArchFace store={store} door={layout.door} />
      </Suspense>
      <mesh ref={sill} material={sillMat} receiveShadow frustumCulled={false}>
        <boxGeometry args={[1, 1, 1]} />
      </mesh>
      <instancedMesh ref={bolts} args={[null, boltMat, 4]} frustumCulled={false}>
        <cylinderGeometry args={[1, 1, 1, 16]} />
      </instancedMesh>
    </group>
  )
}

/* ─── Readiness: compile everything once loaded ─────────────────────────── */
function Warmup({ store }) {
  const { gl, scene, camera } = useThree()
  const frames = useRef(0)
  const compiled = useRef(false)
  useFrame(() => {
    if (store.worldReady || !store.potReady || !store.panoReady || !store.archReady) return
    if (!compiled.current) {
      compiled.current = true
      gl.compile(scene, camera)
    }
    // Exercise both of drei's portal paths before anything is shown: blended
    // (0.5) and portal-only (1), several frames each, since the portal's render
    // priority switches through React state a frame late. Their screen-output
    // shaders used to compile mid-walk, freezing the doorway crossing ≈0.65 s.
    frames.current++
    const f = frames.current
    store.warmBlend = f <= 6 ? 0.5 : f <= 12 ? 1 : undefined
    if (f > 16) store.worldReady = true
  })
  return null
}

function Progress({ store }) {
  const { progress } = useProgress()
  useEffect(() => { store.loader = progress / 100 }, [progress, store])
  return null
}

function Scene({ store }) {
  const layout = useLayout()
  const canvas = useThree((s) => s.gl.domElement)
  useEffect(() => {
    const lost = () => { store.failed = true }
    canvas.addEventListener('webglcontextlost', lost)
    return () => canvas.removeEventListener('webglcontextlost', lost)
  }, [canvas, store])
  const rings = useMemo(() => ringsMaterial(), [])
  const shadowMat = useMemo(() => new THREE.ShadowMaterial({
    color: '#6E3517', opacity: 0.17, transparent: true, depthWrite: false,
  }), [])
  useFrame(() => {
    const pot = ease(APERTURE.pot, store.t)
    shadowMat.opacity = 0.17 * pot
    rings.uniforms.ringOpacity.value = pot
  })
  return (
    <>
      <CameraRig store={store} layout={layout} />
      <Progress store={store} />
      <Warmup store={store} />

      <Suspense fallback={null}>
        <Environment files={HDR_URL} environmentIntensity={0.6} />
      </Suspense>
      <hemisphereLight args={['#FFE9DA', '#B9825C', 0.45]} />
      {/* key light from the right: the contact shadow falls to the left */}
      <directionalLight
        position={[layout.potPos.x + 2.4, 2.6, layout.potPos.z - 0.6]}
        intensity={2.0}
        color="#FFF2E2"
        castShadow
        shadow-mapSize={[2048, 2048]}
        shadow-camera-left={-1.3}
        shadow-camera-right={1.3}
        shadow-camera-top={1.3}
        shadow-camera-bottom={-1.3}
        shadow-bias={-0.0004}
        shadow-normalBias={0.02}
        shadow-radius={10}
        shadow-blurSamples={20}
      />
      <directionalLight position={[-2.5, 1.6, 3]} intensity={0.5} color="#FFE6D2" />
      <directionalLight position={[1.2, 2.2, 7]} intensity={0.7} color="#FFF1E4" />
      {/* sun from the upper left, as in the reference: lights the arch's inner wall */}
      <directionalLight position={[-4, 5, 2]} intensity={1.3} color="#FFE2B8" />

      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[layout.potPos.x, 0, layout.potPos.z]} receiveShadow>
        <planeGeometry args={[8, 8]} />
        <primitive object={shadowMat} attach="material" />
      </mesh>
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[layout.potPos.x, 0.001, layout.potPos.z]} material={rings}>
        <planeGeometry args={[2.6, 2.6]} />
      </mesh>

      <Suspense fallback={null}>
        <PottedPlant store={store} layout={layout} />
      </Suspense>
      <Suspense fallback={null}>
        <Portal store={store} layout={layout} />
      </Suspense>
    </>
  )
}

class SceneBoundary extends Component {
  state = { failed: false }
  static getDerivedStateFromError() { return { failed: true } }
  componentDidCatch() { this.props.store.failed = true }
  render() { return this.state.failed ? null : this.props.children }
}

export default function FieldIntro({ store, active = true }) {
  return (
    <Canvas
      className="intro__canvas"
      shadows="percentage"
      dpr={[1, 2]}   // full sharpness on 4K / retina screens
      frameloop={active ? 'always' : 'never'}
      camera={{ fov: FOV, near: 0.05, far: 90, position: CAM_POS.toArray() }}
      gl={{ alpha: true, antialias: true, powerPreference: 'high-performance', preserveDrawingBuffer: false }}
      onCreated={({ gl }) => {
        gl.setClearColor(0x000000, 0)
        gl.toneMapping = THREE.ACESFilmicToneMapping
        gl.toneMappingExposure = 1.0
      }}
    >
      <SceneBoundary store={store}>
        <Scene store={store} />
      </SceneBoundary>
    </Canvas>
  )
}

useGLTF.preload(POT_URL)
useGLTF.preload(PLANT_URL)
