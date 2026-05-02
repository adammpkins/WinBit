import { createRouter, createWebHashHistory } from 'vue-router'
import { useAppStore } from '../stores/app.js'

const routes = [
  { path: '/login', component: () => import('../views/Login.vue'), meta: { public: true } },
  { path: '/', component: () => import('../views/Transfers.vue') },
  { path: '/settings', component: () => import('../views/Settings.vue') },
  { path: '/log', component: () => import('../views/Log.vue') },
]

const router = createRouter({ history: createWebHashHistory(), routes })

router.beforeEach(async (to) => {
  const app = useAppStore()
  if (!app.isLoggedIn && !to.meta.public) {
    await app.checkAuth()
    if (!app.isLoggedIn) return '/login'
  }
  if (app.isLoggedIn && to.path === '/login') return '/'
})

export default router
