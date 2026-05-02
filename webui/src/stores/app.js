import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api } from '../api/index.js'

export const useAppStore = defineStore('app', () => {
  const isLoggedIn = ref(false)
  const version = ref('')
  const preferences = ref(null)

  async function checkAuth() {
    const data = await api.getMainData(0)
    if (data !== null) {
      isLoggedIn.value = true
      const v = await api.getVersion()
      version.value = v?.trim() ?? ''
    } else {
      isLoggedIn.value = false
    }
  }

  function setLoggedIn(val) {
    isLoggedIn.value = val
  }

  async function loadPreferences() {
    const prefs = await api.getPreferences()
    if (!prefs) return
    preferences.value = prefs
    applyThemeCss(prefs.theme)
    applyAccentCss(prefs.accent_color)
  }

  async function savePreferences(patch) {
    const body = JSON.stringify(patch)
    const res = await fetch('/api/v2/app/setPreferences', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body,
    })
    if (!res.ok) return false
    preferences.value = { ...(preferences.value ?? {}), ...patch }
    if ('theme' in patch) applyThemeCss(patch.theme)
    if ('accent_color' in patch) applyAccentCss(patch.accent_color)
    return true
  }

  return { isLoggedIn, version, preferences, checkAuth, setLoggedIn, loadPreferences, savePreferences }
})

function applyThemeCss(theme) {
  const t = (theme ?? 'System').toLowerCase()
  document.documentElement.setAttribute('data-theme', t)
}

function applyAccentCss(hex) {
  if (hex) {
    document.documentElement.style.setProperty('--accent', hex)
    document.documentElement.style.setProperty('--accent-glow', hex + '40')
    // Derive light/dark variants by lightening/darkening 15%
    document.documentElement.style.setProperty('--accent-light', lighten(hex, 0.15))
    document.documentElement.style.setProperty('--accent-dark', darken(hex, 0.15))
    document.documentElement.style.setProperty('--surface-active', hex + '24')
  } else {
    // Restore defaults
    document.documentElement.style.removeProperty('--accent')
    document.documentElement.style.removeProperty('--accent-glow')
    document.documentElement.style.removeProperty('--accent-light')
    document.documentElement.style.removeProperty('--accent-dark')
    document.documentElement.style.removeProperty('--surface-active')
  }
}

function hexToRgb(hex) {
  const h = hex.replace('#', '')
  return [parseInt(h.slice(0,2),16), parseInt(h.slice(2,4),16), parseInt(h.slice(4,6),16)]
}
function rgbToHex(r, g, b) {
  return '#' + [r, g, b].map(v => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2,'0')).join('')
}
function lighten(hex, amount) {
  const [r,g,b] = hexToRgb(hex)
  return rgbToHex(r + (255-r)*amount, g + (255-g)*amount, b + (255-b)*amount)
}
function darken(hex, amount) {
  const [r,g,b] = hexToRgb(hex)
  return rgbToHex(r*(1-amount), g*(1-amount), b*(1-amount))
}
