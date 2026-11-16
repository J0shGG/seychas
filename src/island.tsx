import { useEffect, useRef, useState, type MouseEvent, type PointerEvent as ReactPointerEvent } from 'react'
import { formatClock, formatMinutes, plural, todayKey } from './format'
import { applyEdge, applyTheme } from './look'
import { STORAGE_KEY, completeCurrentTask, getElapsed } from './store'
import type { IslandEdge, State, Task } from './types'

function readState(): State | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return null
    return JSON.parse(raw) as State
  } catch {
    return null
  }
}

function useLiveState(): { state: State | null; now: number } {
  const [state, setState] = useState<State | null>(readState)
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    const id = window.setInterval(() => {
      setNow(Date.now())
      setState(readState())
    }, 200)
    return () => window.clearInterval(id)
  }, [])

  return { state, now }
}

function clockTime(ms: number): string {
  return new Date(ms).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' })
}

function expandedHeight(hasTitle: boolean, hasIntention: boolean, hasProgress: boolean, hasDone: boolean): number {
  let height = 124
  if (hasTitle) height += 36
  if (hasIntention) height += 22
  if (hasProgress) height += 16
  if (hasDone) height += 40
  return height
}

function currentTaskOf(state: State | null): Task | null {
  if (!state) return null
  const active = state.tasks.find((item) => item.id === state.activeTaskId)
  if (active && !active.done) return active
  const session = state.session
  if (session?.kind === 'focus' && session.taskId) {
    const task = state.tasks.find((item) => item.id === session.taskId)
    if (task && !task.done) return task
  }
  return null
}

export function Island() {
  const { state, now } = useLiveState()
  const [open, setOpen] = useState(false)
  const [covered, setCovered] = useState(false)
  const [dragging, setDragging] = useState(false)
  const [placedEdge, setPlacedEdge] = useState<IslandEdge | null>(null)
  const leaveTimer = useRef(0)
  const watchRef = useRef(0)
  const hoverGen = useRef(0)
  const stopHover = useRef(() => {})
  const draggingRef = useRef(false)
  const swallowClick = useRef(false)
  const stopPointer = useRef<(() => void) | null>(null)
  const completing = useRef(false)
  const storedEdge: IslandEdge = state?.settings?.islandEdge === 'left' || state?.settings?.islandEdge === 'right'
    ? state.settings.islandEdge
    : 'top'
  const edge = placedEdge ?? storedEdge

  useEffect(() => {
    applyTheme(state?.settings?.theme)
  }, [state?.settings?.theme])

  useEffect(() => {
    applyEdge(edge)
  }, [edge])

  useEffect(() => {
    if (placedEdge && storedEdge === placedEdge) setPlacedEdge(null)
  }, [placedEdge, storedEdge])

  useEffect(() => {
    return window.seychas?.onIslandEdge?.((next, persist) => {
      if (next !== 'top' && next !== 'left' && next !== 'right') return
      setPlacedEdge(next)
      if (!persist) return
      try {
        const raw = localStorage.getItem(STORAGE_KEY)
        if (!raw) return
        const parsed = JSON.parse(raw) as State
        if (!parsed.settings) return
        parsed.settings.islandEdge = next
        localStorage.setItem(STORAGE_KEY, JSON.stringify(parsed))
      } catch {
        /* the desk window stores the edge when it is open */
      }
    })
  }, [])

  useEffect(() => {
    return window.seychas?.onTaskDone?.((persist) => {
      if (!persist) return
      try {
        const raw = localStorage.getItem(STORAGE_KEY)
        if (!raw) return
        const parsed = JSON.parse(raw) as State
        localStorage.setItem(STORAGE_KEY, JSON.stringify(completeCurrentTask(parsed)))
      } catch {
        /* the desk window stores the task when it is open */
      }
    })
  }, [])

  useEffect(() => {
    return window.seychas?.onDragging?.((next) => {
      draggingRef.current = next
      setDragging(next)
      if (next) {
        stopHover.current()
        setOpen(false)
        return
      }
      swallowClick.current = true
      window.setTimeout(() => {
        swallowClick.current = false
      }, 400)
    })
  }, [])

  useEffect(() => {
    let alive = true
    const mark = (next: boolean) => {
      setCovered(next)
      if (next) {
        stopHover.current()
        setOpen(false)
      }
    }
    window.seychas?.covered?.().then((covered) => {
      if (alive) mark(Boolean(covered))
    })
    const stop = window.seychas?.onCovered?.(mark)
    return () => {
      alive = false
      stop?.()
    }
  }, [])
  const session = state?.session ?? null
  const elapsed = session ? getElapsed(session, now) : 0
  const remaining = session ? Math.max(0, session.durationMs - elapsed) : 0
  const progress = session && session.durationMs > 0 ? Math.min(1, elapsed / session.durationMs) : 0
  const running = session?.status === 'running'
  const paused = session?.status === 'paused'
  const done = session?.status === 'done'
  const intention = state?.intention?.trim() ?? ''
  const currentTask = currentTaskOf(state)

  let label = currentTask?.title || 'Лампа'
  if (session?.status === 'done') label = session.kind === 'break' ? 'Перерыв окончен' : 'Круг окончен'
  else if (session?.kind === 'break') label = 'Перерыв'

  const clock = session ? formatClock(done ? 0 : remaining) : ''
  const title = currentTask?.title || ''
  let status = ''
  let pace = 'Нет активного круга'
  if (done && session) {
    status = 'готово'
    pace = session.kind === 'break' ? 'Перерыв закончился' : 'Круг закончился'
  } else if (paused && session) {
    status = 'пауза'
    pace = `Прошло ${formatClock(elapsed)} · на паузе`
  } else if (running && session) {
    status = session.kind === 'break' ? 'отдых' : 'идёт'
    pace = `Прошло ${formatClock(elapsed)} · до ${clockTime(now + remaining)}`
  }

  const todayMs =
    (state?.history.find((day) => day.date === todayKey())?.focusedMs ?? 0) +
    (session?.kind === 'focus' && !done ? elapsed : 0)
  const cycles = state?.cyclesToday ?? 0
  const todayLine = `Сегодня ${formatMinutes(todayMs)} · ${cycles} ${plural(cycles, 'круг', 'круга', 'кругов')}`
  const shownTitle = title
  const height = expandedHeight(Boolean(shownTitle), Boolean(intention), Boolean(session && !done), Boolean(currentTask))

  stopHover.current = () => {
    hoverGen.current += 1
    window.clearInterval(watchRef.current)
    watchRef.current = 0
    window.clearTimeout(leaveTimer.current)
  }

  function collapseNow() {
    if (draggingRef.current) return
    stopHover.current()
    setOpen(false)
    window.seychas?.setExpanded?.(false, ISLAND_COLLAPSED)
  }

  function expand() {
    if (draggingRef.current) return
    window.clearTimeout(leaveTimer.current)
    const gen = hoverGen.current
    setOpen(true)
    window.seychas?.setExpanded?.(true, height)
    if (watchRef.current) return
    let outside = 0
    watchRef.current = window.setInterval(() => {
      const ask = window.seychas?.pointerInside
      if (!ask) return
      void ask().then((inside) => {
        if (gen !== hoverGen.current) return
        if (inside) {
          outside = 0
          return
        }
        outside += 1
        if (outside >= 2) collapseNow()
      })
    }, 50)
  }

  function collapse() {
    if (draggingRef.current) return
    window.clearTimeout(leaveTimer.current)
    leaveTimer.current = window.setTimeout(() => {
      const ask = window.seychas?.pointerInside
      if (!ask) {
        collapseNow()
        return
      }
      void ask().then((inside) => {
        if (!inside) collapseNow()
      })
    }, 80)
  }

  useEffect(() => () => {
    stopHover.current()
    stopPointer.current?.()
  }, [])

  const side = edge !== 'top'
  const rail = side && !open && !covered && !dragging
  const cls = [
    'island',
    open && !dragging ? 'is-open' : '',
    running ? 'is-running' : '',
    paused ? 'is-paused' : '',
    done ? 'is-done' : '',
    session ? '' : 'is-idle',
    covered ? 'is-covered' : '',
    dragging ? 'is-dragging' : '',
    side ? 'is-side' : '',
    edge === 'left' ? 'is-left' : '',
    edge === 'right' ? 'is-right' : '',
  ]
    .filter(Boolean)
    .join(' ')

  function completeTask(event: MouseEvent<HTMLButtonElement>) {
    event.preventDefault()
    event.stopPropagation()
    if (completing.current || !currentTask) return
    completing.current = true
    window.setTimeout(() => {
      completing.current = false
    }, 400)
    window.seychas?.completeTask?.()
  }

  function onPointerDown(event: ReactPointerEvent<HTMLDivElement>) {
    if (event.button !== 0) return
    stopPointer.current?.()
    const startX = event.screenX
    const startY = event.screenY
    let stop = () => {}
    const onMove = (ev: PointerEvent) => {
      if ((ev.buttons & 1) === 0) {
        stop()
        return
      }
      if (draggingRef.current) return
      const dx = ev.screenX - startX
      const dy = ev.screenY - startY
      if (dx * dx + dy * dy < 64) return
      stop()
      draggingRef.current = true
      swallowClick.current = true
      stopHover.current()
      setOpen(false)
      setDragging(true)
      void window.seychas?.beginDrag?.().then((started) => {
        if (started) return
        draggingRef.current = false
        swallowClick.current = false
        setDragging(false)
      })
    }
    stop = () => {
      window.removeEventListener('pointermove', onMove)
      window.removeEventListener('pointerup', stop)
      if (stopPointer.current === stop) stopPointer.current = null
    }
    stopPointer.current = stop
    window.addEventListener('pointermove', onMove)
    window.addEventListener('pointerup', stop)
  }

  return (
    <div
      className={cls}
      onMouseEnter={expand}
      onMouseLeave={collapse}
      onPointerDown={onPointerDown}
      onClick={(event) => {
        if (swallowClick.current || draggingRef.current) {
          event.preventDefault()
          return
        }
        window.seychas?.restore()
      }}
    >
      <span className="island-top">
        <span className="island-dot" aria-hidden="true" />
        {clock ? (
          <span className="island-time">{clock}</span>
        ) : open || rail ? (
          <span className="island-time">Лампа</span>
        ) : null}
        <span className="island-label">{label}</span>
        {status ? <span className="island-state">{status}</span> : null}
      </span>
      <span className="island-body" aria-hidden={open ? undefined : true}>
        {shownTitle ? <span className="island-title">{shownTitle}</span> : null}
        <span className="island-more">
          <span>{pace}</span>
          <span>{todayLine}</span>
          {intention ? <span className="island-intent">{intention}</span> : null}
        </span>
        {currentTask ? (
          <button
            type="button"
            className="island-done"
            aria-label={`Задача «${currentTask.title}» выполнена`}
            onPointerDown={(event) => event.stopPropagation()}
            onClick={completeTask}
          >
            Задача выполнена
          </button>
        ) : null}
      </span>
      {session && !done ? (
        <span className="island-progress" aria-hidden="true">
          <span style={{ transform: rail ? `scaleY(${progress})` : `scaleX(${progress})` }} />
        </span>
      ) : null}
    </div>
  )
}

const ISLAND_COLLAPSED = 48
