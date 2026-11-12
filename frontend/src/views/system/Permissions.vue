<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import api, { type 权限矩阵 } from '@/api'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'

/**
 * 权限矩阵：GET /api/system/permissions/matrix、POST /api/system/permissions/{岗位编号}
 * 保存载荷形状**保持不变**：每岗位 [{ 资源编号, 权限编号 }]（全量覆盖，不是增量）。
 *
 * 这个页面最容易出的事故是"改了没保存就走了"，所以：
 *   · 有未保存修改时，切换岗位 / 刷新都要先确认；
 *   · 页头与工具条双处显示"未保存 N 项"；
 *   · 已授 / 未授同时用颜色与图标区分（不只靠颜色）。
 */

const 矩阵 = ref<权限矩阵 | null>(null)
const 当前岗位 = ref<number | null>(null)
const 选中 = ref<Set<string>>(new Set())
/** 该岗位在服务端已保存的授权快照：用来判断"有没有未保存修改" */
const 已存快照 = ref<Set<string>>(new Set())
const 加载中 = ref(false)
const 保存中 = ref(false)
const 错误 = ref('')
const 模块 = ref('')
const 只看已授 = ref(false)
const 筛选展开 = ref(false)
/** 岗位切换的"回拨"守卫：见 换岗位() */
let 忽略下次切换 = false
let 原岗位: number | null = null

const 岗位 = computed(() => 矩阵.value?.岗位 ?? [])
const 资源 = computed(() => 矩阵.value?.资源 ?? [])
const 权限 = computed(() => 矩阵.value?.权限 ?? [])

/** 按 模块 分组显示的筛选项 */
const 模块列表 = computed(() =>
  Array.from(new Set(资源.value.map((r) => r.模块).filter(Boolean))).sort()
)

/** 权限列的总格数：分母，用来算覆盖率 */
const 总格数 = computed(() => 资源.value.length * 权限.value.length)

const 行 = computed(() => {
  let 集 = 模块.value ? 资源.value.filter((r) => r.模块 === 模块.value) : 资源.value
  if (只看已授.value) {
    集 = 集.filter((r) => 权限.value.some((q) => 有(r.资源编号, q.权限编号)))
  }
  return 集
})

/* ------------------------------------------------------------ 选中与差异 */

const key = (资源编号: number, 权限编号: number) => `${资源编号}-${权限编号}`

function 有(资源编号: number, 权限编号: number) {
  return 选中.value.has(key(资源编号, 权限编号))
}

function 切换(资源编号: number, 权限编号: number, v: boolean) {
  const k = key(资源编号, 权限编号)
  const s = new Set(选中.value)
  if (v) s.add(k)
  else s.delete(k)
  选中.value = s
}

/** 整行全选 / 全不选 */
function 切换整行(资源编号: number, v: boolean) {
  const s = new Set(选中.value)
  权限.value.forEach((q) => {
    const k = key(资源编号, q.权限编号)
    if (v) s.add(k)
    else s.delete(k)
  })
  选中.value = s
}

/** 整列全选 / 全不选（只作用于当前显示的行，避免误伤被筛掉的行） */
function 切换整列(权限编号: number, v: boolean) {
  const s = new Set(选中.value)
  行.value.forEach((r) => {
    const k = key(r.资源编号, 权限编号)
    if (v) s.add(k)
    else s.delete(k)
  })
  选中.value = s
}

function 列全选态(权限编号: number): { all: boolean; some: boolean } {
  const 行集 = 行.value
  if (!行集.length) return { all: false, some: false }
  const 中 = 行集.filter((r) => 有(r.资源编号, 权限编号)).length
  return { all: 中 === 行集.length, some: 中 > 0 }
}

/** 载入当前岗位的授权（授权行按 岗位编号 过滤） */
function 载入授权() {
  const s = new Set<string>()
  ;(矩阵.value?.授权 ?? []).forEach((x: any) => {
    if (Number(x.岗位编号) === Number(当前岗位.value)) s.add(key(x.资源编号, x.权限编号))
  })
  选中.value = s
  已存快照.value = new Set(s)
}

const 已授权数 = computed(() => 选中.value.size)

/** 未保存修改 */
const 脏数 = computed(() => {
  let n = 0
 选中.value.forEach((k) => {
    if (!已存快照.value.has(k)) n += 1
  })
  已存快照.value.forEach((k) => {
    if (!选中.value.has(k)) n += 1
  })
  return n
})
const 有未保存 = computed(() => 脏数.value > 0)

/** 覆盖率：已授格数 / 总格数 */
const 覆盖率 = computed(() =>
  总格数.value ? Math.round((已授权数.value / 总格数.value) * 100) : 0
)

/** 当前显示行里、已授权限数为 0 的资源（"这个功能没人能用"） */
const 空资源数 = computed(
  () => 行.value.filter((r) => !权限.value.some((q) => 有(r.资源编号, q.权限编号))).length
)

const 当前岗位名 = computed(
  () => 岗位.value.find((p) => p.岗位编号 === 当前岗位.value)?.岗位名称 ?? '—'
)

/* ------------------------------------------------------------------ 数据 */

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    矩阵.value = await api.system.权限矩阵()
    if (!当前岗位.value) 当前岗位.value = 矩阵.value?.岗位?.[0]?.岗位编号 ?? null
    载入授权()
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '权限矩阵加载失败，请稍后重试'
    矩阵.value = null
  } finally {
    加载中.value = false
  }
}

/** 切换岗位前先确认未保存修改，避免"改了 A 岗位、点 B 岗位就没了" */
async function 换岗位(编号: number | null) {
  if (忽略下次切换) {
    忽略下次切换 = false
    return
  }
  if (编号 === null || 编号 === 当前岗位.value) return
  原岗位 = 当前岗位.value
  if (有未保存.value) {
    try {
      await ElMessageBox.confirm(
        `岗位「${当前岗位名.value}」还有 ${脏数.value} 项修改未保存，切换后将丢失。`,
        '放弃未保存的修改？',
        { type: 'warning', confirmButtonText: '放弃并切换', cancelButtonText: '继续编辑' }
      )
    } catch {
      // 用户选择继续编辑：选择框已经显示成新岗位了，把它拨回原岗位。
      // 回拨触发的那次 change 是自己的、不是用户操作，置位让处理函数直接忽略。
      忽略下次切换 = true
      当前岗位.value = 原岗位
      return
    }
  }
  当前岗位.value = 编号
  载入授权()
}

async function 保存() {
  if (!当前岗位.value) return
  // 载荷形状不变：每岗位 [{ 资源编号, 权限编号 }]
  const 授权 = Array.from(选中.value).map((k) => {
    const [资源编号, 权限编号] = k.split('-').map(Number)
    return { 资源编号, 权限编号 }
  })
  保存中.value = true
  错误.value = ''
  try {
    await api.system.保存权限(当前岗位.value, 授权)
    ElMessage.success(`「${当前岗位名.value}」权限已保存（${授权.length} 项）`)
    已存快照.value = new Set(选中.value)
    await 加载()
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '权限保存失败，请稍后重试'
  } finally {
    保存中.value = false
  }
}

/** 撤销未保存修改：回到服务端快照 */
function 撤销() {
  选中.value = new Set(已存快照.value)
  ElMessage.info('已撤销未保存的修改')
}

function 重置筛选() {
  模块.value = ''
  只看已授.value = false
}

// 浏览器里点了链接/关标签时兜底提醒（只在真改了未存时触发）
watch(有未保存, (脏) => {
  window.onbeforeunload = 脏 ? () => '有未保存的权限修改' : null
})

// 离开本页必须摘掉，否则别的页面/再次进入都会带着这个拦截
onUnmounted(() => {
  window.onbeforeunload = null
})

onMounted(加载)
</script>

<template>
  <div>
    <PageHead
      title="权限矩阵"
      icon="Lock"
      sub="按岗位逐项授权（资源 × 权限）· 保存为全量覆盖 · 超级管理员岗位默认拥有全部权限"
    >
      <template #actions>
        <el-button v-if="有未保存" @click="撤销">
          <el-icon><RefreshLeft /></el-icon> 撤销修改
        </el-button>
        <el-button
          type="primary"
          :loading="保存中"
          :disabled="!当前岗位 || !有未保存"
          @click="保存"
        >
          <el-icon><Check /></el-icon>
          保存{{ 有未保存 ? `（${脏数} 项）` : '该岗位权限' }}
        </el-button>
      </template>
    </PageHead>

    <div class="stat-grid">
      <StatCard
        label="当前岗位"
        icon="User"
        :value="当前岗位名"
        :sub="`已授 <b>${已授权数}</b> 项 / 共 ${总格数} 格 · 覆盖率 <b>${覆盖率}%</b>`"
      />
      <StatCard
        label="未保存修改"
        icon="EditPen"
        :tone="有未保存 ? 'warning' : 'success'"
        :value="脏数.toLocaleString('zh-CN')"
        unit="项"
        :sub="有未保存 ? '切岗位或刷新前会先确认' : '与数据库一致'"
      />
      <StatCard
        label="资源 / 权限项"
        icon="Grid"
        tone="info"
        :value="`${资源.length} × ${权限.length}`"
        :sub="`矩阵共 <b>${总格数}</b> 格 · <b>${模块列表.length}</b> 个模块`"
      />
      <StatCard
        label="当前筛选下无授权资源"
        icon="Warning"
        :tone="空资源数 ? 'warning' : 'success'"
        :value="空资源数.toLocaleString('zh-CN')"
        unit="个"
        :sub="空资源数 ? '这些功能该岗位一个都进不去，请确认是否有意为之' : '每个资源至少授了一项'"
      />
    </div>

    <el-card shadow="never">
      <button class="mobile-filter-toggle el-button el-button--default" @click="筛选展开 = !筛选展开">
        <el-icon><Filter /></el-icon> 筛选
        <span v-if="模块 || 只看已授" class="badge">!</span>
      </button>

      <div class="filter-bar filter-collapsible" :class="{ 'is-open': 筛选展开 }">
        <span class="filter-bar__label">岗位</span>
        <el-select
          :model-value="当前岗位"
          placeholder="选择岗位"
          style="width: 180px"
          @change="换岗位"
        >
          <el-option v-for="p in 岗位" :key="p.岗位编号" :label="p.岗位名称" :value="p.岗位编号" />
        </el-select>

        <span class="filter-bar__label">模块</span>
        <el-select v-model="模块" clearable placeholder="全部模块" style="width: 150px">
          <el-option v-for="m in 模块列表" :key="m" :label="m" :value="m" />
        </el-select>

        <el-checkbox v-model="只看已授" border>只看已授</el-checkbox>

        <el-button @click="重置筛选">
          <el-icon><RefreshLeft /></el-icon> 重置
        </el-button>

        <span class="spacer" />
        <el-tag v-if="有未保存" type="warning" effect="light" size="small">
          <el-icon><EditPen /></el-icon> 未保存 {{ 脏数 }} 项
        </el-tag>
        <el-tag v-else type="success" effect="light" size="small">
          <el-icon><CircleCheck /></el-icon> 已与数据库一致
        </el-tag>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!行.length"
        :empty-title="模块 || 只看已授 ? '当前筛选下没有资源' : '还没有可授权的资源'"
        :empty-desc="
          模块 || 只看已授
            ? '「只看已授」在没有授权时会过滤掉全部行；换个模块或清空筛选再看看。'
            : '权限资源由数据库初始化脚本写入（资源 = 菜单/单据/按钮），空表说明脚本没跑完整。'
        "
        empty-icon="Lock"
        :skeleton-rows="12"
        @retry="加载"
      >
        <template #emptyAction>
          <el-button @click="重置筛选">
            <el-icon><RefreshLeft /></el-icon> 清除筛选
          </el-button>
        </template>

        <el-alert
          v-if="有未保存"
          type="warning"
          :closable="false"
          show-icon
          class="mb-3"
          title="有未保存的修改"
        >
          <template #default>
            「{{ 当前岗位名 }}」有 {{ 脏数 }} 项改动尚未写入数据库；
            点右上角「保存」才会生效，切换岗位会提示是否放弃。
          </template>
        </el-alert>

        <div class="table-wrap table-scroll-hint perm-wrap">
          <el-table
            :data="行"
            size="small"
            :row-class-name="({ row }) =>
              权限.some((q) => 有(row.资源编号, q.权限编号)) ? '' : 'row-none'"
            max-height="calc(100vh - 430px)"
          >
            <!-- 首列粘性：横向滚动时仍知道这行是哪个功能 -->
            <el-table-column
              prop="资源名称"
              label="功能资源"
              min-width="200"
              fixed="left"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <div class="perm-name">
                  <span class="perm-name__text">{{ row.资源名称 }}</span>
                  <el-tag v-if="row.资源类型" size="small" effect="plain" type="info">
                    {{ row.资源类型 }}
                  </el-tag>
                </div>
              </template>
            </el-table-column>

            <el-table-column prop="模块" label="模块" width="110">
              <template #default="{ row }">{{ row.模块 || '—' }}</template>
            </el-table-column>

            <el-table-column prop="路由" label="路由" min-width="170" show-overflow-tooltip>
              <template #default="{ row }">
                <span class="num">{{ row.路由 || '—' }}</span>
              </template>
            </el-table-column>

            <el-table-column label="全选" width="70" align="center" fixed="left">
              <template #default="{ row }">
                <el-checkbox
                  :model-value="权限.length > 0 && 权限.every((q) => 有(row.资源编号, q.权限编号))"
                  :indeterminate="
                    权限.some((q) => 有(row.资源编号, q.权限编号)) &&
                    !权限.every((q) => 有(row.资源编号, q.权限编号))
                  "
                  :aria-label="`${row.资源名称} 全选`"
                  @change="(v: any) => 切换整行(row.资源编号, !!v)"
                />
              </template>
            </el-table-column>

            <el-table-column
              v-for="q in 权限"
              :key="q.权限编号"
              :label="q.权限名称"
              width="96"
              align="center"
            >
              <template #header>
                <div class="perm-col-head">
                  <span>{{ q.权限名称 }}</span>
                  <!-- 列头全选：整列勾/取消，只作用于当前显示行 -->
                  <el-tooltip :content="`整列勾选/取消「${q.权限名称}」（只作用于当前显示的行）`" placement="top">
                    <el-checkbox
                      :model-value="列全选态(q.权限编号).all"
                      :indeterminate="列全选态(q.权限编号).some && !列全选态(q.权限编号).all"
                      size="small"
                      :aria-label="`整列 ${q.权限名称}`"
                      @change="(v: any) => 切换整列(q.权限编号, !!v)"
                    />
                  </el-tooltip>
                </div>
              </template>
              <template #default="{ row }">
                <!-- 已授/未授：勾选框 + 状态点，颜色之外还有形状可辨 -->
                <span class="perm-cell" :class="有(row.资源编号, q.权限编号) ? 'perm-cell--on' : 'perm-cell--off'">
                  <el-checkbox
                    :model-value="有(row.资源编号, q.权限编号)"
                    :aria-label="`${row.资源名称} ${q.权限名称}`"
                    @change="(v: any) => 切换(row.资源编号, q.权限编号, !!v)"
                  />
                </span>
              </template>
            </el-table-column>
          </el-table>
        </div>

        <div class="sum-bar">
          <span>显示 {{ 行.length }} / {{ 资源.length }} 个资源</span>
          <span>已授 <b>{{ 已授权数 }}</b> 项</span>
          <span :class="{ 'sum-bar__danger': 有未保存 }">未保存 <b>{{ 脏数 }}</b> 项</span>
          <span>覆盖率 <b>{{ 覆盖率 }}%</b></span>
          <span class="legend">
            <span class="legend__item"><span class="legend__swatch legend__swatch--on" />已授</span>
            <span class="legend__item"><span class="legend__swatch" />未授</span>
            <span class="legend__item"><span class="legend__swatch legend__swatch--none" />整行无授权</span>
          </span>
        </div>

        <div class="caliber-note">
          <b>口径</b>：一个格子 = 某岗位对某资源的一项权限；保存是<b>全量覆盖</b>（提交该岗位当前的全部
          <code>{资源编号, 权限编号}</code>，数据库以本次提交为准），载荷形状与后端契约一致。
          列头勾选框是<b>整列快捷操作</b>，只作用于当前筛选出来的行；被筛掉的行不受影响。
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

/* 粘性首列需要明确的层级与底色，否则横滚时会"透"出后面的单元格 */
.perm-wrap :deep(.el-table__fixed),
.perm-wrap :deep(.el-table-fixed-column--left) {
  background: var(--bg-surface);
}

.perm-wrap :deep(.el-table__body tr:hover > td.el-table-fixed-column--left) {
  background: var(--bg-hover);
}

.perm-name {
  display: flex;
  align-items: center;
  gap: var(--sp-2);
  min-width: 0;
}

.perm-name__text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* 列头：权限名 + 整列勾选上下排列，避免把列撑宽 */
.perm-col-head {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
  line-height: 1.2;
}

.perm-col-head .el-checkbox {
  height: auto;
}

/* 已授：淡品牌底；未授：透明。配合勾选框，色弱也能分辨 */
.perm-cell {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 2px var(--sp-2);
  border-radius: var(--r-sm);
  transition: background var(--dur-fast) var(--ease-out);
}

.perm-cell--on {
  background: var(--c-brand-50);
}

.perm-cell--off {
  background: transparent;
}

/* 整行无授权：淡红底，提示"这个功能没人能用" */
:deep(.row-none) {
  background: var(--c-danger-50) !important;
}

:deep(.row-none:hover > td.el-table__cell) {
  background: color-mix(in srgb, var(--c-danger-500) 10%, transparent) !important;
}

/* 图例色块的本页扩展 */
.legend__swatch--on {
  background: var(--c-brand-200);
}

.legend__swatch--none {
  background: var(--c-danger-50);
  border: 1px solid var(--c-danger-500);
}
</style>
