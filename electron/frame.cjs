const koffi = require('koffi')
const { screen } = require('electron')

const gdi32 = koffi.load('gdi32.dll')
const user32 = koffi.load('user32.dll')
const dwmapi = koffi.load('dwmapi.dll')

const CreateRoundRectRgn = gdi32.func(
  'void * __stdcall CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse)',
)
const SetWindowRgn = user32.func('int __stdcall SetWindowRgn(void *hwnd, void *rgn, int redraw)')
const DwmSetWindowAttribute = dwmapi.func(
  'int __stdcall DwmSetWindowAttribute(void *hwnd, uint attribute, void *value, uint size)',
)

const DWMWA_WINDOW_CORNER_PREFERENCE = 33
const DWMWCP_DONOTROUND = 1
const DWMWA_BORDER_COLOR = 34
const DWMWA_COLOR_NONE = 0xfffffffe
const DWMWA_CLOAKED = 14
const HWND_TOPMOST = -1n
const SWP_NOSIZE = 0x0001
const SWP_NOMOVE = 0x0002
const SWP_NOACTIVATE = 0x0010

const shell32 = koffi.load('shell32.dll')

const EnumWindowsProc = koffi.proto('int __stdcall EnumWindowsProc(void *hwnd, intptr lParam)')
const EnumWindows = user32.func('int __stdcall EnumWindows(EnumWindowsProc *cb, intptr lParam)')
const GetWindowRect = user32.func('int __stdcall GetWindowRect(void *hwnd, void *rect)')
const GetClientRect = user32.func('int __stdcall GetClientRect(void *hwnd, void *rect)')
const ClientToScreen = user32.func('int __stdcall ClientToScreen(void *hwnd, void *point)')
const IsWindowVisible = user32.func('int __stdcall IsWindowVisible(void *hwnd)')
const IsIconic = user32.func('int __stdcall IsIconic(void *hwnd)')
const GetWindowThreadProcessId = user32.func('uint32 __stdcall GetWindowThreadProcessId(void *hwnd, void *pid)')
const GetClassNameW = user32.func('int __stdcall GetClassNameW(void *hwnd, void *name, int max)')
const GetWindowLongPtrW = user32.func('intptr __stdcall GetWindowLongPtrW(void *hwnd, int index)')
const MonitorFromWindow = user32.func('void * __stdcall MonitorFromWindow(void *hwnd, uint flags)')
const GetMonitorInfoW = user32.func('int __stdcall GetMonitorInfoW(void *monitor, void *info)')
const SetWindowPos = user32.func(
  'int __stdcall SetWindowPos(void *hwnd, intptr insertAfter, int x, int y, int cx, int cy, uint flags)',
)
const DwmGetWindowAttribute = dwmapi.func(
  'int __stdcall DwmGetWindowAttribute(void *hwnd, uint attribute, void *value, uint size)',
)
const SHQueryUserNotificationState = shell32.func('long __stdcall SHQueryUserNotificationState(void *state)')

// Desktop and shell hosts are the size of the screen, but they are not apps.
const SHELL_CLASSES = new Set([
  'Progman',
  'WorkerW',
  'Shell_TrayWnd',
  'Shell_SecondaryTrayWnd',
  'DV2ControlHost',
  'CEF-OSC-WIDGET',
])
const WS_EX_TRANSPARENT = 0x20n
const WS_EX_NOACTIVATE = 0x08000000n
const DWMWA_EXTENDED_FRAME_BOUNDS = 9
const QUNS_RUNNING_D3D_FULL_SCREEN = 3
const QUNS_PRESENTATION_MODE = 4

function windowClass(hwnd) {
  const buf = Buffer.alloc(512)
  const length = GetClassNameW(hwnd, buf, 255)
  if (!length) return ''
  return buf.toString('utf16le', 0, length * 2)
}

function windowPid(hwnd) {
  const buf = Buffer.alloc(4)
  GetWindowThreadProcessId(hwnd, buf)
  return buf.readUInt32LE(0)
}

function readRect(buf) {
  const left = buf.readInt32LE(0)
  const top = buf.readInt32LE(4)
  const right = buf.readInt32LE(8)
  const bottom = buf.readInt32LE(12)
  if (right - left < 200 || bottom - top < 200) return null
  return { left, top, right, bottom }
}

function windowRect(hwnd) {
  const buf = Buffer.alloc(16)
  if (!GetWindowRect(hwnd, buf)) return null
  return readRect(buf)
}

function clientRect(hwnd) {
  const rect = Buffer.alloc(16)
  const origin = Buffer.alloc(8)
  if (!GetClientRect(hwnd, rect) || !ClientToScreen(hwnd, origin)) return null
  const width = rect.readInt32LE(8)
  const height = rect.readInt32LE(12)
  if (width < 200 || height < 200) return null
  const left = origin.readInt32LE(0)
  const top = origin.readInt32LE(4)
  return { left, top, right: left + width, bottom: top + height }
}

function extendedFrame(hwnd) {
  const buf = Buffer.alloc(16)
  if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, buf, 16) !== 0) return null
  return readRect(buf)
}

function isCloaked(hwnd) {
  const buf = Buffer.alloc(4)
  const hr = DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, buf, 4)
  return hr === 0 && buf.readUInt32LE(0) !== 0
}

function exStyle(hwnd) {
  try {
    return BigInt(GetWindowLongPtrW(hwnd, -20))
  } catch {
    return 0n
  }
}

function monitorAreas(hwnd) {
  const monitor = MonitorFromWindow(hwnd, 2)
  if (!monitor) return null
  const info = Buffer.alloc(40)
  info.writeUInt32LE(40, 0)
  if (!GetMonitorInfoW(monitor, info)) return null
  const read = (offset) => ({
    left: info.readInt32LE(offset),
    top: info.readInt32LE(offset + 4),
    right: info.readInt32LE(offset + 8),
    bottom: info.readInt32LE(offset + 12),
  })
  return { screen: read(4), work: read(20) }
}

function windowStyle(hwnd) {
  try {
    return BigInt(GetWindowLongPtrW(hwnd, -16))
  } catch {
    return 0n
  }
}

function coversMonitor(rect, monitor) {
  if (!rect || !monitor) return false
  const width = monitor.right - monitor.left
  const height = monitor.bottom - monitor.top
  // Keep this under a taskbar, so a maximized window does not count.
  const tolX = Math.min(32, Math.max(16, Math.round(width * 0.015)))
  const tolY = Math.min(32, Math.max(16, Math.round(height * 0.015)))
  return (
    rect.left <= monitor.left + tolX &&
    rect.top <= monitor.top + tolY &&
    rect.right >= monitor.right - tolX &&
    rect.bottom >= monitor.bottom - tolY
  )
}

function gameOrPresentation() {
  const buf = Buffer.alloc(4)
  if (SHQueryUserNotificationState(buf) !== 0) return false
  const state = buf.readInt32LE(0)
  return state === QUNS_RUNNING_D3D_FULL_SCREEN || state === QUNS_PRESENTATION_MODE
}

function windowCoversItsMonitor(hwnd) {
  if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || windowPid(hwnd) === process.pid || isCloaked(hwnd)) return false
  const style = exStyle(hwnd)
  if (style & WS_EX_TRANSPARENT || style & WS_EX_NOACTIVATE) return false
  const name = windowClass(hwnd)
  if (!name || SHELL_CLASSES.has(name)) return false
  const areas = monitorAreas(hwnd)
  if (!areas) return false
  const rects = [windowRect(hwnd), extendedFrame(hwnd), clientRect(hwnd)]
  if (rects.some((rect) => coversMonitor(rect, areas.screen))) return true
  // Borderless players often cover only the work area and leave the taskbar.
  const borderless = (windowStyle(hwnd) & 0x00c00000n) === 0n
  return borderless && rects.some((rect) => coversMonitor(rect, areas.work))
}

// Any real app that covers its monitor counts, even when another window has focus.
function fullscreenCover() {
  if (process.platform !== 'win32') return null
  try {
    if (gameOrPresentation()) return 'fullscreen'
    let found = null
    EnumWindows((hwnd) => {
      if (found) return 1
      try {
        if (windowCoversItsMonitor(hwnd)) found = windowClass(hwnd) || 'window'
      } catch {
        // The window can close while the list is walking.
      }
      return 1
    }, 0)
    return found
  } catch {
    return null
  }
}

function liftIsland(win) {
  if (process.platform !== 'win32' || !win || win.isDestroyed()) return
  try {
    const hwnd = win.getNativeWindowHandle().readBigUInt64LE(0)
    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE)
  } catch (error) {
    console.error('island lift', error)
  }
}

function shapeIsland(win, radiusDip) {
  if (process.platform !== 'win32' || !win || win.isDestroyed()) return null
  try {
    return shapeIslandNow(win, radiusDip)
  } catch (error) {
    console.error('island shape', error)
    return { error: String(error) }
  }
}

function shapeIslandNow(win, radiusDip) {
  const hwnd = win.getNativeWindowHandle().readBigUInt64LE(0)
  const corner = Buffer.alloc(4)
  corner.writeInt32LE(DWMWCP_DONOTROUND)
  const cornerHr = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, corner, 4)
  const border = Buffer.alloc(4)
  border.writeUInt32LE(DWMWA_COLOR_NONE)
  const borderHr = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, border, 4)

  const dip = win.getBounds()
  const px = screen.dipToScreenRect(win, { x: 0, y: 0, width: dip.width, height: dip.height })
  if (px.width < 8 || px.height < 8) return { cornerHr, borderHr, px }
  const scale = dip.height > 0 ? px.height / dip.height : 1
  const radius = Number.isFinite(radiusDip) ? radiusDip : dip.height / 2
  const ellipse = Math.max(2, Math.round(Math.min(px.height, radius * 2 * scale)))
  // Right and bottom are exclusive. A diameter equal to the height makes a pill.
  const region = CreateRoundRectRgn(0, 0, px.width, px.height, ellipse, ellipse)
  const regionOk = SetWindowRgn(hwnd, region, 1)
  return { cornerHr, borderHr, regionOk, px, dip }
}

module.exports = { shapeIsland, fullscreenCover, liftIsland }
