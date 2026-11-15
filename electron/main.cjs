const { app, BrowserWindow, Menu, Tray, ipcMain, nativeImage, screen, shell } = require('electron')
const fs = require('node:fs')
const path = require('node:path')
const { fullscreenCover, liftIsland, shapeIsland } = require('./frame.cjs')

const APP_ID = 'app.seychas.desktop'
const ISLAND_W = 292
const ISLAND_H = 48
const ISLAND_OPEN_W = 340
const ISLAND_OPEN_MIN = 120
const ISLAND_OPEN_MAX = 220

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
let islandAnim = null
let islandCovered = false
let coverMisses = 0
let coverTimer = null
let quitting = false
const ISLAND_ANIM_MS = 320

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

function islandBox() {
  if (islandExpanded) {
    const height = Math.max(ISLAND_OPEN_MIN, Math.min(ISLAND_OPEN_MAX, Math.round(islandOpenHeight)))
    return { width: ISLAND_OPEN_W, height, radius: 22 }
  }
  if (islandCovered) return { width: ISLAND_H, height: ISLAND_H, radius: ISLAND_H / 2 }
  return { width: ISLAND_W, height: ISLAND_H, radius: ISLAND_H / 2 }
}

function readCovered() {
  try {
    return Boolean(fullscreenCover())
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

function cancelIslandAnim() {
  if (!islandAnim) return
  clearTimeout(islandAnim)
  islandAnim = null
}

function islandTarget() {
  const point = anchor
    ? { x: Math.round(anchor.x + anchor.width / 2), y: anchor.y + 24 }
    : screen.getCursorScreenPoint()
  const { workArea } = screen.getDisplayNearestPoint(point)
  const box = islandBox()
  const width = Math.min(box.width, Math.max(180, workArea.width - 24))
  const x = Math.round(workArea.x + (workArea.width - width) / 2)
  return { x, y: workArea.y + 10, width, height: box.height, radius: box.radius }
}

function applyIslandFrame(frame) {
  if (!island || island.isDestroyed()) return
  islandRadius = frame.radius
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
  const start = Date.now()
  const step = () => {
    islandAnim = null
    if (!island || island.isDestroyed() || !islandWanted) return
    const t = Math.min(1, (Date.now() - start) / ISLAND_ANIM_MS)
    const e = 1 - (1 - t) ** 3
    applyIslandFrame({
      x: Math.round(from.x + (to.x - from.x) * e),
      y: Math.round(from.y + (to.y - from.y) * e),
      width: Math.max(1, Math.round(from.width + (to.width - from.width) * e)),
      height: Math.max(1, Math.round(from.height + (to.height - from.height) * e)),
      radius: from.radius + (to.radius - from.radius) * e,
    })
    if (t < 1) islandAnim = setTimeout(step, 16)
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
  islandExpanded = Boolean(expanded)
  if (Number.isFinite(height)) islandOpenHeight = height
  if (!islandWanted || !island || island.isDestroyed() || !island.isVisible()) return
  placeIsland(true)
})

ipcMain.handle('seychas:covered', () => islandCovered)

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
  })
  app.on('before-quit', () => {
    quitting = true
  })
  app.on('will-quit', () => {
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
