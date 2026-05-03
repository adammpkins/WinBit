<template>
  <div class="rss-root">
    <div class="toolbar">
      <div class="toolbar-left">
        <fluent-button appearance="accent" class="btn-add" @click="showAddFeed = true">+ Add Feed</fluent-button>
        <fluent-button appearance="lightweight" @click="refreshSelected" :disabled="!selectedFeedPath"><span class="fi" style="margin-right:6px">&#xE72C;</span>Refresh</fluent-button>
        <fluent-button appearance="lightweight" class="btn-danger" @click="removeSelected" :disabled="!selectedFeedPath"><span class="fi" style="margin-right:6px">&#xE711;</span>Remove</fluent-button>
      </div>
      <div class="toolbar-right">
        <span class="feed-count text-secondary" v-if="flatFeeds.length">
          {{ flatFeeds.length }} feed{{ flatFeeds.length !== 1 ? 's' : '' }}
        </span>
      </div>
    </div>

    <div class="rss-body">
      <!-- Feed tree sidebar -->
      <div class="feed-sidebar">
        <div
          class="feed-item"
          :class="{ active: selectedFeedPath === '__all__' }"
          @click="selectFeed('__all__', null)"
        >
          <span class="feed-icon fi">&#xE968;</span>
          <span class="feed-label">All Feeds</span>
          <span class="feed-badge" v-if="totalUnread > 0">{{ totalUnread }}</span>
        </div>
        <template v-for="item in treeItems" :key="item.path">
          <div
            v-if="item.type === 'folder'"
            class="folder-item"
            :style="{ paddingLeft: (item.depth * 12 + 8) + 'px' }"
            @click="toggleFolder(item.path)"
          >
            <span class="folder-arrow">{{ (folderOpenState[item.path] ?? true) ? '▾' : '▸' }}</span>
            <span class="feed-label">{{ item.name }}</span>
          </div>
          <div
            v-else-if="item.type === 'feed' && item.visible"
            class="feed-item"
            :class="{ active: selectedFeedPath === item.path }"
            :style="{ paddingLeft: (item.depth * 12 + 8) + 'px' }"
            @click="selectFeed(item.path, item.url)"
          >
            <span class="feed-icon fi">&#xE968;</span>
            <span class="feed-label">{{ item.name }}</span>
            <span class="feed-badge" v-if="item.unread > 0">{{ item.unread }}</span>
          </div>
        </template>
      </div>

      <!-- Article list + detail -->
      <div class="articles-area">
        <div class="article-list" v-if="displayedArticles.length > 0">
          <div
            v-for="art in displayedArticles"
            :key="art.id"
            class="article-row"
            :class="{ unread: !art.isRead, selected: selectedArticle?.id === art.id }"
            @click="selectArticle(art)"
          >
            <span class="article-dot" :class="{ unread: !art.isRead }"></span>
            <span class="article-title">{{ art.title }}</span>
            <span class="article-date text-secondary">{{ fmtDate(art.date) }}</span>
          </div>
        </div>
        <div class="empty-state" v-else>
          <div class="empty-icon fi">&#xE968;</div>
          <div class="empty-title">No articles</div>
          <div class="empty-sub text-secondary">
            {{ flatFeeds.length === 0 ? 'Add a feed to get started' : 'Select a feed or wait for refresh' }}
          </div>
        </div>

        <!-- Article detail panel -->
        <div class="article-detail" v-if="selectedArticle">
          <div class="detail-header">
            <span class="detail-title">{{ selectedArticle.title }}</span>
            <span class="detail-date text-secondary">{{ fmtDate(selectedArticle.date) }}</span>
          </div>
          <div class="detail-actions">
            <fluent-button appearance="accent" v-if="selectedArticle.torrentURL" @click="addTorrent(selectedArticle.torrentURL)">
              <span class="fi" style="margin-right:6px">&#xE896;</span>Download Torrent
            </fluent-button>
            <fluent-button appearance="lightweight" v-if="selectedArticle.link" @click="openLink(selectedArticle.link)">
              <span class="fi" style="margin-right:6px">&#xE71B;</span>Open Link
            </fluent-button>
          </div>
        </div>
      </div>
    </div>

    <!-- Add Feed dialog -->
    <div class="dialog-backdrop" v-if="showAddFeed" @click.self="showAddFeed = false">
      <div class="dialog panel">
        <div class="dialog-title">Add RSS Feed</div>
        <div class="dialog-field">
          <label>Feed URL</label>
          <input v-model="newFeedUrl" placeholder="https://..." class="dialog-input" @keyup.enter="confirmAddFeed" />
        </div>
        <div class="dialog-field">
          <label>Name (optional)</label>
          <input v-model="newFeedName" placeholder="Leave blank to use feed title" class="dialog-input" />
        </div>
        <div class="dialog-buttons">
          <fluent-button appearance="accent" @click="confirmAddFeed" :disabled="!newFeedUrl.trim()">Add Feed</fluent-button>
          <fluent-button appearance="lightweight" @click="showAddFeed = false">Cancel</fluent-button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'
import { api } from '../api/index.js'

const rawTree = ref({})
const selectedFeedPath = ref('__all__')
const selectedFeedUrl = ref(null)
const selectedArticle = ref(null)
const showAddFeed = ref(false)
const newFeedUrl = ref('')
const newFeedName = ref('')

// Tracks open/closed state for each folder by path. Absent key means open (default).
const folderOpenState = reactive({})

function toggleFolder(path) {
  folderOpenState[path] = !(folderOpenState[path] ?? true)
}

// Flatten the nested tree into an ordered list of items carrying depth, type, and visibility.
// Visibility is derived from folderOpenState so it updates reactively when folders are toggled.
function flattenTree(node, depth, pathPrefix) {
  const items = []
  for (const [name, value] of Object.entries(node)) {
    const path = pathPrefix ? `${pathPrefix}/${name}` : name
    if (value && typeof value === 'object' && !value.url) {
      // Folder — no url property distinguishes it from a feed object
      items.push({ type: 'folder', name, path, depth })
      // Recurse; child visibility depends on this folder being open
      const isOpen = folderOpenState[path] ?? true
      if (isOpen) {
        items.push(...flattenTree(value, depth + 1, path))
      }
    } else if (value && value.url) {
      // Feed
      const arts = value.articles || []
      const unread = arts.filter(a => !a.isRead).length
      // Determine visibility: walk ancestor paths and check folderOpenState
      const pathParts = path.split('/')
      let visible = true
      for (let i = 1; i < pathParts.length; i++) {
        const ancestorPath = pathParts.slice(0, i).join('/')
        if (folderOpenState[ancestorPath] === false) {
          visible = false
          break
        }
      }
      items.push({ type: 'feed', name, path, url: value.url, depth, articles: arts, unread, lastBuildDate: value.lastBuildDate, visible })
    }
  }
  return items
}

// treeItems recomputes whenever rawTree or folderOpenState changes
const treeItems = computed(() => flattenTree(rawTree.value, 0, ''))

const flatFeeds = computed(() => treeItems.value.filter(i => i.type === 'feed'))

const totalUnread = computed(() => flatFeeds.value.reduce((n, f) => n + (f.unread || 0), 0))

const displayedArticles = computed(() => {
  if (selectedFeedPath.value === '__all__') {
    return flatFeeds.value.flatMap(f => f.articles || []).sort((a, b) => new Date(b.date) - new Date(a.date))
  }
  const feed = flatFeeds.value.find(f => f.path === selectedFeedPath.value)
  return (feed?.articles || []).slice().sort((a, b) => new Date(b.date) - new Date(a.date))
})

function selectFeed(path, url) {
  selectedFeedPath.value = path
  selectedFeedUrl.value = url
  selectedArticle.value = null
}

async function selectArticle(art) {
  selectedArticle.value = art
  if (!art.isRead) {
    const feedPath = selectedFeedPath.value === '__all__'
      ? flatFeeds.value.find(f => (f.articles || []).some(a => a.id === art.id))?.path ?? null
      : selectedFeedPath.value
    if (feedPath) {
      await api.markRssAsRead(feedPath, art.id)
      await loadData()
    }
  }
}

async function refreshSelected() {
  if (!selectedFeedPath.value || selectedFeedPath.value === '__all__') return
  await api.refreshRssItem(selectedFeedPath.value)
  await loadData()
}

async function removeSelected() {
  if (!selectedFeedPath.value || selectedFeedPath.value === '__all__') return
  await api.removeRssItem(selectedFeedPath.value)
  selectedFeedPath.value = '__all__'
  selectedFeedUrl.value = null
  selectedArticle.value = null
  await loadData()
}

async function confirmAddFeed() {
  const url = newFeedUrl.value.trim()
  if (!url) return
  const name = newFeedName.value.trim()
  await api.addRssFeed(url, name || '')
  newFeedUrl.value = ''
  newFeedName.value = ''
  showAddFeed.value = false
  await loadData()
}

async function addTorrent(torrentUrl) {
  await api.addMagnet(torrentUrl)
}

function openLink(link) {
  window.open(link, '_blank', 'noopener')
}

function fmtDate(iso) {
  if (!iso) return ''
  const d = new Date(iso)
  if (isNaN(d.getTime())) return ''
  return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
}

async function loadData() {
  rawTree.value = await api.getRssItems(true)
}

let pollTimer = null

onMounted(() => {
  loadData()
  pollTimer = setInterval(loadData, 10000)
})
onUnmounted(() => clearInterval(pollTimer))
</script>

<style scoped>
.rss-root { display: flex; flex-direction: column; height: 100%; padding: 16px; gap: 12px; }

.toolbar {
  display: flex; align-items: center; justify-content: space-between;
  height: var(--toolbar-height, 40px); flex-shrink: 0;
}
.toolbar-left { display: flex; align-items: center; gap: 6px; }
.toolbar-right { display: flex; align-items: center; gap: 12px; }

.btn-danger { color: var(--status-error, #e05c5c); }
.feed-count { font-size: 12px; }

.rss-body {
  display: flex; flex: 1; overflow: hidden; gap: 0;
}

/* Feed tree sidebar */
.feed-sidebar {
  width: 200px; flex-shrink: 0;
  display: flex; flex-direction: column;
  border-right: 1px solid var(--border-subtle);
  overflow-y: auto; padding: 4px 0;
}

.feed-item, .folder-item {
  display: flex; align-items: center; gap: 8px;
  padding: 6px 12px; cursor: pointer;
  font-size: 13px; color: rgba(255,255,255,0.6);
  border-left: 2px solid transparent;
  transition: background 0.12s, color 0.12s;
  border-radius: 4px;
}
.feed-item:hover, .folder-item:hover { background: rgba(255,255,255,0.05); color: rgba(255,255,255,0.85); }
.feed-item.active {
  background: rgba(116,77,169,0.25); color: white; font-weight: 600;
  border-left: 2px solid var(--accent);
}
.feed-icon { font-size: 12px; flex-shrink: 0; }
.feed-label { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.feed-badge {
  background: var(--accent); color: white; border-radius: 999px;
  font-size: 10px; font-weight: 700; padding: 1px 6px; flex-shrink: 0;
  font-family: monospace;
}
.folder-arrow { font-size: 10px; flex-shrink: 0; width: 12px; }

/* Article area */
.articles-area {
  flex: 1; overflow: hidden; display: flex; flex-direction: column;
}

.article-list {
  flex: 1; overflow-y: auto;
}

.article-row {
  display: flex; align-items: center; gap: 10px;
  padding: 9px 14px; cursor: pointer; font-size: 13px;
  border-bottom: 1px solid rgba(255,255,255,0.04);
  transition: background 0.1s;
}
.article-row:hover { background: rgba(255,255,255,0.04); }
.article-row.selected { background: rgba(116,77,169,0.2); }

.article-dot {
  width: 7px; height: 7px; border-radius: 50%; flex-shrink: 0;
  background: rgba(255,255,255,0.15);
}
.article-dot.unread { background: var(--accent); }

.article-title { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.article-row.unread .article-title { font-weight: 600; color: var(--text-primary, white); }
.article-date { font-size: 11px; flex-shrink: 0; font-family: monospace; }

/* Empty state */
.empty-state {
  flex: 1; display: flex; flex-direction: column;
  align-items: center; justify-content: center; gap: 8px;
}
.empty-icon { font-size: 48px; opacity: 0.3; }
.empty-title { font-size: 16px; font-weight: 600; opacity: 0.5; }
.empty-sub { font-size: 13px; opacity: 0.5; }

/* Article detail panel */
.article-detail {
  flex-shrink: 0; border-top: 1px solid var(--border-subtle);
  padding: 12px 14px; display: flex; flex-direction: column; gap: 8px;
  max-height: 140px;
}
.detail-header { display: flex; align-items: flex-start; gap: 12px; }
.detail-title { flex: 1; font-size: 14px; font-weight: 600; line-height: 1.4; }
.detail-date { font-size: 11px; font-family: monospace; flex-shrink: 0; padding-top: 2px; }
.detail-actions { display: flex; gap: 8px; }

/* Add Feed dialog */
.dialog-backdrop {
  position: fixed; inset: 0; background: rgba(0,0,0,0.5);
  display: flex; align-items: center; justify-content: center; z-index: 100;
}
.dialog {
  width: 400px; padding: 24px; display: flex; flex-direction: column; gap: 16px;
  border-radius: 8px;
}
.dialog-title { font-size: 16px; font-weight: 700; }
.dialog-field { display: flex; flex-direction: column; gap: 6px; }
.dialog-field label { font-size: 12px; color: rgba(255,255,255,0.6); }
.dialog-input {
  background: rgba(255,255,255,0.07); border: 1px solid rgba(255,255,255,0.12);
  border-radius: 4px; padding: 7px 10px; color: white; font-size: 13px;
  outline: none; font-family: inherit;
}
.dialog-input:focus { border-color: var(--accent); }
.dialog-buttons { display: flex; gap: 8px; justify-content: flex-end; }
</style>
