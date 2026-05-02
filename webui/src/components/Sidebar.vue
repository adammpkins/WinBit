<template>
  <nav class="sidebar">
    <div class="sidebar-brand">
      <span class="brand-icon">⚡</span>
      <span class="brand-name">WinBit</span>
    </div>

    <div class="sidebar-nav">
      <RouterLink
        v-for="item in nav"
        :key="item.to"
        :to="item.to"
        class="nav-item"
        active-class="nav-item--active"
      >
        <span class="nav-icon">{{ item.icon }}</span>
        <span class="nav-label">{{ item.label }}</span>
      </RouterLink>
    </div>

    <div class="sidebar-footer">
      <fluent-button
        appearance="lightweight"
        class="logout-btn"
        @click="logout"
      >
        ↩ Sign Out
      </fluent-button>
      <span class="version-str text-secondary">{{ version }}</span>
    </div>
  </nav>
</template>

<script setup>
import { storeToRefs } from 'pinia'
import { useRouter } from 'vue-router'
import { useAppStore } from '../stores/app.js'
import { api } from '../api/index.js'

const router = useRouter()
const appStore = useAppStore()
const { version } = storeToRefs(appStore)

const nav = [
  { to: '/',         icon: '⬇↑', label: 'Transfers' },
  { to: '/log',      icon: '📋',  label: 'Log' },
  { to: '/settings', icon: '⚙',   label: 'Settings' },
]

async function logout() {
  await api.logout()
  appStore.setLoggedIn(false)
  router.push('/login')
}
</script>

<style scoped>
.sidebar {
  width: var(--sidebar-width);
  flex-shrink: 0;
  height: 100%;
  display: flex;
  flex-direction: column;
  background: rgba(14, 12, 28, 0.6);
  backdrop-filter: blur(32px) saturate(200%);
  border-right: 1px solid var(--border-subtle);
  padding: 0;
}

.sidebar-brand {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 20px 16px 16px;
  border-bottom: 1px solid var(--border-subtle);
  margin-bottom: 8px;
}

.brand-icon {
  font-size: 22px;
  filter: drop-shadow(0 0 10px var(--accent-glow));
  flex-shrink: 0;
}

.brand-name {
  font-size: 17px;
  font-weight: 700;
  letter-spacing: -0.3px;
  background: linear-gradient(135deg, #e8e8f0 0%, var(--accent-light) 100%);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}

.sidebar-nav {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 0 8px;
  overflow-y: auto;
}

.nav-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 9px 12px;
  border-radius: var(--radius-md);
  color: var(--text-secondary);
  text-decoration: none;
  font-size: 13.5px;
  transition: background var(--t-fast), color var(--t-fast);
  position: relative;
}

.nav-item:hover {
  background: var(--surface-2);
  color: var(--text-primary);
}

.nav-item--active {
  background: var(--surface-active);
  color: var(--text-primary);
}

.nav-item--active::before {
  content: '';
  position: absolute;
  left: 0;
  top: 6px;
  bottom: 6px;
  width: 3px;
  background: var(--accent);
  border-radius: 0 3px 3px 0;
}

.nav-icon { font-size: 15px; width: 20px; text-align: center; flex-shrink: 0; }
.nav-label { font-weight: 450; }

.sidebar-footer {
  padding: 12px 8px 16px;
  border-top: 1px solid var(--border-subtle);
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.logout-btn { width: 100%; justify-content: flex-start; font-size: 13px; }
.version-str { font-size: 11px; padding: 0 4px; }
</style>
