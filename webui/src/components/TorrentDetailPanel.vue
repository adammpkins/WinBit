<template>
  <div class="detail-panel">
    <!-- Tab bar matches WinBit's properties pivot style -->
    <div class="tab-bar">
      <button
        v-for="tab in tabs"
        :key="tab"
        class="tab-btn"
        :class="{ active: activeTab === tab }"
        @click="activeTab = tab"
      >{{ tab }}</button>
      <div class="tab-spacer" />
      <button class="close-btn fi" @click="$emit('close')" title="Close">&#xE711;</button>
    </div>

    <!-- General tab -->
    <div v-if="activeTab === 'General'" class="tab-content">
      <div class="info-grid">
        <div class="info-row">
          <span class="info-label">Info hash</span>
          <span class="info-value mono">{{ torrent.hash ?? '—' }}</span>
        </div>
        <div class="info-row">
          <span class="info-label">Save path</span>
          <span class="info-value" :title="torrent.save_path">{{ torrent.save_path ?? '—' }}</span>
        </div>
        <div class="info-row">
          <span class="info-label">Comment</span>
          <span class="info-value">{{ torrent.comment || '—' }}</span>
        </div>
        <div class="info-row">
          <span class="info-label">Downloaded</span>
          <span class="info-value mono">{{ fmtSize(torrent.downloaded) }} / {{ fmtSize(torrent.size) }}</span>
        </div>
        <div class="info-row">
          <span class="info-label">Uploaded</span>
          <span class="info-value mono">{{ fmtSize(torrent.uploaded) }}</span>
        </div>
        <div class="info-row">
          <span class="info-label">Seeds</span>
          <span class="info-value mono">{{ torrent.num_seeds ?? '—' }}</span>
        </div>
        <div class="info-row">
          <span class="info-label">Peers</span>
          <span class="info-value mono">{{ torrent.num_leechs ?? '—' }}</span>
        </div>
        <div class="info-row">
          <span class="info-label">Ratio</span>
          <span class="info-value mono">{{ torrent.ratio != null ? torrent.ratio.toFixed(3) : '—' }}</span>
        </div>
        <div class="info-row">
          <span class="info-label">Added on</span>
          <span class="info-value mono">{{ fmtDate(torrent.added_on) }}</span>
        </div>
        <div class="info-row">
          <span class="info-label">Completed on</span>
          <span class="info-value mono">{{ fmtDate(torrent.completion_on) }}</span>
        </div>
      </div>
    </div>

    <!-- Trackers tab -->
    <div v-else-if="activeTab === 'Trackers'" class="tab-content">
      <div v-if="!trackers.length" class="tab-empty">No trackers</div>
      <table v-else class="detail-table">
        <thead><tr><th>URL</th><th>Status</th><th>Seeds</th><th>Leeches</th><th>Message</th></tr></thead>
        <tbody>
          <tr v-for="t in trackers" :key="t.url">
            <td class="mono url-cell">{{ t.url }}</td>
            <td><span :class="'tracker-status s' + t.status">{{ trackerStatusLabel(t.status) }}</span></td>
            <td class="mono">{{ t.num_seeds ?? '—' }}</td>
            <td class="mono">{{ t.num_leeches ?? '—' }}</td>
            <td class="msg-cell">{{ t.msg }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Peers tab -->
    <div v-else-if="activeTab === 'Peers'" class="tab-content">
      <div v-if="!peers.length" class="tab-empty">No connected peers</div>
      <table v-else class="detail-table">
        <thead><tr><th>IP</th><th>Client</th><th>Progress</th><th>↓ Speed</th><th>↑ Speed</th><th>Flags</th></tr></thead>
        <tbody>
          <tr v-for="p in peers" :key="p.ip + ':' + p.port">
            <td class="mono">{{ p.ip }}:{{ p.port }}</td>
            <td>{{ p.client || '—' }}</td>
            <td class="mono">{{ (p.progress * 100).toFixed(1) }}%</td>
            <td class="mono">{{ fmtSpeed(p.dl_speed) }}</td>
            <td class="mono">{{ fmtSpeed(p.up_speed) }}</td>
            <td class="mono flags">{{ p.flags }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Content tab -->
    <div v-else-if="activeTab === 'Content'" class="tab-content">
      <div v-if="!files.length" class="tab-empty">No file info available</div>
      <table v-else class="detail-table">
        <thead><tr><th>Name</th><th>Size</th><th>Progress</th></tr></thead>
        <tbody>
          <tr v-for="f in files" :key="f.index">
            <td class="name-cell" :title="f.name">{{ f.name }}</td>
            <td class="mono">{{ fmtSize(f.size) }}</td>
            <td class="mono">{{ (f.progress * 100).toFixed(1) }}%</td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Pieces tab -->
    <div v-else-if="activeTab === 'Pieces'" class="tab-content">
      <div v-if="!pieces.length" class="tab-empty">No piece data</div>
      <div v-else class="pieces-bar">
        <div
          v-for="(state, i) in pieces"
          :key="i"
          class="piece"
          :class="state === 2 ? 'piece-done' : state === 1 ? 'piece-dl' : 'piece-miss'"
        />
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, watch, onMounted, onUnmounted } from 'vue'
import { api } from '../api/index.js'

const props = defineProps({ torrent: { type: Object, required: true } })
defineEmits(['close'])

const tabs = ['General', 'Trackers', 'Peers', 'Content', 'Pieces']
const activeTab = ref('General')
const trackers = ref([])
const peers = ref([])
const files = ref([])
const pieces = ref([])
let pollTimer = null

async function loadTabData() {
  if (!props.torrent?.hash) return
  const hash = props.torrent.hash
  try {
    if (activeTab.value === 'Trackers') {
      trackers.value = await api.getTrackers(hash) ?? []
    } else if (activeTab.value === 'Peers') {
      const r = await api.getPeers(hash)
      peers.value = r?.peers ? Object.values(r.peers) : []
    } else if (activeTab.value === 'Content') {
      files.value = await api.getFiles(hash) ?? []
    } else if (activeTab.value === 'Pieces') {
      pieces.value = await api.getPieceStates(hash) ?? []
    }
  } catch { /* non-fatal */ }
}

watch([activeTab, () => props.torrent?.hash], () => {
  loadTabData()
}, { immediate: true })

onMounted(() => { pollTimer = setInterval(loadTabData, 3000) })
onUnmounted(() => { clearInterval(pollTimer) })

function trackerStatusLabel(s) {
  return ['Disabled', 'Not contacted', 'Working', 'Updating', 'Not working'][s] ?? 'Unknown'
}

function fmtSize(bytes) {
  if (!bytes) return '—'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let i = 0; let v = bytes
  while (v >= 1024 && i < units.length - 1) { v /= 1024; i++ }
  return v.toFixed(i === 0 ? 0 : 1) + ' ' + units[i]
}

function fmtDate(unix) {
  if (!unix || unix <= 0) return '—'
  return new Date(unix * 1000).toLocaleString()
}

function fmtSpeed(bps) {
  if (!bps || bps < 512) return bps > 0 ? bps + ' B/s' : '0 B/s'
  const units = ['B/s', 'KB/s', 'MB/s', 'GB/s']
  let i = 0, v = bps
  while (v >= 1024 && i < units.length - 1) { v /= 1024; i++ }
  return v.toFixed(1) + ' ' + units[i]
}
</script>

<style scoped>
.detail-panel {
  flex-shrink: 0;
  height: 220px;
  display: flex;
  flex-direction: column;
  border-top: 1px solid rgba(255, 255, 255, 0.08);
  background: rgba(255, 255, 255, 0.02);
  font-family: "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif;
}

/* Tab bar — mirrors WinBit's properties pivot */
.tab-bar {
  display: flex;
  align-items: flex-end;
  height: 36px;
  padding: 0 12px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  gap: 0;
  flex-shrink: 0;
}

.tab-btn {
  background: none;
  border: none;
  border-bottom: 2px solid transparent;
  color: rgba(255, 255, 255, 0.55);
  font-size: 12px;
  font-family: inherit;
  font-weight: 400;
  padding: 0 12px 8px;
  cursor: pointer;
  transition: color 0.12s ease, border-color 0.12s ease;
  white-space: nowrap;
}
.tab-btn:hover { color: rgba(255, 255, 255, 0.85); }
.tab-btn.active {
  color: #fff;
  border-bottom-color: var(--accent, #744DA9);
  font-weight: 600;
}

.tab-spacer { flex: 1; }

.close-btn {
  background: none;
  border: none;
  color: rgba(255, 255, 255, 0.4);
  font-size: 12px;
  cursor: pointer;
  padding: 0 4px 8px;
  line-height: 1;
  transition: color 0.12s ease;
  align-self: flex-end;
}
.close-btn:hover { color: rgba(255, 255, 255, 0.8); }

/* Tab content area */
.tab-content {
  flex: 1;
  overflow-y: auto;
  padding: 10px 16px;
}

/* General tab: responsive grid of label+value pairs */
.info-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: 10px 24px;
}

.info-row {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.info-label {
  font-size: 10px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: rgba(255, 255, 255, 0.4);
}

.info-value {
  font-size: 12px;
  color: rgba(255, 255, 255, 0.9);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.mono { font-family: "Cascadia Code", "Consolas", monospace; }

/* Shared table styles for Trackers, Peers, Content tabs */
.detail-table { width: 100%; border-collapse: collapse; font-size: 12px; }
.detail-table th { text-align: left; padding: 4px 8px; color: rgba(255,255,255,0.5); border-bottom: 1px solid rgba(255,255,255,0.1); font-weight: 600; font-size: 11px; text-transform: uppercase; }
.detail-table td { padding: 4px 8px; border-bottom: 1px solid rgba(255,255,255,0.05); }
.detail-table tr:hover td { background: rgba(255,255,255,0.04); }

.tab-empty { color: rgba(255,255,255,0.35); font-size: 12px; padding: 16px; text-align: center; }

.url-cell { max-width: 200px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.msg-cell { max-width: 150px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: rgba(255,255,255,0.5); }
.name-cell { max-width: 300px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

/* Tracker status colour coding */
.tracker-status.s2 { color: #4ade80; }
.tracker-status.s4 { color: #f87171; }
.tracker-status.s3 { color: #facc15; }

.flags { letter-spacing: 0.05em; color: rgba(255,255,255,0.5); }

/* Pieces bar */
.pieces-bar { display: flex; flex-wrap: wrap; gap: 1px; padding: 8px; }
.piece { width: 4px; height: 8px; border-radius: 1px; }
.piece-done { background: var(--accent, #744da9); }
.piece-dl { background: #facc15; }
.piece-miss { background: rgba(255,255,255,0.12); }
</style>
