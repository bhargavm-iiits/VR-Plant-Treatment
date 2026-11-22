const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

/**
 * For a dialog's onKeyDown: Tab and Shift+Tab stay inside it.
 *
 * A dialog that covers the page and says aria-modal must also keep the
 * keyboard, or Tab walks into controls nobody can see. The dialogs here hold
 * one or two controls, so wrapping at the ends is all it takes.
 */
export function keepFocusInside(event) {
  if (event.key !== 'Tab') return
  const items = [...event.currentTarget.querySelectorAll(FOCUSABLE)].filter((el) => el.offsetParent !== null)
  if (items.length === 0) {
    event.preventDefault()
    return
  }
  const first = items[0]
  const last = items[items.length - 1]
  const active = document.activeElement
  if (event.shiftKey && (active === first || !event.currentTarget.contains(active))) {
    event.preventDefault()
    last.focus()
  } else if (!event.shiftKey && (active === last || !event.currentTarget.contains(active))) {
    event.preventDefault()
    first.focus()
  }
}
