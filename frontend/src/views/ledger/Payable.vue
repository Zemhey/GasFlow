<script setup lang="ts">
import ViewLedger from './ViewLedger.vue'
</script>

<template>
  <ViewLedger
    view="vw_应付汇总"
    title="应付台账"
    副标题="按供应商汇总的应付余额与账期占用（与「供应商应付」同口径，这里是全量台账）"
    :重点列="['应付余额', '已付金额']"
  />
</template>
