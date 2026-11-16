export type AmbientKind = 'off' | 'rain' | 'fire' | 'cafe'

export type IslandEdge = 'top' | 'left' | 'right'

export type ThemeId = 'lamp' | 'light' | 'dark' | 'ink'

export type Task = {
  id: string
  title: string
  done: boolean
  createdAt: number
  completedAt?: number
  focusedMs: number
}

export type Thought = {
  id: string
  text: string
  createdAt: number
}

export type SessionKind = 'focus' | 'break'

export type SessionStatus = 'running' | 'paused' | 'done'

export type Session = {
  id: string
  taskId: string | null
  kind: SessionKind
  durationMs: number
  startedAt: number
  elapsedMs: number
  runningSince: number | null
  status: SessionStatus
}

export type DayStat = {
  date: string
  focusedMs: number
  sessions: number
  completedTasks: number
}

export type Settings = {
  focusMin: number
  shortBreakMin: number
  longBreakMin: number
  cyclesUntilLong: number
  chime: boolean
  ambient: AmbientKind
  notify: boolean
  islandEdge: IslandEdge
  theme: ThemeId
}

export type State = {
  onboarded: boolean
  tasks: Task[]
  activeTaskId: string | null
  session: Session | null
  thoughts: Thought[]
  intention: string
  intentionDate: string
  settings: Settings
  history: DayStat[]
  cyclesToday: number
  cyclesDate: string
}

export type Action =
  | { type: 'COMPLETE_ONBOARDING' }
  | { type: 'ADD_TASK'; title: string }
  | { type: 'REMOVE_TASK'; id: string }
  | { type: 'TOGGLE_DONE'; id: string }
  | { type: 'SELECT_TASK'; id: string | null }
  | { type: 'SET_INTENTION'; value: string }
  | { type: 'START_SESSION'; kind: SessionKind }
  | { type: 'PAUSE' }
  | { type: 'RESUME' }
  | { type: 'STOP' }
  | { type: 'SESSION_FINISHED' }
  | { type: 'ACK_DONE' }
  | { type: 'ADD_THOUGHT'; text: string }
  | { type: 'PROMOTE_THOUGHT'; id: string }
  | { type: 'DISMISS_THOUGHT'; id: string }
  | { type: 'UPDATE_SETTINGS'; patch: Partial<Settings> }
