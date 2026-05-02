const BASE = '/api/v2'

async function call(path, opts = {}) {
  const res = await fetch(BASE + path, opts)
  return res
}

export const api = {
  async login(username, password) {
    const body = new URLSearchParams({ username, password })
    const res = await call('/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: body.toString()
    })
    const text = await res.text()
    return text.trim() === 'Ok.'
  },

  async logout() {
    await call('/auth/logout', { method: 'POST' })
  },

  async getVersion() {
    const res = await call('/app/version')
    if (!res.ok) return null
    return res.text()
  },

  async getMainData(rid = 0) {
    const res = await call(`/sync/maindata?rid=${rid}`)
    if (res.status === 403) return null
    if (!res.ok) return null
    return res.json()
  },

  async addMagnet(url) {
    const body = new URLSearchParams({ urls: url })
    const res = await call('/torrents/add', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: body.toString()
    })
    return res.ok
  },

  async pauseTorrent(hash) {
    const body = new URLSearchParams({ hashes: hash })
    await call('/torrents/stop', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: body.toString()
    })
  },

  async resumeTorrent(hash) {
    const body = new URLSearchParams({ hashes: hash })
    await call('/torrents/start', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: body.toString()
    })
  },

  async pauseAll() {
    const body = new URLSearchParams({ hashes: 'all' })
    await call('/torrents/stop', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: body.toString()
    })
  },

  async resumeAll() {
    const body = new URLSearchParams({ hashes: 'all' })
    await call('/torrents/start', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: body.toString()
    })
  },

  async deleteTorrent(hash, deleteFiles = false) {
    const body = new URLSearchParams({ hashes: hash, deleteFiles: String(deleteFiles) })
    await call('/torrents/delete', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: body.toString()
    })
  },

  async getPreferences() {
    const res = await call('/app/preferences')
    if (!res.ok) return null
    return res.json()
  },

  async setPreferences(prefs) {
    const body = new URLSearchParams({ json: JSON.stringify(prefs) })
    await call('/app/setPreferences', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: body.toString()
    })
  },

  async getLogs(lastKnownId = -1) {
    const res = await call(`/log/main?last_known_id=${lastKnownId}`)
    if (!res.ok) return []
    return res.json()
  },

  async addTorrentFiles(files) {
    const fd = new FormData()
    for (const f of files) fd.append('torrents', f)
    const res = await call('/torrents/add', { method: 'POST', body: fd })
    return res.ok
  }
}
