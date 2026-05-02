<template>
  <div class="app-shell">
    <Sidebar v-if="showSidebar" />
    <main class="app-main">
      <RouterView v-slot="{ Component }">
        <Transition name="fade" mode="out-in">
          <component :is="Component" :key="$route.path" />
        </Transition>
      </RouterView>
    </main>
  </div>
</template>

<script setup>
import { computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import Sidebar from './components/Sidebar.vue'
import { useAppStore } from './stores/app.js'

const route = useRoute()
const appStore = useAppStore()
const showSidebar = computed(() => route.path !== '/login')

// Load preferences whenever the user becomes authenticated
watch(() => appStore.isLoggedIn, (loggedIn) => {
  if (loggedIn) appStore.loadPreferences()
}, { immediate: true })
</script>

<style>
.app-shell {
  display: flex;
  width: 100%;
  height: 100%;
}

.app-main {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.app-main > * { flex: 1; min-height: 0; }

/* Route fade transition */
.fade-enter-active,
.fade-leave-active {
  transition: opacity 150ms ease, transform 150ms ease;
}
.fade-enter-from {
  opacity: 0;
  transform: translateY(4px);
}
.fade-leave-to {
  opacity: 0;
  transform: translateY(-4px);
}
</style>
