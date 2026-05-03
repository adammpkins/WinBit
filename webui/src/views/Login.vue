<template>
  <div class="login-root">
    <div class="login-card panel-elevated">
      <div class="login-brand">
        <img class="login-icon" src="/winbit-icon.png" alt="" />
        <span class="login-name">WinBit</span>
      </div>
      <p class="login-sub">Sign in to continue</p>

      <div class="field-group">
        <label class="field-label">Username</label>
        <fluent-text-field
          :value="username"
          placeholder="admin"
          @change="username = $event.target.value"
          @keyup.enter="login"
          autofocus
        />
      </div>

      <div class="field-group">
        <label class="field-label">Password</label>
        <fluent-text-field
          :value="password"
          type="password"
          placeholder="••••••••"
          @change="password = $event.target.value"
          @keyup.enter="login"
        />
      </div>

      <div v-if="error" class="login-error">{{ error }}</div>

      <fluent-button
        appearance="accent"
        class="login-btn"
        :disabled="loading || undefined"
        @click="login"
      >
        {{ loading ? 'Signing in…' : 'Sign In' }}
      </fluent-button>
    </div>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '../api/index.js'
import { useAppStore } from '../stores/app.js'

const router = useRouter()
const appStore = useAppStore()

const username = ref('')
const password = ref('')
const error = ref('')
const loading = ref(false)

async function login() {
  if (loading.value) return
  error.value = ''
  loading.value = true
  try {
    const ok = await api.login(username.value || 'admin', password.value)
    if (ok) {
      appStore.setLoggedIn(true)
      router.push('/')
    } else {
      error.value = 'Invalid username or password.'
      password.value = ''
    }
  } catch {
    error.value = 'Could not reach WinBit. Is it running?'
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.login-root {
  width: 100%;
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
}

.login-card {
  width: 360px;
  padding: 40px 36px;
  display: flex;
  flex-direction: column;
  gap: 0;
}

.login-brand {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 8px;
}

.login-icon {
  width: 32px;
  height: 32px;
  filter: drop-shadow(0 0 12px var(--accent-glow));
  display: block;
}

.login-name {
  font-size: 22px;
  font-weight: 600;
  letter-spacing: -0.3px;
  color: var(--text-primary);
}

.login-sub {
  color: var(--text-secondary);
  font-size: 13px;
  margin-bottom: 28px;
}

.field-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-bottom: 16px;
}

.field-label {
  font-size: 12px;
  font-weight: 500;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.login-error {
  background: rgba(239, 83, 80, 0.12);
  border: 1px solid rgba(239, 83, 80, 0.3);
  border-radius: var(--radius-sm);
  color: #ef9a9a;
  font-size: 13px;
  padding: 10px 12px;
  margin-bottom: 16px;
}

.login-btn {
  margin-top: 4px;
  width: 100%;
}

/* Style fluent-text-field to match design */
fluent-text-field {
  width: 100%;
}
</style>
