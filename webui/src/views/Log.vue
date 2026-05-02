<template>
  <div class="log-root">
    <div class="log-toolbar">
      <div class="log-title-group">
        <h1 class="page-title">Log</h1>
        <span class="entry-count text-secondary">{{ filteredLogs.length }} entries</span>
      </div>
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
    </div>
    <div class="log-panel panel">
      <div v-if="!filteredLogs.length" class="log-empty">
        <span style="opacity:0.4; font-size:24px">📋</span>
        <p class="text-secondary" style="margin-top:8px">No log entries</p>
      </div>
      <div v-else class="log-entries" ref="scrollEl">
        <div
          v-for="entry in filteredLogs"
          :key="entry.id"
          class="log-entry"
          :class="severityClass(entry.type)"
        >
          <span class="log-time mono">{{ formatTime(entry.timestamp) }}</span>
          <span class="log-msg" :title="entry.message">{{ entry.message }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted, nextTick } from 'vue'
import { api } from '../api/index.js'

const logs = ref([])
const scrollEl = ref(null)
const filter = ref('all')

// lastKnownId tracks the highest id seen so each poll only fetches new entries.
// The server returns entries with id > lastKnownId when the param is supplied.
const lastKnownId = ref(-1)

const filteredLogs = computed(() => {
  if (filter.value === 'warn') return logs.value.filter(e => e.type === 2)
  if (filter.value === 'err')  return logs.value.filter(e => e.type === 3)
  return logs.value
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
  if (type === 2) return 'log-warn'
  if (type === 3) return 'log-err'
  return ''
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
.log-entry:hover { background: var(--surface-1); }
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
</style>
