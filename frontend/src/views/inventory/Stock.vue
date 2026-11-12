<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import api, { type 库存行 } from '@/api'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'

const 行 = ref<库存行[]>([])
const 加载中 = ref(false)
const 错误 = ref('')
const 关键词 = ref('')
/** 低库存阈值：可用系统参数「库存预警下限」覆盖，取不到就用默认 10 */
const 阈值 = ref(10)
const 阈值来源 = ref('默认值')
const 筛选展开 = ref(false)
/** 只显示低库存行的开关：仓管最常用的视图 */
const 只看低库存 = ref(false)

const 过滤后 = computed(() => {
  let out = 行.value
  if (只看低库存.value) out = out.filter((r) => 是低库存(r))
  const k = 关键词.value.trim()
  if (!k) return out
  return out.filter(
    (r) => String(r.规格名称 ?? '').includes(k) || String(r.仓库 ?? '').includes(k)
  )
})

const 合计 = computed(() => {
  const t = 过滤后.value
  return {
    实瓶: t.reduce((s, r) => s + Number(r.实瓶数量 ?? 0), 0),
    空瓶: t.reduce((s, r) => s + Number(r.空瓶数量 ?? 0), 0),
    在借: t.reduce((s, r) => s + Number(r.在借瓶数 ?? 0), 0),
    报废: t.reduce((s, r) => s + Number(r.报废数量 ?? 0), 0),
    金额: t.reduce((s, r) => s + Number(r.库存金额 ?? 0), 0)
  }
})

const 低库存数 = computed(() => 行.value.filter((r) => 是低库存(r)).length)

/** el-table 合计行（列顺序与下面列定义一致：规格/仓库/实瓶/空瓶/在借/报废/金额/状态） */
const 合计行 = computed(() => [
  '合计',
  '',
  合计.value.实瓶,
  合计.value.空瓶,
  合计.value.在借,
  合计.value.报废,
  '¥' + 金额(合计.value.金额),
  ''
])

function 是低库存(row: 库存行) {
  return Number(row.实瓶数量 ?? 0) < 阈值.value
}

function 金额(v: any): string {
  return Number(v ?? 0).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

/** 库存健康度：低库存项占全部规格的比例，给仓管一个"整体判断" */
const 健康度 = computed(() => {
  const t = 行.value.length
  if (!t) return 100
  return Math.round(((t - 低库存数.value) / t) * 100)
})

const 有筛选 = computed(() => !!关键词.value.trim() || 只看低库存.value)

async function 读取阈值() {
  try {
    const 参数 = await api.system.参数()
    const p = (参数 ?? []).find(
      (x: any) => String(x.参数名).includes('预警') && String(x.参数名).includes('库存')
    )
    const n = Number(p?.参数值)
    if (!Number.isNaN(n) && n > 0) {
      阈值.value = n
      阈值来源.value = `系统参数 ${p.参数名}`
    }
  } catch {
    /* 系统参数接口未实现或无权：用默认阈值 */
  }
}

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    const r: any = await api.inventory.库存()
    // 后端把未实现的 /api 路由回落到 SPA 首页（HTTP 200 + HTML），axios 不会抛错。
    // 不校验就会把字符串当数组，computed 里的 reduce 直接崩掉（整页白屏）。
    if (!Array.isArray(r)) {
      错误.value = '库存接口返回的不是数据（可能后端尚未实现 /api/inventory/stock），请联系管理员'
      行.value = []
      return
    }
    行.value = r
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '库存数据加载失败，请稍后重试'
    行.value = []
  } finally {
    加载中.value = false
  }
}

function 重置() {
  关键词.value = ''
  只看低库存.value = false
}

function 导出() {
  if (!过滤后.value.length) {
    ElMessage.warning('当前没有可导出的数据')
    return
  }
  const 表头 = ['规格名称', '仓库', '实瓶数量', '空瓶数量', '在借瓶数', '报废数量', '库存金额']
  const 体 = 过滤后.value.map((r) =>
    [r.规格名称, r.仓库, r.实瓶数量, r.空瓶数量, r.在借瓶数, r.报废数量, Number(r.库存金额 ?? 0).toFixed(2)]
      .map((v) => `"${String(v ?? '')}"`)
      .join(',')
  )
  const csv = '\uFEFF' + [表头.join(','), ...体].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `库存查询_${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success(`已导出 ${过滤后.value.length} 行`)
}

onMounted(async () => {
  await Promise.all([加载(), 读取阈值()])
})
</script>

<template>
  <div>
    <PageHead
      title="库存查询"
      icon="Box"
      sub="五维结存（仓库 × 规格 × 批次 × 状态 × 权属）· 数据来自库存结存，随过账实时变化"
    >
      <template #actions>
        <el-button :disabled="!过滤后.length" @click="导出">
          <el-icon><Download /></el-icon> 导出
        </el-button>
        <el-button type="primary" :loading="加载中" @click="加载">
          <el-icon><Refresh /></el-icon> 刷新
        </el-button>
      </template>
    </PageHead>

    <!-- KPI：把"总量"和"风险"提到最上面，不用看表格就能判断 -->
    <div class="stat-grid">
      <StatCard
        label="实瓶合计"
        icon="Box"
        :value="合计.实瓶.toLocaleString('zh-CN')"
        unit="瓶"
        :sub="`空瓶 <b>${合计.空瓶.toLocaleString('zh-CN')}</b> · 在借 <b>${合计.在借.toLocaleString('zh-CN')}</b>`"
      />
      <StatCard
        label="库存金额"
        icon="Coin"
        tone="info"
        :value="`¥${金额(合计.金额)}`"
        :sub="`按移动加权单价计（阶段 5 口径）`"
      />
      <StatCard
        label="低库存规格"
        icon="Warning"
        :tone="低库存数 ? 'danger' : 'success'"
        :value="低库存数.toLocaleString('zh-CN')"
        unit="项"
        :sub="`低于预警线 <b>${阈值}</b>（${阈值来源}）· 库存健康度 <b>${健康度}%</b>`"
      />
      <StatCard
        label="报废数量"
        icon="Delete"
        tone="warning"
        :value="合计.报废.toLocaleString('zh-CN')"
        unit="瓶"
        sub="在用钢瓶中已判废的数量"
      />
    </div>

    <el-card shadow="never">
      <button class="mobile-filter-toggle el-button el-button--default" @click="筛选展开 = !筛选展开">
        <el-icon><Filter /></el-icon> 筛选
      </button>

      <div class="filter-bar filter-collapsible" :class="{ 'is-open': 筛选展开 }">
        <el-input
          v-model="关键词"
          placeholder="规格 / 仓库"
          clearable
          style="width: 200px"
          @keyup.enter="() => {}"
        >
          <template #prefix><el-icon><Search /></el-icon></template>
        </el-input>

        <span class="filter-bar__label">低库存线</span>
        <el-input-number v-model="阈值" :min="1" :max="9999" :controls="false" style="width: 90px" />
        <span class="filter-bar__label">实瓶</span>

        <el-checkbox v-model="只看低库存" border>
          只看低库存{{ 低库存数 ? `（${低库存数}）` : '' }}
        </el-checkbox>

        <el-button @click="重置">
          <el-icon><RefreshLeft /></el-icon> 重置
        </el-button>

        <span class="spacer" />
        <el-tag v-if="低库存数" type="danger" effect="light" size="small">
          <el-icon><Warning /></el-icon> 低库存 {{ 低库存数 }} 项
        </el-tag>
        <el-tag v-else type="success" effect="light" size="small">
          <el-icon><CircleCheck /></el-icon> 库存正常
        </el-tag>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!过滤后.length"
        :empty-title="有筛选 ? '没有符合筛选条件的库存' : '还没有库存数据'"
        :empty-desc="
          有筛选
            ? '放宽关键词，或取消「只看低库存」。'
            : '库存结存由采购到货 / 销售出库 / 调拨盘点等过账过程写入，先录入一张到货单并过账。'
        "
        empty-icon="Box"
        @retry="加载"
      >
        <template #emptyAction>
          <el-button v-if="有筛选" @click="重置">
            <el-icon><RefreshLeft /></el-icon> 清除筛选
          </el-button>
        </template>

        <div class="table-wrap table-scroll-hint">
          <el-table
            :data="过滤后"
            size="small"
            stripe
            :row-class-name="({ row }) => (是低库存(row) ? 'row-low' : '')"
            :summary-method="() => 合计行"
            show-summary
            max-height="560"
          >
            <el-table-column prop="规格名称" label="规格名称" min-width="160" show-overflow-tooltip />
            <el-table-column prop="仓库" label="仓库" width="130" show-overflow-tooltip />
            <el-table-column label="实瓶数量" width="110" align="right">
              <template #default="{ row }">
                <span class="num" :class="{ down: 是低库存(row) }">{{ row.实瓶数量 }}</span>
              </template>
            </el-table-column>
            <el-table-column prop="空瓶数量" label="空瓶数量" width="110" align="right">
              <template #default="{ row }"><span class="num">{{ row.空瓶数量 }}</span></template>
            </el-table-column>
            <el-table-column prop="在借瓶数" label="在借瓶数" width="110" align="right">
              <template #default="{ row }"><span class="num">{{ row.在借瓶数 }}</span></template>
            </el-table-column>
            <el-table-column prop="报废数量" label="报废数量" width="110" align="right">
              <template #default="{ row }">
                <span class="num" :class="{ flat: !Number(row.报废数量) }">{{ row.报废数量 }}</span>
              </template>
            </el-table-column>
            <el-table-column label="库存金额" width="140" align="right">
              <template #default="{ row }"><span class="num">¥{{ 金额(row.库存金额) }}</span></template>
            </el-table-column>
            <el-table-column label="库存状态" width="120" align="center" fixed="right">
              <template #default="{ row }">
                <el-tag v-if="是低库存(row)" type="danger" size="small" effect="light">
                  低于 {{ 阈值 }}
                </el-tag>
                <el-tag v-else type="success" size="small" effect="light">正常</el-tag>
              </template>
            </el-table-column>
          </el-table>
        </div>

        <div class="sum-bar">
          <span>筛选合计（{{ 过滤后.length }} 行）</span>
          <span>实瓶 <b>{{ 合计.实瓶.toLocaleString('zh-CN') }}</b></span>
          <span>空瓶 <b>{{ 合计.空瓶.toLocaleString('zh-CN') }}</b></span>
          <span>在借 <b>{{ 合计.在借.toLocaleString('zh-CN') }}</b></span>
          <span class="sum-bar__danger">报废 <b>{{ 合计.报废.toLocaleString('zh-CN') }}</b></span>
          <span>库存金额 <b>¥{{ 金额(合计.金额) }}</b></span>
        </div>

        <div class="caliber-note">
          <b>口径</b>：数量与金额都取自<b>库存结存</b>（五维：仓库 × 商品 × 批次 × 库存状态 × 权属），
          由过账过程维护，<b>不会出现"账实不符"的中间态</b>；库存金额 = Σ(结存数量 × 移动加权成本单价)。
          低库存线优先取系统参数（当前：<b>{{ 阈值来源 }}</b>），取不到时用默认 10。
        </div>
      </DataState>
    </el-card>
  </div>
</template>

<style scoped>
/* 桌面：筛选条横向排列。
   ⚠ 必须限定在 >767px：窄屏由 05-响应式.css 把 .filter-bar 改成纵向，
   这里若无条件写 display:flex 会覆盖它，导致 align-items:stretch 把
   输入框拉成整宽巨块（批次 3 实测到的问题）。 */
@media (min-width: 768px) {
  .filter-collapsible {
    display: flex;
  }
}

/* 低库存行整体淡红底：一眼扫过去就知道哪几行要补货 */
:deep(.row-low) {
  background: var(--c-danger-50) !important;
}

/* 悬浮时在 danger-50 之上叠一层淡红，不用写死色值（暗色模式才能跟着变） */
:deep(.row-low:hover > td.el-table__cell) {
  background: color-mix(in srgb, var(--c-danger-500) 14%, var(--c-danger-50)) !important;
}
</style>
