// One shared layer keeps the leaf positions continuous during navigation.
// 21 leaves on desktop, 11 on phones (the rest are hidden in CSS).
const LEAVES = Array.from({ length: 21 })

export default function FallingLeaves() {
  return (
    <div className="falling-leaves" aria-hidden="true">
      {LEAVES.map((_, i) => <span key={i} />)}
    </div>
  )
}
