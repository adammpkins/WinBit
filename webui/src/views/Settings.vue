<template>
  <div class="settings-root">
    <div class="settings-header">
      <h1 class="page-title">Settings</h1>
      <div v-if="saved" class="save-badge"><span class="fi" style="margin-right:4px">&#xE73E;</span>Saved</div>
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
              class="accent-swatch accent-swatch--clear fi"
              :class="{ active: !prefs.accent_color }"
              @click="setPref('accent_color', null)"
              title="Default"
            >&#xE711;</div>
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

        <div class="setting-row">
          <div class="setting-info">
            <span class="setting-label">Pre-allocate Disk Space</span>
            <span class="setting-desc text-secondary">Reserve full file size on disk before downloading</span>
          </div>
          <div class="toggle" :class="{ on: prefs.preallocate_all }"
            @click="setPref('preallocate_all', !prefs.preallocate_all)">
            <div class="toggle-thumb" />
          </div>
        </div>

        <div class="setting-row">
          <div class="setting-info">
            <span class="setting-label">Auto Torrent Management</span>
            <span class="setting-desc text-secondary">Automatically move torrents based on category save paths</span>
          </div>
          <div class="toggle" :class="{ on: prefs.auto_tmm_enabled }"
            @click="setPref('auto_tmm_enabled', !prefs.auto_tmm_enabled)">
            <div class="toggle-thumb" />
          </div>
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
            <span class="setting-label">Username</span>
            <span class="setting-desc text-secondary">Web UI login username (min 3 characters)</span>
          </div>
          <input type="text" class="port-input" style="width: 160px; text-align: left; font-family: inherit"
            :value="prefs.web_ui_username ?? 'admin'"
            @change="setPref('web_ui_username', $event.target.value)" />
        </div>

        <div class="setting-row">
          <div class="setting-info">
            <span class="setting-label">Password</span>
            <span class="setting-desc text-secondary">New password (min 6 characters, leave blank to keep current)</span>
          </div>
          <div class="speed-input-wrap">
            <input type="password" class="port-input" style="width: 160px; text-align: left; font-family: inherit"
              v-model="newPassword"
              placeholder="New password"
              @keydown.enter="applyPassword" />
            <fluent-button appearance="lightweight" @click="applyPassword">Set</fluent-button>
          </div>
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
            <span class="fi" style="margin-right:6px">&#xE72C;</span>WebUI restarting… reconnecting
          </div>
        </Transition>
      </section>

      <!-- Speed -->
      <section class="settings-section panel">
        <h2 class="section-title">Speed</h2>

        <div class="setting-row">
          <div class="setting-info">
            <span class="setting-label">Download Limit</span>
            <span class="setting-desc text-secondary">Global download speed cap (0 = unlimited)</span>
          </div>
          <div class="speed-input-wrap">
            <input type="number" class="port-input" min="0"
              :value="bpsToKbps(prefs.dl_limit)"
              @change="setPref('dl_limit', kbpsToBps(+$event.target.value))" />
            <span class="speed-unit">kB/s</span>
          </div>
        </div>

        <div class="setting-row">
          <div class="setting-info">
            <span class="setting-label">Upload Limit</span>
            <span class="setting-desc text-secondary">Global upload speed cap (0 = unlimited)</span>
          </div>
          <div class="speed-input-wrap">
            <input type="number" class="port-input" min="0"
              :value="bpsToKbps(prefs.up_limit)"
              @change="setPref('up_limit', kbpsToBps(+$event.target.value))" />
            <span class="speed-unit">kB/s</span>
          </div>
        </div>

        <div class="setting-row">
          <div class="setting-info">
            <span class="setting-label">Alt Download Limit</span>
            <span class="setting-desc text-secondary">Alternative speed mode download cap</span>
          </div>
          <div class="speed-input-wrap">
            <input type="number" class="port-input" min="0"
              :value="bpsToKbps(prefs.alt_dl_limit)"
              @change="setPref('alt_dl_limit', kbpsToBps(+$event.target.value))" />
            <span class="speed-unit">kB/s</span>
          </div>
        </div>

        <div class="setting-row">
          <div class="setting-info">
            <span class="setting-label">Alt Upload Limit</span>
            <span class="setting-desc text-secondary">Alternative speed mode upload cap</span>
          </div>
          <div class="speed-input-wrap">
            <input type="number" class="port-input" min="0"
              :value="bpsToKbps(prefs.alt_up_limit)"
              @change="setPref('alt_up_limit', kbpsToBps(+$event.target.value))" />
            <span class="speed-unit">kB/s</span>
          </div>
        </div>

        <div class="setting-row">
          <div class="setting-info">
            <span class="setting-label">Schedule Bandwidth</span>
            <span class="setting-desc text-secondary">Auto-switch to alt-speed on a schedule</span>
          </div>
          <div class="toggle" :class="{ on: prefs.scheduler_enabled }"
            @click="setPref('scheduler_enabled', !prefs.scheduler_enabled)">
            <div class="toggle-thumb" />
          </div>
        </div>

        <template v-if="prefs.scheduler_enabled">
          <div class="setting-row">
            <div class="setting-info">
              <span class="setting-label">From</span>
              <span class="setting-desc text-secondary">Alt-speed start time</span>
            </div>
            <div class="time-inputs">
              <input type="number" class="port-input time-h" min="0" max="23"
                :value="prefs.schedule_from_hour ?? 8"
                @change="setPref('schedule_from_hour', +$event.target.value)" />
              <span class="time-sep">:</span>
              <input type="number" class="port-input time-m" min="0" max="59"
                :value="prefs.schedule_from_min ?? 0"
                @change="setPref('schedule_from_min', +$event.target.value)" />
            </div>
          </div>

          <div class="setting-row">
            <div class="setting-info">
              <span class="setting-label">To</span>
              <span class="setting-desc text-secondary">Alt-speed end time</span>
            </div>
            <div class="time-inputs">
              <input type="number" class="port-input time-h" min="0" max="23"
                :value="prefs.schedule_to_hour ?? 20"
                @change="setPref('schedule_to_hour', +$event.target.value)" />
              <span class="time-sep">:</span>
              <input type="number" class="port-input time-m" min="0" max="59"
                :value="prefs.schedule_to_min ?? 0"
                @change="setPref('schedule_to_min', +$event.target.value)" />
            </div>
          </div>

          <div class="setting-row">
            <div class="setting-info">
              <span class="setting-label">Days</span>
              <span class="setting-desc text-secondary">Which days the schedule applies</span>
            </div>
            <select class="days-select"
              :value="prefs.scheduler_days ?? 0"
              @change="setPref('scheduler_days', +$event.target.value)">
              <option value="0">Every day</option>
              <option value="1">Weekdays</option>
              <option value="2">Weekends</option>
              <option value="3">Monday</option>
              <option value="4">Tuesday</option>
              <option value="5">Wednesday</option>
              <option value="6">Thursday</option>
              <option value="7">Friday</option>
              <option value="8">Saturday</option>
              <option value="9">Sunday</option>
            </select>
          </div>
        </template>
      </section>

      <!-- Connection -->
      <section class="settings-section panel">
        <h2 class="section-title">Connection</h2>

        <div class="setting-row">
          <div class="setting-info">
            <span class="setting-label">Listen Port</span>
            <span class="setting-desc text-secondary">BitTorrent incoming connection port</span>
          </div>
          <input
            type="number"
            class="port-input"
            :value="prefs.listen_port ?? 6881"
            min="1"
            max="65535"
            @change="setPref('listen_port', +$event.target.value)"
          />
        </div>

        <div class="setting-row">
          <div class="setting-info">
            <span class="setting-label">UPnP / NAT-PMP</span>
            <span class="setting-desc text-secondary">Automatic port forwarding via UPnP</span>
          </div>
          <div
            class="toggle"
            :class="{ on: prefs.upnp ?? true }"
            @click="setPref('upnp', !(prefs.upnp ?? true))"
          >
            <div class="toggle-thumb" />
          </div>
        </div>
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

        <div class="setting-row">
          <div class="setting-info">
            <span class="setting-label">Encryption Mode</span>
            <span class="setting-desc text-secondary">MSE/PE protocol encryption for peer connections</span>
          </div>
          <select class="days-select"
            :value="prefs.encryption ?? 0"
            @change="setPref('encryption', +$event.target.value)">
            <option value="0">Prefer encryption</option>
            <option value="1">Require encryption</option>
            <option value="2">Disable encryption</option>
          </select>
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
const newPassword = ref('')

function applyPassword() {
  if (newPassword.value.length >= 6) {
    setPref('web_ui_password', newPassword.value)
    newPassword.value = ''
  }
}
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

function bpsToKbps(bps) { return bps ? Math.round(bps / 1024) : 0 }
function kbpsToBps(kbps) { return kbps ? kbps * 1024 : 0 }

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

.port-input {
  width: 90px;
  padding: 4px 8px;
  border-radius: 4px;
  border: 1px solid rgba(255, 255, 255, 0.15);
  background: rgba(255, 255, 255, 0.06);
  color: white;
  font-size: 13px;
  text-align: right;
  font-family: monospace;
}
.port-input:focus {
  outline: none;
  border-color: var(--accent, #744da9);
}

.speed-input-wrap { display: flex; align-items: center; gap: 6px; }
.speed-unit { font-size: 12px; color: rgba(255,255,255,0.4); white-space: nowrap; }
.time-inputs { display: flex; align-items: center; gap: 4px; }
.time-h { width: 52px !important; }
.time-m { width: 52px !important; }
.time-sep { color: rgba(255,255,255,0.5); font-size: 14px; font-weight: 600; }
.days-select {
  background: rgba(255,255,255,0.06);
  border: 1px solid rgba(255,255,255,0.12);
  border-radius: 4px;
  color: white;
  padding: 5px 8px;
  font-size: 13px;
  cursor: pointer;
}
.days-select:focus { outline: none; border-color: var(--accent); }

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
