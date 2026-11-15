const { app, BrowserWindow, Menu, shell } = require('electron')
const fs = require('node:fs')
const path = require('node:path')

const APP_ID = 'app.seychas.desktop'
app.setAppUserModelId(APP_ID)
Menu.setApplicationMenu(null)

/** @type {import('electron').BrowserWindow | null} */
let win = null

function iconFile() {
  if (app.isPackaged) return undefined
  const candidates = [
    path.join(__dirname, '..', 'build', 'icon.ico'),
    path.join(__dirname, '..', 'build', 'icon.png'),
    path.join(__dirname, '..', 'public', 'icon.jpg'),
  ]
  return candidates.find((file) => fs.existsSync(file))
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

  win.once('ready-to-show', () => win?.show())

  win.webContents.setWindowOpenHandler(({ url }) => {
    shell.openExternal(url)
    return { action: 'deny' }
  })

  if (app.isPackaged) {
    win.loadFile(path.join(__dirname, '..', 'dist', 'index.html'))
  } else {
    win.loadURL('http://127.0.0.1:5173')
  }

  win.on('closed', () => {
    win = null
  })
}

const locked = app.requestSingleInstanceLock()
if (!locked) {
  app.quit()
} else {
  app.on('second-instance', () => {
    if (!win) return
    if (win.isMinimized()) win.restore()
    win.show()
    win.focus()
  })
  app.whenReady().then(createWindow)
  app.on('window-all-closed', () => {
    if (process.platform !== 'darwin') app.quit()
  })
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow()
  })
}
