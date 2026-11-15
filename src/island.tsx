import { useEffect, useRef, useState } from 'react'
import { formatClock, formatMinutes, plural, todayKey } from './format'
import { STORAGE_KEY, getElapsed } from './store'
import type { State } from './types'

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

function expandedHeight(hasTitle: boolean, hasIntention: boolean, hasProgress: boolean): number {
  let height = 124
  if (hasTitle) height += 36
  if (hasIntention) height += 22
  if (hasProgress) height += 16
  return height
}

export function Island() {
  const { state, now } = useLiveState()
  const [open, setOpen] = useState(false)
  const leaveTimer = useRef(0)
  const coveredRef = useRef(false)

  useEffect(() => {
    let alive = true
    const mark = (covered: boolean) => {
      coveredRef.current = covered
      if (covered) setOpen(false)
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

  let label = 'Сейчас'
  if (session?.status === 'done') label = session.kind === 'break' ? 'Перерыв окончен' : 'Круг окончен'
  else if (session?.kind === 'break') label = 'Перерыв'
  else if (session) {
    label = state?.tasks.find((task) => task.id === session.taskId)?.title || 'Фокус'
  }

  const clock = session ? formatClock(done ? 0 : remaining) : ''
  const title = session && label !== 'Перерыв' && label !== 'Круг окончен' && label !== 'Перерыв окончен' ? label : ''
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
  const height = expandedHeight(Boolean(title), Boolean(intention), Boolean(session && !done))

  function expand() {
    if (coveredRef.current) return
    window.clearTimeout(leaveTimer.current)
    setOpen(true)
    window.seychas?.setExpanded?.(true, height)
  }

  function collapse() {
    window.clearTimeout(leaveTimer.current)
    leaveTimer.current = window.setTimeout(() => {
      setOpen(false)
      window.seychas?.setExpanded?.(false, ISLAND_COLLAPSED)
    }, 90)
  }

  const cls = [
    'island',
    open ? 'is-open' : '',
    running ? 'is-running' : '',
    paused ? 'is-paused' : '',
    done ? 'is-done' : '',
    session ? '' : 'is-idle',
  ]
    .filter(Boolean)
    .join(' ')

  return (
    <button
      type="button"
      className={cls}
      onMouseEnter={expand}
      onMouseLeave={collapse}
      onClick={() => window.seychas?.restore()}
      aria-label={clock ? `${label}, ${clock}. ${pace}. Открыть приложение` : 'Открыть Сейчас'}
    >
      <span className="island-top">
        <span className="island-dot" aria-hidden="true" />
        {clock ? <span className="island-time">{clock}</span> : open ? <span className="island-time">Сейчас</span> : null}
        <span className="island-label">{label}</span>
        {status ? <span className="island-state">{status}</span> : null}
      </span>
      <span className="island-body" aria-hidden={open ? undefined : true}>
        {title ? <span className="island-title">{title}</span> : null}
        <span className="island-more">
          <span>{pace}</span>
          <span>{todayLine}</span>
          {intention ? <span className="island-intent">{intention}</span> : null}
        </span>
      </span>
      {session && !done ? (
        <span className="island-progress" aria-hidden="true">
          <span style={{ transform: `scaleX(${progress})` }} />
        </span>
      ) : null}
    </button>
  )
}

const ISLAND_COLLAPSED = 48
