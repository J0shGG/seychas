export function pad(n: number): string {
  return String(n).padStart(2, '0')
}

export function todayKey(d = new Date()): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

export function yesterdayKey(d = new Date()): string {
  const y = new Date(d)
  y.setDate(y.getDate() - 1)
  return todayKey(y)
}

export function formatClock(ms: number): string {
  const total = Math.max(0, Math.ceil(ms / 1000))
  const m = Math.floor(total / 60)
  const s = total % 60
  return `${pad(m)}:${pad(s)}`
}

export function plural(n: number, one: string, few: string, many: string): string {
  const n10 = n % 10
  const n100 = n % 100
  if (n10 === 1 && n100 !== 11) return one
  if (n10 >= 2 && n10 <= 4 && (n100 < 12 || n100 > 14)) return few
  return many
}

export function formatMinutes(ms: number): string {
  const m = Math.round(ms / 60000)
  if (ms <= 0) return `0 ${plural(0, 'минута', 'минуты', 'минут')}`
  if (m < 1) return 'меньше минуты'
  if (m < 60) return `${m} ${plural(m, 'минута', 'минуты', 'минут')}`
  const h = Math.floor(m / 60)
  const rm = m % 60
  const hours = `${h} ${plural(h, 'час', 'часа', 'часов')}`
  if (rm === 0) return hours
  return `${hours} ${rm} ${plural(rm, 'минута', 'минуты', 'минут')}`
}

export function formatDateRu(d = new Date()): string {
  return d.toLocaleDateString('ru-RU', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
  })
}

export function uid(): string {
  if (typeof crypto !== 'undefined' && crypto.randomUUID) return crypto.randomUUID()
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`
}

export function parseKey(key: string): Date {
  const [y, m, d] = key.split('-').map(Number)
  return new Date(y, m - 1, d)
}

export function formatCompact(ms: number): string {
  const m = Math.round(ms / 60000)
  if (m < 1) return ''
  if (m < 60) return `${m}м`
  const h = Math.floor(m / 60)
  const rm = m % 60
  return rm ? `${h}ч ${rm}м` : `${h}ч`
}

export function formatDayHeading(key: string): string {
  if (key === todayKey()) return 'Сегодня'
  if (key === yesterdayKey()) return 'Вчера'
  return parseKey(key).toLocaleDateString('ru-RU', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
  })
}

export function formatMonthTitle(year: number, month: number): string {
  const raw = new Date(year, month, 1).toLocaleDateString('ru-RU', { month: 'long' })
  const name = raw.replace(/\.$/, '')
  return `${name.charAt(0).toUpperCase()}${name.slice(1)} ${year}`
}

export const WEEKDAYS = ['пн', 'вт', 'ср', 'чт', 'пт', 'сб', 'вс'] as const

export function startOfWeek(d = new Date()): Date {
  const x = new Date(d.getFullYear(), d.getMonth(), d.getDate())
  const day = x.getDay()
  const diff = day === 0 ? -6 : 1 - day
  x.setDate(x.getDate() + diff)
  return x
}

export function weekKeys(d = new Date()): string[] {
  const start = startOfWeek(d)
  return Array.from({ length: 7 }, (_, i) => {
    const n = new Date(start)
    n.setDate(start.getDate() + i)
    return todayKey(n)
  })
}

export function monthCells(year: number, month: number): (string | null)[] {
  const first = new Date(year, month, 1)
  const days = new Date(year, month + 1, 0).getDate()
  const mondayIndex = first.getDay() === 0 ? 6 : first.getDay() - 1
  const cells: (string | null)[] = Array.from({ length: mondayIndex }, () => null)
  for (let d = 1; d <= days; d++) cells.push(todayKey(new Date(year, month, d)))
  while (cells.length % 7 !== 0) cells.push(null)
  return cells
}

export function heatLevel(ms: number): 0 | 1 | 2 | 3 | 4 {
  const m = ms / 60000
  if (m <= 0) return 0
  if (m < 25) return 1
  if (m < 50) return 2
  if (m < 100) return 3
  return 4
}
