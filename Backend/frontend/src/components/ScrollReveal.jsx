import { useLayoutEffect, useRef } from 'react'

let observer

function show(element, state = 'seen') {
  if (element.dataset.reveal !== 'waiting') return
  element.dataset.reveal = state
  observer.unobserve(element)
}

/** Keep the original semantic element and its layout while it enters view. */
export default function ScrollReveal({ as: Tag = 'section', children, ...props }) {
  const ref = useRef(null)

  useLayoutEffect(() => {
    const element = ref.current
    if (!window.IntersectionObserver) return
    observer ??= new IntersectionObserver((entries) => {
      for (const entry of entries) {
        if (entry.isIntersecting) show(entry.target)
      }
    })
    element.dataset.reveal = 'waiting'
    observer.observe(element)
    // Tabbing or following a link into a section should reveal it immediately.
    const onFocus = () => show(element, 'focused')
    element.addEventListener('focusin', onFocus, { once: true })
    return () => {
      observer.unobserve(element)
      element.removeEventListener('focusin', onFocus)
      delete element.dataset.reveal
    }
  }, [Tag])

  return <Tag ref={ref} {...props}>{children}</Tag>
}
