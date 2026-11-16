const crypto = require('node:crypto')
const fs = require('node:fs')
const path = require('node:path')
const { Transform } = require('node:stream')
const { pipeline } = require('node:stream/promises')

const OWNER = 'J0shGG'
const REPO = 'seychas'
const SETUP_NAME = 'Seychas-Setup.exe'
const LATEST_URL = `https://api.github.com/repos/${OWNER}/${REPO}/releases/latest`
const CHECK_TIMEOUT_MS = 15000
const STALL_TIMEOUT_MS = 60000

function parseVersion(value) {
  const match = String(value ?? '')
    .trim()
    .replace(/^v/i, '')
    .match(/^(\d+)\.(\d+)\.(\d+)/)
  if (!match) return null
  return [Number(match[1]), Number(match[2]), Number(match[3])]
}

function isNewer(remote, local) {
  const next = parseVersion(remote)
  const current = parseVersion(local)
  if (!next || !current) return false
  for (let i = 0; i < 3; i += 1) {
    if (next[i] !== current[i]) return next[i] > current[i]
  }
  return false
}

function updateFromRelease(release, localVersion) {
  const version = String(release?.tag_name ?? '').replace(/^v/i, '')
  if (!isNewer(version, localVersion)) return null
  const assets = Array.isArray(release?.assets) ? release.assets : []
  const asset = assets.find((item) => item?.name === SETUP_NAME)
  const assetId = Number(asset?.id)
  const digest = String(asset?.digest ?? '')
  const sha256 = digest.startsWith('sha256:') ? digest.slice('sha256:'.length).toLowerCase() : ''
  if (!Number.isInteger(assetId) || assetId <= 0) return null
  if (!/^[a-f0-9]{64}$/.test(sha256)) return null
  return {
    version,
    current: parseVersion(localVersion).join('.'),
    assetId,
    sha256,
  }
}

function allowedDownloadUrl(raw) {
  let url
  try {
    url = new URL(raw)
  } catch {
    return false
  }
  if (url.protocol !== 'https:') return false
  const host = url.hostname
  if (host === 'github.com' || host === 'api.github.com') return true
  return host.endsWith('.githubusercontent.com')
}

function headerValue(headers, name) {
  const value = headers?.[name] ?? headers?.[name.toLowerCase()]
  if (Array.isArray(value)) return value[0] ?? ''
  return value == null ? '' : String(value)
}

function electronNet() {
  const electron = require('electron')
  if (!electron?.net) throw new Error('electron net unavailable')
  return electron
}

function requestResponse(url, headers, timeoutMs) {
  const { net } = electronNet()
  return new Promise((resolve, reject) => {
    const request = net.request({ url, redirect: 'manual' })
    for (const [key, value] of Object.entries(headers)) request.setHeader(key, value)
    let settled = false
    let timer = null
    const fail = (error) => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      reject(error)
    }
    const arm = () => {
      clearTimeout(timer)
      timer = setTimeout(() => {
        request.abort()
        fail(new Error('timeout'))
      }, timeoutMs)
    }
    arm()
    request.on('redirect', (_status, _method, redirectUrl) => {
      if (!allowedDownloadUrl(redirectUrl)) {
        fail(new Error('redirect'))
        return
      }
      arm()
      request.followRedirect()
    })
    request.on('response', (response) => {
      const status = response.statusCode || 0
      if (status >= 300 && status < 400) return
      if (settled) return
      settled = true
      clearTimeout(timer)
      if (status < 200 || status >= 300) {
        let reported = false
        const report = () => {
          if (reported) return
          reported = true
          reject(new Error(`status ${status}`))
        }
        response.on('data', () => {})
        response.on('error', report)
        response.on('end', report)
        return
      }
      resolve(response)
    })
    request.on('error', fail)
    request.end()
  })
}

async function readBody(response) {
  const chunks = []
  for await (const chunk of response) chunks.push(chunk)
  return Buffer.concat(chunks).toString('utf8')
}

async function findUpdate(localVersion) {
  const response = await requestResponse(
    LATEST_URL,
    {
      Accept: 'application/vnd.github+json',
      'User-Agent': 'Seychas',
      'X-GitHub-Api-Version': '2022-11-28',
    },
    CHECK_TIMEOUT_MS,
  )
  const release = JSON.parse(await readBody(response))
  return updateFromRelease(release, localVersion)
}

let cache = null

async function checkForUpdate(localVersion) {
  if (cache?.version === localVersion) return cache.update
  const update = await findUpdate(localVersion)
  cache = { version: localVersion, update }
  return update
}

function assetUrl(assetId) {
  return `https://api.github.com/repos/${OWNER}/${REPO}/releases/assets/${assetId}`
}

async function streamToFile(response, dest, onProgress) {
  const hash = crypto.createHash('sha256')
  const total = Number(headerValue(response.headers, 'content-length')) || 0
  let received = 0
  let done = false
  let stall = null
  const touch = () => {
    clearTimeout(stall)
    stall = setTimeout(() => {
      if (!done) response.destroy(new Error('timeout'))
    }, STALL_TIMEOUT_MS)
  }
  touch()
  const hasher = new Transform({
    transform(chunk, _encoding, callback) {
      touch()
      hash.update(chunk)
      received += chunk.length
      if (onProgress) onProgress(total > 0 ? Math.min(1, received / total) : -1)
      callback(null, chunk)
    },
  })
  try {
    await pipeline(response, hasher, fs.createWriteStream(dest))
  } finally {
    done = true
    clearTimeout(stall)
  }
  return hash.digest('hex')
}

let downloadJob = null

function downloadUpdate(onProgress) {
  if (!downloadJob) {
    downloadJob = runDownload(onProgress).finally(() => {
      downloadJob = null
    })
  }
  return downloadJob
}

async function runDownload(onProgress) {
  const { app } = electronNet()
  const update = await checkForUpdate(app.getVersion())
  if (!update) throw new Error('no update')
  const dest = path.join(app.getPath('temp'), `Seychas-Setup-${update.version}.exe`)
  const part = `${dest}.part`
  await fs.promises.rm(part, { force: true })
  const response = await requestResponse(
    assetUrl(update.assetId),
    {
      Accept: 'application/octet-stream',
      'User-Agent': 'Seychas',
      'X-GitHub-Api-Version': '2022-11-28',
    },
    CHECK_TIMEOUT_MS,
  )
  let digest = ''
  try {
    digest = await streamToFile(response, part, onProgress)
  } catch (error) {
    await fs.promises.rm(part, { force: true })
    throw error
  }
  const left = Buffer.from(digest, 'hex')
  const right = Buffer.from(update.sha256, 'hex')
  if (left.length !== right.length || !crypto.timingSafeEqual(left, right)) {
    await fs.promises.rm(part, { force: true })
    throw new Error('digest')
  }
  await fs.promises.rm(dest, { force: true })
  await fs.promises.rename(part, dest)
  return dest
}

module.exports = {
  SETUP_NAME,
  allowedDownloadUrl,
  checkForUpdate,
  downloadUpdate,
  isNewer,
  parseVersion,
  updateFromRelease,
}
