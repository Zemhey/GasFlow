<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import api from '@/api'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'

const 全部 = ref<any[]>([])
const 风险 = ref<any[]>([])
const 阈值 = ref(30)
const 加载中 = ref(false)
const 错误 = ref('')
/** 移动端：筛选条默认收起 */
const 筛选展开 = ref(false)
/** 前端搜索：后端只按天数过滤，这里补一个客户/区域/电话的本地筛选 */
const 关键词 = ref('')

/* ------------------------------------------------------------------ 数据分档 */

/**
 * 分级口径与后端一致：≥60 天流失风险 / ≥ 阈值 待回访 / 其余正常。
 * 阈值可由用户在筛选条里调（7-180 天），调完点「刷新」重新向后端取数。
 */
function 标签(d: any) {
  const n = Number(d)
  if (d === null || d === undefined || Number.isNaN(n)) return 'info'
  if (n >= 60) return 'danger'
  if (n >= 阈值.value) return 'warning'
  return 'success'
}

function 档名(d: any): string {
  const n = Number(d)
  if (d === null || d === undefined || Number.isNaN(n)) return '从未充气'
  if (n >= 60) return '流失风险'
  if (n >= 阈值.value) return '待回访'
  return '正常'
}

/**
 * 「待回访」直接采用后端返回的风险名单（后端判据：间隔天数 ≥ 阈值），
 * 不在前端重算 —— 否则阈值边界（等于阈值算不算）容易与后端不一致。
 */
const 待回访 = computed(() => 风险.value)
const 风险客户 = computed(() => 风险.value.filter((r) => Number(r.间隔天数 ?? 0) >= 60))
/** 近 30 天有充气记录：活跃客户的近似判据（接口给的「近30天次数」> 0） */
const 近期活跃 = computed(() => 全部.value.filter((r) => Number(r.近30天次数 ?? 0) > 0).length)
/** 从未充气：间隔天数为空（后端 MAX(送气日期) 取不到值，DATEDIFF 返回 NULL） */
const 从未充气 = computed(() => 全部.value.filter((r) => r.间隔天数 === null || r.间隔天数 === undefined).length)

const 过滤后 = computed(() => {
  const k = 关键词.value.trim()
  if (!k) return 全部.value
  return 全部.value.filter((r) =>
    [r.客户, r.区域, r.电话].some((v) => String(v ?? '').includes(k))
  )
})

const 有筛选 = computed(() => !!关键词.value.trim())

function 日期(v: any): string {
  const s = String(v ?? '')
  return s ? s.slice(0, 10) : '—'
}

function 间隔文本(v: any): string {
  if (v === null || v === undefined || v === '') return '从未'
  return `${Number(v)} 天`
}

const 空标题 = computed(() => (有筛选.value ? '没有符合条件的客户' : '没有有效客户记录'))
const 空说明 = computed(() =>
  有筛选.value
    ? '换个客户 / 区域 / 电话再试，或清空关键词看全部。'
    : '预警只统计「是否有效 = 1」的客户；客户档案为空时这里不会有数据。请先在客户档案里维护客户，并录入送气单。'
)

/* ------------------------------------------------------------------ 加载 */

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    const r: any = await api.reports.预警({ 天数: 阈值.value })
    全部.value = r?.全部 ?? []
    风险.value = r?.风险 ?? []
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '预警数据加载失败，请稍后重试'
    全部.value = []
    风险.value = []
  } finally {
    加载中.value = false
  }
}

function 重置() {
  关键词.value = ''
  阈值.value = 30
  加载()
}

/** 导出当前视图（默认按「间隔天数」倒序，最需要回访的排在最前） */
function 导出() {
  const 数据 = [...过滤后.value].sort(
    (a, b) => Number(b.间隔天数 ?? -1) - Number(a.间隔天数 ?? -1)
  )
  if (!数据.length) {
    ElMessage.warning('当前没有可导出的数据')
    return
  }
  const 表头 = ['客户', '区域', '联系电话', '最后充气', '间隔天数', '档位', '近30天次数']
  const 体 = 数据.map((r) =>
    [
      r.客户,
      r.区域,
      r.电话,
      日期(r.最后充气) === '—' ? '' : 日期(r.最后充气),
      r.间隔天数 ?? '',
      档名(r.间隔天数),
      r.近30天次数 ?? 0
    ]
      .map((v) => `"${String(v ?? '')}"`)
      .join(',')
  )
  const csv = '\uFEFF' + [表头.join(','), ...体].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `客户预警_${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success(`已导出 ${数据.length} 行`)
}

onMounted(加载)
</script>

<template>
  <div>
    <PageHead
      title="客户预警"
      icon="Bell"
      sub="按最后充气日期距今天数分级：≥ 阈值待回访 · ≥ 60 天流失风险 · 只统计有效客户"
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

    <div class="stat-grid">
      <StatCard
        label="有效客户"
        icon="User"
        :value="全部.length.toLocaleString('zh-CN')"
        unit="家"
        :sub="`近 30 天有充气 <b>${近期活跃}</b> 家 · 从未充气 <b>${从未充气}</b> 家`"
      />
      <StatCard
        label="待回访"
        icon="Phone"
        :tone="待回访.length ? 'warning' : 'success'"
        :value="待回访.length.toLocaleString('zh-CN')"
        unit="家"
        :sub="`间隔 ≥ <b>${阈值}</b> 天（后端按此阈值筛选）· 其中 ≥ 60 天 <b>${风险客户.length}</b> 家`"
      />
      <StatCard
        label="流失风险"
        icon="Warning"
        :tone="风险客户.length ? 'danger' : 'success'"
        :value="风险客户.length.toLocaleString('zh-CN')"
        unit="家"
        sub="间隔 ≥ 60 天，建议优先电话回访"
      />
      <StatCard
        label="正常"
        icon="CircleCheck"
        tone="info"
        :value="(全部.length - 待回访.length).toLocaleString('zh-CN')"
        unit="家"
        :sub="`间隔小于 <b>${阈值}</b> 天`"
      />
    </div>

    <el-card shadow="never">
      <button
        class="mobile-filter-toggle el-button el-button--default"
        @click="筛选展开 = !筛选展开"
      >
        <el-icon><Filter /></el-icon> 筛选
        <span v-if="有筛选 || 阈值 !== 30" class="badge">!</span>
      </button>

      <div class="filter-bar filter-collapsible" :class="{ 'is-open': 筛选展开 }">
        <el-input
          v-model="关键词"
          placeholder="客户 / 区域 / 电话"
          clearable
          style="width: 200px"
        >
          <template #prefix><el-icon><Search /></el-icon></template>
        </el-input>

        <span class="filter-bar__label">超过</span>
        <el-input-number
          v-model="阈值"
          :min="7"
          :max="180"
          :step="7"
          :controls="false"
          style="width: 80px"
        />
        <span class="filter-bar__label">天未充气视为需回访</span>

        <el-button type="primary" @click="加载">
          <el-icon><Search /></el-icon> 查询
        </el-button>
        <el-button @click="重置">
          <el-icon><RefreshLeft /></el-icon> 重置
        </el-button>

        <span class="spacer" />
        <el-tag v-if="风险客户.length" type="danger" effect="light" size="small">
          <el-icon><Warning /></el-icon> 流失风险 {{ 风险客户.length }}
        </el-tag>
        <el-tag type="warning" effect="light" size="small">待回访 {{ 待回访.length }}</el-tag>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!过滤后.length"
        :empty-title="空标题"
        :empty-desc="空说明"
        empty-icon="Bell"
        @retry="加载"
      >
        <template #emptyAction>
          <el-button v-if="有筛选" @click="关键词 = ''">
            <el-icon><RefreshLeft /></el-icon> 清空关键词
          </el-button>
          <el-button v-else @click="加载">
            <el-icon><Refresh /></el-icon> 刷新
          </el-button>
        </template>

        <div class="legend mb-2">
          <span class="legend__item"><i class="legend__swatch swatch-ok" />&lt; {{ 阈值 }} 天 正常</span>
          <span class="legend__item"><i class="legend__swatch swatch-warn" />≥ {{ 阈值 }} 天 待回访</span>
          <span class="legend__item"><i class="legend__swatch swatch-bad" />≥ 60 天 流失风险</span>
          <span class="legend__item"><i class="legend__swatch swatch-none" />无记录 从未充气</span>
        </div>

        <div class="table-wrap table-scroll-hint">
          <el-table :data="过滤后" size="small" stripe max-height="calc(100vh - 320px)">
            <el-table-column prop="客户" label="客户" min-width="180" show-overflow-tooltip />
            <el-table-column prop="区域" label="区域" width="120">
              <template #default="{ row }">{{ row.区域 || '—' }}</template>
            </el-table-column>
            <el-table-column label="联系电话" width="150">
              <template #default="{ row }">
                <span class="num">{{ row.电话 || '—' }}</span>
              </template>
            </el-table-column>
            <el-table-column label="最后充气" width="140">
              <template #default="{ row }">
                <span class="num">{{ 日期(row.最后充气) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="间隔天数" width="130" align="right">
              <template #default="{ row }">
                <el-tag :type="标签(row.间隔天数)" size="small" effect="light">
                  {{ 间隔文本(row.间隔天数) }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="档位" width="120" align="center">
              <template #default="{ row }">
                <span :class="标签(row.间隔天数) === 'danger' ? 'down' : 标签(row.间隔天数) === 'warning' ? 'text-warning' : 'flat'">
                  {{ 档名(row.间隔天数) }}
                </span>
              </template>
            </el-table-column>
            <el-table-column label="近30天充气次数" width="150" align="right">
              <template #default="{ row }">
                <span class="num" :class="{ flat: !Number(row.近30天次数) }">
                  {{ row.近30天次数 ?? 0 }}
                </span>
              </template>
            </el-table-column>
          </el-table>
        </div>

        <div class="sum-bar">
          <span>当前视图（{{ 过滤后.length }} / {{ 全部.length }} 家）</span>
          <span>待回访（≥ {{ 阈值 }} 天）<b>{{ 待回访.length }}</b></span>
          <span class="sum-bar__danger">流失风险（≥ 60 天）<b>{{ 风险客户.length }}</b></span>
          <span>从未充气 <b>{{ 从未充气 }}</b></span>
          <span>近 30 天有充气 <b>{{ 近期活跃 }}</b></span>
        </div>

        <div class="caliber-note">
          <b>口径</b>：名单取预警接口，逐条统计<b>有效客户</b>（是否有效 = 1）；
          「最后充气」= 该客户所有送气单的<b>最大送气日期</b>（SQL MAX，不用 Access 的 Last()）；
          「间隔天数」= DATEDIFF(day, 最后充气, 今天)，由<b>后端计算</b>，前端不重算；
          「近30天充气次数」= 送气日期在最近 30 天内的送气单张数。
          档位阈值：<b>{{
            阈值
          }} 天</b>及以上为待回访（该名单由<b>后端</b>按此阈值筛选并返回）；<b>60 天</b>及以上为流失风险
          （前端在全部客户里按间隔天数分档展示）；
          最后充气为空表示该客户<b>从未充气</b>，DATEDIFF 返回 NULL。
        </div>
      </DataState>
    </el-card>
  </div>
</template>

<style scoped>
/* 图例色块：与档位标签语义色一致 */
.legend__swatch.swatch-ok {
  background: var(--c-success-500);
}

.legend__swatch.swatch-warn {
  background: var(--c-warning-500);
}

.legend__swatch.swatch-bad {
  background: var(--c-danger-500);
}

.legend__swatch.swatch-none {
  background: var(--c-ink-200);
}
</style>
