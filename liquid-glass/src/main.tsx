import React from 'react'
import {createRoot} from 'react-dom/client'
import App from './App'
import './glass.css'
import './style.css'
// Load the local button font before layout; the first hover must not swap glyphs.
document.fonts.load('500 12px "DanmuCinema UI"', '搜索弹幕联合搜索').catch(error => {
  console.error('Unable to load the bundled button font', error)
}).finally(() => createRoot(document.getElementById('root')!).render(<App/>))
