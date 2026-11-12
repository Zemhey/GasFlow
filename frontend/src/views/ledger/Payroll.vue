<script setup lang="ts">
import ViewLedger from './ViewLedger.vue'
</script>

<template>
  <ViewLedger
    view="vw_薪资汇总"
    title="薪资台账"
    副标题="按员工 × 月份的薪资汇总（生成口径：(员工, 年月) 唯一）"
    :重点列="['应发合计', '实发合计']"
  />
</template>
