import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { api } from '../api/index.js'

export const useTorrentsStore = defineStore('torrents', () => {
  const torrents = ref({})
  const serverState = ref({})
  const rid = ref(0)
  let _timer = null

  const torrentList = computed(() =>
    Object.entries(torrents.value).map(([hash, t]) => ({ hash, ...t }))
  )

  async function fetchUpdate() {
    const data = await api.getMainData(rid.value)
    if (!data) return

    rid.value = data.rid

    if (data.full_update) {
      torrents.value = data.torrents ?? {}
      serverState.value = data.server_state ?? {}
      return
    }

    // Apply delta
    const next = { ...torrents.value }
    if (data.torrents) {
      for (const [hash, delta] of Object.entries(data.torrents)) {
        next[hash] = { ...(next[hash] ?? {}), ...delta }
      }
    }
    if (data.torrents_removed) {
      for (const hash of data.torrents_removed) {
        delete next[hash]
      }
    }
    torrents.value = next

    if (data.server_state) {
      serverState.value = { ...serverState.value, ...data.server_state }
    }
  }

  function startPolling() {
    if (_timer) return
    fetchUpdate()
    _timer = setInterval(fetchUpdate, 2000)
  }

  function stopPolling() {
    if (_timer) { clearInterval(_timer); _timer = null }
    torrents.value = {}
    serverState.value = {}
    rid.value = 0
  }

  return { torrents, torrentList, serverState, startPolling, stopPolling }
})
