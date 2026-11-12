<script setup lang="ts">
import ViewLedger from './ViewLedger.vue'
</script>

<template>
  <ViewLedger
    view="vw_配送任务"
    title="配送台账"
    副标题="配送任务 / 路线 / 车辆 / 送达情况，派车与回场考核的依据"
    :重点列="[]"
    任务列="任务编号"
  />
</template>
