<script setup lang="ts">
import ViewLedger from './ViewLedger.vue'
</script>

<template>
  <ViewLedger
    view="vw_应收汇总"
    title="应收台账"
    副标题="按客户汇总的应收余额、信用额度与账期占用（超信用额度的客户优先看）"
    :重点列="['应收余额', '已收金额']"
  />
</template>
