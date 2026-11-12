<script setup lang="ts">
import ViewLedger from './ViewLedger.vue'
</script>

<template>
  <ViewLedger
    view="vw_库存流水"
    title="库存盘点台账"
    副标题="库存流水全量台账（业务类型 / 方向 / 数量 / 单据来源），可与结存逐笔对账"
    :重点列="['数量']"
  />
</template>
