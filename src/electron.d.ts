export {}

declare global {
  interface Window {
    seychasDesktop?: boolean
    seychas?: {
      restore: () => void
      setExpanded?: (expanded: boolean, height: number) => void
      covered?: () => Promise<boolean>
      onCovered?: (callback: (covered: boolean) => void) => () => void
    }
  }
}
