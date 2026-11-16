import type { IslandEdge, ThemeId } from './types'

export function applyTheme(theme: ThemeId | undefined) {
  const next = theme === 'light' || theme === 'dark' || theme === 'ink' ? theme : 'lamp'
  const root = document.documentElement
  if (next === 'lamp') delete root.dataset.theme
  else root.dataset.theme = next
  window.seychas?.setTheme?.(next)
}

export function applyEdge(edge: IslandEdge | undefined) {
  const next = edge === 'left' || edge === 'right' ? edge : 'top'
  window.seychas?.setEdge?.(next)
}
