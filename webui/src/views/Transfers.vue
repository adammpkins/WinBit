<template>
  <div class="transfers-root">
    <div class="toolbar">
      <div class="toolbar-left">
        <fluent-button appearance="accent" class="btn-add" @click="showAddMagnet = true">
          + Add Magnet
        </fluent-button>
        <fluent-button appearance="lightweight" @click="api.pauseAll()">⏸ Pause All</fluent-button>
        <fluent-button appearance="lightweight" @click="api.resumeAll()">▶ Resume All</fluent-button>
      </div>
      <div class="toolbar-right">
        <div class="speed-display" v-if="serverState.dl_info_speed !== undefined">
          <span class="speed-item">
            <span class="speed-arrow dl-arrow">↓</span>
            <span class="mono">{{ fmtSpeed(serverState.dl_info_speed) }}</span>
          </span>
          <span class="speed-item">
            <span class="speed-arrow seed-arrow">↑</span>
            <span class="mono">{{ fmtSpeed(serverState.up_info_speed) }}</span>
          </span>
        </div>
        <span class="torrent-count text-secondary">
          {{ torrentList.length }} torrent{{ torrentList.length !== 1 ? 's' : '' }}
        </span>
      </div>
    </div>

    <!-- Split container: content-area (sidebar + table) on top, detail panel pinned to bottom -->
    <div class="split-container">
      <div class="content-area">
        <div class="filter-sidebar">
          <button
            v-for="f in filterDefs"
            :key="f.id"
            class="filter-btn"
            :class="{ active: activeFilter.type === 'status' && activeFilter.value === f.id }"
            @click="activeFilter = { type: 'status', value: f.id }"
          >
            <span class="filter-label">{{ f.label }}</span>
            <span class="filter-count">{{ f.count }}</span>
          </button>

          <template v-if="categorySections.length">
            <div class="filter-section-label">Categories</div>
            <button v-for="cat in categorySections" :key="'cat-' + cat.name"
              class="filter-btn"
              :class="{ active: activeFilter.type === 'category' && activeFilter.value === cat.name }"
              @click="activeFilter = { type: 'category', value: cat.name }"
            >
              <span class="filter-label">{{ cat.name }}</span>
              <span class="filter-count">{{ cat.count }}</span>
            </button>
          </template>

          <template v-if="tagSections.length">
            <div class="filter-section-label">Tags</div>
            <button v-for="tag in tagSections" :key="'tag-' + tag.name"
              class="filter-btn"
              :class="{ active: activeFilter.type === 'tag' && activeFilter.value === tag.name }"
              @click="activeFilter = { type: 'tag', value: tag.name }"
            >
              <span class="filter-label">{{ tag.name }}</span>
              <span class="filter-count">{{ tag.count }}</span>
            </button>
          </template>
        </div>
        <div class="table-area panel">
          <TransfersTable
            :torrents="filteredList"
            :selectedHash="selectedTorrent?.hash ?? null"
            @select="onTorrentSelected"
          />
        </div>
      </div>

      <TorrentDetailPanel
        v-if="selectedTorrent"
        :torrent="selectedTorrent"
        @close="selectedTorrent = null"
      />
    </div>

    <AddMagnetDialog :open="showAddMagnet" @close="showAddMagnet = false" />
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { storeToRefs } from 'pinia'
import { useTorrentsStore } from '../stores/torrents.js'
import TransfersTable from '../components/TransfersTable.vue'
import TorrentDetailPanel from '../components/TorrentDetailPanel.vue'
import AddMagnetDialog from '../components/AddMagnetDialog.vue'
import { api } from '../api/index.js'

const store = useTorrentsStore()
const { torrentList, serverState } = storeToRefs(store)
const showAddMagnet = ref(false)
const selectedTorrent = ref(null)
const activeFilter = ref({ type: 'status', value: 'all' })

function matchesFilter(t, filter) {
  switch (filter) {
    case 'all': return true
    case 'downloading': return ['downloading', 'stalledDL', 'queuedDL', 'forcedDL', 'metaDL', 'forcedMetaDL'].includes(t.state)
    case 'seeding': return ['uploading', 'stalledUP', 'queuedUP', 'forcedUP'].includes(t.state)
    case 'completed': return t.progress != null && t.progress >= 1
    case 'paused': return ['pausedDL', 'pausedUP'].includes(t.state)
    case 'errored': return ['error', 'missingFiles'].includes(t.state)
    default: return true
  }
}

function parseTags(tags) {
  if (!tags) return []
  return tags.split(',').map(s => s.trim()).filter(Boolean)
}

const filteredList = computed(() => {
  const { type, value } = activeFilter.value
  if (type === 'status') return torrentList.value.filter(t => matchesFilter(t, value))
  if (type === 'category') return torrentList.value.filter(t => (t.category ?? '') === value)
  if (type === 'tag') return torrentList.value.filter(t => parseTags(t.tags).includes(value))
  return torrentList.value
})

const filterDefs = computed(() => [
  { id: 'all',         label: 'All',         count: torrentList.value.length },
  { id: 'downloading', label: 'Downloading',  count: torrentList.value.filter(t => matchesFilter(t, 'downloading')).length },
  { id: 'seeding',     label: 'Seeding',      count: torrentList.value.filter(t => matchesFilter(t, 'seeding')).length },
  { id: 'completed',   label: 'Completed',    count: torrentList.value.filter(t => matchesFilter(t, 'completed')).length },
  { id: 'paused',      label: 'Paused',       count: torrentList.value.filter(t => matchesFilter(t, 'paused')).length },
  { id: 'errored',     label: 'Errored',      count: torrentList.value.filter(t => matchesFilter(t, 'errored')).length },
])

const categorySections = computed(() => {
  const counts = {}
  for (const t of torrentList.value) {
    const cat = t.category || ''
    if (cat) counts[cat] = (counts[cat] ?? 0) + 1
  }
  return Object.entries(counts).map(([name, count]) => ({ name, count })).sort((a, b) => a.name.localeCompare(b.name))
})

const tagSections = computed(() => {
  const counts = {}
  for (const t of torrentList.value) {
    for (const tag of parseTags(t.tags)) {
      counts[tag] = (counts[tag] ?? 0) + 1
    }
  }
  return Object.entries(counts).map(([name, count]) => ({ name, count })).sort((a, b) => a.name.localeCompare(b.name))
})

// Clicking the already-selected row deselects (closes the detail panel).
function onTorrentSelected(torrent) {
  selectedTorrent.value = selectedTorrent.value?.hash === torrent.hash ? null : torrent
}

function fmtSpeed(bps) {
  if (!bps || bps < 1024) return bps > 0 ? bps + ' B/s' : '0 B/s'
  const units = ['B/s', 'KB/s', 'MB/s', 'GB/s']
  let i = 0; let v = bps
  while (v >= 1024 && i < units.length - 1) { v /= 1024; i++ }
  return v.toFixed(1) + ' ' + units[i]
}

onMounted(() => store.startPolling())
onUnmounted(() => store.stopPolling())
</script>

<style scoped>
.transfers-root { display: flex; flex-direction: column; height: 100%; padding: 16px; gap: 12px; }

.toolbar {
  display: flex; align-items: center; justify-content: space-between;
  height: var(--toolbar-height); flex-shrink: 0;
}
.toolbar-left { display: flex; align-items: center; gap: 6px; }
.toolbar-right { display: flex; align-items: center; gap: 16px; }

.btn-add {
  --accent-fill-rest: var(--accent);
  --accent-fill-hover: var(--accent-light);
}

.speed-display { display: flex; align-items: center; gap: 12px; }
.speed-item { display: flex; align-items: center; gap: 5px; font-size: 13px; }
.speed-arrow { font-weight: 700; font-size: 14px; }
.dl-arrow { color: var(--status-dl); }
.seed-arrow { color: var(--status-seed); }

.torrent-count { font-size: 12px; }

/* Split container: content-area on top, detail panel pinned at the bottom */
.split-container {
  display: flex;
  flex-direction: column;
  flex: 1;
  overflow: hidden;
  gap: 0;
}

/* Horizontal row: sidebar left, table right */
.content-area {
  display: flex;
  flex-direction: row;
  flex: 1;
  overflow: hidden;
  min-height: 0;
}

.filter-sidebar {
  width: 140px;
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  padding: 4px 0;
  border-right: 1px solid rgba(255, 255, 255, 0.06);
  overflow-y: auto;
}

.filter-btn {
  width: 100%;
  text-align: left;
  background: none;
  border: none;
  border-left: 2px solid transparent;
  padding: 6px 12px;
  cursor: pointer;
  border-radius: 4px;
  display: flex;
  justify-content: space-between;
  align-items: center;
  color: rgba(255, 255, 255, 0.6);
  font-size: 13px;
  transition: background 0.12s, color 0.12s;
}

.filter-btn:hover {
  background: rgba(255, 255, 255, 0.06);
}

.filter-btn.active {
  background: rgba(116, 77, 169, 0.25);
  color: white;
  font-weight: 600;
  border-left: 2px solid var(--accent);
}

.filter-label {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.filter-count {
  font-size: 11px;
  color: rgba(255, 255, 255, 0.4);
  font-family: monospace;
  margin-left: 4px;
  flex-shrink: 0;
}

.filter-btn.active .filter-count {
  color: rgba(255, 255, 255, 0.7);
}

.filter-section-label {
  font-size: 9px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: rgba(255, 255, 255, 0.3);
  padding: 10px 12px 4px;
  flex-shrink: 0;
}

.table-area { flex: 1; overflow: hidden; min-height: 120px; }
</style>
