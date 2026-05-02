<template>
  <div class="settings-root">
    <div class="settings-header">
      <h1 class="page-title">Settings</h1>
      <div v-if="saved" class="save-badge">✓ Saved</div>
    </div>

    <div v-if="!prefs" class="loading-state text-secondary">Loading settings…</div>

    <div v-else class="settings-body">

      <!-- Appearance -->
      <section class="settings-section panel">
        <h2 class="section-title">Appearance</h2>

        <div class="setting-row">
          <div class="setting-info">
            <span class="setting-label">Theme</span>
            <span class="setting-desc text-secondary">Interface color scheme</span>
          </div>
          <div class="theme-toggle">
            <button
              v-for="t in themes"
              :key="t.value"
              class="theme-btn"
              :class="{ active: prefs.theme === t.value }"
              @click="setPref('theme', t.value)"
            >{{ t.label }}</button>
          </div>
        </div>

        <div class="setting-row">
          <div class="setting-info">
            <span class="setting-label">Accent Color</span>
            <span class="setting-desc text-secondary">Highlight color (leave blank for default purple)</span>
          </div>
          <div class="accent-row">
            <div
              v-for="c in accentPresets"
              :key="c"
              class="accent-swatch"
              :class="{ active: prefs.accent_color === c }"
              :style="{ background: c }"
              @click="setPref('accent_color', c)"
            />
            <div
              class="accent-swatch accent-swatch--clear"
              :class="{ active: !prefs.accent_color }"
              @click="setPref('accent_color', null)"
              title="Default"
            >✕</div>
          </div>
        </div>
      </section>

      <!-- Downloads -->
      <section class="settings-section panel">
        <h2 class="section-title">Downloads</h2>

        <div class="setting-row">
          <div class="setting-info">
            <span class="setting-label">Default Save Path</span>
            <span class="setting-desc text-secondary">Where new torrents are downloaded</span>
          </div>
          <fluent-text-field
            :value="prefs.save_path"
            placeholder="C:\Downloads"
            class="setting-input"
            @change="setPref('save_path', $event.target.value)"
          />
        </div>
      </section>

      <!-- Web UI -->
      <section class="settings-section panel">
        <h2 class="section-title">Web UI</h2>

        <div class="setting-row">
          <div class="setting-info">
            <span class="setting-label">Port</span>
            <span class="setting-desc text-secondary">
              Kestrel listen port — WebUI restarts automatically
              <span v-if="portChanged" class="restart-hint"> · navigate to new port after save</span>
            </span>
          </div>
          <fluent-text-field
            :value="String(portDraft)"
            class="setting-input setting-input--sm"
            @change="portDraft = Number($event.target.value)"
            @blur="commitPort"
            @keyup.enter="commitPort"
          />
        </div>

        <div class="setting-row">
          <div class="setting-info">
            <span class="setting-label">Remote Access</span>
            <span class="setting-desc text-secondary">
              Allow connections from other devices on the network — WebUI restarts automatically
            </span>
          </div>
          <div
            class="toggle"
            :class="{ on: prefs.web_ui_enable_remote_access }"
            @click="setNetworkPref('web_ui_enable_remote_access', !prefs.web_ui_enable_remote_access)"
          >
            <div class="toggle-thumb" />
          </div>
        </div>

        <Transition name="slide-fade">
          <div v-if="restarting" class="restart-notice">
            ↻ WebUI restarting… reconnecting
          </div>
        </Transition>
      </section>

      <!-- BitTorrent -->
      <section class="settings-section panel">
        <h2 class="section-title">BitTorrent</h2>

        <div class="setting-row" v-for="item in btItems" :key="item.key">
          <div class="setting-info">
            <span class="setting-label">{{ item.label }}</span>
            <span class="setting-desc text-secondary">{{ item.desc }}</span>
          </div>
          <div
            class="toggle"
            :class="{ on: prefs[item.key] }"
            @click="setPref(item.key, !prefs[item.key])"
          >
            <div class="toggle-thumb" />
          </div>
        </div>
      </section>

    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useAppStore } from '../stores/app.js'

const appStore = useAppStore()
const { preferences } = storeToRefs(appStore)
const saved = ref(false)
const restarting = ref(false)
const portDraft = ref(0)
const portChanged = ref(false)
let saveTimer = null

const prefs = computed(() => preferences.value ? { ...preferences.value } : null)

watch(prefs, (p) => {
  if (p && portDraft.value === 0) portDraft.value = p.web_ui_port
}, { immediate: true })

const themes = [
  { label: 'System', value: 'System' },
  { label: 'Dark',   value: 'Dark' },
  { label: 'Light',  value: 'Light' },
]

const accentPresets = [
  '#7c6af7', '#0078d4', '#00b4d8', '#2dc653',
  '#ff6b35', '#e63946', '#f77f00', '#9b59b6',
]

const btItems = [
  { key: 'dht',  label: 'DHT',               desc: 'Distributed Hash Table peer discovery' },
  { key: 'pex',  label: 'Peer Exchange (PEX)', desc: 'Share peer lists between connected peers' },
  { key: 'lsd',  label: 'Local Service Discovery', desc: 'Find peers on your local network' },
]

async function setPref(key, value) {
  await appStore.savePreferences({ [key]: value })
  saved.value = true
  clearTimeout(saveTimer)
  saveTimer = setTimeout(() => { saved.value = false }, 2000)
}

async function setNetworkPref(key, value) {
  await appStore.savePreferences({ [key]: value })
  restarting.value = true
  // Poll until the server responds again after Kestrel restart (~500ms + startup)
  await pollReconnect()
  restarting.value = false
}

async function commitPort() {
  if (!prefs.value || portDraft.value === prefs.value.web_ui_port) return
  portChanged.value = portDraft.value !== prefs.value.web_ui_port
  await appStore.savePreferences({ web_ui_port: portDraft.value })
  restarting.value = true
  await pollReconnect()
  restarting.value = false
}

async function pollReconnect() {
  for (let i = 0; i < 20; i++) {
    await new Promise(r => setTimeout(r, 300))
    try {
      const res = await fetch('/api/v2/app/version')
      if (res.ok) return
    } catch { /* still down */ }
  }
}
</script>

<style scoped>
.settings-root { display: flex; flex-direction: column; height: 100%; padding: 20px; gap: 16px; overflow: hidden; }

.settings-header { display: flex; align-items: center; justify-content: space-between; height: var(--toolbar-height); }
.page-title { font-size: 18px; font-weight: 600; letter-spacing: -0.2px; }

.save-badge {
  font-size: 12px; font-weight: 500;
  color: var(--status-seed);
  background: rgba(102, 187, 106, 0.12);
  border: 1px solid rgba(102, 187, 106, 0.25);
  padding: 4px 10px; border-radius: 20px;
}

.loading-state { flex: 1; display: flex; align-items: center; justify-content: center; }

.settings-body { flex: 1; overflow-y: auto; display: flex; flex-direction: column; gap: 12px; }

.settings-section { padding: 18px 20px; display: flex; flex-direction: column; gap: 0; }

.section-title {
  font-size: 12px; font-weight: 600; text-transform: uppercase;
  letter-spacing: 0.07em; color: var(--text-accent);
  margin-bottom: 14px;
}

.setting-row {
  display: flex; align-items: center; justify-content: space-between;
  padding: 11px 0; gap: 16px;
  border-bottom: 1px solid var(--border-subtle);
}
.setting-row:last-child { border-bottom: none; }

.setting-info { display: flex; flex-direction: column; gap: 2px; }
.setting-label { font-size: 13px; font-weight: 450; }
.setting-desc { font-size: 11px; margin-top: 1px; }

.setting-input { min-width: 220px; }
.setting-input--sm { min-width: 90px; }

/* Theme toggle */
.theme-toggle { display: flex; gap: 4px; }
.theme-btn {
  background: var(--surface-1); border: 1px solid var(--border-subtle);
  color: var(--text-secondary); cursor: pointer; padding: 6px 14px;
  border-radius: var(--radius-sm); font-size: 12px; font-weight: 500;
  font-family: inherit; transition: background var(--t-fast), color var(--t-fast), border-color var(--t-fast);
}
.theme-btn:hover { background: var(--surface-2); color: var(--text-primary); }
.theme-btn.active {
  background: var(--surface-active); border-color: var(--border-accent);
  color: var(--text-accent);
}

/* Accent swatches */
.accent-row { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }
.accent-swatch {
  width: 24px; height: 24px; border-radius: 50%; cursor: pointer;
  border: 2px solid transparent; transition: transform var(--t-fast), border-color var(--t-fast);
}
.accent-swatch:hover { transform: scale(1.12); }
.accent-swatch.active { border-color: var(--text-primary); transform: scale(1.15); }
.accent-swatch--clear {
  background: var(--surface-2) !important;
  color: var(--text-tertiary); font-size: 11px;
  display: flex; align-items: center; justify-content: center;
  border-color: var(--border-subtle);
}

/* Restart notice */
.restart-notice {
  margin-top: 8px;
  padding: 8px 12px;
  background: rgba(124, 106, 247, 0.10);
  border: 1px solid var(--border-accent);
  border-radius: var(--radius-sm);
  font-size: 12px;
  color: var(--text-accent);
}
.restart-hint { color: var(--status-pause); }

.slide-fade-enter-active, .slide-fade-leave-active { transition: all 200ms ease; }
.slide-fade-enter-from, .slide-fade-leave-to { opacity: 0; transform: translateY(-6px); }

/* Toggle switch */
.toggle {
  width: 40px; height: 22px; border-radius: 11px;
  background: var(--surface-3); border: 1px solid var(--border-default);
  cursor: pointer; position: relative; flex-shrink: 0;
  transition: background var(--t-base), border-color var(--t-base);
}
.toggle.on { background: var(--accent); border-color: var(--accent); }
.toggle-thumb {
  position: absolute; top: 2px; left: 2px;
  width: 16px; height: 16px; border-radius: 50%;
  background: var(--text-secondary);
  transition: left var(--t-base), background var(--t-base);
}
.toggle.on .toggle-thumb { left: 20px; background: #fff; }
</style>
