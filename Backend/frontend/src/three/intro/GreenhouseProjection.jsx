import { useEffect, useMemo } from 'react'
import * as THREE from 'three'

const IMAGE_ASPECT = 1592 / 1080
const VANISHING_X = 0.548

function readDisparity(image) {
  const canvas = document.createElement('canvas')
  canvas.width = image.width
  canvas.height = image.height
  const context = canvas.getContext('2d', { willReadFrequently: true })
  context.drawImage(image, 0, 0)
  return context.getImageData(0, 0, image.width, image.height).data
}

function makeSurface(camera, image, disparity) {
  const region = [-0.12, -0.12, 1.12, 1.24]
  const [left, top, right, bottom] = region
  const surface = new THREE.PlaneGeometry(1, 1, 185, 138)
  const positions = surface.getAttribute('position')
  const uv = surface.getAttribute('uv')
  const halfHeight = Math.tan(THREE.MathUtils.degToRad(camera.fov / 2))
  const imageHeight = Math.max(1, camera.aspect / IMAGE_ASPECT) * 1.11
  const imageWidth = imageHeight * IMAGE_ASPECT

  for (let i = 0; i < uv.count; i++) {
    const x = THREE.MathUtils.lerp(left, right, uv.getX(i))
    const y = THREE.MathUtils.lerp(top, bottom, 1 - uv.getY(i))
    // At the threshold the camera can see slightly below the source image.
    // Widen the paving along the ground rays and keep sampling its nearest edge.
    const below = Math.max(0, y - 1)
    const sampleX = THREE.MathUtils.clamp(VANISHING_X + (x - VANISHING_X) / (1 + 1.8 * below), 0, 1)
    const onPath = THREE.MathUtils.smoothstep(sampleX, 0.40, 0.445)
      * (1 - THREE.MathUtils.smoothstep(sampleX, 0.67, 0.71))
    const sampleY = THREE.MathUtils.clamp(y, 0, 1) - below * (0.09 + 0.38 * onPath)
    const column = Math.round(sampleX * (image.width - 1))
    const row = Math.round(sampleY * (image.height - 1))
    const proximity = disparity[4 * (column + row * image.width)] / 255
    const depth = 1 / THREE.MathUtils.lerp(1 / 15, 1 / 3.85, proximity ** 0.82)
    positions.setXYZ(i,
      depth * halfHeight * imageWidth * (x - VANISHING_X) * 2,
      depth * halfHeight * imageHeight * (0.564 - y) * 2,
      -depth,
    )
    uv.setXY(i, sampleX, 1 - sampleY)
  }
  surface.computeBoundingSphere()
  return surface
}

export default function GreenhouseProjection({ camera, depth, photo }) {
  const surface = useMemo(() => {
    const disparity = readDisparity(depth.image)
    return makeSurface(camera, depth.image, disparity)
  }, [camera, depth])

  useEffect(() => () => surface.dispose(), [surface])

  return (
    <mesh position={camera.position} quaternion={camera.quaternion} geometry={surface}>
      <meshBasicMaterial map={photo} toneMapped={false} />
    </mesh>
  )
}
