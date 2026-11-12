<script setup lang="ts">
import ViewLedger from './ViewLedger.vue'
</script>

<template>
  <ViewLedger
    view="vw_采购到货"
    title="采购台账"
    副标题="采购到货台账：供应商 / 到货日期 / 数量 / 金额 / 质检结论（进货成本与结算的依据）"
    :重点列="['数量', '金额']"
  />
</template>
