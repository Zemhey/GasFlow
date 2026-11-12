<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import api from '@/api'
import { 配送阶段, 配送阶段名称, 配送阶段标签 } from '@/config/modules'
import { useAuthStore } from '@/stores/auth'
import PageHead from '@/components/PageHead.vue'
import DataState from '@/components/DataState.vue'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const 资源 = 'Dist.Task'

const 模式 = computed<'plan' | 'task'>(() => (route.path.startsWith('/dispatch/plans/') ? 'plan' : 'task'))
const id = computed(() => Number(route.params.id ?? 0))
const 数据 = ref<any>({})
const 加载中 = ref(false)
const 错误 = ref('')
const 动作中 = ref(false)

const 计划 = computed(() => 数据.value?.计划 ?? null)
const 任务 = computed(() => 数据.value?.任务 ?? null)
const 任务编号 = computed(() => Number(任务.value?.任务编号 ?? 0))
const 计划编号 = computed(() => Number(计划.value?.计划编号 ?? 0))

/** 跨计划/任务归一化后的阶段：0 计划 / 1 已转任务 / 2 已装车 / 3 在途 / 4 已回场 */
const 阶段 = computed(() => {
  if (!任务.value) return 0
  const s = Number(任务.value.单据状态)
  if (s === 0) return 1
  if (s === 1) return 2
  if (s === 2) return 3
  if (s === 3) return 4
  return 9
})

const 下一步 = computed<{ 动作: 'convert' | 'load' | 'out' | 'return'; 名: string; 类型: string } | null>(() => {
  if (!auth.有权限(资源, 'edit')) return null
  if (阶段.value === 0 && 计划编号.value) return { 动作: 'convert', 名: '转任务', 类型: 'warning' }
  if (阶段.value === 1 && 任务编号.value) return { 动作: 'load', 名: '装车', 类型: 'primary' }
  if (阶段.value === 2 && 任务编号.value) return { 动作: 'out', 名: '出车', 类型: 'success' }
  if (阶段.value === 3 && 任务编号.value) return { 动作: 'return', 名: '回场', 类型: 'warning' }
  return null
})

const 标题 = computed(() => 任务.value?.单据号码 || 计划.value?.单据号码 || '配送任务')
const 副标题 = computed(
  () => `计划 → 转任务 → 装车 → 出车 → 回场 · 当前：${配送阶段名称(阶段.value)}`
)

function 数字(v: any): string {
  const n = Number(v ?? 0)
  return Number.isFinite(n) ? n.toLocaleString('zh-CN') : '0'
}
function 时间(v: any): string {
  return v ? String(v).replace('T', ' ').slice(0, 16) : '—'
}
function 日期(v: any): string {
  return v ? String(v).slice(0, 10) : '—'
}

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    const r: any = 模式.value === 'plan' ? await api.dispatch.计划详情(id.value) : await api.dispatch.任务详情(id.value)
    数据.value = r ?? {}
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '配送详情加载失败，请稍后重试'
    数据.value = {}
  } finally {
    加载中.value = false
  }
}

async function 执行(动作: 'convert' | 'load' | 'out' | 'return') {
  const 说明: Record<string, string> = {
    convert: `将把网点「${计划.value?.网点名称 ?? ''}」当前全部待派订单转入本趟任务。`,
    load: '将从网点实体仓按 FIFO 跨批次装车，产生装车单、调拨单与库存流水。',
    out: '在途库存将转为车仓可用，任务进入「在途」。',
    return: '回场会自动清点车仓、按余货调回实体仓；未做送气出库的货物将原样返回。'
  }
  const 名称: Record<string, string> = { convert: '转任务', load: '装车', out: '出车', return: '回场' }
  const 确认 = await ElMessageBox.confirm(说明[动作], `${名称[动作]}确认`, {
    type: 'warning',
    confirmButtonText: `确定${名称[动作]}`,
    cancelButtonText: '取消'
  })
    .then(() => true)
    .catch(() => false)
  if (!确认) return

  动作中.value = true
  try {
    if (动作 === 'convert') {
      const r: any = await api.dispatch.转任务(计划编号.value)
      ElMessage.success(`已生成任务 ${r?.单据号码 ?? r?.任务编号 ?? ''}`)
      if (r?.任务编号) await router.replace(`/dispatch/tasks/${r.任务编号}`)
      else await 加载()
    } else {
      const r: any =
        动作 === 'load'
          ? await api.dispatch.装车(任务编号.value)
          : 动作 === 'out'
            ? await api.dispatch.出车(任务编号.value)
            : await api.dispatch.回场(任务编号.value)
      ElMessage.success(
        动作 === 'return'
          ? `已回场，余瓶 ${r?.车仓余瓶数 ?? 0} / 差异 ${r?.差异瓶数 ?? 0}`
          : `已${名称[动作]}：${r?.单据号码 ?? ''}`
      )
      await 加载()
    }
  } catch (e) {
    console.debug('配送动作未完成', e)
  } finally {
    动作中.value = false
  }
}

watch(
  () => route.fullPath,
  () => {
    加载()
  }
)

onMounted(加载)
</script>

<template>
  <div>
    <PageHead :title="标题" icon="Van" :sub="副标题">
      <template #actions>
        <el-button @click="router.push('/dispatch/tasks')">
          <el-icon><Back /></el-icon> 返回列表
        </el-button>
        <el-button :loading="加载中" @click="加载">
          <el-icon><Refresh /></el-icon> 刷新
        </el-button>
        <el-button
          v-if="下一步"
          :type="下一步.类型 as any"
          :loading="动作中"
          @click="执行(下一步.动作)"
        >
          {{ 下一步.名 }}
        </el-button>
      </template>
    </PageHead>

    <DataState :loading="加载中" :error="错误" :empty="!计划 && !任务" empty-title="配送单据不存在" @retry="加载">
      <div class="status-steps">
        <el-steps :active="阶段 + 1" finish-status="success" simple>
          <el-step v-for="s in 配送阶段" :key="s.value" :title="s.label" />
        </el-steps>
      </div>

      <el-card shadow="never">
        <template #header>
          <div class="card-head">
            <span>计划与任务</span>
            <el-tag :type="配送阶段标签(阶段)" size="small" effect="light">{{ 配送阶段名称(阶段) }}</el-tag>
          </div>
        </template>
        <el-descriptions :column="3" border size="small">
          <el-descriptions-item label="计划单号">{{ 计划?.单据号码 ?? '—' }}</el-descriptions-item>
          <el-descriptions-item label="计划日期">{{ 日期(计划?.计划日期) }}</el-descriptions-item>
          <el-descriptions-item label="网点">{{ 计划?.网点名称 ?? 任务?.网点名称 ?? '—' }}</el-descriptions-item>
          <el-descriptions-item label="车辆">{{ 计划?.车牌 ?? 任务?.车牌 ?? '—' }}</el-descriptions-item>
          <el-descriptions-item label="司机">{{ 计划?.司机姓名 ?? 任务?.司机姓名 ?? '—' }}</el-descriptions-item>
          <el-descriptions-item label="跟车">{{ 计划?.跟车姓名 ?? 任务?.跟车姓名 ?? '—' }}</el-descriptions-item>
          <el-descriptions-item label="路线">{{ 计划?.路线名称 ?? 任务?.路线说明 ?? '—' }}</el-descriptions-item>
          <el-descriptions-item label="计划出车">{{ 时间(计划?.计划出车时间) }}</el-descriptions-item>
          <el-descriptions-item label="计划客户 / 瓶数">
            {{ 数字(任务?.计划客户数 ?? 计划?.预估客户数) }} 户 /
            {{ 数字(任务?.计划瓶数 ?? 计划?.预估瓶数) }} 瓶
          </el-descriptions-item>
          <el-descriptions-item label="任务单号">{{ 任务?.单据号码 ?? '未转任务' }}</el-descriptions-item>
          <el-descriptions-item label="任务日期">{{ 日期(任务?.任务日期) }}</el-descriptions-item>
          <el-descriptions-item label="实际客户 / 瓶数">
            {{ 任务 ? `${数字(任务.实际客户数)} 户 / ${数字(任务.实际瓶数)} 瓶` : '—' }}
          </el-descriptions-item>
          <el-descriptions-item label="装车瓶数">{{ 数字(计划?.装车瓶数 ?? 任务?.计划瓶数) }}</el-descriptions-item>
          <el-descriptions-item label="出车时间">{{ 时间(任务?.出车时间) }}</el-descriptions-item>
          <el-descriptions-item label="回场时间">{{ 时间(任务?.回场时间) }}</el-descriptions-item>
          <el-descriptions-item label="装车单">{{ 数据.装车单?.单据号码 ?? '—' }}</el-descriptions-item>
          <el-descriptions-item label="回场单">{{ 数据.回场单?.单据号码 ?? '—' }}</el-descriptions-item>
          <el-descriptions-item label="备注">{{ 计划?.备注 ?? 任务?.备注 ?? '—' }}</el-descriptions-item>
        </el-descriptions>
      </el-card>

      <el-card shadow="never" class="mt-3">
        <template #header>
          <div class="card-head">
            <span>配送明细</span>
            <span class="hint">{{ 数据.配送明细?.length ?? 0 }} 家客户 · 一个销售订单一行</span>
          </div>
        </template>
        <el-table :data="数据.配送明细 ?? []" size="small" stripe>
          <el-table-column prop="行号" label="序" width="60" />
          <el-table-column prop="客户名称" label="客户" min-width="150" show-overflow-tooltip>
            <template #default="{ row }">{{ row.客户名称 ?? row.客户编号 }}</template>
          </el-table-column>
          <el-table-column prop="订单单据号码" label="销售订单" width="155" />
          <el-table-column prop="收货地址" label="收货地址" min-width="190" show-overflow-tooltip />
          <el-table-column label="计划" width="80" align="right">
            <template #default="{ row }">{{ 数字(row.计划瓶数) }}</template>
          </el-table-column>
          <el-table-column label="送达" width="80" align="right">
            <template #default="{ row }">{{ 数字(row.送达瓶数) }}</template>
          </el-table-column>
          <el-table-column prop="配送状态" label="配送状态" width="100" />
          <el-table-column prop="送气单编号" label="送气单" width="100">
            <template #default="{ row }">{{ row.送气单编号 ?? '—' }}</template>
          </el-table-column>
          <el-table-column label="送达时间" width="145">
            <template #default="{ row }">{{ 时间(row.送达时间) }}</template>
          </el-table-column>
        </el-table>
        <div v-if="!(数据.配送明细?.length)" class="empty-inline">
          还没有配送明细：计划需先「转任务」，任务明细由库过程按网点待派订单生成。
        </div>
      </el-card>

      <el-card v-if="数据.装车单" shadow="never" class="mt-3">
        <template #header>
          <div class="card-head">
            <span>装车单 {{ 数据.装车单.单据号码 }}</span>
            <span class="hint">{{ 数据.装车单.调拨单号码 ? `调拨 ${数据.装车单.调拨单号码}` : '装车调拨已过账' }}</span>
          </div>
        </template>
        <el-descriptions :column="4" border size="small">
          <el-descriptions-item label="装车日期">{{ 日期(数据.装车单.装车日期) }}</el-descriptions-item>
          <el-descriptions-item label="调出仓">{{ 数据.装车单.调出仓库名称 ?? '—' }}</el-descriptions-item>
          <el-descriptions-item label="车仓">{{ 数据.装车单.车仓名称 ?? '—' }}</el-descriptions-item>
          <el-descriptions-item label="装车瓶数">{{ 数字(数据.装车单.装车瓶数) }}</el-descriptions-item>
        </el-descriptions>
        <el-table :data="数据.装车明细 ?? []" size="small" stripe class="mt-3">
          <el-table-column prop="行号" label="行" width="60" />
          <el-table-column prop="商品名称" label="商品" min-width="150" />
          <el-table-column prop="批次编号" label="批次" width="90" />
          <el-table-column label="数量" width="90" align="right">
            <template #default="{ row }">{{ 数字(row.装车数量) }}</template>
          </el-table-column>
          <el-table-column label="成本单价" width="110" align="right">
            <template #default="{ row }">{{ Number(row.成本单价 ?? 0).toFixed(4) }}</template>
          </el-table-column>
        </el-table>
      </el-card>

      <el-card v-if="数据.回场单" shadow="never" class="mt-3">
        <template #header>
          <div class="card-head">
            <span>回场单 {{ 数据.回场单.单据号码 }}</span>
            <el-tag :type="Number(数据.回场单.差异瓶数) ? 'danger' : 'success'" size="small" effect="light">
              差异 {{ 数字(数据.回场单.差异瓶数) }}
            </el-tag>
          </div>
        </template>
        <el-descriptions :column="4" border size="small">
          <el-descriptions-item label="回场日期">{{ 日期(数据.回场单.回场日期) }}</el-descriptions-item>
          <el-descriptions-item label="车仓余瓶">{{ 数字(数据.回场单.车仓余瓶数) }}</el-descriptions-item>
          <el-descriptions-item label="回收空瓶">{{ 数字(数据.回场单.回收空瓶数) }}</el-descriptions-item>
          <el-descriptions-item label="差异瓶数">{{ 数字(数据.回场单.差异瓶数) }}</el-descriptions-item>
          <el-descriptions-item label="调入仓">{{ 数据.回场单.调入仓库名称 ?? '—' }}</el-descriptions-item>
          <el-descriptions-item label="调拨单">{{ 数据.回场单.调拨单号码 ?? '无余货，未调拨' }}</el-descriptions-item>
          <el-descriptions-item label="盘点单">{{ 数据.回场单.盘点单号码 ?? '无差异，未盘点' }}</el-descriptions-item>
          <el-descriptions-item label="回场人">{{ 数据.回场单.回场人姓名 ?? '—' }}</el-descriptions-item>
        </el-descriptions>

        <el-table :data="数据.回场明细 ?? []" size="small" stripe class="mt-3">
          <el-table-column prop="行号" label="行" width="60" />
          <el-table-column prop="商品名称" label="商品" min-width="150" />
          <el-table-column prop="批次编号" label="批次" width="90" />
          <el-table-column label="装车" width="80" align="right">
            <template #default="{ row }">{{ 数字(row.装车数量) }}</template>
          </el-table-column>
          <el-table-column label="送达" width="80" align="right">
            <template #default="{ row }">{{ 数字(row.送达数量) }}</template>
          </el-table-column>
          <el-table-column label="回收" width="80" align="right">
            <template #default="{ row }">{{ 数字(row.回收数量) }}</template>
          </el-table-column>
        </el-table>

        <template v-if="数据.回场实盘?.length">
          <div class="sub-title">回场实盘（差异的唯一基准：司机清点的物理实数）</div>
          <el-table :data="数据.回场实盘" size="small" stripe>
            <el-table-column prop="商品名称" label="商品" min-width="150" />
            <el-table-column prop="批次编号" label="批次" width="90" />
            <el-table-column label="装车" width="80" align="right">
              <template #default="{ row }">{{ 数字(row.装车数量) }}</template>
            </el-table-column>
            <el-table-column label="送达" width="80" align="right">
              <template #default="{ row }">{{ 数字(row.送达数量) }}</template>
            </el-table-column>
            <el-table-column label="应剩" width="80" align="right">
              <template #default="{ row }">{{ 数字(row.应剩数量) }}</template>
            </el-table-column>
            <el-table-column label="实盘" width="80" align="right">
              <template #default="{ row }">{{ 数字(row.实盘数量) }}</template>
            </el-table-column>
            <el-table-column label="差异" width="80" align="right">
              <template #default="{ row }">{{ 数字(row.盘点差异) }}</template>
            </el-table-column>
          </el-table>
        </template>
      </el-card>

      <div class="caliber-note">
        <b>口径</b>：装车 = 实体仓 → 车仓调拨（经过在途），出车 = 在途到货转为车仓可用；
        回场 = 回场清点/差异盘点 → 车仓余货调回同一实体仓。全程库存只由库过程写入，
        本页不直接改结存。
      </div>
    </DataState>
  </div>
</template>

<style scoped>
.status-steps {
  margin-bottom: 12px;
}

.card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.sub-title {
  margin: 16px 0 8px;
  font-weight: var(--fw-medium);
  color: var(--c-ink-600);
}

.empty-inline {
  padding: 18px 0 6px;
  color: var(--c-ink-400);
  font-size: 13px;
}
</style>
