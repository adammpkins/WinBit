<template>
  <svg
    class="speed-graph"
    :width="width"
    :height="height"
    role="img"
    aria-label="Speed history"
  >
    <!-- Download line -->
    <polyline
      v-if="dlPoints"
      :points="dlPoints"
      fill="none"
      :stroke="dlColor"
      stroke-width="1.5"
      stroke-linecap="round"
      stroke-linejoin="round"
      opacity="0.85"
    />
    <!-- Upload line -->
    <polyline
      v-if="upPoints"
      :points="upPoints"
      fill="none"
      :stroke="upColor"
      stroke-width="1.5"
      stroke-linecap="round"
      stroke-linejoin="round"
      opacity="0.85"
    />
  </svg>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  dlHistory: { type: Array, default: () => [] },
  upHistory: { type: Array, default: () => [] },
  width: { type: Number, default: 120 },
  height: { type: Number, default: 32 },
  dlColor: { type: String, default: 'var(--status-dl, #4FC3F7)' },
  upColor: { type: String, default: 'var(--status-seed, #66BB6A)' },
})

function toPoints(history, maxVal) {
  if (!history || history.length < 2) return null
  const n = history.length
  const w = props.width
  const h = props.height
  const pad = 2
  const usableH = h - pad * 2
  return history.map((v, i) => {
    const x = (i / (n - 1)) * w
    const y = maxVal > 0 ? h - pad - (v / maxVal) * usableH : h - pad
    return `${x.toFixed(1)},${y.toFixed(1)}`
  }).join(' ')
}

const combinedMax = computed(() => {
  const allVals = [...props.dlHistory, ...props.upHistory]
  return allVals.length > 0 ? Math.max(...allVals, 1) : 1
})

const dlPoints = computed(() => toPoints(props.dlHistory, combinedMax.value))
const upPoints = computed(() => toPoints(props.upHistory, combinedMax.value))
</script>

<style scoped>
.speed-graph {
  display: block;
  flex-shrink: 0;
  opacity: 0.8;
}
</style>
