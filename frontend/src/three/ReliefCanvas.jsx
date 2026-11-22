import { useEffect, useRef, useState } from 'react'

import { prefersReducedMotion } from '../lib/usePageTitle'

/**
 * A picture turned into a three.js relief.
 *
 * The photo is mapped onto a finely divided plane, and a depth map pushes each
 * vertex toward the viewer, so the terrain is real geometry: it turns with the
 * pointer and lays back as the page scrolls, and near parts slide over far
 * ones. The camera is orthographic on purpose. With no perspective, the relief
 * at rest is pixel-for-pixel the photo underneath it, so the DOM pin and labels
 * placed over the photo stay exactly where they were; the depth only shows
 * once it tilts. It turns about `pivot`, which the caller sets to its pin.
 *
 * This sits on top of the ordinary <img> and fades in when it is ready. If
 * WebGL is missing, the context is lost, the reader prefers reduced motion, or
 * the screen is narrow, the canvas never appears and the photo is the page.
 * three.js is imported here, on demand and after a pause, so it is never part
 * of the first paint or the entry bundle.
 */
export default function ReliefCanvas({
  image, depth, pivot = [0.5, 0.5], fit = [0.5, 0.5], height = 0.075, scrollTilt = 0.3, pointerTilt = 0.045, delay = 600,
}) {
  const canvasRef = useRef(null)
  const [ready, setReady] = useState(false)

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas || prefersReducedMotion() || !window.WebGLRenderingContext) return
    const wide = window.matchMedia('(min-width: 900px)')
    if (!wide.matches) return

    let disposed = false
    let cleanup = () => {}
    const begin = window.setTimeout(async () => {
      let THREE
      try {
        THREE = await import('three')
      } catch {
        return
      }
      if (disposed) return

      const load = (url) => new Promise((resolve, reject) => new THREE.TextureLoader().load(url, resolve, undefined, reject))
      let map, depthMap, renderer
      try {
        [map, depthMap] = await Promise.all([load(image), load(depth)])
        renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true, powerPreference: 'low-power' })
      } catch {
        map?.dispose()
        depthMap?.dispose()
        return
      }
      if (disposed) {
        map.dispose(); depthMap.dispose(); renderer.dispose()
        return
      }

      // Colours pass straight through, so the relief matches the <img> under it exactly.
      renderer.outputColorSpace = THREE.LinearSRGBColorSpace
      renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.75))
      for (const texture of [map, depthMap]) {
        // Mirrored past the edge: when the plane lays back, what shows beyond
        // the photo is more paper and soil, not a stretched last pixel.
        texture.wrapS = texture.wrapT = THREE.MirroredRepeatWrapping
        texture.minFilter = THREE.LinearFilter
        texture.generateMipmaps = false
      }
      map.anisotropy = Math.min(4, renderer.capabilities.getMaxAnisotropy())

      const aspect = map.image.width / map.image.height
      const OVERSCAN = 1.5
      const geometry = new THREE.PlaneGeometry(aspect * OVERSCAN, OVERSCAN, 240, 136)
      const material = new THREE.ShaderMaterial({
        uniforms: { uMap: { value: map }, uDepth: { value: depthMap }, uHeight: { value: height }, uScale: { value: OVERSCAN } },
        vertexShader: `
          uniform sampler2D uDepth; uniform float uHeight; uniform float uScale;
          varying vec2 vUv;
          void main() {
            vUv = (uv - 0.5) * uScale + 0.5;
            vec3 p = position;
            p.z += texture2D(uDepth, vUv).r * uHeight;
            gl_Position = projectionMatrix * modelViewMatrix * vec4(p, 1.0);
          }`,
        fragmentShader: `
          uniform sampler2D uMap;
          varying vec2 vUv;
          void main() { gl_FragColor = texture2D(uMap, vUv); }`,
      })
      const mesh = new THREE.Mesh(geometry, material)
      // Turn about the pin: offset the mesh inside a group placed at the pivot.
      const px = (pivot[0] - 0.5) * aspect
      const py = 0.5 - pivot[1]
      const pz = height * 0.6
      mesh.position.set(-px, -py, -pz)
      const group = new THREE.Group()
      group.position.set(px, py, 0)
      group.add(mesh)
      const scene = new THREE.Scene()
      scene.add(group)
      const camera = new THREE.OrthographicCamera(-aspect / 2, aspect / 2, 0.5, -0.5, -10, 10)

      const root = canvas.closest('[data-relief-root]') ?? canvas.parentElement
      const target = { x: 0, y: 0 }
      const current = { x: 0, y: 0 }
      const pointer = { x: 0, y: 0 }
      let scroll = 0
      let frame = 0

      const resize = () => {
        const { clientWidth: w, clientHeight: h } = canvas
        if (!w || !h) return
        renderer.setSize(w, h, false)
        const view = w / h
        // Cover the canvas exactly as the photo under it does (object-fit: cover
        // at object-position `fit`), so the two line up at rest.
        let halfW = view / 2
        let halfH = 0.5
        let cx = 0
        let cy = 0
        if (view <= aspect) {
          cx = (fit[0] - 0.5) * (aspect - view)
        } else {
          halfW = aspect / 2
          halfH = aspect / view / 2
          cy = (0.5 - fit[1]) * (1 - aspect / view)
        }
        camera.left = cx - halfW
        camera.right = cx + halfW
        camera.top = cy + halfH
        camera.bottom = cy - halfH
        camera.updateProjectionMatrix()
        schedule()
      }

      const aim = () => {
        // Scrolling lays the terrain back (its far edge drops away); the pointer adds a small lean.
        target.x = -scroll * scrollTilt + pointer.y * pointerTilt * 0.7
        target.y = pointer.x * pointerTilt
        schedule()
      }

      const render = () => {
        frame = 0
        current.x += (target.x - current.x) * 0.1
        current.y += (target.y - current.y) * 0.1
        group.rotation.set(current.x, current.y, 0)
        renderer.render(scene, camera)
        if (!ready) setReady(true)
        if (Math.abs(target.x - current.x) + Math.abs(target.y - current.y) > 0.0004) schedule()
      }
      // Draws only while something is still moving; an idle page costs nothing.
      const schedule = () => {
        if (!frame && !disposed && !document.hidden) frame = requestAnimationFrame(render)
      }

      const onScroll = () => {
        const box = root.getBoundingClientRect()
        const lead = Math.min(box.top + window.scrollY, window.innerHeight)
        scroll = Math.min(1, Math.max(0, 1 - box.bottom / (box.height + lead)))
        if (box.bottom > 0 && box.top < window.innerHeight) aim()
      }
      const onPointer = (e) => {
        if (e.pointerType && e.pointerType !== 'mouse') return
        const box = root.getBoundingClientRect()
        if (box.bottom <= 0 || box.top >= window.innerHeight) return
        pointer.x = Math.max(-1, Math.min(1, ((e.clientX - box.left) / box.width) * 2 - 1))
        pointer.y = Math.max(-1, Math.min(1, ((e.clientY - box.top) / box.height) * 2 - 1))
        aim()
      }
      const onLeave = () => {
        pointer.x = 0
        pointer.y = 0
        aim()
      }
      const onLost = (e) => {
        e.preventDefault()
        setReady(false)     // the photo underneath is the page again
      }
      const onNarrow = () => {
        if (!wide.matches) setReady(false)
        else schedule()
      }

      const observer = new ResizeObserver(resize)
      observer.observe(canvas)
      window.addEventListener('scroll', onScroll, { passive: true })
      window.addEventListener('pointermove', onPointer, { passive: true })
      document.documentElement.addEventListener('pointerleave', onLeave)
      document.addEventListener('visibilitychange', schedule)
      canvas.addEventListener('webglcontextlost', onLost)
      wide.addEventListener('change', onNarrow)
      resize()
      onScroll()
      schedule()

      cleanup = () => {
        if (frame) cancelAnimationFrame(frame)
        observer.disconnect()
        window.removeEventListener('scroll', onScroll)
        window.removeEventListener('pointermove', onPointer)
        document.documentElement.removeEventListener('pointerleave', onLeave)
        document.removeEventListener('visibilitychange', schedule)
        canvas.removeEventListener('webglcontextlost', onLost)
        wide.removeEventListener('change', onNarrow)
        geometry.dispose()
        material.dispose()
        map.dispose()
        depthMap.dispose()
        renderer.dispose()
      }
    }, delay)

    return () => {
      disposed = true
      window.clearTimeout(begin)
      cleanup()
    }
    // `ready` is deliberately not a dependency: it is this effect's own output.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [image, depth, height, scrollTilt, pointerTilt, delay, pivot[0], pivot[1], fit[0], fit[1]])

  return <canvas ref={canvasRef} className={ready ? 'relief is-ready' : 'relief'} aria-hidden="true" />
}
