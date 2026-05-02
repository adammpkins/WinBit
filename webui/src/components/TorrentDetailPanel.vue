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
      <button class="close-btn" @click="$emit('close')" title="Close">✕</button>
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

    <!-- Other tabs: stub until implemented -->
    <div v-else class="tab-content tab-stub">
      <span class="stub-text">{{ activeTab }} — coming soon</span>
    </div>
  </div>
</template>

<script setup>
import { ref } from 'vue'

const props = defineProps({ torrent: { type: Object, required: true } })
defineEmits(['close'])

const tabs = ['General', 'Trackers', 'Peers', 'Content', 'Speed']
const activeTab = ref('General')

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

/* Stub for unimplemented tabs */
.tab-stub {
  display: flex;
  align-items: center;
  justify-content: center;
}
.stub-text {
  font-size: 12px;
  color: rgba(255, 255, 255, 0.3);
  font-style: italic;
}

.mono { font-family: "Cascadia Code", "Consolas", monospace; }
</style>
