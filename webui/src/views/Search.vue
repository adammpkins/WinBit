<template>
  <div class="search-root">
    <!-- Search bar -->
    <div class="search-bar">
      <input
        v-model="query"
        class="search-input"
        placeholder="Search torrents…"
        @keyup.enter="startSearch"
      />
      <select v-model="category" class="cat-select">
        <option value="all">All Categories</option>
        <option value="movies">Movies</option>
        <option value="tv">TV Shows</option>
        <option value="music">Music</option>
        <option value="software">Software</option>
        <option value="books">Books</option>
        <option value="games">Games</option>
      </select>
      <fluent-button appearance="accent" class="btn-search" @click="startSearch" :disabled="searching || !query.trim()">
        <span class="fi" style="margin-right:6px" v-html="searching ? '&#xE895;' : '&#xE721;'"></span>{{ searching ? 'Searching…' : 'Search' }}
      </fluent-button>
      <fluent-button appearance="lightweight" v-if="searching" @click="stopSearch"><span class="fi" style="margin-right:6px">&#xE71A;</span>Stop</fluent-button>
      <span class="result-count text-secondary" v-if="results.length > 0">
        {{ results.length }} result{{ results.length !== 1 ? 's' : '' }}{{ searching ? '…' : '' }}
      </span>
    </div>

    <!-- No plugins warning -->
    <div class="no-plugins-notice" v-if="!hasPlugins && !searching && results.length === 0">
      <div class="notice-icon fi">&#xE713;</div>
      <div class="notice-text">No search plugins configured. Add a Torznab indexer in Settings → BitTorrent → Search Plugins.</div>
    </div>

    <!-- Results table -->
    <div class="results-area panel" v-if="results.length > 0 || (searching && results.length === 0)">
      <div class="empty-search" v-if="searching && results.length === 0">
        <div class="spinner fi">&#xE895;</div>
        <div class="text-secondary">Searching…</div>
      </div>
      <template v-else>
        <div class="results-table-wrap">
          <table class="results-table">
            <thead>
              <tr>
                <th @click="sortBy('fileName')" class="th-sortable">
                  Name <span class="sort-arrow">{{ sortCol === 'fileName' ? (sortDir > 0 ? '▲' : '▼') : '' }}</span>
                </th>
                <th @click="sortBy('fileSize')" class="th-sortable th-num">
                  Size <span class="sort-arrow">{{ sortCol === 'fileSize' ? (sortDir > 0 ? '▲' : '▼') : '' }}</span>
                </th>
                <th @click="sortBy('nbSeeders')" class="th-sortable th-num">
                  Seeds <span class="sort-arrow">{{ sortCol === 'nbSeeders' ? (sortDir > 0 ? '▲' : '▼') : '' }}</span>
                </th>
                <th @click="sortBy('nbLeechers')" class="th-sortable th-num">
                  Peers <span class="sort-arrow">{{ sortCol === 'nbLeechers' ? (sortDir > 0 ? '▲' : '▼') : '' }}</span>
                </th>
                <th class="th-num">Source</th>
                <th class="th-action"></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(r, i) in sortedResults" :key="i" class="result-row">
                <td class="td-name">
                  <a v-if="r.siteUrl" :href="r.siteUrl" target="_blank" rel="noopener" class="result-link">{{ r.fileName }}</a>
                  <span v-else>{{ r.fileName }}</span>
                </td>
                <td class="td-num">{{ fmtSize(r.fileSize) }}</td>
                <td class="td-num seed">{{ r.nbSeeders >= 0 ? r.nbSeeders : '—' }}</td>
                <td class="td-num peer">{{ r.nbLeechers >= 0 ? r.nbLeechers : '—' }}</td>
                <td class="td-num source">{{ r.engineName }}</td>
                <td class="td-action">
                  <button class="add-btn fi" @click="addTorrent(r)" :disabled="!r.fileUrl" title="Add to WinBit">&#xE896;</button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </template>
    </div>

    <!-- Empty state (no search yet) -->
    <div class="empty-state" v-if="!searching && results.length === 0 && hasPlugins && !didSearch">
      <div class="empty-icon fi">&#xE721;</div>
      <div class="empty-title">Search for torrents</div>
      <div class="empty-sub text-secondary">Enter a query above to search across your configured indexers</div>
    </div>
    <div class="empty-state" v-if="!searching && results.length === 0 && didSearch">
      <div class="empty-icon fi">&#xE82D;</div>
      <div class="empty-title">No results</div>
      <div class="empty-sub text-secondary">Try a different query or check your indexer configuration</div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { api } from '../api/index.js'

const query = ref('')
const category = ref('all')
const results = ref([])
const searching = ref(false)
const didSearch = ref(false)
const hasPlugins = ref(true)
const sortCol = ref('nbSeeders')
const sortDir = ref(-1) // -1 = desc, 1 = asc
let currentJobId = null
let pollTimer = null

const sortedResults = computed(() => {
  const col = sortCol.value
  const dir = sortDir.value
  return [...results.value].sort((a, b) => {
    const av = a[col] ?? -Infinity
    const bv = b[col] ?? -Infinity
    if (typeof av === 'string') return dir * av.localeCompare(bv)
    return dir * (av - bv)
  })
})

function sortBy(col) {
  if (sortCol.value === col) sortDir.value *= -1
  else { sortCol.value = col; sortDir.value = -1 }
}

async function startSearch() {
  if (!query.value.trim() || searching.value) return
  await stopSearch()
  results.value = []
  searching.value = true
  didSearch.value = true

  const resp = await api.searchStart(query.value.trim(), 'all', category.value)
  if (!resp) { searching.value = false; return }
  currentJobId = resp.id

  pollTimer = setInterval(async () => {
    const data = await api.searchResults(currentJobId, 0, 500)
    if (!data) return
    results.value = data.results
    if (data.status === 'Stopped') {
      searching.value = false
      clearInterval(pollTimer)
      pollTimer = null
    }
  }, 1000)
}

async function stopSearch() {
  if (pollTimer) { clearInterval(pollTimer); pollTimer = null }
  if (currentJobId != null) {
    await api.searchStop(currentJobId)
    await api.searchDelete(currentJobId)
    currentJobId = null
  }
  searching.value = false
}

async function addTorrent(r) {
  await api.addMagnet(r.fileUrl)
}

function fmtSize(bytes) {
  if (!bytes || bytes < 0) return '—'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let i = 0; let v = bytes
  while (v >= 1024 && i < units.length - 1) { v /= 1024; i++ }
  return v.toFixed(1) + ' ' + units[i]
}

onMounted(async () => {
  const plugins = await api.getSearchPlugins()
  hasPlugins.value = plugins.length > 0
})

onUnmounted(() => stopSearch())
</script>

<style scoped>
.search-root { display: flex; flex-direction: column; height: 100%; padding: 16px; gap: 12px; }

.search-bar {
  display: flex; align-items: center; gap: 8px; flex-shrink: 0;
  height: var(--toolbar-height, 40px);
}

.search-input {
  flex: 1; min-width: 0;
  background: rgba(255,255,255,0.07); border: 1px solid rgba(255,255,255,0.12);
  border-radius: 6px; padding: 7px 12px; color: white; font-size: 14px;
  outline: none; font-family: inherit;
}
.search-input:focus { border-color: var(--accent); }
.search-input::placeholder { color: rgba(255,255,255,0.3); }

.cat-select {
  background: rgba(255,255,255,0.07); border: 1px solid rgba(255,255,255,0.12);
  border-radius: 6px; padding: 6px 10px; color: rgba(255,255,255,0.8); font-size: 13px;
  outline: none; cursor: pointer; font-family: inherit;
}
.cat-select:focus { border-color: var(--accent); }

.btn-search {
  flex-shrink: 0;
}

.result-count { font-size: 12px; white-space: nowrap; }

/* No plugins notice */
.no-plugins-notice {
  display: flex; align-items: center; gap: 12px;
  background: rgba(255,200,50,0.1); border: 1px solid rgba(255,200,50,0.25);
  border-radius: 6px; padding: 10px 14px; font-size: 13px;
  color: rgba(255,220,100,0.9); flex-shrink: 0;
}
.notice-icon { font-size: 18px; flex-shrink: 0; }

/* Results area */
.results-area { flex: 1; overflow: hidden; display: flex; flex-direction: column; }

.empty-search {
  flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 12px;
}
.spinner { font-size: 32px; animation: spin 1s linear infinite; }
@keyframes spin { to { transform: rotate(360deg); } }

.results-table-wrap { flex: 1; overflow-y: auto; }

.results-table {
  width: 100%; border-collapse: collapse; font-size: 13px;
}
.results-table th {
  position: sticky; top: 0;
  background: rgba(20,18,36,0.95); padding: 8px 10px;
  text-align: left; font-weight: 600; font-size: 11px;
  text-transform: uppercase; letter-spacing: 0.05em;
  color: rgba(255,255,255,0.5); border-bottom: 1px solid rgba(255,255,255,0.08);
  white-space: nowrap; cursor: default;
}
.th-sortable { cursor: pointer; user-select: none; }
.th-sortable:hover { color: rgba(255,255,255,0.8); }
.th-num { text-align: right; }
.th-action { width: 40px; }
.sort-arrow { color: var(--accent); font-size: 10px; }

.result-row { border-bottom: 1px solid rgba(255,255,255,0.04); transition: background 0.1s; }
.result-row:hover { background: rgba(255,255,255,0.04); }

.td-name { padding: 8px 10px; max-width: 400px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.result-link { color: var(--accent-light, #a78bdf); text-decoration: none; }
.result-link:hover { text-decoration: underline; }
.td-num { padding: 8px 10px; text-align: right; font-family: monospace; font-size: 12px; white-space: nowrap; }
.seed { color: var(--status-seed, #4caf7d); }
.peer { color: rgba(255,255,255,0.5); }
.source { color: rgba(255,255,255,0.4); font-size: 11px; }
.td-action { padding: 4px 8px; text-align: center; }
.add-btn {
  background: var(--accent); color: white; border: none; border-radius: 4px;
  padding: 3px 8px; cursor: pointer; font-size: 13px;
  transition: opacity 0.1s;
}
.add-btn:hover { opacity: 0.85; }
.add-btn:disabled { opacity: 0.3; cursor: default; }

/* Empty state */
.empty-state {
  flex: 1; display: flex; flex-direction: column;
  align-items: center; justify-content: center; gap: 8px;
}
.empty-icon { font-size: 48px; opacity: 0.3; }
.empty-title { font-size: 16px; font-weight: 600; opacity: 0.5; }
.empty-sub { font-size: 13px; opacity: 0.5; }
</style>
