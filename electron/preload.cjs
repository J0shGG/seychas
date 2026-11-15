const { contextBridge, ipcRenderer } = require('electron')

contextBridge.exposeInMainWorld('seychasDesktop', true)
contextBridge.exposeInMainWorld('seychas', {
  restore: () => ipcRenderer.send('seychas:restore'),
  setExpanded: (expanded, height) => ipcRenderer.send('seychas:island-expanded', Boolean(expanded), height),
  covered: () => ipcRenderer.invoke('seychas:covered'),
  onCovered: (callback) => {
    const listener = (_event, covered) => callback(Boolean(covered))
    ipcRenderer.on('seychas:island-covered', listener)
    return () => ipcRenderer.removeListener('seychas:island-covered', listener)
  },
})
