import { useCallback, useEffect, useRef, useState, type FormEvent, type MouseEvent } from 'react'
import lampIcon from './assets/icon.jpg'
import { unlockAudio } from './audio'
import { applyEdge, applyTheme } from './look'
import {
  WEEKDAYS,
  formatClock,
  formatCompact,
  formatDateRu,
  formatDayHeading,
  formatMinutes,
  formatMonthTitle,
  heatLevel,
  monthCells,
  parseKey,
  plural,
  todayKey,
  weekKeys,
} from './format'
import type { DayStat } from './types'
import {
  StoreProvider,
  breakMinutes,
  streakDays,
  todayStat,
  useStore,
} from './store'

export default function App() {
  return (
    <StoreProvider>
      <Root />
    </StoreProvider>
  )
}

function Root() {
  const { state, dispatch } = useStore()
  const update = useUpdateOffer()

  useEffect(() => {
    applyTheme(state.settings.theme)
    applyEdge(state.settings.islandEdge)
  }, [state.settings.theme, state.settings.islandEdge])

  useEffect(() => {
    return window.seychas?.onIslandEdge?.((edge) => {
      if (edge !== 'top' && edge !== 'left' && edge !== 'right') return
      dispatch({ type: 'UPDATE_SETTINGS', patch: { islandEdge: edge } })
    })
  }, [dispatch])

  useEffect(() => {
    return window.seychas?.onTaskDone?.(() => {
      dispatch({ type: 'COMPLETE_CURRENT' })
    })
  }, [dispatch])
  return (
    <>
      {state.onboarded ? (
        <Desk updateOpen={Boolean(update.offer)} dismissUpdate={update.dismiss} />
      ) : (
        <Onboarding />
      )}
      {update.offer && (
        <UpdateCard
          current={update.offer.current}
          version={update.offer.version}
          progress={update.progress}
          error={update.error}
          busy={update.busy}
          onLater={update.dismiss}
          onInstall={() => void update.install()}
        />
      )}
    </>
  )
}

function Onboarding() {
  const { dispatch } = useStore()
  return (
    <div className="onboarding">
      <img src={lampIcon} alt="Лампа" />
      <h1>Лампа</h1>
      <p className="rule">В каждый момент — только одна задача.</p>
      <p className="sub">Поставь её на стол, зажги лампу и работай, пока горит свет.</p>
      <button
        className="primary"
        onClick={() => {
          unlockAudio()
          dispatch({ type: 'COMPLETE_ONBOARDING' })
        }}
      >
        К столу
      </button>
    </div>
  )
}

function Desk({
  updateOpen,
  dismissUpdate,
}: {
  updateOpen: boolean
  dismissUpdate: () => void
}) {
  const { state, remaining } = useStore()
  const session = state.session
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [journalOpen, setJournalOpen] = useState(false)
  const [journalDay, setJournalDay] = useState(() => todayKey())
  const online = useOnlineCount()
  const running = session?.status === 'running'
  const paused = session?.status === 'paused'
  const done = session?.status === 'done'

  const cls = [
    'app',
    session?.kind === 'focus' && session.status !== 'done' ? 'is-focus' : '',
    running ? 'is-running' : '',
    paused ? 'is-paused' : '',
    done ? 'is-done' : '',
  ]
    .filter(Boolean)
    .join(' ')

  const active = state.tasks.find((t) => t.id === state.activeTaskId)
  const title = session?.kind === 'break' ? 'Перерыв' : active?.title || 'Свободный фокус'

  useEffect(() => {
    if (running) {
      document.title = `${formatClock(remaining)} · ${title}`
    } else {
      document.title = 'Лампа'
    }
  }, [running, remaining, title])

  useShortcuts({
    modalOpen: settingsOpen || journalOpen || updateOpen,
    closeModals: () => {
      if (updateOpen) {
        dismissUpdate()
        return
      }
      setSettingsOpen(false)
      setJournalOpen(false)
    },
  })

  function openJournal(day = todayKey()) {
    setJournalDay(day)
    setJournalOpen(true)
    setSettingsOpen(false)
  }

  return (
    <div className={cls}>
      <div className="grain" />
      <div className="shell">
        <header className="top">
          <div className="brand">
            <img src={lampIcon} alt="" />
            <div>
              <h1>Лампа</h1>
              <p>одна задача</p>
            </div>
          </div>
          <div>
            <div className="top-meta">
              <div className="date">{formatDateRu()}</div>
              <div className="streak">{streakLabel(state.history)}</div>
              {online != null && (
                <div className="online" title="Только число. Случайный код, пока приложение открыто.">
                  {onlineLabel(online)}
                </div>
              )}
            </div>
            <div className="top-actions">
              <button className="icon-btn" onClick={() => openJournal()} title="Дни работы">
                <CalendarIcon />
              </button>
              <button className="icon-btn" onClick={() => setSettingsOpen(true)} title="Настройки">
                <SettingsIcon />
              </button>
              <button className="icon-btn" onClick={() => void toggleFullscreen()} title="Полный экран">
                ▢
              </button>
            </div>
          </div>
        </header>

        <div className="intention">
          <label htmlFor="intention">Сегодня важно</label>
          <IntentionField />
        </div>

        <div className="desk">
          <TaskColumn />
          <Stage />
          <DayColumn onOpenJournal={openJournal} />
        </div>
      </div>

      <footer className="footer">
        <span>
          <span className="kbd">N</span> новая · <span className="kbd">Пробел</span> лампа ·{' '}
          <span className="kbd">Enter</span> готово · <span className="kbd">Esc</span> сбросить
        </span>
        <span>Задачи остаются на этом компьютере</span>
      </footer>

      {settingsOpen && <Settings onClose={() => setSettingsOpen(false)} />}
      {journalOpen && (
        <Journal
          selected={journalDay}
          onSelect={setJournalDay}
          onClose={() => setJournalOpen(false)}
        />
      )}
      {done && <FinishedCard />}
    </div>
  )
}

function IntentionField() {
  const { state, dispatch } = useStore()
  return (
    <input
      id="intention"
      value={state.intention}
      placeholder="Одна фраза, которая держит день"
      onChange={(e) => dispatch({ type: 'SET_INTENTION', value: e.target.value })}
    />
  )
}

function TaskColumn() {
  const { state, dispatch } = useStore()
  const inputRef = useRef<HTMLInputElement>(null)
  const open = state.tasks.filter((t) => !t.done)

  function onSubmit(e: FormEvent) {
    e.preventDefault()
    const value = inputRef.current?.value ?? ''
    dispatch({ type: 'ADD_TASK', title: value })
    if (inputRef.current) inputRef.current.value = ''
  }

  return (
    <aside className="panel col-side">
      <h2>На столе</h2>
      <form className="task-form" onSubmit={onSubmit}>
        <input ref={inputRef} id="new-task" placeholder="Добавить задачу" maxLength={120} />
        <button type="submit">+</button>
      </form>
      {open.length === 0 ? (
        <p className="active-hint">Стол пуст. Напиши, чем займёшься.</p>
      ) : (
        <div className="list">
          {open.map((task) => (
            <div
              key={task.id}
              className={`slip ${task.id === state.activeTaskId ? 'active' : ''} ${task.done ? 'done' : ''}`}
            >
              <button
                className="check"
                aria-label="Готово"
                onClick={() => dispatch({ type: 'TOGGLE_DONE', id: task.id })}
              />
              <button
                className="slip-title"
                style={{ background: 'none', border: 'none', padding: 0, textAlign: 'left', color: 'inherit' }}
                onClick={() => dispatch({ type: 'SELECT_TASK', id: task.id })}
              >
                {task.title}
                {task.focusedMs > 0 && (
                  <div className="slip-meta">{formatMinutes(task.focusedMs)} в фокусе</div>
                )}
              </button>
              <button className="x" aria-label="Удалить" onClick={() => dispatch({ type: 'REMOVE_TASK', id: task.id })}>
                ×
              </button>
            </div>
          ))}
        </div>
      )}
    </aside>
  )
}

const FOCUS_PRESETS = [15, 25, 50, 90]

function clampFocusMinutes(raw: string): number | null {
  if (!/^\d+$/.test(raw)) return null
  const minutes = Number(raw)
  if (minutes < 1 || minutes > 180) return null
  return minutes
}

function MinutesField({
  value,
  disabled,
  onCommit,
}: {
  value: number
  disabled?: boolean
  onCommit: (minutes: number) => void
}) {
  const [text, setText] = useState(String(value))

  useEffect(() => {
    setText(String(value))
  }, [value])

  function commit() {
    const next = clampFocusMinutes(text)
    if (next == null) {
      setText(String(value))
      return
    }
    setText(String(next))
    if (next !== value) onCommit(next)
  }

  const custom = !FOCUS_PRESETS.includes(value)

  return (
    <label className={`chip minutes ${custom ? 'on' : ''}${disabled ? ' is-disabled' : ''}`}>
      <input
        type="text"
        inputMode="numeric"
        value={text}
        disabled={disabled}
        aria-label="Свои минуты"
        onChange={(event) => setText(event.target.value.replace(/\D/g, '').slice(0, 3))}
        onBlur={commit}
        onKeyDown={(event) => {
          if (event.key !== 'Enter') return
          event.preventDefault()
          commit()
          event.currentTarget.blur()
        }}
      />
      <span>мин</span>
    </label>
  )
}

function Stage() {
  const { state, dispatch, remaining, elapsed } = useStore()
  const session = state.session
  const active = state.tasks.find((t) => t.id === state.activeTaskId)
  const duration = session?.durationMs ?? state.settings.focusMin * 60_000
  const progress = session ? Math.min(1, elapsed / duration) : 0
  const thoughtRef = useRef<HTMLInputElement>(null)

  const label =
    session?.kind === 'break'
      ? 'Перерыв'
      : session?.status === 'paused'
        ? 'Пауза'
        : session?.status === 'running'
          ? 'Фокус'
          : 'Готов'

  const ringAction =
    session?.status === 'running' ? 'pause' : session?.status === 'paused' ? 'resume' : session ? null : 'start'

  function lightRing(event: MouseEvent<HTMLButtonElement>) {
    const button = event.currentTarget
    const burst = button.querySelector('.ring-burst')
    button.classList.remove('is-pressed')
    burst?.classList.remove('is-on')
    void button.offsetWidth
    button.classList.add('is-pressed')
    burst?.classList.add('is-on')
    unlockAudio()
    if (ringAction === 'start') dispatch({ type: 'START_SESSION', kind: 'focus' })
    else if (ringAction === 'pause') dispatch({ type: 'PAUSE' })
    else if (ringAction === 'resume') dispatch({ type: 'RESUME' })
  }

  const ring = (
    <>
      <TimerRing progress={progress} />
      <div className="ring-face">
        <div className="flame" />
        <div className="time">{formatClock(session ? remaining : duration)}</div>
        <div className="kind">{label}</div>
      </div>
    </>
  )

  return (
    <section className="panel stage">
      <div className="presets">
        {FOCUS_PRESETS.map((m) => (
          <button
            key={m}
            className={`chip ${state.settings.focusMin === m ? 'on' : ''}`}
            onClick={() => dispatch({ type: 'UPDATE_SETTINGS', patch: { focusMin: m } })}
            disabled={session?.status === 'running'}
          >
            {m} мин
          </button>
        ))}
        <MinutesField
          value={state.settings.focusMin}
          disabled={session?.status === 'running'}
          onCommit={(minutes) => dispatch({ type: 'UPDATE_SETTINGS', patch: { focusMin: minutes } })}
        />
      </div>

      <div className="ring-wrap">
        {ringAction ? (
          <button
            type="button"
            className="ring-hit"
            aria-label={
              ringAction === 'pause'
                ? 'Пауза'
                : ringAction === 'resume'
                  ? 'Продолжить'
                  : `Зажечь лампу, ${state.settings.focusMin} минут`
            }
            onClick={lightRing}
          >
            {ring}
            <span className="ring-burst" aria-hidden="true" />
          </button>
        ) : (
          <div className="ring-hit">{ring}</div>
        )}
      </div>

      <p className="active-title">
        {session?.kind === 'break' ? 'Отпусти плечи' : active?.title || 'Что сейчас важно?'}
      </p>
      <p className="active-hint">
        {session?.kind === 'break'
          ? 'Короткий свет. Потом вернёмся к делу.'
          : active
            ? 'Свет горит только здесь.'
            : 'Выбери задачу слева или начни свободный фокус.'}
      </p>

      <div className="actions">
        {(session?.status === 'running' || session?.status === 'paused') && (
          <button className="ghost" onClick={() => dispatch({ type: 'STOP' })}>
            Сбросить
          </button>
        )}
        {active && !active.done && (
          <button className="ghost" onClick={() => dispatch({ type: 'TOGGLE_DONE', id: active.id })}>
            Задача готова
          </button>
        )}
      </div>

      <form
        className="thoughts"
        onSubmit={(e) => {
          e.preventDefault()
          const value = thoughtRef.current?.value ?? ''
          dispatch({ type: 'ADD_THOUGHT', text: value })
          if (thoughtRef.current) thoughtRef.current.value = ''
        }}
      >
        <input ref={thoughtRef} placeholder="Мысль унести сюда — и вернуться к делу" />
      </form>
      {state.thoughts.length > 0 && (
        <div className="thought-list">
          {state.thoughts.slice(0, 5).map((t) => (
            <div className="thought" key={t.id}>
              <span>{t.text}</span>
              <span>
                <button
                  className="tiny"
                  data-action="promote"
                  onClick={() => dispatch({ type: 'PROMOTE_THOUGHT', id: t.id })}
                >
                  в задачи
                </button>{' '}
                <button className="tiny" onClick={() => dispatch({ type: 'DISMISS_THOUGHT', id: t.id })}>
                  убрать
                </button>
              </span>
            </div>
          ))}
        </div>
      )}
    </section>
  )
}

function TimerRing({ progress }: { progress: number }) {
  const r = 108
  const c = 2 * Math.PI * r
  const amount = Math.min(1, Math.max(0, progress))
  const offset = c * (1 - amount)
  const visible = amount > 0.001
  return (
    <svg viewBox="0 0 240 240" className="timer-ring">
      <circle className="ring-track" cx="120" cy="120" r={r} fill="none" />
      <circle
        className="ring-glow"
        cx="120"
        cy="120"
        r={r}
        fill="none"
        strokeDasharray={c}
        strokeDashoffset={offset}
        strokeLinecap="round"
        transform="rotate(-90 120 120)"
        opacity={visible ? 0.4 : 0}
      />
      <circle
        className="ring-arc"
        cx="120"
        cy="120"
        r={r}
        fill="none"
        strokeDasharray={c}
        strokeDashoffset={offset}
        strokeLinecap="round"
        transform="rotate(-90 120 120)"
        opacity={visible ? 1 : 0}
      />
    </svg>
  )
}

function DayColumn({ onOpenJournal }: { onOpenJournal: (day?: string) => void }) {
  const { state, elapsed } = useStore()
  const today = todayStat(state.history)
  const live = liveFocusMs(state.session, elapsed)
  const focusedMs = today.focusedMs + live
  const done = state.tasks.filter((t) => t.done).slice(0, 8)
  const week = weekKeys()
  const byDate = new Map(state.history.map((d) => [d.date, d]))
  const weekMs = week.reduce((sum, key) => sum + (byDate.get(key)?.focusedMs ?? 0), 0) + live

  return (
    <aside className="panel col-side">
      <h2>Сегодня</h2>
      <div className="stat-row">
        <span>В фокусе</span>
        <b>{formatMinutes(focusedMs)}</b>
      </div>
      <div className="stat-row">
        <span>Сессии</span>
        <b>
          {today.sessions} {plural(today.sessions, 'круг', 'круга', 'кругов')}
        </b>
      </div>
      <div className="stat-row">
        <span>Сделано</span>
        <b>
          {today.completedTasks} {plural(today.completedTasks, 'задача', 'задачи', 'задач')}
        </b>
      </div>

      <h2 className="week-label">Эта неделя</h2>
      <div className="week-strip">
        {week.map((key, i) => {
          const ms = (byDate.get(key)?.focusedMs ?? 0) + (key === todayKey() ? live : 0)
          return (
            <button
              key={key}
              className={`week-cell heat-${heatLevel(ms)} ${key === todayKey() ? 'today' : ''}`}
              title={`${WEEKDAYS[i]}, ${formatMinutes(ms)}`}
              onClick={() => onOpenJournal(key)}
            >
              <span>{WEEKDAYS[i]}</span>
              <em>{formatCompact(ms) || '·'}</em>
            </button>
          )
        })}
      </div>
      <div className="stat-row">
        <span>За неделю</span>
        <b>{formatMinutes(weekMs)}</b>
      </div>
      <button className="ghost journal-link" onClick={() => onOpenJournal()}>
        Все дни
      </button>

      {done.length > 0 && (
        <div className="done-list">
          <h2>Закрыто</h2>
          {done.map((t) => (
            <div className="done-item" key={t.id}>
              ✓ {t.title}
            </div>
          ))}
        </div>
      )}
    </aside>
  )
}

function liveFocusMs(session: { kind: string; status: string } | null, elapsed: number): number {
  if (session?.kind === 'focus' && (session.status === 'running' || session.status === 'paused')) return elapsed
  return 0
}

function Journal({
  selected,
  onSelect,
  onClose,
}: {
  selected: string
  onSelect: (day: string) => void
  onClose: () => void
}) {
  const { state, elapsed } = useStore()
  const selectedDate = parseKey(selected)
  const [cursor, setCursor] = useState(() => ({
    year: selectedDate.getFullYear(),
    month: selectedDate.getMonth(),
  }))
  const byDate = new Map(state.history.map((d) => [d.date, d]))
  const live = liveFocusMs(state.session, elapsed)
  const cells = monthCells(cursor.year, cursor.month)
  const now = new Date()
  const canNext =
    cursor.year < now.getFullYear() || (cursor.year === now.getFullYear() && cursor.month < now.getMonth())

  const monthMs = cells.reduce((sum, key) => {
    if (!key) return sum
    const extra = key === todayKey() ? live : 0
    return sum + (byDate.get(key)?.focusedMs ?? 0) + extra
  }, 0)
  const monthDays = cells.filter((key) => {
    if (!key) return false
    const ms = (byDate.get(key)?.focusedMs ?? 0) + (key === todayKey() ? live : 0)
    return ms > 0
  }).length

  const day = byDate.get(selected)
  const dayMs = (day?.focusedMs ?? 0) + (selected === todayKey() ? live : 0)
  const logged = [...state.history].sort((a, b) => (a.date < b.date ? 1 : -1)).slice(0, 21)

  function shiftMonth(delta: number) {
    const d = new Date(cursor.year, cursor.month + delta, 1)
    if (delta > 0 && !canNext) return
    setCursor({ year: d.getFullYear(), month: d.getMonth() })
  }

  return (
    <div className="overlay" onClick={onClose}>
      <div className="card settings-card journal-card" onClick={(e) => e.stopPropagation()}>
        <div className="journal-head">
          <h3>Дни работы</h3>
          <div className="month-nav">
            <button className="icon-btn dark" onClick={() => shiftMonth(-1)} aria-label="Предыдущий месяц">
              ‹
            </button>
            <span>{formatMonthTitle(cursor.year, cursor.month)}</span>
            <button className="icon-btn dark" onClick={() => shiftMonth(1)} disabled={!canNext} aria-label="Следующий месяц">
              ›
            </button>
          </div>
        </div>

        <div className="cal">
          <div className="cal-weekdays">
            {WEEKDAYS.map((d) => (
              <span key={d}>{d}</span>
            ))}
          </div>
          <div className="cal-grid">
            {cells.map((key, i) => {
              if (!key) return <span key={`e${i}`} className="cal-cell empty" />
              const ms = (byDate.get(key)?.focusedMs ?? 0) + (key === todayKey() ? live : 0)
              const future = key > todayKey()
              return (
                <button
                  key={key}
                  className={`cal-cell heat-${heatLevel(ms)} ${key === selected ? 'selected' : ''} ${key === todayKey() ? 'today' : ''}`}
                  disabled={future}
                  onClick={() => onSelect(key)}
                >
                  <strong>{Number(key.slice(-2))}</strong>
                  <em>{formatCompact(ms)}</em>
                </button>
              )
            })}
          </div>
        </div>

        <div className="legend">
          <span>мало</span>
          <i className="heat-1" />
          <i className="heat-2" />
          <i className="heat-3" />
          <i className="heat-4" />
          <span>много</span>
        </div>

        <div className="day-detail">
          <h4>{formatDayHeading(selected)}</h4>
          {dayMs > 0 || (day?.sessions ?? 0) > 0 || (day?.completedTasks ?? 0) > 0 ? (
            <p>
              {formatMinutes(dayMs)}
              {day ? ` · ${day.sessions} ${plural(day.sessions, 'круг', 'круга', 'кругов')}` : ''}
              {day && day.completedTasks > 0
                ? ` · ${day.completedTasks} ${plural(day.completedTasks, 'задача', 'задачи', 'задач')}`
                : ''}
            </p>
          ) : (
            <p>В этот день лампа ещё не горела.</p>
          )}
        </div>

        <div className="stat-row ink">
          <span>За месяц</span>
          <b>
            {formatMinutes(monthMs)}
            {monthDays > 0 ? ` · ${monthDays} ${plural(monthDays, 'день', 'дня', 'дней')}` : ''}
          </b>
        </div>

        {logged.length > 0 && (
          <div className="day-log">
            <h2>Журнал</h2>
            {logged.map((entry) => {
              const ms = entry.focusedMs + (entry.date === todayKey() ? live : 0)
              return (
                <button
                  key={entry.date}
                  className={`log-row ${entry.date === selected ? 'on' : ''}`}
                  onClick={() => {
                    onSelect(entry.date)
                    const d = parseKey(entry.date)
                    setCursor({ year: d.getFullYear(), month: d.getMonth() })
                  }}
                >
                  <span>{formatDayHeading(entry.date)}</span>
                  <b>{formatMinutes(ms)}</b>
                </button>
              )
            })}
          </div>
        )}

        <div className="actions" style={{ marginTop: 18 }}>
          <button className="primary" onClick={onClose}>
            К столу
          </button>
        </div>
      </div>
    </div>
  )
}

function SettingsIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <circle cx="12" cy="12" r="3" stroke="currentColor" strokeWidth="2" />
      <path
        d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  )
}

function CalendarIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true">
      <rect x="1.5" y="3.5" width="13" height="11" rx="1.5" stroke="currentColor" strokeWidth="1.3" />
      <path d="M1.5 6.5h13" stroke="currentColor" strokeWidth="1.3" />
      <path d="M5 1.5v3M11 1.5v3" stroke="currentColor" strokeWidth="1.3" strokeLinecap="round" />
    </svg>
  )
}

function useUpdateOffer() {
  const [offer, setOffer] = useState<{ version: string; current: string } | null>(null)
  const [progress, setProgress] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    const api = window.seychas
    if (!api?.checkUpdate) return
    let alive = true
    const stop = api.onUpdateProgress?.((ratio) => {
      if (alive) setProgress(ratio)
    })
    api
      .checkUpdate()
      .then((result) => {
        if (alive && result?.update) setOffer(result.update)
      })
      .catch(() => {})
    return () => {
      alive = false
      stop?.()
    }
  }, [])

  const dismiss = useCallback(() => {
    if (busy) return
    setOffer(null)
  }, [busy])

  const install = useCallback(async () => {
    if (!window.seychas?.installUpdate || busy) return
    setBusy(true)
    setError(null)
    try {
      const result = await window.seychas.installUpdate()
      if (!result?.ok) {
        setBusy(false)
        setError(result?.message || 'Не получилось скачать обновление.')
      }
    } catch {
      setBusy(false)
      setError('Не получилось скачать обновление.')
    }
  }, [busy])

  return { offer, progress, error, busy, dismiss, install }
}

function UpdateCard({
  current,
  version,
  progress,
  error,
  busy,
  onLater,
  onInstall,
}: {
  current: string
  version: string
  progress: number | null
  error: string | null
  busy: boolean
  onLater: () => void
  onInstall: () => void
}) {
  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (event.key !== 'Escape' || busy) return
      event.preventDefault()
      event.stopPropagation()
      onLater()
    }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  }, [busy, onLater])

  const percent = progress != null && progress >= 0 ? Math.round(progress * 100) : null
  return (
    <div
      className="overlay update-overlay"
      role="dialog"
      aria-labelledby="update-title"
      onClick={() => {
        if (!busy) onLater()
      }}
    >
      <div className="card update-card" onClick={(event) => event.stopPropagation()}>
        <h3 id="update-title">Есть новая версия</h3>
        <p>
          На GitHub вышла {version}. Сейчас у тебя {current}. Установщик скачается, откроется, и «Лампа»
          закроется.
        </p>
        {busy && percent != null && (
          <div className="update-bar" aria-hidden="true">
            <span style={{ width: `${percent}%` }} />
          </div>
        )}
        {error && <p className="update-error">{error}</p>}
        <div className="actions">
          <button className="primary" disabled={busy} onClick={onInstall}>
            {busy ? (percent == null ? 'Скачиваю…' : `Скачиваю ${percent}%`) : 'Обновить'}
          </button>
          <button className="ghost" disabled={busy} onClick={onLater}>
            Позже
          </button>
        </div>
      </div>
    </div>
  )
}

function FinishedCard() {
  const { state, dispatch } = useStore()
  const minutes = breakMinutes(state)
  const focusEnded = state.session?.kind === 'focus'
  return (
    <div className="overlay">
      <div className="card">
        <h3>{focusEnded ? 'Свет погас' : 'Пора к делу'}</h3>
        <p>
          {focusEnded
            ? `Круг закрыт. Можно отдохнуть ${minutes} мин или зажечь лампу снова.`
            : 'Перерыв закончился. Вернись к одной задаче.'}
        </p>
        <div className="actions">
          {focusEnded ? (
            <>
              <button
                className="primary"
                onClick={() => {
                  dispatch({ type: 'ACK_DONE' })
                  dispatch({ type: 'START_SESSION', kind: 'break' })
                }}
              >
                Отдохнуть {minutes} мин
              </button>
              <button
                className="ghost"
                onClick={() => {
                  dispatch({ type: 'ACK_DONE' })
                  dispatch({ type: 'START_SESSION', kind: 'focus' })
                }}
              >
                Ещё круг
              </button>
            </>
          ) : (
            <button
              className="primary"
              onClick={() => {
                dispatch({ type: 'ACK_DONE' })
                dispatch({ type: 'START_SESSION', kind: 'focus' })
              }}
            >
              Зажечь лампу
            </button>
          )}
          <button className="ghost" onClick={() => dispatch({ type: 'ACK_DONE' })}>
            К столу
          </button>
        </div>
      </div>
    </div>
  )
}

function Settings({ onClose }: { onClose: () => void }) {
  const { state, dispatch } = useStore()
  const s = state.settings

  async function toggleNotify() {
    const next = !s.notify
    if (next && typeof Notification !== 'undefined' && Notification.permission === 'default') {
      await Notification.requestPermission()
    }
    dispatch({ type: 'UPDATE_SETTINGS', patch: { notify: next } })
  }

  return (
    <div className="overlay" onClick={onClose}>
      <div className="card settings-card" onClick={(e) => e.stopPropagation()}>
        <h3>Настройки</h3>
        <div className="field">
          <span>Тема</span>
          <div className="presets">
            {(
              [
                ['lamp', 'Лампа'],
                ['light', 'Светлая'],
                ['dark', 'Тёмная'],
                ['ink', 'Чернила'],
              ] as const
            ).map(([id, label]) => (
              <button
                key={id}
                className={`chip ${s.theme === id ? 'on' : ''}`}
                onClick={() => dispatch({ type: 'UPDATE_SETTINGS', patch: { theme: id } })}
              >
                {label}
              </button>
            ))}
          </div>
        </div>
        <div className="field">
          <span>Длина круга</span>
          <div className="presets">
            {FOCUS_PRESETS.map((m) => (
              <button
                key={m}
                className={`chip ${s.focusMin === m ? 'on' : ''}`}
                onClick={() => dispatch({ type: 'UPDATE_SETTINGS', patch: { focusMin: m } })}
              >
                {m}
              </button>
            ))}
            <MinutesField
              value={s.focusMin}
              onCommit={(minutes) => dispatch({ type: 'UPDATE_SETTINGS', patch: { focusMin: minutes } })}
            />
          </div>
        </div>
        <div className="field">
          <span>Фон</span>
          <div className="presets">
            {(
              [
                ['off', 'Тишина'],
                ['rain', 'Дождь'],
                ['fire', 'Огонь'],
                ['cafe', 'Кафе'],
              ] as const
            ).map(([id, label]) => (
              <button
                key={id}
                className={`chip ${s.ambient === id ? 'on' : ''}`}
                onClick={() => {
                  unlockAudio()
                  dispatch({ type: 'UPDATE_SETTINGS', patch: { ambient: id } })
                }}
              >
                {label}
              </button>
            ))}
          </div>
        </div>
        <div className="field">
          <span>Громкость</span>
          <div className="volume">
            <input
              type="range"
              min={0}
              max={100}
              step={1}
              value={Math.round(s.volume * 100)}
              aria-label="Громкость"
              onChange={(event) => {
                unlockAudio()
                dispatch({ type: 'UPDATE_SETTINGS', patch: { volume: Number(event.target.value) / 100 } })
              }}
            />
            <em>{Math.round(s.volume * 100)}%</em>
          </div>
        </div>
        <div className="toggle-row">
          <span>Колокол в конце</span>
          <button className={`chip ${s.chime ? 'on' : ''}`} onClick={() => dispatch({ type: 'UPDATE_SETTINGS', patch: { chime: !s.chime } })}>
            {s.chime ? 'вкл' : 'выкл'}
          </button>
        </div>
        <div className="toggle-row">
          <span>Уведомление системы</span>
          <button className={`chip ${s.notify ? 'on' : ''}`} onClick={() => void toggleNotify()}>
            {s.notify ? 'вкл' : 'выкл'}
          </button>
        </div>
        <div className="actions" style={{ marginTop: 18 }}>
          <button className="primary" onClick={onClose}>
            Закрыть
          </button>
        </div>
        <p className="settings-credit">by J0shGG</p>
      </div>
    </div>
  )
}

function useOnlineCount() {
  const [online, setOnline] = useState<number | null>(null)

  useEffect(() => {
    const api = window.seychas
    if (!api?.presence) return
    let alive = true
    const apply = (count: number | null) => {
      if (alive && typeof count === 'number' && count > 0) setOnline(count)
    }
    api
      .presence()
      .then(apply)
      .catch(() => {})
    const stop = api.onPresence?.(apply)
    return () => {
      alive = false
      stop?.()
    }
  }, [])

  return online
}

function onlineLabel(count: number) {
  return `${count} ${plural(count, 'человек', 'человека', 'человек')} сейчас`
}

function streakLabel(history: DayStat[]) {
  const n = streakDays(history)
  if (n === 0) return 'Начни первую сессию сегодня'
  return `${n} ${plural(n, 'день', 'дня', 'дней')} подряд`
}

function useShortcuts({
  modalOpen,
  closeModals,
}: {
  modalOpen: boolean
  closeModals: () => void
}) {
  const { state, dispatch } = useStore()

  useEffect(() => {
    function typingTarget(el: EventTarget | null) {
      if (!(el instanceof HTMLElement)) return false
      const tag = el.tagName
      return tag === 'INPUT' || tag === 'TEXTAREA' || el.isContentEditable
    }

    function onKey(e: KeyboardEvent) {
      if (e.repeat) return
      const typing = typingTarget(e.target)

      if (e.key === 'Escape') {
        if (modalOpen) {
          closeModals()
          return
        }
        if (state.session) dispatch({ type: 'STOP' })
        return
      }

      if (typing) return

      if (e.code === 'Space') {
        e.preventDefault()
        unlockAudio()
        if (!state.session) dispatch({ type: 'START_SESSION', kind: 'focus' })
        else if (state.session.status === 'running') dispatch({ type: 'PAUSE' })
        else if (state.session.status === 'paused') dispatch({ type: 'RESUME' })
        return
      }

      if (e.key === 'Enter' && state.activeTaskId) {
        dispatch({ type: 'TOGGLE_DONE', id: state.activeTaskId })
        return
      }

      if (e.key === 'n' || e.key === 'N' || e.key === 'т' || e.key === 'Т') {
        document.getElementById('new-task')?.focus()
        return
      }

      if (e.key === 'f' || e.key === 'F' || e.key === 'а' || e.key === 'А') {
        void toggleFullscreen()
      }
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [closeModals, dispatch, modalOpen, state.activeTaskId, state.session])
}

async function toggleFullscreen() {
  if (document.fullscreenElement) await document.exitFullscreen()
  else await document.documentElement.requestFullscreen()
}
