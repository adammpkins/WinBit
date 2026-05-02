import {
  provideFluentDesignSystem,
  allComponents,
  baseLayerLuminance,
  StandardLuminance,
} from '@fluentui/web-components'

// Apply Fluent dark mode before anything renders
provideFluentDesignSystem().register(allComponents)
baseLayerLuminance.withDefault(StandardLuminance.DarkMode)

import { createApp } from 'vue'
import { createPinia } from 'pinia'
import router from './router/index.js'
import App from './App.vue'
import './assets/mica.css'

createApp(App).use(createPinia()).use(router).mount('#app')
