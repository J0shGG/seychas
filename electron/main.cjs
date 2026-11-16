const { app, BrowserWindow, Menu, Tray, ipcMain, nativeImage, screen, shell } = require('electron')
const fs = require('node:fs')
const path = require('node:path')
const { fullscreenCover, liftIsland, setSmoothTimers, shapeIsland } = require('./frame.cjs')
const { checkForUpdate, downloadUpdate } = require('./update.cjs')
const { startPresence } = require('./presence.cjs')

const APP_ID = 'app.seychas.desktop'
const ISLAND_W = 292
const ISLAND_H = 48
const ISLAND_OPEN_W = 340
const ISLAND_OPEN_MIN = 120
const ISLAND_OPEN_MAX = 220
const SIDE_OPEN_W = 280
const SIDE_OPEN_MIN = 168
const SIDE_OPEN_MAX = 300
const THEME_BG = {
  lamp: '#120e0b',
  light: '#f3eadc',
  dark: '#101012',
  ink: '#101624',
}

app.setAppUserModelId(APP_ID)
Menu.setApplicationMenu(null)

/** @type {import('electron').BrowserWindow | null} */
let win = null
/** @type {import('electron').BrowserWindow | null} */
let island = null
/** @type {import('electron').Tray | null} */
let tray = null
/** @type {{ x: number, y: number, width: number, height: number } | null} */
let anchor = null
let islandReady = false
let islandWanted = false
let islandExpanded = false
let islandOpenHeight = 168
let islandRadius = ISLAND_H / 2
let islandEdge = 'top'
let islandAnim = null
let islandPainted = ''
let islandCovered = false
let coverMisses = 0
let coverTimer = null
let quitting = false
let latestCount = null
let stopPresence = null
const ISLAND_ANIM_MS = 320
const ISLAND_FRAME_MS = 1000 / 60

function iconFile() {
  if (app.isPackaged) return undefined
  const candidates = [
    path.join(__dirname, '..', 'build', 'icon.ico'),
    path.join(__dirname, '..', 'build', 'icon.png'),
    path.join(__dirname, '..', 'public', 'icon.jpg'),
  ]
  return candidates.find((file) => fs.existsSync(file))
}

function loadApp(window, islandMode) {
  if (app.isPackaged) {
    const file = path.join(__dirname, '..', 'dist', 'index.html')
    if (islandMode) window.loadFile(file, { query: { island: '1' } })
    else window.loadFile(file)
    return
  }
  window.loadURL(islandMode ? 'http://127.0.0.1:5173/?island=1' : 'http://127.0.0.1:5173')
}

function rememberAnchor() {
  if (!win || win.isDestroyed() || win.isMinimized() || !win.isVisible()) return
  anchor = win.getBounds()
}

function islandSide() {
  return islandEdge === 'left' || islandEdge === 'right'
}

function islandBoxFor(expanded) {
  if (islandCovered) return { width: ISLAND_H, height: ISLAND_H, radius: ISLAND_H / 2 }
  if (!islandSide()) {
    if (expanded) {
      const height = Math.max(ISLAND_OPEN_MIN, Math.min(ISLAND_OPEN_MAX, Math.round(islandOpenHeight)))
      return { width: ISLAND_OPEN_W, height, radius: 22 }
    }
    return { width: ISLAND_W, height: ISLAND_H, radius: ISLAND_H / 2 }
  }
  if (expanded) {
    // Stay at least as tall as the collapsed rail, so the top and bottom edges do not slide off the cursor.
    const height = Math.min(SIDE_OPEN_MAX, Math.max(ISLAND_W, Math.round(islandOpenHeight)))
    return { width: SIDE_OPEN_W, height, radius: 22 }
  }
  return { width: ISLAND_H, height: ISLAND_W, radius: ISLAND_H / 2 }
}

function islandBox() {
  return islandBoxFor(islandExpanded)
}

function readCovered() {
  try {
    const hwnd = island && !island.isDestroyed() ? island.getNativeWindowHandle().readBigUInt64LE(0) : null
    return Boolean(fullscreenCover(hwnd))
  } catch (error) {
    console.error('fullscreen watch', error)
    return islandCovered
  }
}

function publishCovered() {
  if (!island || island.isDestroyed()) return
  island.webContents.send('seychas:island-covered', islandCovered)
}

function applyCovered(covered) {
  if (!island || island.isDestroyed()) return
  if (covered) {
    islandExpanded = false
    liftIsland(island)
  }
  if (island.isVisible()) placeIsland(true)
  publishCovered()
}

function refreshCovered() {
  if (!islandWanted || !island || island.isDestroyed() || !island.isVisible()) return
  const covered = readCovered()
  if (covered) {
    coverMisses = 0
    if (islandCovered) {
      liftIsland(island)
      return
    }
    islandCovered = true
    applyCovered(true)
    return
  }
  if (!islandCovered) return
  coverMisses += 1
  if (coverMisses < 2) return
  islandCovered = false
  applyCovered(false)
}

let reshapeTimer = null

function scheduleReshape() {
  if (reshapeTimer) return
  reshapeTimer = setTimeout(() => {
    reshapeTimer = null
    if (!island || island.isDestroyed()) return
    shapeIsland(island, islandRadius, true)
  }, 32)
}

function cancelIslandAnim() {
  if (islandAnim) clearTimeout(islandAnim)
  islandAnim = null
  setSmoothTimers(false)
}

function islandTarget() {
  const point = anchor
    ? { x: Math.round(anchor.x + anchor.width / 2), y: anchor.y + 24 }
    : screen.getCursorScreenPoint()
  const { workArea } = screen.getDisplayNearestPoint(point)
  const box = islandBox()
  return placeBox(box, workArea)
}

function placeBox(box, workArea) {
  const margin = 10
  let width = box.width
  let height = box.height
  if (islandSide()) {
    height = Math.min(box.height, Math.max(ISLAND_H, workArea.height - 24))
    width = Math.min(box.width, Math.max(ISLAND_H, workArea.width - 24))
  } else {
    width = Math.min(box.width, Math.max(180, workArea.width - 24))
  }
  let x = Math.round(workArea.x + (workArea.width - width) / 2)
  let y = workArea.y + margin
  if (islandEdge === 'left') {
    x = workArea.x + margin
    y = Math.round(workArea.y + (workArea.height - height) / 2)
  } else if (islandEdge === 'right') {
    x = Math.round(workArea.x + workArea.width - width - margin)
    y = Math.round(workArea.y + (workArea.height - height) / 2)
  }
  return { x, y, width, height, radius: box.radius }
}

function pointerInIsland() {
  if (!island || island.isDestroyed() || !island.isVisible() || islandCovered) return false
  const cursor = screen.getCursorScreenPoint()
  const { workArea } = screen.getDisplayNearestPoint(cursor)
  const collapsed = placeBox(islandBoxFor(false), workArea)
  const expanded = placeBox(islandBoxFor(true), workArea)
  const pad = 12
  const left = Math.min(collapsed.x, expanded.x) - pad
  const top = Math.min(collapsed.y, expanded.y) - pad
  const right = Math.max(collapsed.x + collapsed.width, expanded.x + expanded.width) + pad
  const bottom = Math.max(collapsed.y + collapsed.height, expanded.y + expanded.height) + pad
  return cursor.x >= left && cursor.x < right && cursor.y >= top && cursor.y < bottom
}

function applyIslandFrame(frame) {
  if (!island || island.isDestroyed()) return
  islandRadius = frame.radius
  const key = `${frame.x}|${frame.y}|${frame.width}|${frame.height}|${Math.round(frame.radius * 2)}`
  if (key === islandPainted) return
  islandPainted = key
  island.setBounds({ x: frame.x, y: frame.y, width: frame.width, height: frame.height })
  shapeIsland(island, frame.radius)
}

function placeIsland(animated) {
  if (!island || island.isDestroyed()) return
  const to = islandTarget()
  const fromBounds = island.getBounds()
  const from = {
    x: fromBounds.x,
    y: fromBounds.y,
    width: fromBounds.width,
    height: fromBounds.height,
    radius: islandRadius,
  }
  const settled =
    from.x === to.x &&
    from.y === to.y &&
    from.width === to.width &&
    from.height === to.height &&
    Math.abs(from.radius - to.radius) < 0.4
  if (!animated || settled) {
    cancelIslandAnim()
    applyIslandFrame(to)
    return
  }
  cancelIslandAnim()
  const start = performance.now()
  let frameIndex = 0
  setSmoothTimers(true)
  const step = () => {
    islandAnim = null
    if (!island || island.isDestroyed() || !islandWanted) {
      setSmoothTimers(false)
      return
    }
    const now = performance.now()
    const t = Math.min(1, (now - start) / ISLAND_ANIM_MS)
    const e = 1 - (1 - t) ** 3
    applyIslandFrame({
      x: Math.round(from.x + (to.x - from.x) * e),
      y: Math.round(from.y + (to.y - from.y) * e),
      width: Math.max(1, Math.round(from.width + (to.width - from.width) * e)),
      height: Math.max(1, Math.round(from.height + (to.height - from.height) * e)),
      radius: from.radius + (to.radius - from.radius) * e,
    })
    if (t >= 1) {
      setSmoothTimers(false)
      scheduleReshape()
      return
    }
    frameIndex += 1
    let due = start + frameIndex * ISLAND_FRAME_MS
    if (performance.now() - due > ISLAND_FRAME_MS) {
      frameIndex = Math.ceil((performance.now() - start) / ISLAND_FRAME_MS)
      due = start + frameIndex * ISLAND_FRAME_MS
    }
    islandAnim = setTimeout(step, Math.max(0, due - performance.now()))
  }
  step()
}

function showIsland() {
  islandWanted = true
  if (!island || island.isDestroyed() || !islandReady) return
  islandCovered = readCovered()
  coverMisses = 0
  const wasVisible = island.isVisible()
  placeIsland(false)
  if (!wasVisible) {
    island.showInactive()
    shapeIsland(island, islandBox().radius)
  }
  if (islandCovered) liftIsland(island)
  publishCovered()
}

function hideIsland() {
  islandWanted = false
  islandExpanded = false
  islandCovered = false
  coverMisses = 0
  islandRadius = ISLAND_H / 2
  islandPainted = ''
  cancelIslandAnim()
  if (!island || island.isDestroyed() || !island.isVisible()) return
  island.hide()
}

function restoreMain() {
  if (quitting) return
  if (!win || win.isDestroyed()) {
    if (!island || island.isDestroyed()) {
      islandReady = false
      createIsland()
    }
    createWindow()
    return
  }
  if (win.isMinimized()) win.restore()
  win.show()
  win.focus()
  hideIsland()
}

function quitFromTray() {
  quitting = true
  app.quit()
}

function loadTrayImage() {
  const file = path.join(__dirname, '..', 'build', 'icon.ico')
  const image = nativeImage.createFromBuffer(fs.readFileSync(file))
  const sized = image.resize({ width: 16, height: 16 })
  return sized.isEmpty() ? image : sized
}

function createTray() {
  if (tray) return
  tray = new Tray(loadTrayImage())
  tray.setToolTip('Сейчас')
  tray.setContextMenu(
    Menu.buildFromTemplate([
      { label: 'Открыть', click: () => restoreMain() },
      { type: 'separator' },
      { label: 'Выйти', click: () => quitFromTray() },
    ]),
  )
  tray.on('click', () => restoreMain())
}

function tuckIntoIsland() {
  if (!win || win.isDestroyed()) return
  rememberAnchor()
  if (win.isFullScreen()) win.setFullScreen(false)
  win.hide()
  showIsland()
}

function createIsland() {
  island = new BrowserWindow({
    width: ISLAND_W,
    height: ISLAND_H,
    frame: false,
    transparent: true,
    roundedCorners: false,
    resizable: false,
    movable: false,
    minimizable: false,
    maximizable: false,
    skipTaskbar: true,
    alwaysOnTop: true,
    show: false,
    hasShadow: false,
    thickFrame: false,
    backgroundColor: '#00000000',
    title: 'Сейчас',
    webPreferences: {
      preload: path.join(__dirname, 'preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  })

  island.setAlwaysOnTop(true, 'floating')
  island.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true })
  island.webContents.setBackgroundThrottling(false)
  island.on('resize', () => scheduleReshape())

  island.once('ready-to-show', () => {
    islandReady = true
    if (islandWanted) showIsland()
  })

  island.on('close', (event) => {
    if (quitting) return
    event.preventDefault()
    restoreMain()
  })

  loadApp(island, true)
}

function createWindow() {
  win = new BrowserWindow({
    width: 1360,
    height: 900,
    minWidth: 980,
    minHeight: 700,
    backgroundColor: '#120e0b',
    title: 'Сейчас',
    icon: iconFile(),
    autoHideMenuBar: true,
    show: false,
    webPreferences: {
      preload: path.join(__dirname, 'preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  })

  // The session clock and chime keep running after the window is minimized.
  win.webContents.setBackgroundThrottling(false)

  win.once('ready-to-show', () => {
    rememberAnchor()
    win?.show()
  })

  win.webContents.setWindowOpenHandler(({ url }) => {
    shell.openExternal(url)
    return { action: 'deny' }
  })

  win.on('move', rememberAnchor)
  win.on('resize', rememberAnchor)
  win.on('minimize', () => showIsland())
  win.on('restore', () => hideIsland())
  win.on('show', () => {
    if (!win?.isMinimized()) hideIsland()
  })
  win.on('close', (event) => {
    if (quitting) return
    event.preventDefault()
    tuckIntoIsland()
  })
  win.on('closed', () => {
    win = null
    if (!quitting) return
    if (island && !island.isDestroyed()) island.destroy()
  })

  loadApp(win, false)
}

ipcMain.on('seychas:restore', () => {
  restoreMain()
})

ipcMain.on('seychas:island-expanded', (_event, expanded, height) => {
  if (islandCovered && expanded) return
  const next = Boolean(expanded)
  const nextHeight = Number.isFinite(height) ? height : islandOpenHeight
  if (next === islandExpanded && Math.round(nextHeight) === Math.round(islandOpenHeight)) return
  islandExpanded = next
  islandOpenHeight = nextHeight
  if (!islandWanted || !island || island.isDestroyed() || !island.isVisible()) return
  placeIsland(true)
})

ipcMain.handle('seychas:pointer-inside', () => pointerInIsland())

ipcMain.handle('seychas:covered', () => islandCovered)

ipcMain.on('seychas:island-edge', (_event, edge) => {
  const next = edge === 'left' || edge === 'right' ? edge : 'top'
  if (next === islandEdge) return
  islandEdge = next
  islandExpanded = false
  if (!islandWanted || !island || island.isDestroyed() || !island.isVisible()) return
  placeIsland(true)
})

ipcMain.on('seychas:theme', (_event, theme) => {
  const color = THEME_BG[theme] || THEME_BG.lamp
  if (!win || win.isDestroyed()) return
  win.setBackgroundColor(color)
})

ipcMain.handle('seychas:presence', () => latestCount)

ipcMain.handle('seychas:update-check', async () => {
  try {
    const update = await checkForUpdate(app.getVersion())
    if (!update) return { update: null }
    return { update: { version: update.version, current: update.current } }
  } catch (error) {
    console.error('update check', error)
    return { update: null }
  }
})

ipcMain.handle('seychas:update-install', async (event) => {
  try {
    const file = await downloadUpdate((ratio) => {
      if (event.sender.isDestroyed()) return
      event.sender.send('seychas:update-progress', ratio)
    })
    quitting = true
    const opened = await shell.openPath(file)
    if (opened) {
      quitting = false
      return { ok: false, message: 'Не получилось открыть установщик.' }
    }
    app.quit()
    return { ok: true }
  } catch (error) {
    console.error('update install', error)
    return { ok: false, message: 'Не получилось скачать обновление.' }
  }
})

const locked = app.requestSingleInstanceLock()
if (!locked) {
  app.quit()
} else {
  app.on('second-instance', () => {
    restoreMain()
  })
  app.whenReady().then(() => {
    createTray()
    createIsland()
    createWindow()
    screen.on('display-metrics-changed', () => {
      if (islandWanted) placeIsland()
    })
    coverTimer = setInterval(refreshCovered, 250)
    stopPresence = startPresence((count) => {
      latestCount = count
      if (win && !win.isDestroyed()) win.webContents.send('seychas:presence', count)
    })
  })
  app.on('before-quit', () => {
    quitting = true
  })
  app.on('will-quit', () => {
    cancelIslandAnim()
    stopPresence?.()
    stopPresence = null
    if (coverTimer) clearInterval(coverTimer)
    coverTimer = null
    tray?.destroy()
    tray = null
  })
  app.on('window-all-closed', () => {
    // The tray keeps the app alive. Quit only from the tray menu.
  })
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      quitting = false
      islandReady = false
      createIsland()
      createWindow()
    }
  })
}
