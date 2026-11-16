const { contextBridge, ipcRenderer } = require('electron')

contextBridge.exposeInMainWorld('seychasDesktop', true)
contextBridge.exposeInMainWorld('seychas', {
  restore: () => ipcRenderer.send('seychas:restore'),
  setExpanded: (expanded, height) => ipcRenderer.send('seychas:island-expanded', Boolean(expanded), height),
  setEdge: (edge) => ipcRenderer.send('seychas:island-edge', edge),
  setTheme: (theme) => ipcRenderer.send('seychas:theme', theme),
  covered: () => ipcRenderer.invoke('seychas:covered'),
  onCovered: (callback) => {
    const listener = (_event, covered) => callback(Boolean(covered))
    ipcRenderer.on('seychas:island-covered', listener)
    return () => ipcRenderer.removeListener('seychas:island-covered', listener)
  },
  checkUpdate: () => ipcRenderer.invoke('seychas:update-check'),
  installUpdate: () => ipcRenderer.invoke('seychas:update-install'),
  onUpdateProgress: (callback) => {
    const listener = (_event, ratio) => callback(Number(ratio))
    ipcRenderer.on('seychas:update-progress', listener)
    return () => ipcRenderer.removeListener('seychas:update-progress', listener)
  },
  presence: () => ipcRenderer.invoke('seychas:presence'),
  onPresence: (callback) => {
    const listener = (_event, count) => callback(Number(count))
    ipcRenderer.on('seychas:presence', listener)
    return () => ipcRenderer.removeListener('seychas:presence', listener)
  },
})
