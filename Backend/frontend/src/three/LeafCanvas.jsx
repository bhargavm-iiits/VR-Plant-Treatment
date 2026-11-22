import { useEffect, useRef, useState } from 'react'
import * as THREE from 'three'
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js'
import { RoomEnvironment } from 'three/examples/jsm/environments/RoomEnvironment.js'
import { DRACOLoader } from 'three/examples/jsm/loaders/DRACOLoader.js'
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js'

import { ChevronLeft, ChevronRight, Minus, Plus, Recenter } from '../components/Icons'
import { prefersReducedMotion } from '../lib/usePageTitle'

const TURN = Math.PI / 8      // one press of a turn button
const ZOOM = 0.82             // one press of a zoom button
const FOV = 32

/**
 * The 3D leaf: one model, turned with a drag or with the buttons under it.
 *
 * Dragging is one way in, never the only one. Every movement has a button, so
 * a keyboard, a switch or a tap does everything a mouse can. The wheel only
 * zooms once the viewer has been clicked (or with Ctrl held); until then it
 * scrolls the page as usual, so the viewer never traps a scroll.
 *
 * The renderer draws only while something is moving: during a drag, while the
 * leaf turns by itself, and for a moment after. A still leaf costs nothing.
 * It turns by itself until the first touch, and not at all for a reader who
 * asked for reduced motion.
 *
 * Loading and failure are reported through `onState`; the shell around this
 * keeps the leaf's picture on screen for both.
 */
export default function LeafCanvas({ src, label, onState, t }) {
  const canvasRef = useRef(null)
  const stage = useRef(null)
  const [spin, setSpin] = useState(() => !prefersReducedMotion())
  const spinRef = useRef(spin)
  spinRef.current = spin

  // The renderer, camera and controls: made once, reused for every leaf.
  useEffect(() => {
    const canvas = canvasRef.current
    let renderer
    try {
      renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true, powerPreference: 'low-power' })
    } catch {
      onState('failed')
      return
    }
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2))
    renderer.outputColorSpace = THREE.SRGBColorSpace
    renderer.toneMapping = THREE.ACESFilmicToneMapping
    renderer.toneMappingExposure = 1.05

    const scene = new THREE.Scene()
    const pmrem = new THREE.PMREMGenerator(renderer)
    const room = new RoomEnvironment()
    const environment = pmrem.fromScene(room, 0.04).texture
    scene.environment = environment
    const key = new THREE.DirectionalLight(0xfff1dc, 1.6)   // warm afternoon light, as on the rest of the site
    key.position.set(1.5, 2.2, 2.4)
    scene.add(key)

    const camera = new THREE.PerspectiveCamera(FOV, 1, 0.01, 50)
    camera.position.set(0, 0, 3)

    // Registered before the controls, so it is asked first: the wheel belongs
    // to the page until the reader has clicked into the viewer.
    let engaged = false
    const onWheel = (e) => {
      if (!engaged && !e.ctrlKey) e.stopImmediatePropagation()
    }
    canvas.addEventListener('wheel', onWheel, { passive: true })

    const controls = new OrbitControls(camera, canvas)
    controls.enablePan = false
    controls.enableDamping = true
    controls.dampingFactor = 0.08
    controls.rotateSpeed = 0.9
    controls.autoRotateSpeed = 1.6
    // A vertical swipe scrolls the page; a sideways drag turns the leaf.
    canvas.style.touchAction = 'pan-y'

    let frame = 0
    let activeUntil = 0
    let disposed = false
    const draw = (now) => {
      frame = 0
      if (disposed) return
      controls.autoRotate = spinRef.current
      controls.update()
      renderer.render(scene, camera)
      if (spinRef.current || now < activeUntil) schedule()
    }
    const schedule = () => {
      if (!frame && !disposed && !document.hidden) frame = requestAnimationFrame(draw)
    }
    const kick = (ms = 400) => {
      activeUntil = performance.now() + ms
      schedule()
    }

    const onStart = () => {
      engaged = true
      setSpin(false)        // the reader has taken hold of it
      kick(1500)
    }
    const onChange = () => kick()
    const onLeave = () => { engaged = false }
    const onLost = (e) => {
      e.preventDefault()
      onState('failed')
    }
    controls.addEventListener('start', onStart)
    controls.addEventListener('change', onChange)
    canvas.addEventListener('pointerleave', onLeave)
    canvas.addEventListener('webglcontextlost', onLost)
    document.addEventListener('visibilitychange', schedule)

    const resize = () => {
      const { clientWidth: w, clientHeight: h } = canvas
      if (!w || !h) return
      renderer.setSize(w, h, false)
      camera.aspect = w / h
      camera.updateProjectionMatrix()
      stage.current?.frameModel?.()
      kick()
    }
    const observer = new ResizeObserver(resize)
    observer.observe(canvas)

    stage.current = { renderer, scene, camera, controls, kick, model: null, home: null, frameModel: null }
    resize()

    return () => {
      disposed = true
      if (frame) cancelAnimationFrame(frame)
      observer.disconnect()
      controls.removeEventListener('start', onStart)
      controls.removeEventListener('change', onChange)
      canvas.removeEventListener('wheel', onWheel)
      canvas.removeEventListener('pointerleave', onLeave)
      canvas.removeEventListener('webglcontextlost', onLost)
      document.removeEventListener('visibilitychange', schedule)
      controls.dispose()
      disposeModel(stage.current?.model)
      environment.dispose()
      room.dispose?.()
      pmrem.dispose()
      renderer.dispose()
      stage.current = null
    }
    // Set up once. `onState` and `t` are read through the latest render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // The leaf itself: loaded whenever the source changes, the old one freed.
  useEffect(() => {
    const s = stage.current
    if (!s) return
    let alive = true
    onState('loading')
    const draco = new DRACOLoader().setDecoderPath(`${import.meta.env.BASE_URL}draco/`)
    const loader = new GLTFLoader().setDRACOLoader(draco)
    loader.load(
      src,
      (gltf) => {
        if (!alive || !stage.current) {
          disposeModel(gltf.scene)
          return
        }
        const model = gltf.scene
        // Centre it on the origin, so it turns about its own middle.
        const box = new THREE.Box3().setFromObject(model)
        const centre = box.getCenter(new THREE.Vector3())
        model.position.sub(centre)
        const size = box.getSize(new THREE.Vector3())
        model.traverse((node) => {
          if (!node.isMesh) return
          node.material.side = THREE.DoubleSide
          for (const map of [node.material.map, node.material.normalMap]) {
            if (map) map.anisotropy = Math.min(8, s.renderer.capabilities.getMaxAnisotropy())
          }
        })

        disposeModel(s.model)
        s.scene.remove(s.model)
        s.model = model
        s.scene.add(model)

        // Far enough back that the whole leaf fits, whichever way the frame is shaped.
        s.frameModel = () => {
          const halfFov = THREE.MathUtils.degToRad(FOV / 2)
          const fitHeight = (size.y / 2) / Math.tan(halfFov)
          const fitWidth = (Math.max(size.x, size.z) / 2) / (Math.tan(halfFov) * s.camera.aspect)
          const distance = Math.max(fitHeight, fitWidth) * 1.22
          s.controls.minDistance = distance * 0.35
          s.controls.maxDistance = distance * 2.2
          s.home = distance
        }
        s.frameModel()
        s.controls.target.set(0, 0, 0)
        s.camera.position.set(0, 0, s.home)
        s.controls.update()
        s.kick(600)
        onState('ready')
      },
      undefined,
      () => {
        if (alive) onState('failed')
      },
    )
    return () => {
      alive = false
      draco.dispose()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [src])

  // The spin toggle starts or stops the loop.
  useEffect(() => {
    stage.current?.kick(300)
  }, [spin])

  const turn = (angle) => {
    const s = stage.current
    if (!s) return
    setSpin(false)
    s.camera.position.sub(s.controls.target).applyAxisAngle(new THREE.Vector3(0, 1, 0), angle).add(s.controls.target)
    s.controls.update()
    s.kick()
  }
  const zoom = (factor) => {
    const s = stage.current
    if (!s) return
    const offset = s.camera.position.clone().sub(s.controls.target)
    const length = THREE.MathUtils.clamp(offset.length() * factor, s.controls.minDistance, s.controls.maxDistance)
    s.camera.position.copy(s.controls.target).add(offset.setLength(length))
    s.controls.update()
    s.kick()
  }
  const reset = () => {
    const s = stage.current
    if (!s || !s.home) return
    setSpin(false)
    s.camera.position.set(0, 0, s.home)
    s.controls.target.set(0, 0, 0)
    s.controls.update()
    s.kick()
  }

  return (
    <>
      <canvas ref={canvasRef} className="leafview__canvas" role="img" aria-label={label} />
      <div className="leafview__bar" role="group" aria-label={t('leaf.controls')}>
        <button type="button" className="iconbtn" aria-label={t('leaf.turnLeft')} onClick={() => turn(-TURN)}>
          <ChevronLeft size={22} />
        </button>
        <button type="button" className="iconbtn" aria-label={t('leaf.turnRight')} onClick={() => turn(TURN)}>
          <ChevronRight size={22} />
        </button>
        <button type="button" className="iconbtn" aria-label={t('home.zoomIn')} onClick={() => zoom(ZOOM)}>
          <Plus size={22} />
        </button>
        <button type="button" className="iconbtn" aria-label={t('home.zoomOut')} onClick={() => zoom(1 / ZOOM)}>
          <Minus size={22} />
        </button>
        <button type="button" className="iconbtn" aria-label={t('field.recenter')} onClick={reset}>
          <Recenter size={22} />
        </button>
        <button type="button" className="leafview__spin" aria-pressed={spin} onClick={() => setSpin((on) => !on)}>
          {t('leaf.spin')}
        </button>
      </div>
    </>
  )
}

function disposeModel(model) {
  model?.traverse?.((node) => {
    if (!node.isMesh) return
    node.geometry?.dispose()
    for (const material of [].concat(node.material)) {
      for (const value of Object.values(material)) {
        if (value?.isTexture) value.dispose()
      }
      material.dispose()
    }
  })
}
