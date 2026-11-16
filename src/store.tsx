import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useReducer,
  useRef,
  useState,
  type Dispatch,
  type ReactNode,
} from 'react'
import lampIcon from './assets/icon.jpg'
import { playChime, setAmbient, setVolume } from './audio'
import { todayKey, uid, yesterdayKey } from './format'
import type { Action, DayStat, IslandEdge, Session, Settings, State, Task, ThemeId } from './types'

export const STORAGE_KEY = 'seychas.v1'
const MIN_CREDIT_MS = 30_000

const defaultSettings: Settings = {
  focusMin: 25,
  shortBreakMin: 5,
  longBreakMin: 15,
  cyclesUntilLong: 4,
  chime: true,
  ambient: 'off',
  volume: 1,
  notify: false,
  islandEdge: 'top',
  theme: 'lamp',
}

const ISLAND_EDGES: IslandEdge[] = ['top', 'left', 'right']
const THEMES: ThemeId[] = ['lamp', 'light', 'dark', 'ink']

function cleanSettings(raw: Partial<Settings> | undefined): Settings {
  const settings = { ...defaultSettings, ...raw }
  if (!ISLAND_EDGES.includes(settings.islandEdge)) settings.islandEdge = 'top'
  if (!THEMES.includes(settings.theme)) settings.theme = 'lamp'
  const level = Number(settings.volume)
  settings.volume = Number.isFinite(level) ? Math.min(1, Math.max(0, level)) : 1
  const focus = Math.round(Number(settings.focusMin))
  settings.focusMin = Number.isFinite(focus) ? Math.min(180, Math.max(1, focus)) : 25
  return settings
}

const emptyState: State = {
  onboarded: false,
  tasks: [],
  activeTaskId: null,
  session: null,
  thoughts: [],
  intention: '',
  intentionDate: todayKey(),
  settings: defaultSettings,
  history: [],
  cyclesToday: 0,
  cyclesDate: todayKey(),
}

export function getElapsed(session: Session, now = Date.now()): number {
  if (session.status === 'done') return Math.min(session.elapsedMs, session.durationMs)
  const extra = session.runningSince ? Math.max(0, now - session.runningSince) : 0
  return session.elapsedMs + extra
}

function creditDay(history: DayStat[], date: string, focusedMs: number, sessions: number, completedTasks = 0): DayStat[] {
  const next = [...history]
  const i = next.findIndex((d) => d.date === date)
  if (i === -1) {
    next.push({ date, focusedMs, sessions, completedTasks })
  } else {
    next[i] = {
      ...next[i],
      focusedMs: next[i].focusedMs + focusedMs,
      sessions: next[i].sessions + sessions,
      completedTasks: next[i].completedTasks + completedTasks,
    }
  }
  return next
}

function applyFocusCredit(state: State, elapsedMs: number): State {
  if (elapsedMs < MIN_CREDIT_MS) return state
  const date = todayKey()
  const tasks = state.tasks.map((t) =>
    t.id === state.session?.taskId ? { ...t, focusedMs: t.focusedMs + elapsedMs } : t,
  )
  return {
    ...state,
    tasks,
    history: creditDay(state.history, date, elapsedMs, 1),
  }
}

function finishSession(state: State, now: number): State {
  const session = state.session
  if (!session || session.status === 'done') return state
  const elapsed = Math.min(getElapsed(session, now), session.durationMs)
  const done: Session = {
    ...session,
    elapsedMs: elapsed,
    runningSince: null,
    status: 'done',
  }
  let next: State = { ...state, session: done }
  if (session.kind === 'focus') {
    next = applyFocusCredit(next, elapsed)
    const date = todayKey()
    const cycles = next.cyclesDate === date ? next.cyclesToday + 1 : 1
    next = { ...next, cyclesToday: cycles, cyclesDate: date }
  }
  return next
}

function currentTaskId(state: State): string | null {
  const active = state.tasks.find((item) => item.id === state.activeTaskId)
  if (active && !active.done) return active.id
  const session = state.session
  if (session?.kind === 'focus' && session.taskId) {
    const task = state.tasks.find((item) => item.id === session.taskId)
    if (task && !task.done) return task.id
  }
  return null
}

function nextOpenTaskId(tasks: Task[], currentId: string): string | null {
  const index = tasks.findIndex((item) => item.id === currentId)
  for (let i = index + 1; i < tasks.length; i += 1) {
    if (!tasks[i].done) return tasks[i].id
  }
  for (let i = 0; i < index; i += 1) {
    if (!tasks[i].done) return tasks[i].id
  }
  return null
}

export function completeCurrentTask(state: State, now = Date.now()): State {
  const currentId = currentTaskId(state)
  if (!currentId) return state
  const nextId = nextOpenTaskId(state.tasks, currentId)
  let tasks = state.tasks.map((item) =>
    item.id === currentId ? { ...item, done: true, completedAt: now } : item,
  )
  let history = creditDay(state.history, todayKey(), 0, 0, 1)
  let session = state.session
  if (session?.kind === 'focus' && session.taskId === currentId && session.status !== 'done') {
    const elapsed = getElapsed(session, now)
    if (elapsed >= MIN_CREDIT_MS) {
      tasks = tasks.map((item) => (item.id === currentId ? { ...item, focusedMs: item.focusedMs + elapsed } : item))
      history = creditDay(history, todayKey(), elapsed, 0, 0)
      session = { ...session, taskId: nextId, elapsedMs: 0, runningSince: session.status === 'running' ? now : null }
    } else {
      session = { ...session, taskId: nextId }
    }
  }
  return { ...state, tasks, history, activeTaskId: nextId, session }
}

function stopSession(state: State, now: number): State {
  const session = state.session
  if (!session) return state
  const elapsed = getElapsed(session, now)
  let next: State = { ...state, session: null }
  if (session.kind === 'focus' && session.status !== 'done') {
    next = applyFocusCredit({ ...next, session }, elapsed)
    next = { ...next, session: null }
  }
  return next
}

function hydrate(raw: State): State {
  const date = todayKey()
  let state: State = {
    ...emptyState,
    ...raw,
    settings: cleanSettings(raw.settings),
    tasks: raw.tasks ?? [],
    thoughts: raw.thoughts ?? [],
    history: raw.history ?? [],
  }
  if (state.intentionDate !== date) {
    state = { ...state, intention: '', intentionDate: date }
  }
  if (state.cyclesDate !== date) {
    state = { ...state, cyclesToday: 0, cyclesDate: date }
  }
  if (state.session?.status === 'running') {
    const elapsed = getElapsed(state.session)
    if (elapsed >= state.session.durationMs) {
      state = finishSession(state, Date.now())
    }
  }
  return state
}

function load(): State {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return emptyState
    return hydrate(JSON.parse(raw) as State)
  } catch {
    return emptyState
  }
}

export function breakMinutes(state: State): number {
  const { shortBreakMin, longBreakMin, cyclesUntilLong } = state.settings
  if (state.cyclesToday > 0 && state.cyclesToday % cyclesUntilLong === 0) return longBreakMin
  return shortBreakMin
}

function reducer(state: State, action: Action): State {
  const now = Date.now()
  switch (action.type) {
    case 'COMPLETE_ONBOARDING':
      return { ...state, onboarded: true }
    case 'ADD_TASK': {
      const title = action.title.trim()
      if (!title) return state
      const task: Task = {
        id: uid(),
        title,
        done: false,
        createdAt: now,
        focusedMs: 0,
      }
      return {
        ...state,
        tasks: [task, ...state.tasks],
        activeTaskId: state.activeTaskId ?? task.id,
      }
    }
    case 'REMOVE_TASK': {
      const tasks = state.tasks.filter((t) => t.id !== action.id)
      const activeTaskId = state.activeTaskId === action.id ? (tasks.find((t) => !t.done)?.id ?? null) : state.activeTaskId
      return { ...state, tasks, activeTaskId }
    }
    case 'TOGGLE_DONE': {
      const tasks = state.tasks.map((t) => {
        if (t.id !== action.id) return t
        const done = !t.done
        return { ...t, done, completedAt: done ? now : undefined }
      })
      const toggled = tasks.find((t) => t.id === action.id)
      let history = state.history
      if (toggled?.done) history = creditDay(history, todayKey(), 0, 0, 1)
      let activeTaskId = state.activeTaskId
      if (toggled?.done && activeTaskId === action.id) {
        activeTaskId = tasks.find((t) => !t.done)?.id ?? null
      }
      return { ...state, tasks, history, activeTaskId }
    }
    case 'COMPLETE_CURRENT':
      return completeCurrentTask(state, now)
    case 'SELECT_TASK':
      return { ...state, activeTaskId: action.id }
    case 'SET_INTENTION':
      return { ...state, intention: action.value, intentionDate: todayKey() }
    case 'START_SESSION': {
      if (state.session?.status === 'running' || state.session?.status === 'paused') return state
      const minutes =
        action.kind === 'focus' ? state.settings.focusMin : breakMinutes(state)
      const session: Session = {
        id: uid(),
        taskId: action.kind === 'focus' ? state.activeTaskId : null,
        kind: action.kind,
        durationMs: minutes * 60_000,
        startedAt: now,
        elapsedMs: 0,
        runningSince: now,
        status: 'running',
      }
      return { ...state, session }
    }
    case 'PAUSE': {
      if (state.session?.status !== 'running') return state
      return {
        ...state,
        session: {
          ...state.session,
          elapsedMs: getElapsed(state.session, now),
          runningSince: null,
          status: 'paused',
        },
      }
    }
    case 'RESUME': {
      if (state.session?.status !== 'paused') return state
      return {
        ...state,
        session: {
          ...state.session,
          runningSince: now,
          status: 'running',
        },
      }
    }
    case 'STOP':
      return stopSession(state, now)
    case 'SESSION_FINISHED':
      return finishSession(state, now)
    case 'ACK_DONE':
      return { ...state, session: null }
    case 'ADD_THOUGHT': {
      const text = action.text.trim()
      if (!text) return state
      return {
        ...state,
        thoughts: [{ id: uid(), text, createdAt: now }, ...state.thoughts].slice(0, 40),
      }
    }
    case 'PROMOTE_THOUGHT': {
      const thought = state.thoughts.find((t) => t.id === action.id)
      if (!thought) return state
      const task: Task = {
        id: uid(),
        title: thought.text,
        done: false,
        createdAt: now,
        focusedMs: 0,
      }
      return {
        ...state,
        thoughts: state.thoughts.filter((t) => t.id !== action.id),
        tasks: [task, ...state.tasks],
      }
    }
    case 'DISMISS_THOUGHT':
      return { ...state, thoughts: state.thoughts.filter((t) => t.id !== action.id) }
    case 'UPDATE_SETTINGS':
      return { ...state, settings: { ...state.settings, ...action.patch } }
    default:
      return state
  }
}

type StoreValue = {
  state: State
  dispatch: Dispatch<Action>
  now: number
  elapsed: number
  remaining: number
}

const StoreContext = createContext<StoreValue | null>(null)

function useNow(enabled: boolean): number {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    if (!enabled) return
    setNow(Date.now())
    const id = window.setInterval(() => setNow(Date.now()), 250)
    return () => window.clearInterval(id)
  }, [enabled])
  return now
}

function ping(title: string, body: string) {
  if (typeof Notification === 'undefined') return
  if (Notification.permission !== 'granted') return
  try {
    new Notification(title, { body, icon: lampIcon })
  } catch {
    /* ignore */
  }
}

export function StoreProvider({ children }: { children: ReactNode }) {
  const [state, dispatch] = useReducer(reducer, undefined, load)
  const running = state.session?.status === 'running'
  const now = useNow(Boolean(running))
  const elapsed = state.session ? getElapsed(state.session, now) : 0
  const remaining = state.session ? Math.max(0, state.session.durationMs - elapsed) : 0
  const chimed = useRef(new Set<string>())

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(state))
  }, [state])

  useEffect(() => {
    if (state.session?.status === 'running' && remaining <= 0) {
      dispatch({ type: 'SESSION_FINISHED' })
    }
  }, [remaining, state.session?.status])

  useEffect(() => {
    const session = state.session
    if (session?.status !== 'done') return
    if (chimed.current.has(session.id)) return
    chimed.current.add(session.id)
    if (state.settings.chime) void playChime()
    if (state.settings.notify) {
      const title = session.kind === 'focus' ? 'Сессия закончилась' : 'Перерыв закончился'
      const body =
        session.kind === 'focus' ? 'Можно отдохнуть или зажечь лампу ещё раз.' : 'Пора вернуться к задаче.'
      ping(title, body)
    }
  }, [state.session, state.settings.chime, state.settings.notify])

  useEffect(() => {
    setVolume(state.settings.volume)
  }, [state.settings.volume])

  useEffect(() => {
    const on = state.session?.status === 'running' && state.session.kind === 'focus'
    void setAmbient(on ? state.settings.ambient : 'off')
    return () => {
      void setAmbient('off')
    }
  }, [state.session?.status, state.session?.kind, state.settings.ambient])

  const value = useMemo<StoreValue>(
    () => ({ state, dispatch, now, elapsed, remaining }),
    [state, now, elapsed, remaining],
  )

  return <StoreContext.Provider value={value}>{children}</StoreContext.Provider>
}

export function useStore(): StoreValue {
  const ctx = useContext(StoreContext)
  if (!ctx) throw new Error('useStore outside provider')
  return ctx
}

export function streakDays(history: DayStat[]): number {
  const map = new Set(history.filter((d) => d.focusedMs > 0).map((d) => d.date))
  const today = todayKey()
  const start = map.has(today) ? today : yesterdayKey()
  if (!map.has(start) && start !== today) return 0
  let count = 0
  const cursor = new Date()
  if (start !== today) cursor.setDate(cursor.getDate() - 1)
  while (map.has(todayKey(cursor))) {
    count += 1
    cursor.setDate(cursor.getDate() - 1)
  }
  return count
}

export function todayStat(history: DayStat[]): DayStat {
  const date = todayKey()
  return history.find((d) => d.date === date) ?? { date, focusedMs: 0, sessions: 0, completedTasks: 0 }
}


