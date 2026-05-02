<template>
  <div class="table-wrap">
    <div v-if="!torrents.length" class="empty-state">
      <div class="empty-icon">⬇</div>
      <p>No torrents yet</p>
      <p class="text-secondary" style="font-size:12px; margin-top:4px">Add a magnet link to get started</p>
    </div>

    <template v-else>
      <div class="table-head">
        <div class="col col-name sortable" @click="toggleSort('name')">
          Name <span class="sort-indicator">{{ sortIndicator('name') }}</span>
        </div>
        <div class="col col-size sortable" @click="toggleSort('size')">
          Size <span class="sort-indicator">{{ sortIndicator('size') }}</span>
        </div>
        <div class="col col-status sortable" @click="toggleSort('state')">
          Status <span class="sort-indicator">{{ sortIndicator('state') }}</span>
        </div>
        <div class="col col-progress sortable" @click="toggleSort('progress')">
          Progress <span class="sort-indicator">{{ sortIndicator('progress') }}</span>
        </div>
        <div class="col col-speed sortable" @click="toggleSort('dlspeed')">
          ↓ Speed <span class="sort-indicator">{{ sortIndicator('dlspeed') }}</span>
        </div>
        <div class="col col-speed sortable" @click="toggleSort('upspeed')">
          ↑ Speed <span class="sort-indicator">{{ sortIndicator('upspeed') }}</span>
        </div>
        <div class="col col-ratio sortable" @click="toggleSort('ratio')">
          Ratio <span class="sort-indicator">{{ sortIndicator('ratio') }}</span>
        </div>
        <div class="col col-eta sortable" @click="toggleSort('eta')">
          ETA <span class="sort-indicator">{{ sortIndicator('eta') }}</span>
        </div>
      </div>

      <div class="table-body" @click="onBodyClick" @contextmenu.prevent="onBodyContextMenu">
        <div
          v-for="t in sortedTorrents"
          :key="t.hash"
          class="table-row"
          :class="{ selected: selectedHash === t.hash }"
        >
          <div class="col col-name">
            <span class="torrent-name" :title="t.name">{{ t.name }}</span>
          </div>
          <div class="col col-size mono">{{ fmtSize(t.size) }}</div>
          <div class="col col-status">
            <span class="badge" :class="stateClass(t.state)">{{ fmtState(t.state) }}</span>
          </div>
          <div class="col col-progress">
            <div class="progress-track">
              <div class="progress-fill" :class="stateClass(t.state)" :style="{ width: pct(t.progress) + '%' }" />
            </div>
            <span class="progress-label mono">{{ pct(t.progress).toFixed(1) }}%</span>
          </div>
          <div class="col col-speed mono">{{ t.state === 'downloading' ? fmtSpeed(t.dlspeed) : '—' }}</div>
          <div class="col col-speed mono">{{ fmtSpeed(t.upspeed) }}</div>
          <div class="col col-ratio mono">{{ fmtRatio(t.ratio) }}</div>
          <div class="col col-eta mono">{{ fmtEta(t.eta) }}</div>
        </div>
      </div>
    </template>

    <!-- Context menu — teleported to body to escape any stacking context -->
    <Teleport to="body">
      <div
        v-if="ctx.visible"
        class="context-menu"
        :style="{ top: ctx.y + 'px', left: ctx.x + 'px' }"
        @mouseleave="ctx.visible = false"
      >
        <button class="ctx-item" @click="ctxAction('pause')">⏸ Pause</button>
        <button class="ctx-item" @click="ctxAction('resume')">▶ Resume</button>
        <div class="ctx-sep" />
        <button class="ctx-item ctx-danger" @click="ctxAction('delete')">🗑 Delete</button>
      </div>
    </Teleport>

    <!-- Delete confirmation bar — absolute positioned at the bottom of .table-wrap -->
    <div v-if="delConfirm.visible" class="del-confirm-bar">
      <span class="del-confirm-msg">🗑 Delete "{{ truncate(delConfirm.torrent?.name, 40) }}"?</span>
      <button class="del-btn del-btn-soft" @click="confirmDelete(false)">Delete torrent only</button>
      <button class="del-btn del-btn-hard" @click="confirmDelete(true)">Delete with files</button>
      <button class="del-btn del-btn-cancel" @click="delConfirm.visible = false">Cancel</button>
    </div>
  </div>
</template>

<script setup>
import { reactive, computed, onMounted, ref } from 'vue'
import { api } from '../api/index.js'

const props = defineProps({
  torrents: { type: Array, default: () => [] },
  selectedHash: { type: String, default: null },
})

const emit = defineEmits(['select'])

const ctx = reactive({ visible: false, x: 0, y: 0, torrent: null })
const delConfirm = reactive({ visible: false, torrent: null })

// ── Sort state ──────────────────────────────────────────────────────────────

const sortColumn = ref(null)   // null = unsorted; otherwise a column key string
const sortReverse = ref(false)

onMounted(async () => {
  try {
    const prefs = await api.getPreferences()
    if (prefs) {
      sortColumn.value = prefs.transfers_sort_column ?? null
      sortReverse.value = prefs.transfers_sort_reverse ?? false
    }
  } catch { /* non-fatal — defaults are fine */ }
})

// Returns the raw sortable value for a torrent row given a column key.
// Null/undefined fields coerce to a safe default so subtraction never yields NaN.
function getSortValue(t, col) {
  switch (col) {
    case 'name':    return (t.name ?? '').toLowerCase()
    case 'size':    return t.size ?? 0
    case 'progress':return t.progress ?? 0
    case 'state':   return t.state ?? ''
    case 'dlspeed': return t.dlspeed ?? 0
    case 'upspeed': return t.upspeed ?? 0
    case 'ratio':   return t.ratio ?? 0
    case 'eta': {
      const v = t.eta
      // Values ≥ 8 640 000 seconds (100 days) are the "infinite" sentinel; cluster them at the top when sorting desc.
      return (!v || v < 0 || v >= 8640000) ? Number.MAX_SAFE_INTEGER : v
    }
    default: return 0
  }
}

const sortedTorrents = computed(() => {
  if (!sortColumn.value) return props.torrents

  // Shallow copy — never mutate the prop array.
  const rows = props.torrents.slice()
  const col = sortColumn.value
  const dir = sortReverse.value ? -1 : 1

  rows.sort((a, b) => {
    const av = getSortValue(a, col)
    const bv = getSortValue(b, col)
    if (typeof av === 'string') return av.localeCompare(bv) * dir
    return (av - bv) * dir
  })

  return rows
})

function sortIndicator(col) {
  if (sortColumn.value !== col) return ''
  return sortReverse.value ? '▼' : '▲'
}

function toggleSort(col) {
  if (sortColumn.value === col) {
    if (!sortReverse.value) {
      sortReverse.value = true
    } else {
      // Third click on the same column clears the sort entirely.
      sortColumn.value = null
      sortReverse.value = false
    }
  } else {
    sortColumn.value = col
    sortReverse.value = false
  }
  persistSort()
}

async function persistSort() {
  try {
    await api.setPreferences({
      transfers_sort_column: sortColumn.value,
      transfers_sort_reverse: sortReverse.value,
    })
  } catch { /* non-fatal */ }
}

// ── Row interaction ─────────────────────────────────────────────────────────

function torrentFromRow(el) {
  const row = el?.closest('.table-row')
  if (!row) return null
  const idx = Array.from(row.parentElement?.children ?? []).indexOf(row)
  return idx >= 0 ? sortedTorrents.value[idx] ?? null : null
}

function onBodyClick(e) {
  const torrent = torrentFromRow(e.target)
  if (torrent) emit('select', torrent)
}

function onBodyContextMenu(e) {
  const torrent = torrentFromRow(e.target)
  if (torrent) showContext(e, torrent)
}

function showContext(e, t) {
  ctx.torrent = t
  ctx.x = e.clientX
  ctx.y = e.clientY
  ctx.visible = true
}

async function ctxAction(action) {
  const t = ctx.torrent
  ctx.visible = false
  if (!t) return
  if (action === 'pause') await api.pauseTorrent(t.hash)
  if (action === 'resume') await api.resumeTorrent(t.hash)
  if (action === 'delete') {
    // Show the inline confirm bar instead of deleting immediately.
    delConfirm.torrent = t
    delConfirm.visible = true
  }
}

async function confirmDelete(deleteFiles) {
  const t = delConfirm.torrent
  delConfirm.visible = false
  delConfirm.torrent = null
  if (!t) return
  await api.deleteTorrent(t.hash, deleteFiles)
}

// ── Formatters ───────────────────────────────────────────────────────────────

function truncate(str, max) {
  if (!str) return ''
  return str.length > max ? str.slice(0, max) + '...' : str
}

function pct(p) { return Math.min(100, Math.max(0, (p ?? 0) * 100)) }

function fmtSize(bytes) {
  if (!bytes) return '—'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let i = 0; let v = bytes
  while (v >= 1024 && i < units.length - 1) { v /= 1024; i++ }
  return v.toFixed(i === 0 ? 0 : 1) + ' ' + units[i]
}

function fmtSpeed(bps) {
  if (!bps || bps < 1024) return bps > 0 ? bps + ' B/s' : '—'
  return fmtSize(bps) + '/s'
}

function fmtEta(sec) {
  if (!sec || sec < 0 || sec >= 8640000) return '∞'
  const h = Math.floor(sec / 3600)
  const m = Math.floor((sec % 3600) / 60)
  const s = sec % 60
  if (h > 0) return `${h}h ${m}m`
  if (m > 0) return `${m}m ${s}s`
  return `${s}s`
}

function fmtRatio(ratio) {
  if (!ratio || ratio === 0) return '—'
  return ratio.toFixed(2)
}

const STATE_LABELS = {
  downloading: 'Downloading', uploading: 'Seeding', pausedDL: 'Paused',
  pausedUP: 'Paused', stalledDL: 'Stalled', stalledUP: 'Stalled',
  checkingDL: 'Checking', checkingUP: 'Checking', queuedDL: 'Queued',
  queuedUP: 'Queued', error: 'Error', missingFiles: 'Missing', metaDL: 'Fetching',
  forcedDL: 'Forced DL', forcedUP: 'Forced UP',
}

function fmtState(state) { return STATE_LABELS[state] ?? state }

function stateClass(state) {
  if (state === 'downloading' || state === 'forcedDL' || state === 'metaDL') return 'dl'
  if (state === 'uploading' || state === 'forcedUP') return 'seed'
  if (state === 'pausedDL' || state === 'pausedUP') return 'paused'
  if (state === 'checkingDL' || state === 'checkingUP') return 'check'
  if (state === 'error' || state === 'missingFiles') return 'err'
  return 'queue'
}
</script>

<style scoped>
.table-wrap { display: flex; flex-direction: column; height: 100%; position: relative; overflow: hidden; }

.empty-state {
  flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center;
  color: var(--text-secondary);
}
.empty-icon { font-size: 40px; opacity: 0.25; margin-bottom: 12px; }

/* Table layout */
.table-head, .table-row {
  display: grid;
  grid-template-columns: 1fr 90px 100px 130px 90px 90px 55px 55px;
  align-items: center;
  padding: 0 12px;
  gap: 0;
}

.table-head {
  height: 32px;
  border-bottom: 1px solid var(--border-subtle);
  position: sticky;
  top: 0;
  background: color-mix(in srgb, var(--surface-1) 80%, transparent);
  backdrop-filter: blur(8px);
  z-index: 1;
}

.col {
  padding: 0 6px;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}

.table-head .col {
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--text-tertiary);
}

/* Sortable column headers */
.sortable { cursor: pointer; user-select: none; }
.sortable:hover { opacity: 0.8; }
.sort-indicator { font-size: 0.75em; margin-left: 4px; color: var(--fluent-accent, #0078d4); }

.table-body { flex: 1; overflow-y: auto; }

.table-row {
  height: 36px;
  cursor: default;
  border-bottom: 1px solid rgba(255, 255, 255, 0.03);
  transition: background var(--t-fast);
}
.table-row:hover { background: var(--surface-1); }
.table-row.selected { background: var(--surface-active); }

.torrent-name {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 13px;
}

/* Status badges */
.badge {
  display: inline-block;
  font-size: 11px;
  font-weight: 500;
  padding: 2px 7px;
  border-radius: 20px;
  letter-spacing: 0.02em;
}
.badge.dl    { background: rgba(79, 195, 247, 0.15); color: var(--status-dl); }
.badge.seed  { background: rgba(102, 187, 106, 0.15); color: var(--status-seed); }
.badge.paused{ background: rgba(255, 167, 38, 0.15); color: var(--status-pause); }
.badge.check { background: rgba(206, 147, 216, 0.15); color: var(--status-check); }
.badge.err   { background: rgba(239, 83, 80, 0.15); color: var(--status-err); }
.badge.queue { background: rgba(120, 144, 156, 0.15); color: var(--status-queue); }

/* Progress */
.col-progress { display: flex; align-items: center; gap: 8px; }
.progress-track {
  flex: 1; height: 4px; background: rgba(255, 255, 255, 0.08);
  border-radius: 2px; overflow: hidden;
}
.progress-fill {
  height: 100%; border-radius: 2px;
  transition: width 400ms ease;
  background: var(--accent);
}
.progress-fill.dl    { background: var(--status-dl); }
.progress-fill.seed  { background: var(--status-seed); }
.progress-fill.paused{ background: var(--status-pause); }
.progress-fill.check { background: var(--status-check); }
.progress-fill.err   { background: var(--status-err); }
.progress-fill.queue { background: var(--status-queue); }
.progress-label { font-size: 11px; color: var(--text-tertiary); min-width: 38px; text-align: right; }

/* Ratio column — right-aligned like other numeric columns */
.col-ratio { text-align: right; }

/* Delete confirmation bar */
.del-confirm-bar {
  position: absolute;
  bottom: 0;
  left: 0;
  right: 0;
  background: color-mix(in srgb, var(--surface-1) 95%, transparent);
  border-top: 1px solid var(--border-subtle);
  padding: 10px 16px;
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: 13px;
  z-index: 50;
}
.del-confirm-msg {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--text-primary);
}
.del-btn {
  flex-shrink: 0;
  padding: 5px 12px;
  border-radius: var(--radius-sm);
  border: 1px solid transparent;
  font-size: 12px;
  font-family: inherit;
  cursor: pointer;
  transition: background var(--t-fast), border-color var(--t-fast);
}
.del-btn-soft {
  background: rgba(239, 83, 80, 0.12);
  border-color: rgba(239, 83, 80, 0.3);
  color: #ef9a9a;
}
.del-btn-soft:hover { background: rgba(239, 83, 80, 0.22); }
.del-btn-hard {
  background: rgba(239, 83, 80, 0.25);
  border-color: rgba(239, 83, 80, 0.5);
  color: #ef5350;
}
.del-btn-hard:hover { background: rgba(239, 83, 80, 0.4); }
.del-btn-cancel {
  background: var(--surface-2);
  border-color: var(--border-subtle);
  color: var(--text-secondary);
}
.del-btn-cancel:hover { background: var(--surface-3, var(--surface-2)); color: var(--text-primary); }

/* Context menu */
.context-menu {
  position: fixed; z-index: 1000;
  padding: 6px;
  min-width: 160px;
  display: flex; flex-direction: column; gap: 2px;
  background: var(--surface-2);
  border: 1px solid rgba(255, 255, 255, 0.18);
  border-radius: var(--radius-md);
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.7), 0 0 0 1px rgba(124, 106, 247, 0.12);
}
.ctx-item {
  background: none; border: none; cursor: pointer;
  color: var(--text-primary); text-align: left;
  padding: 8px 12px; border-radius: var(--radius-sm);
  font-size: 13px; font-family: inherit;
  transition: background var(--t-fast);
}
.ctx-item:hover { background: var(--surface-2); }
.ctx-item.ctx-danger { color: #ef9a9a; }
.ctx-item.ctx-danger:hover { background: rgba(239, 83, 80, 0.12); }
.ctx-sep { height: 1px; background: var(--border-subtle); margin: 4px 0; }
</style>
