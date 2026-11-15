const { contextBridge } = require('electron')

contextBridge.exposeInMainWorld('seychasDesktop', true)
