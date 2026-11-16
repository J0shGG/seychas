const crypto = require('node:crypto')

const TOPIC = 'seychas-online-j0shgg-v1k8p3'
const WINDOW_MS = 14 * 60 * 1000
// ntfy.sh allows 250 messages a day from one address. A beat every 6 minutes stays under that.
const BEAT_MS = 6 * 60 * 1000
const POLL_MS = 30 * 1000
const PAUSE_AFTER_LIMIT_MS = 6 * 60 * 60 * 1000
const ID_RE = /^[a-f0-9]{32}$/

function countOnline(body, now, selfId, selfAt) {
  const ids = new Set()
  for (const line of String(body ?? '').split('\n')) {
    const trimmed = line.trim()
    if (!trimmed) continue
    let row
    try {
      row = JSON.parse(trimmed)
    } catch {
      continue
    }
    if (row?.event !== 'message') continue
    const message = String(row.message ?? '')
    if (!ID_RE.test(message)) continue
    const at = Number(row.time) * 1000
    if (!Number.isFinite(at) || now - at > WINDOW_MS) continue
    ids.add(message)
  }
  if (selfId && selfAt && now - selfAt <= WINDOW_MS) ids.add(selfId)
  return ids.size
}

function requestText(method, url, body) {
  const { net } = require('electron')
  return new Promise((resolve, reject) => {
    const request = net.request({ method, url, redirect: 'manual' })
    request.setHeader('User-Agent', 'Seychas')
    if (body != null) {
      request.setHeader('Content-Type', 'text/plain')
      request.setHeader('Priority', 'min')
    }
    let settled = false
    const fail = (error) => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      reject(error)
    }
    const timer = setTimeout(() => {
      request.abort()
      fail(new Error('timeout'))
    }, 12000)
    request.on('redirect', () => fail(new Error('redirect')))
    request.on('response', (response) => {
      const status = response.statusCode || 0
      const chunks = []
      response.on('data', (chunk) => chunks.push(chunk))
      response.on('end', () => {
        if (settled) return
        settled = true
        clearTimeout(timer)
        const text = Buffer.concat(chunks).toString('utf8')
        if (status < 200 || status >= 300) {
          const error = new Error(`status ${status}`)
          error.status = status
          reject(error)
          return
        }
        resolve(text)
      })
      response.on('error', fail)
    })
    request.on('error', fail)
    if (body != null) request.write(body)
    request.end()
  })
}

function startPresence(onCount) {
  const id = crypto.randomBytes(16).toString('hex')
  const beatUrl = `https://ntfy.sh/${TOPIC}`
  let selfAt = 0
  let stopped = false
  let beating = false
  let polling = false
  let pauseBeatsUntil = 0
  let latest = null

  const emit = (count) => {
    if (!Number.isInteger(count) || count <= 0 || count === latest) return
    latest = count
    onCount(count)
  }

  async function beat() {
    if (stopped || beating || Date.now() < pauseBeatsUntil) return
    beating = true
    try {
      await requestText('POST', beatUrl, id)
      selfAt = Date.now()
    } catch (error) {
      if (error?.status === 429) pauseBeatsUntil = Date.now() + PAUSE_AFTER_LIMIT_MS
      console.error('presence beat', error)
    } finally {
      beating = false
    }
  }

  async function poll() {
    if (stopped || polling) return
    polling = true
    try {
      const since = Math.floor((Date.now() - WINDOW_MS) / 1000)
      const body = await requestText('GET', `${beatUrl}/json?poll=1&since=${since}`)
      emit(countOnline(body, Date.now(), id, selfAt))
    } catch (error) {
      console.error('presence poll', error)
    } finally {
      polling = false
    }
  }

  const run = async () => {
    await beat()
    await poll()
  }
  void run()
  const beatTimer = setInterval(() => void beat(), BEAT_MS)
  const pollTimer = setInterval(() => void poll(), POLL_MS)

  return () => {
    stopped = true
    clearInterval(beatTimer)
    clearInterval(pollTimer)
  }
}

module.exports = {
  BEAT_MS,
  TOPIC,
  WINDOW_MS,
  countOnline,
  startPresence,
}
