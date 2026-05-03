<template>
  <div class="log-root">
    <div class="log-toolbar">
      <div class="log-title-group">
        <h1 class="page-title">Log</h1>
        <span class="entry-count text-secondary">{{ filteredLogs.length }} entries</span>
      </div>
      <div class="toolbar-filters">
        <div class="filter-toggle">
          <button
            class="theme-btn"
            :class="{ active: filter === 'all' }"
            @click="filter = 'all'"
          >All</button>
          <button
            class="theme-btn"
            :class="{ active: filter === 'warn' }"
            @click="filter = 'warn'"
          >Warnings</button>
          <button
            class="theme-btn"
            :class="{ active: filter === 'err' }"
            @click="filter = 'err'"
          >Errors</button>
        </div>
        <div class="filter-toggle">
          <button
            class="theme-btn"
            :class="{ active: timeFilter === 'all' }"
            @click="timeFilter = 'all'"
          >All time</button>
          <button
            class="theme-btn"
            :class="{ active: timeFilter === 'today' }"
            @click="timeFilter = 'today'"
          >Today</button>
          <button
            class="theme-btn"
            :class="{ active: timeFilter === 'hour' }"
            @click="timeFilter = 'hour'"
          >Last hour</button>
        </div>
      </div>
    </div>
    <div class="log-panel panel">
      <div v-if="!filteredLogs.length" class="log-empty">
        <span class="fi" style="opacity:0.4; font-size:24px">&#xE9F9;</span>
        <p class="text-secondary" style="margin-top:8px">No log entries</p>
      </div>
      <div v-else class="log-entries" ref="scrollEl">
        <div
          v-for="entry in filteredLogs"
          :key="entry.id"
          class="log-entry"
          :class="[severityClass(entry.type), { selected: selectedLog?.id === entry.id }]"
          @click="selectLog(entry)"
        >
          <span class="log-time mono">{{ formatTime(entry.timestamp) }}</span>
          <span class="log-msg" :title="entry.message">{{ entry.message }}</span>
        </div>
      </div>
    </div>
    <div v-if="selectedLog" class="log-detail-pane">
      <div class="log-detail-header">
        <span class="log-detail-ts">{{ fmtFullDate(selectedLog.timestamp) }}</span>
        <span class="log-detail-badge" :class="levelClass(selectedLog.type)">{{ levelLabel(selectedLog.type) }}</span>
        <button class="log-detail-close fi" @click="selectedLog = null">&#xE711;</button>
      </div>
      <div class="log-detail-body">{{ selectedLog.message }}</div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted, nextTick } from 'vue'
import { api } from '../api/index.js'

const logs = ref([])
const scrollEl = ref(null)
const filter = ref('all')
const timeFilter = ref('all')
const selectedLog = ref(null)

// lastKnownId tracks the highest id seen so each poll only fetches new entries.
// The server returns entries with id > lastKnownId when the param is supplied.
const lastKnownId = ref(-1)

// LogSeverity flag bits: Normal=1, Info=2, Warning=4, Critical=8.
const filteredLogs = computed(() => {
  let list = logs.value

  if (filter.value === 'warn') list = list.filter(e => e.type === 4)
  if (filter.value === 'err')  list = list.filter(e => e.type === 8)

  if (timeFilter.value === 'today') {
    // Calendar-day boundary — not a rolling 24h window.
    const todayStart = new Date().setHours(0, 0, 0, 0)
    list = list.filter(e => e.timestamp >= todayStart)
  } else if (timeFilter.value === 'hour') {
    const cutoff = Date.now() - 3_600_000
    list = list.filter(e => e.timestamp >= cutoff)
  }

  return list
})

async function poll() {
  const entries = await api.getLogs(lastKnownId.value)
  if (!entries || entries.length === 0) return

  logs.value.push(...entries)

  // Track the highest id so the next incremental fetch starts after it.
  const maxId = entries.reduce((m, e) => Math.max(m, e.id), lastKnownId.value)
  lastKnownId.value = maxId

  await nextTick()
  if (scrollEl.value) scrollEl.value.scrollTop = scrollEl.value.scrollHeight
}

let timerId = null

onMounted(async () => {
  await poll()
  timerId = setInterval(poll, 3000)
})

onUnmounted(() => {
  clearInterval(timerId)
})

function formatTime(ms) {
  return new Date(ms).toLocaleTimeString()
}

function severityClass(type) {
  if (type === 4) return 'log-warn'
  if (type === 8) return 'log-err'
  return ''
}

function selectLog(entry) {
  selectedLog.value = selectedLog.value?.id === entry.id ? null : entry
}

function fmtFullDate(ms) {
  return new Date(ms).toLocaleString()
}

function levelClass(type) {
  if (type === 4) return 'lvl-warn'
  if (type === 8) return 'lvl-error'
  return 'lvl-info'
}

function levelLabel(type) {
  if (type === 4) return 'Warning'
  if (type === 8) return 'Critical'
  return 'Info'
}
</script>

<style scoped>
.log-root { display: flex; flex-direction: column; height: 100%; padding: 20px; gap: 12px; }

.log-toolbar {
  display: flex; align-items: center; justify-content: space-between;
  height: var(--toolbar-height);
}

.log-title-group { display: flex; align-items: baseline; gap: 10px; }
.page-title { font-size: 18px; font-weight: 600; letter-spacing: -0.2px; }
.entry-count { font-size: 12px; }

/* Severity + time-range filter groups, side-by-side on the right of the toolbar */
.toolbar-filters { display: flex; gap: 10px; align-items: center; }
/* Filter toggle — mirrors .theme-toggle / .theme-btn from Settings.vue */
.filter-toggle { display: flex; gap: 4px; }
.theme-btn {
  background: var(--surface-1); border: 1px solid var(--border-subtle);
  color: var(--text-secondary); cursor: pointer; padding: 6px 14px;
  border-radius: var(--radius-sm); font-size: 12px; font-weight: 500;
  font-family: inherit;
  transition: background var(--t-fast), color var(--t-fast), border-color var(--t-fast);
}
.theme-btn:hover { background: var(--surface-2); color: var(--text-primary); }
.theme-btn.active {
  background: var(--surface-active); border-color: var(--border-accent);
  color: var(--text-accent);
}

.log-panel { flex: 1; overflow: hidden; display: flex; flex-direction: column; }
.log-empty { flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; }
.log-entries { flex: 1; overflow-y: auto; padding: 8px 0; }

.log-entry {
  display: grid;
  grid-template-columns: 80px 1fr;
  gap: 12px;
  padding: 3px 14px;
  font-size: 12px;
  border-bottom: 1px solid transparent;
  /* Prevent the row itself from growing beyond one line */
  min-width: 0;
}
.log-time { color: var(--text-tertiary); white-space: nowrap; }

.log-msg {
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
  min-width: 0;
}

/* Severity colors apply to the entire row so timestamp inherits the tint */
.log-warn { color: #ffa726; }
.log-err  { color: #ef5350; }

.log-entry { cursor: pointer; }
.log-entry.selected { background: rgba(116, 77, 169, 0.2); }
.log-entry:hover { background: rgba(255, 255, 255, 0.05); }

.log-detail-pane {
  flex-shrink: 0;
  border-top: 1px solid rgba(255, 255, 255, 0.1);
  background: rgba(0, 0, 0, 0.3);
  display: flex;
  flex-direction: column;
  max-height: 160px;
  min-height: 80px;
}
.log-detail-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 12px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.07);
  flex-shrink: 0;
}
.log-detail-ts {
  font-size: 11px;
  color: rgba(255, 255, 255, 0.5);
  font-family: monospace;
}
.log-detail-badge {
  font-size: 10px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  padding: 1px 6px;
  border-radius: 3px;
  background: rgba(255, 255, 255, 0.12);
}
.log-detail-badge.lvl-warn { background: rgba(255, 167, 38, 0.25); color: #ffa726; }
.log-detail-badge.lvl-error { background: rgba(239, 83, 80, 0.25); color: #ef5350; }
.log-detail-badge.lvl-info { background: rgba(255, 255, 255, 0.1); color: rgba(255,255,255,0.7); }
.log-detail-close {
  margin-left: auto;
  background: none;
  border: none;
  color: rgba(255, 255, 255, 0.4);
  cursor: pointer;
  font-size: 13px;
  padding: 2px 6px;
  border-radius: 3px;
}
.log-detail-close:hover { background: rgba(255,255,255,0.1); color: white; }
.log-detail-body {
  flex: 1;
  overflow-y: auto;
  padding: 8px 12px;
  font-size: 12px;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-all;
  color: rgba(255, 255, 255, 0.85);
  font-family: 'Consolas', 'Cascadia Code', monospace;
}
</style>
