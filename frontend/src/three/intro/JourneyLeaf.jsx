import { useEffect, useLayoutEffect, useMemo, useRef } from 'react'
import { useFrame, useLoader, useThree } from '@react-three/fiber'
import * as THREE from 'three'

import leafMaterial from '../../assets/art/journey-leaf-material.webp'

const LEAF_ASPECT = 1183 / 640
const smooth = (start, end, value) => THREE.MathUtils.smoothstep(value, start, end)

function makeBlade() {
  const geometry = new THREE.PlaneGeometry(LEAF_ASPECT, 1, 48, 28)
  const positions = geometry.getAttribute('position')

  for (let index = 0; index < positions.count; index += 1) {
    const along = positions.getX(index) / LEAF_ASPECT + 0.5
    const across = positions.getY(index) * 2
    const blade = Math.sin(Math.PI * along)
    // Keep the midrib proud, roll the margins back, and curl the blade up at the tip.
    const midrib = 0.09 * blade * (1 - across * across)
    const tip = 0.13 * (1 - along) ** 4
    const margin = 0.035 * blade * across * Math.sin(along * 2 * Math.PI)
    positions.setZ(index, midrib + tip + margin)
  }

  geometry.computeVertexNormals()
  geometry.computeBoundingSphere()
  return geometry
}

export default function JourneyLeaf({ store }) {
  const object = useRef(null)
  const camera = useThree((state) => state.camera)
  const gl = useThree((state) => state.gl)
  const texture = useLoader(THREE.TextureLoader, leafMaterial)
  const geometry = useMemo(makeBlade, [])
  const roll = useMemo(() => new THREE.Euler(0, 0, 0, 'ZXY'), [])
  const rotation = useMemo(() => new THREE.Quaternion(), [])

  useLayoutEffect(() => {
    texture.colorSpace = THREE.SRGBColorSpace
    texture.anisotropy = Math.min(8, gl.capabilities.getMaxAnisotropy())
    texture.needsUpdate = true
  }, [gl, texture])

  useEffect(() => () => geometry.dispose(), [geometry])

  useFrame(() => {
    const group = object.current
    const progress = store.journeyView ?? 0
    group.visible = progress > 0.55
    if (!group.visible) return

    const approach = smooth(0.55, 0.72, progress)
    const settle = smooth(0.64, 0.84, progress)
    const depth = THREE.MathUtils.lerp(3.3, 1.8, approach)
    const height = 2 * depth * Math.tan(THREE.MathUtils.degToRad(camera.fov / 2))
    const width = height * camera.aspect
    const narrow = camera.aspect < 0.9

    const screenX = (narrow ? -0.06 : -0.26) * settle
    const screenY = THREE.MathUtils.lerp(-0.18, narrow ? 0.17 : -0.03, approach)
    group.position.set(screenX * width / 2, screenY * height / 2, -depth)
    group.position.applyQuaternion(camera.quaternion).add(camera.position)

    roll.set(
      THREE.MathUtils.lerp(0.85, -0.12, approach),
      THREE.MathUtils.lerp(1.26, 0.09, approach),
      THREE.MathUtils.lerp(0.19, -0.63, approach),
    )
    group.quaternion.copy(camera.quaternion).multiply(rotation.setFromEuler(roll))

    const finalSpan = Math.min(height * 1.17, width * (narrow ? 0.99 : 0.8))
    const span = finalSpan * THREE.MathUtils.lerp(0.047, 1.03 - 0.03 * settle, approach)
    group.scale.setScalar(span / LEAF_ASPECT)
  })

  return (
    <group ref={object} visible={false}>
      <mesh>
        <primitive object={geometry} attach="geometry" />
        <meshPhysicalMaterial
          map={texture}
          bumpMap={texture}
          bumpScale={0.013}
          alphaTest={0.37}
          alphaToCoverage
          side={THREE.DoubleSide}
          emissiveMap={texture}
          emissive="#c5bd99"
          emissiveIntensity={0.26}
          roughness={0.63}
          clearcoat={0.17}
          clearcoatRoughness={0.52}
          specularIntensity={0.45}
          fog={false}
        />
      </mesh>
    </group>
  )
}
