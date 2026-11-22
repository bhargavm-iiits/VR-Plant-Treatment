import { lazy, Suspense } from 'react'

// The relief and three.js load on demand, after the page has painted. Until
// then (and if the chunk fails to load) nothing renders here, and the ordinary
// photo underneath is the page. See ReliefCanvas for how it works.
const ReliefCanvas = lazy(() => import('./ReliefCanvas').catch(() => ({ default: () => null })))

export default function ReliefScene(props) {
  return (
    <Suspense fallback={null}>
      <ReliefCanvas {...props} />
    </Suspense>
  )
}
