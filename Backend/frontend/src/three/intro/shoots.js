import * as THREE from 'three'

/**
 * Rig the scanned plant (Poly Haven potted_plant_02) for growth.
 *
 * Its foliage is one mesh made of separate pieces: stems that start at the
 * soil centre, and leaf blades whose base sits on a stem's tip. Here those
 * pieces are found (connected components, welded across UV seams), each blade
 * is attached to the stem it grows from, and every stem + its blades becomes a
 * "shoot" with its own pivot at the soil. Shoots then emerge on separate,
 * staggered curves -- central pair first, side leaves later -- instead of the
 * whole plant scaling as one block.
 *
 * Input geometry is in the model's own space (node transform applied).
 * Output pivots and geometry stay in that space.
 */
export function rigShoots(sourceMesh, soilY) {
  sourceMesh.updateWorldMatrix(true, false)
  const geo = sourceMesh.geometry.clone()
  geo.applyMatrix4(new THREE.Matrix4().makeTranslation(...sourceMesh.position.toArray()))
  const pos = geo.attributes.position
  const n = pos.count
  const index = geo.index ? geo.index.array : Array.from({ length: n }, (_, i) => i)

  // union-find over triangles, then weld vertices that share a position
  const parent = new Int32Array(n).map((_, i) => i)
  const find = (a) => {
    while (parent[a] !== a) { parent[a] = parent[parent[a]]; a = parent[a] }
    return a
  }
  const union = (a, b) => { const ra = find(a); const rb = find(b); if (ra !== rb) parent[ra] = rb }
  for (let i = 0; i < index.length; i += 3) { union(index[i], index[i + 1]); union(index[i + 1], index[i + 2]) }
  const seen = new Map()
  for (let i = 0; i < n; i++) {
    const k = `${pos.getX(i).toFixed(5)},${pos.getY(i).toFixed(5)},${pos.getZ(i).toFixed(5)}`
    if (seen.has(k)) union(i, seen.get(k))
    else seen.set(k, i)
  }

  // gather components
  const comps = new Map()
  for (let i = 0; i < n; i++) {
    const r = find(i)
    if (!comps.has(r)) comps.set(r, { verts: [], tris: [] })
    comps.get(r).verts.push(i)
  }
  for (let i = 0; i < index.length; i += 3) comps.get(find(index[i])).tris.push(index[i], index[i + 1], index[i + 2])

  const v = new THREE.Vector3()
  const pieces = [...comps.values()].map((c) => {
    let base = null
    let lowY = Infinity
    const centroid = new THREE.Vector3()
    for (const i of c.verts) {
      v.fromBufferAttribute(pos, i)
      centroid.add(v)
      if (v.y < lowY) { lowY = v.y; base = v.clone() }
    }
    centroid.divideScalar(c.verts.length)
    return { ...c, base, centroid }
  })

  // stems: start low, near the soil centre
  const isStem = (p) => p.base.y < soilY + 0.07 && Math.hypot(p.base.x, p.base.z) < 0.1
  const stems = pieces.filter(isStem)
  const blades = pieces.filter((p) => !isStem(p))
  for (const s of stems) {
    s.blades = []
    let far = 0
    for (const i of s.verts) {
      v.fromBufferAttribute(pos, i)
      const d = v.distanceTo(s.base)
      if (d > far) { far = d; s.tip = v.clone() }
    }
  }
  if (!stems.length) return []

  // each blade grows from the stem whose tip is nearest its base
  for (const b of blades) {
    let best = stems[0]
    let bestD = Infinity
    for (const s of stems) {
      const d = s.tip.distanceTo(b.base)
      if (d < bestD) { bestD = d; best = s }
    }
    best.blades.push(b)
  }

  // build one geometry per shoot, pivoted at its stem base
  const attrs = Object.keys(geo.attributes)
  const shoots = stems.map((s) => {
    const members = [s, ...s.blades]
    const map = new Map()
    const out = {}
    for (const name of attrs) out[name] = []
    const idx = []
    const centroid = new THREE.Vector3()
    let count = 0
    for (const m of members) {
      for (let k = 0; k < m.tris.length; k++) {
        const vi = m.tris[k]
        if (!map.has(vi)) {
          map.set(vi, map.size)
          for (const name of attrs) {
            const a = geo.attributes[name]
            for (let c = 0; c < a.itemSize; c++) out[name].push(a.array[vi * a.itemSize + c])
          }
          v.fromBufferAttribute(pos, vi)
          centroid.add(v)
          count++
        }
        idx.push(map.get(vi))
      }
    }
    centroid.divideScalar(Math.max(1, count))
    const g = new THREE.BufferGeometry()
    for (const name of attrs) {
      const a = geo.attributes[name]
      g.setAttribute(name, new THREE.Float32BufferAttribute(out[name], a.itemSize, a.normalized))
    }
    g.setIndex(idx)
    g.translate(-s.base.x, -s.base.y, -s.base.z)
    g.computeBoundingSphere()
    const outward = new THREE.Vector3(centroid.x - s.base.x, 0, centroid.z - s.base.z)
    const radial = outward.length()
    outward.normalize()
    return { geometry: g, pivot: s.base.clone(), centroid, radial, height: centroid.y - s.base.y, axis: new THREE.Vector3(-outward.z, 0, outward.x) }
  })

  // Order: the most upright, central shoots first (the reference's first
  // leaf pair), then the rest outwards (side leaves unfurl late).
  shoots.sort((a, b) => (a.radial - a.height * 0.6) - (b.radial - b.height * 0.6))
  const n2 = shoots.length
  shoots.forEach((s, i) => {
    if (i < 2) {
      s.start = 0.02 + i * 0.06   // first tip ≈ 1.0 s, its partner just after
      s.span = 0.55
    } else {
      // reference 2.4 s (47%): still just the slim stem and first pair;
      // the side leaves unfurl between ≈2.6 s and 4.2 s
      s.start = 0.46 + ((i - 2) / Math.max(1, n2 - 3)) * 0.4
      s.span = 0.24
    }
  })
  return shoots
}

/** A small, flat-ish blade for drifting leaves: the n smallest pieces' geometry. */
export function bladeGeometries(sourceMesh, soilY, count = 4) {
  const shoots = rigShoots(sourceMesh, soilY)
  return shoots
    .map((s) => s.geometry)
    .sort((a, b) => a.boundingSphere.radius - b.boundingSphere.radius)
    .slice(0, count)
}
