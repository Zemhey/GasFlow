<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import api, { type 系统参数行 } from '@/api'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'

/**
 * 系统参数：GET /api/system/params、POST /api/system/params（数组=批量）
 *
 * 改这里等于改全系统的行为（月结口径、预警线、默认科目……），所以：
 *   · 只提交"改动过"的行，避免把整表原值回写一遍；
 *   · 保存成功后重新拉取，以后端为准（防止有人在别处改过）；
 *   · 是否有"未保存修改"始终可见，离开前不会稀里糊涂丢掉。
 */

const 行 = ref<系统参数行[]>([])
const 加载中 = ref(false)
const 保存中 = ref(false)
const 错误 = ref('')
/** 已修改的参数名集合：只提交改过的行 */
const 已改 = ref<Set<string>>(new Set())
const 分组 = ref<string>('')
const 关键词 = ref('')
const 只看已改 = ref(false)
const 筛选展开 = ref(false)

const 分组列表 = computed(() => Array.from(new Set(行.value.map((r) => r.参数分组 || '通用'))).sort())

const 命中 = computed(() => {
  const k = 关键词.value.trim().toLowerCase()
  return 行.value.filter((r) => {
    if (分组.value && (r.参数分组 || '通用') !== 分组.value) return false
    if (!k) return true
    return [r.参数名, r.参数说明, r.参数值]
      .map((v) => String(v ?? '').toLowerCase())
      .some((v) => v.includes(k))
  })
})

/** 表格实际显示的行：可选"只看已改" */
const 过滤后 = computed(() => (只看已改.value ? 命中.value.filter((r) => 已改.value.has(r.参数名)) : 命中.value))

const 有筛选 = computed(() => !!分组.value || !!关键词.value.trim() || 只看已改.value)

/* ------------------------------------------------------------------ KPI */

const 已改数 = computed(() => 已改.value.size)
const 待存列表 = computed(() => 行.value.filter((r) => 已改.value.has(r.参数名)))
const 布尔数 = computed(() => 行.value.filter((r) => 控件类型(r.参数类型) === 'switch').length)
const 日期数 = computed(() => 行.value.filter((r) => 控件类型(r.参数类型) === 'date').length)

/* ------------------------------------------------------------ 类型与编辑 */

/** 参数类型决定编辑控件 */
function 控件类型(t: string): 'switch' | 'number' | 'date' | 'text' {
  switch (String(t).toLowerCase()) {
    case 'bool':
    case 'boolean':
      return 'switch'
    case 'int':
    case 'decimal':
    case 'number':
      return 'number'
    case 'date':
    case 'datetime':
      return 'date'
    default:
      return 'text'
  }
}

function 是布尔(v: any) {
  const s = String(v ?? '').toLowerCase()
  return s === 'true' || s === '1' || s === 'false' || s === '0'
}

function 布尔值(row: 系统参数行) {
  const s = String(row.参数值 ?? '').toLowerCase()
  return s === 'true' || s === '1'
}

/** 改动统一入口：标脏 + 记录"是否已改回原值"由 已改 集合的刷新负责 */
function 标脏(row: 系统参数行) {
  const s = new Set(已改.value)
  s.add(row.参数名)
  已改.value = s
}

/**
 * 数字型参数的编辑值：接口里 参数值 一律是字符串（数据库存文本），
 * 但 el-input-number 只收 number —— 直接绑字符串会触发 prop 类型告警，
 * 且"清空"时会变成 null。这里在渲染时转成 number，写回时再转回字符串。
 */
function 数值(row: 系统参数行): number | undefined {
  const n = Number(row.参数值)
  return Number.isFinite(n) ? n : undefined
}

/** el-input-number 回写：null（被清空）保留为空串，其余按字符串落回 参数值 */
function 设数字(row: 系统参数行, v: number | null | undefined) {
  row.参数值 = v === null || v === undefined ? '' : String(v)
  标脏(row)
}

function 设布尔(row: 系统参数行, v: boolean) {
  row.参数值 = v ? 'true' : 'false'
  标脏(row)
}

/** 撤销单行改动：重新拉一次太贵，这里直接从服务端值无法回填，
 *  所以只移除"已改"标记并提示刷新 —— 明确比静默丢改动安全。 */
function 撤销标记(row: 系统参数行) {
  const s = new Set(已改.value)
  s.delete(row.参数名)
  已改.value = s
}

/* ------------------------------------------------------------------ 数据 */

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    行.value = (await api.system.参数()) ?? []
    已改.value = new Set()
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '系统参数加载失败，请稍后重试'
    行.value = []
    已改.value = new Set()
  } finally {
    加载中.value = false
  }
}

async function 保存() {
  const 待存 = 待存列表.value
  if (!待存.length) {
    ElMessage.info('没有改动')
    return
  }
  保存中.value = true
  错误.value = ''
  try {
    // 契约 POST /api/system/params：既可以传数组（批量），后端若只认单条会报错，
    // 这里按数组提交，失败时提示。
    await api.system.保存参数(待存)
    ElMessage.success(`已保存 ${待存.length} 项参数`)
    await 加载()
  } catch (e: any) {
    // 就地显示：参数保存失败是最不该"toast 一闪而过"的场景
    错误.value = e?.response?.data?.message ?? '参数保存失败：请检查参数值格式，确认后重试'
  } finally {
    保存中.value = false
  }
}

function 重置筛选() {
  分组.value = ''
  关键词.value = ''
  只看已改.value = false
}

onMounted(加载)
</script>

<template>
  <div>
    <PageHead
      title="系统参数"
      icon="Setting"
      sub="改这里等于改全系统的行为（月结口径 / 预警线 / 默认科目等）· 只在保存后生效，保存前可撤销"
    >
      <template #actions>
        <el-button :loading="加载中" @click="加载">
          <el-icon><Refresh /></el-icon> 重新读取
        </el-button>
        <el-button
          type="primary"
          :loading="保存中"
          :disabled="!已改数"
          @click="保存"
        >
          <el-icon><Select /></el-icon>
          保存修改{{ 已改数 ? `（${已改数}）` : '' }}
        </el-button>
      </template>
    </PageHead>

    <div class="stat-grid">
      <StatCard
        label="参数总数"
        icon="Setting"
        tone="info"
        :value="行.length.toLocaleString('zh-CN')"
        unit="项"
        :sub="`分布在 <b>${分组列表.length}</b> 个分组`"
      />
      <StatCard
        label="未保存修改"
        icon="EditPen"
        :tone="已改数 ? 'warning' : 'success'"
        :value="已改数.toLocaleString('zh-CN')"
        unit="项"
        :sub="已改数 ? '点右上角「保存修改」写入数据库' : '当前已全部保存'"
      />
      <StatCard
        label="开关型参数"
        icon="Switch"
        :value="布尔数.toLocaleString('zh-CN')"
        unit="项"
        sub="true / false，直接勾选即可，改完需保存"
      />
      <StatCard
        label="日期型参数"
        icon="Calendar"
        tone="brand"
        :value="日期数.toLocaleString('zh-CN')"
        unit="项"
        sub="如 biz.期初日期，影响期初与批次入库日"
      />
    </div>

    <el-card shadow="never">
      <button class="mobile-filter-toggle el-button el-button--default" @click="筛选展开 = !筛选展开">
        <el-icon><Filter /></el-icon> 筛选
        <span v-if="有筛选" class="badge">!</span>
      </button>

      <div class="filter-bar filter-collapsible" :class="{ 'is-open': 筛选展开 }">
        <el-select v-model="分组" clearable placeholder="全部分组" style="width: 160px">
          <el-option v-for="g in 分组列表" :key="g" :label="g" :value="g" />
        </el-select>

        <el-input v-model="关键词" placeholder="参数名 / 说明 / 值" clearable style="width: 220px">
          <template #prefix><el-icon><Search /></el-icon></template>
        </el-input>

        <el-checkbox v-model="只看已改" border>
          只看已改{{ 已改数 ? `（${已改数}）` : '' }}
        </el-checkbox>

        <el-button @click="重置筛选">
          <el-icon><RefreshLeft /></el-icon> 重置
        </el-button>

        <span class="spacer" />
        <el-tag v-if="已改数" type="warning" effect="light" size="small">
          <el-icon><EditPen /></el-icon> {{ 已改数 }} 项未保存
        </el-tag>
        <span v-else class="hint">共 {{ 行.length }} 项</span>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!过滤后.length"
        :empty-title="有筛选 ? '没有符合条件的参数' : '还没有系统参数'"
        :empty-desc="
          有筛选
            ? '换个分组或关键词试试；「只看已改」在没有任何改动时一定是空的。'
            : '系统参数由数据库初始化脚本写入（biz.* / fin.* 等分组），空表说明脚本没跑完整。'
        "
        empty-icon="Setting"
        :skeleton-rows="10"
        @retry="加载"
      >
        <template #emptyAction>
          <el-button v-if="有筛选" @click="重置筛选">
            <el-icon><RefreshLeft /></el-icon> 清除筛选
          </el-button>
        </template>

        <div class="table-wrap table-scroll-hint">
          <el-table
            :data="过滤后"
            size="small"
            stripe
            :row-class-name="({ row }) => (已改.has(row.参数名) ? 'row-dirty' : '')"
            max-height="calc(100vh - 380px)"
          >
            <el-table-column prop="参数名" label="参数名" width="240" show-overflow-tooltip>
              <template #default="{ row }">
                <span class="num">{{ row.参数名 }}</span>
                <el-icon v-if="已改.has(row.参数名)" class="dirty-mark" title="有未保存的修改">
                  <EditPen />
                </el-icon>
              </template>
            </el-table-column>

            <el-table-column label="参数值" min-width="280">
              <template #default="{ row }">
                <el-switch
                  v-if="控件类型(row.参数类型) === 'switch'"
                  :model-value="布尔值(row)"
                  @change="(v: any) => 设布尔(row, !!v)"
                />
                <el-input-number
                  v-else-if="控件类型(row.参数类型) === 'number'"
                  :model-value="数值(row)"
                  :controls="false"
                  size="small"
                  style="width: 100%"
                  @update:model-value="(v: any) => 设数字(row, v)"
                />
                <el-date-picker
                  v-else-if="控件类型(row.参数类型) === 'date'"
                  v-model="row.参数值"
                  type="date"
                  value-format="YYYY-MM-DD"
                  size="small"
                  style="width: 100%"
                  @change="标脏(row)"
                />
                <el-input v-else v-model="row.参数值" size="small" @change="标脏(row)" />
              </template>
            </el-table-column>

            <el-table-column prop="参数类型" label="类型" width="100">
              <template #default="{ row }">
                <el-tag size="small" effect="plain" type="info">{{ row.参数类型 || '—' }}</el-tag>
              </template>
            </el-table-column>

            <el-table-column prop="参数分组" label="分组" width="120">
              <template #default="{ row }">{{ row.参数分组 || '通用' }}</template>
            </el-table-column>

            <el-table-column prop="参数说明" label="说明" min-width="260" show-overflow-tooltip>
              <template #default="{ row }">{{ row.参数说明 || '—' }}</template>
            </el-table-column>

            <el-table-column label="状态" width="110" align="center" fixed="right">
              <template #default="{ row }">
                <el-button
                  v-if="已改.has(row.参数名)"
                  link
                  type="warning"
                  @click="撤销标记(row)"
                >
                  撤销标记
                </el-button>
                <span v-else class="flat">—</span>
              </template>
            </el-table-column>
          </el-table>
        </div>

        <div class="sum-bar">
          <span>显示 {{ 过滤后.length }} / 共 {{ 行.length }} 项</span>
          <span>未保存 <b :class="{ 'sum-bar__danger': 已改数 }">{{ 已改数 }}</b> 项</span>
          <span>分组 <b>{{ 分组列表.length }}</b> 个</span>
        </div>

        <div class="caliber-note">
          <b>口径</b>：参数值是<b>字符串</b>，编辑控件由<b>参数类型</b>决定（bool→开关、int/decimal→数字、
          date→日期、其余→文本），数据库里存文本、由各自业务过程转换。只有<b>改动过的行</b>会被提交；
          「撤销标记」只取消"待提交"标记、<b>不会</b>把值还原成数据库里的旧值 —— 要还原请点「重新读取」（会丢弃全部未保存修改）。
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

/* 参数名旁的小铅笔：不用看颜色也能知道哪几行改过（无障碍） */
.dirty-mark {
  margin-left: var(--sp-1);
  color: var(--c-warning-500);
  vertical-align: middle;
}

/* 已改行淡黄底：与"未改"行一眼区分 */
:deep(.row-dirty) {
  background: var(--c-warning-50) !important;
}

:deep(.row-dirty:hover > td.el-table__cell) {
  background: color-mix(in srgb, var(--c-warning-500) 14%, transparent) !important;
}
</style>
