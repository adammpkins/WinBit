<template>
  <fluent-dialog
    ref="dialogEl"
    :hidden="!open"
    modal="true"
    style="--dialog-width: 520px"
  >
    <div class="dialog-content">
      <div class="dialog-header">
        <span class="dialog-title">Add Torrent</span>
      </div>

      <div class="dialog-body">
        <label class="field-label">Magnet URI</label>
        <fluent-text-field
          :value="magnetUrl"
          placeholder="magnet:?xt=urn:btih:..."
          @change="magnetUrl = $event.target.value"
          @keyup.enter="add"
          class="magnet-input"
        />

        <div class="or-divider"><span>or</span></div>

        <label class="field-label">.torrent File</label>
        <input
          type="file"
          ref="fileInput"
          accept=".torrent"
          multiple
          style="display:none"
          @change="onFileChange"
        />
        <fluent-button appearance="lightweight" class="file-btn" @click="fileInput.click()">
          📎 Browse .torrent files…
        </fluent-button>
        <div v-if="selectedFiles.length" class="file-list">
          <div v-for="(file, i) in selectedFiles" :key="i" class="file-item">
            <span>{{ file.name }}</span>
            <button @click="removeFile(i)">×</button>
          </div>
        </div>

        <div v-if="error" class="dialog-error">{{ error }}</div>
      </div>

      <div class="dialog-footer">
        <fluent-button appearance="lightweight" @click="cancel">Cancel</fluent-button>
        <fluent-button
          appearance="accent"
          :disabled="(!magnetUrl.trim() && !selectedFiles.length) || loading || undefined"
          @click="add"
        >
          {{ loading ? 'Adding…' : 'Add Torrent' }}
        </fluent-button>
      </div>
    </div>
  </fluent-dialog>
</template>

<script setup>
import { ref, watch } from 'vue'
import { api } from '../api/index.js'

const props = defineProps({ open: Boolean })
const emit = defineEmits(['close'])

const magnetUrl = ref('')
const error = ref('')
const loading = ref(false)
const fileInput = ref(null)
const selectedFiles = ref([])

watch(() => props.open, (val) => {
  if (val) {
    magnetUrl.value = ''
    error.value = ''
    selectedFiles.value = []
  }
})

function cancel() { emit('close') }

function onFileChange(e) {
  selectedFiles.value = Array.from(e.target.files)
  // Reset native input so re-selecting the same file triggers change again
  fileInput.value.value = ''
}

function removeFile(i) {
  selectedFiles.value = selectedFiles.value.filter((_, idx) => idx !== i)
}

async function add() {
  const hasMagnet = magnetUrl.value.trim().length > 0
  const hasFiles = selectedFiles.value.length > 0
  if ((!hasMagnet && !hasFiles) || loading.value) return

  error.value = ''
  loading.value = true
  try {
    let magnetOk = true
    let filesOk = true

    if (hasMagnet) {
      magnetOk = await api.addMagnet(magnetUrl.value.trim())
    }
    if (hasFiles) {
      filesOk = await api.addTorrentFiles(selectedFiles.value)
    }

    if (magnetOk && filesOk) {
      emit('close')
    } else {
      error.value = 'Failed to add torrent. Check your input and try again.'
    }
  } catch {
    error.value = 'Network error. Please try again.'
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.dialog-content {
  display: flex;
  flex-direction: column;
  padding: 28px;
  gap: 20px;
  min-width: 440px;
}

.dialog-header {}
.dialog-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--text-primary);
  letter-spacing: -0.2px;
}

.dialog-body { display: flex; flex-direction: column; gap: 8px; }

.field-label {
  font-size: 12px;
  font-weight: 500;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.magnet-input { width: 100%; }

.or-divider {
  display: flex;
  align-items: center;
  gap: 10px;
  color: rgba(255, 255, 255, 0.2);
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  margin: 4px 0;
}
.or-divider::before,
.or-divider::after {
  content: '';
  flex: 1;
  height: 1px;
  background: rgba(255, 255, 255, 0.08);
}

.file-btn {
  width: 100%;
  text-align: left;
  border: 1px dashed rgba(255, 255, 255, 0.15);
  border-radius: 4px;
}

.file-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.file-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: rgba(255, 255, 255, 0.04);
  border-radius: 4px;
  padding: 4px 8px;
  font-size: 12px;
  color: rgba(255, 255, 255, 0.7);
}

.file-item button {
  background: none;
  border: none;
  color: rgba(255, 255, 255, 0.3);
  cursor: pointer;
  padding: 0 4px;
  font-size: 14px;
}

.file-item button:hover {
  color: rgba(255, 255, 255, 0.7);
}

.dialog-error {
  background: rgba(239, 83, 80, 0.1);
  border: 1px solid rgba(239, 83, 80, 0.25);
  border-radius: var(--radius-sm);
  color: #ef9a9a;
  font-size: 12px;
  padding: 8px 10px;
}

.dialog-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>
