<script setup lang="ts">
import ViewLedger from './ViewLedger.vue'
</script>

<template>
  <ViewLedger
    view="vw_科目余额"
    title="科目余额"
    副标题="按会计期间 × 科目汇总的期末余额，用于试算平衡与报表取数"
    :重点列="['期末余额']"
  />
</template>
