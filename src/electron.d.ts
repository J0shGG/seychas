export {}

declare global {
  interface Window {
    seychasDesktop?: boolean
    seychas?: {
      restore: () => void
      setExpanded?: (expanded: boolean, height: number) => void
      setEdge?: (edge: 'top' | 'left' | 'right') => void
      setTheme?: (theme: 'lamp' | 'light' | 'dark' | 'ink') => void
      covered?: () => Promise<boolean>
      pointerInside?: () => Promise<boolean>
      beginDrag?: () => Promise<boolean>
      completeTask?: () => void
      onDragging?: (callback: (dragging: boolean) => void) => () => void
      onIslandEdge?: (
        callback: (edge: 'top' | 'left' | 'right', persist: boolean) => void,
      ) => () => void
      onCovered?: (callback: (covered: boolean) => void) => () => void
      onTaskDone?: (callback: (persist: boolean) => void) => () => void
      checkUpdate?: () => Promise<{ update: { version: string; current: string } | null }>
      installUpdate?: () => Promise<{ ok: boolean; message?: string }>
      onUpdateProgress?: (callback: (ratio: number) => void) => () => void
      presence?: () => Promise<number | null>
      onPresence?: (callback: (count: number) => void) => () => void
    }
  }
}
