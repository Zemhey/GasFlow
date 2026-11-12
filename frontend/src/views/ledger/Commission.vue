<script setup lang="ts">
import ViewLedger from './ViewLedger.vue'
</script>

<template>
  <ViewLedger
    view="vw_提成明细报表"
    title="提成台账"
    副标题="按员工 × 月份 × 规则的提成计算明细（口径：只算已签收）"
    :重点列="['提成金额']"
  />
</template>
