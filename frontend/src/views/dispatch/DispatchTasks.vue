<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import api from '@/api'
import { 配送阶段, 配送阶段名称, 配送阶段标签 } from '@/config/modules'
import { useLookupStore } from '@/stores/lookup'
import { useAuthStore } from '@/stores/auth'
import PageHead from '@/components/PageHead.vue'
import DataState from '@/components/DataState.vue'

const router = useRouter()
const lookup = useLookupStore()
const auth = useAuthStore()
const 资源 = 'Dist.Task'

const 行 = ref<any[]>([])
const 总数 = ref(0)
const 加载中 = ref(false)
const 错误 = ref('')
const 筛选展开 = ref(false)
const 操作中 = ref<string | null>(null)
const 查询 = reactive({ page: 1, size: 20 })
const 关键词 = ref('')
const 阶段 = ref<number | null>(null)
const 日期 = ref<string[]>([])

const 可建计划 = computed(() => auth.有权限(资源, 'add'))
const 可调度 = computed(() => auth.有权限(资源, 'edit'))
const 有筛选 = computed(() => !!关键词.value.trim() || 阶段.value !== null || (日期.value?.length ?? 0) > 0)

function 今天(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function 数字(v: any): string {
  const n = Number(v ?? 0)
  return Number.isFinite(n) ? n.toLocaleString('zh-CN') : '0'
}

function 时间(v: any): string {
  return v ? String(v).replace('T', ' ').slice(0, 16) : '—'
}

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    const params: any = { page: 查询.page, size: 查询.size }
    if (关键词.value.trim()) params.keyword = 关键词.value.trim()
    if (阶段.value !== null) params.阶段 = 阶段.value
    if (日期.value?.length === 2) {
      params.from = 日期.value[0]
      params.to = 日期.value[1]
    }
    const r: any = await api.dispatch.列表(params)
    行.value = r?.items ?? []
    总数.value = r?.total ?? 0
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '配送任务加载失败，请稍后重试'
    行.value = []
    总数.value = 0
  } finally {
    加载中.value = false
  }
}

function 重置() {
  关键词.value = ''
  阶段.value = null
  日期.value = []
  查询.page = 1
  加载()
}

function 详情(row: any) {
  if (row.任务编号) router.push(`/dispatch/tasks/${row.任务编号}`)
  else router.push(`/dispatch/plans/${row.计划编号}`)
}

function 显示动作(row: any, 动作: 'convert' | 'load' | 'out' | 'return'): boolean {
  if (!可调度.value) return false
  const 当前 = Number(row.阶段)
  return (
    (动作 === 'convert' && 当前 === 0) ||
    (动作 === 'load' && 当前 === 1) ||
    (动作 === 'out' && 当前 === 2) ||
    (动作 === 'return' && 当前 === 3)
  )
}

async function 执行(row: any, 动作: 'convert' | 'load' | 'out' | 'return') {
  const 说明: Record<string, string> = {
    convert: `将把网点「${row.网点名称}」当前全部待派订单转入本趟任务（库过程口径：一次吸走该网点全部候选订单）。`,
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

  const key = `${动作}-${row.任务编号 ?? row.计划编号}`
  操作中.value = key
  try {
    let r: any
    if (动作 === 'convert') {
      r = await api.dispatch.转任务(Number(row.计划编号))
      ElMessage.success(`已生成任务 ${r?.单据号码 ?? r?.任务编号 ?? ''}`)
      await 加载()
      if (r?.任务编号) router.push(`/dispatch/tasks/${r.任务编号}`)
    } else {
      const 任务编号 = Number(row.任务编号)
      r =
        动作 === 'load'
          ? await api.dispatch.装车(任务编号)
          : 动作 === 'out'
            ? await api.dispatch.出车(任务编号)
            : await api.dispatch.回场(任务编号)
      ElMessage.success(
        动作 === 'return'
          ? `已回场，余瓶 ${r?.车仓余瓶数 ?? 0} / 差异 ${r?.差异瓶数 ?? 0}`
          : `已${名称[动作]}：${r?.单据号码 ?? ''}`
      )
      await 加载()
    }
  } catch (e) {
    // 业务提示已由 http 拦截器弹出；这里只吞掉 Promise，避免未处理异常
    console.debug('配送动作未完成', e)
  } finally {
    操作中.value = null
  }
}

/* ------------------------------------------------------------ 新建计划 */

const 计划可见 = ref(false)
const 提交中 = ref(false)
const 路线 = ref<any[]>([])
const 计划表单 = reactive({
  网点编号: null as number | null,
  车辆编号: null as number | null,
  出车仓库编号: null as number | null,
  计划日期: 今天(),
  路线编号: null as number | null,
  司机编号: null as number | null,
  跟车编号: null as number | null,
  计划出车时间: '' as string,
  备注: '' as string
})

const 网点选项 = computed(() =>
  lookup.列表('经营网点').map((r) => ({ label: r.网点名称, value: r.网点编号 }))
)
const 车辆选项 = computed(() =>
  lookup
    .列表('车辆')
    .filter(
      (r) =>
        r.是否有效 &&
        (!计划表单.网点编号 || Number(r.所属网点编号) === Number(计划表单.网点编号))
    )
    .map((r) => ({ label: `${r.车牌}${r.别名 ? `（${r.别名}）` : ''}`, value: r.车辆编号 }))
)
const 仓库选项 = computed(() =>
  lookup
    .列表('仓库')
    .filter(
      (r) =>
        r.是否有效 &&
        !r.是否车仓 &&
        (!计划表单.网点编号 || Number(r.所属网点编号) === Number(计划表单.网点编号))
    )
    .sort((a, b) => (a.仓库类型 === '总仓' ? -1 : 0) - (b.仓库类型 === '总仓' ? -1 : 0))
    .map((r) => ({ label: `${r.仓库名称}（${r.仓库类型}）`, value: r.仓库编号 }))
)
const 员工选项 = computed(() =>
  lookup
    .列表('员工')
    .filter((r) => r.是否在职)
    .map((r) => ({ label: `${r.工号} ${r.姓名}`, value: r.员工编号 }))
)
const 路线选项 = computed(() =>
  路线.value
    .filter((r) => !计划表单.网点编号 || Number(r.所属网点编号) === Number(计划表单.网点编号))
    .map((r) => ({ label: `${r.路线编码} ${r.路线名称}`, value: r.路线编号 }))
)

function 选车辆(车辆编号: number | null) {
  const v = lookup.列表('车辆').find((x) => Number(x.车辆编号) === Number(车辆编号))
  计划表单.司机编号 = v?.默认司机员工编号 ?? null
  计划表单.跟车编号 = v?.默认跟车员工编号 ?? null
}

function 顶层实体仓(): number | null {
  const 仓 = 仓库选项.value
  return 仓.length ? Number(仓[0].value) : null
}

watch(
  () => 计划表单.网点编号,
  () => {
    计划表单.车辆编号 = null
    计划表单.路线编号 = null
    计划表单.司机编号 = null
    计划表单.跟车编号 = null
    计划表单.出车仓库编号 = 顶层实体仓()
    const 第一辆 = 车辆选项.value[0]
    if (第一辆) {
      计划表单.车辆编号 = Number(第一辆.value)
      选车辆(计划表单.车辆编号)
    }
  }
)

async function 打开新建() {
  await lookup.加载()
  try {
    const r = await api.dispatch.选项()
    路线.value = r?.路线 ?? []
  } catch {
    路线.value = []
  }
  计划表单.计划日期 = 今天()
  计划表单.计划出车时间 = ''
  计划表单.备注 = ''
  计划表单.网点编号 = 网点选项.value[0]?.value ?? null
  计划表单.车辆编号 = null
  计划表单.路线编号 = null
  计划表单.司机编号 = null
  计划表单.跟车编号 = null
  计划表单.出车仓库编号 = 顶层实体仓()
  const 第一辆 = 车辆选项.value[0]
  if (第一辆) {
    计划表单.车辆编号 = Number(第一辆.value)
    选车辆(计划表单.车辆编号)
  }
  计划可见.value = true
}

async function 提交计划() {
  if (!计划表单.网点编号 || !计划表单.车辆编号 || !计划表单.出车仓库编号) {
    ElMessage.warning('请选择网点、车辆和出车仓库')
    return
  }
  提交中.value = true
  try {
    const r: any = await api.dispatch.生成计划({
      ...计划表单,
      计划出车时间: 计划表单.计划出车时间 || null
    })
    ElMessage.success(`计划 ${r?.单据号码 ?? ''} 已创建：预估 ${数字(r?.预估客户数)} 户 / ${数字(r?.预估瓶数)} 瓶`)
    计划可见.value = false
    查询.page = 1
    await 加载()
    if (!Number(r?.预估客户数)) {
      ElMessage.warning('当前网点没有可配送的已审核订单；转任务会提示无单可派')
    }
  } catch {
    // 业务提示已由 http 拦截器弹出
  } finally {
    提交中.value = false
  }
}

onMounted(async () => {
  await lookup.加载()
  await 加载()
})
</script>

<template>
  <div>
    <PageHead
      title="配送任务"
      icon="Van"
      :sub="`共 ${总数.toLocaleString('zh-CN')} 趟 · 状态机：计划 → 已转任务 → 已装车 → 在途 → 已回场`"
    >
      <template #actions>
        <el-button :disabled="!行.length" @click="加载">
          <el-icon><Refresh /></el-icon> 刷新
        </el-button>
        <el-button v-if="可建计划" type="success" @click="打开新建">
          <el-icon><Plus /></el-icon> 新建配送计划
        </el-button>
      </template>
    </PageHead>

    <el-card shadow="never">
      <button class="mobile-filter-toggle el-button el-button--default" @click="筛选展开 = !筛选展开">
        <el-icon><Filter /></el-icon> 筛选
        <span v-if="有筛选" class="badge">!</span>
      </button>

      <div class="filter-bar filter-collapsible" :class="{ 'is-open': 筛选展开 }">
        <el-input
          v-model="关键词"
          placeholder="计划号 / 任务号 / 车牌 / 司机 / 路线"
          clearable
          style="width: 240px"
          @keyup.enter="查询.page = 1; 加载()"
        >
          <template #prefix><el-icon><Search /></el-icon></template>
        </el-input>

        <el-select v-model="阶段" clearable placeholder="全部阶段" style="width: 130px" @change="查询.page = 1; 加载()">
          <el-option v-for="s in 配送阶段" :key="s.value" :label="s.label" :value="s.value" />
        </el-select>

        <el-date-picker
          v-model="日期"
          type="daterange"
          value-format="YYYY-MM-DD"
          start-placeholder="计划开始"
          end-placeholder="计划结束"
          style="width: 250px"
        />

        <el-button type="primary" @click="查询.page = 1; 加载()">
          <el-icon><Search /></el-icon> 查询
        </el-button>
        <el-button @click="重置">
          <el-icon><RefreshLeft /></el-icon> 重置
        </el-button>
        <span class="spacer" />
        <span class="hint">共 {{ 总数.toLocaleString('zh-CN') }} 趟</span>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!行.length"
        :empty-title="有筛选 ? '没有符合条件的配送任务' : '还没有配送计划'"
        :empty-desc="
          有筛选
            ? '试试放宽日期范围、换一个阶段，或清空关键词。'
            : '先新建配送计划，再依次转任务、装车、出车、回场。'
        "
        empty-icon="Van"
        :skeleton-rows="8"
        @retry="加载"
      >
        <template #emptyAction>
          <el-button v-if="有筛选" @click="重置">
            <el-icon><RefreshLeft /></el-icon> 清除筛选
          </el-button>
          <el-button v-else-if="可建计划" type="primary" @click="打开新建">
            <el-icon><Plus /></el-icon> 新建配送计划
          </el-button>
        </template>

        <div class="table-wrap table-scroll-hint">
          <el-table :data="行" size="small" stripe>
            <el-table-column prop="计划日期" label="计划日期" width="110">
              <template #default="{ row }">{{ String(row.计划日期 ?? '').slice(0, 10) }}</template>
            </el-table-column>
            <el-table-column label="计划单号" width="165">
              <template #default="{ row }">
                <el-button link type="primary" class="doc-no" @click="详情(row)">
                  {{ row.计划单据号码 }}
                </el-button>
              </template>
            </el-table-column>
            <el-table-column label="任务单号" width="165">
              <template #default="{ row }">
                <el-button v-if="row.任务单据号码" link type="primary" class="doc-no" @click="详情(row)">
                  {{ row.任务单据号码 }}
                </el-button>
                <span v-else class="none">未转任务</span>
              </template>
            </el-table-column>
            <el-table-column prop="网点名称" label="网点" min-width="130" show-overflow-tooltip />
            <el-table-column prop="车牌" label="车辆" width="110" />
            <el-table-column prop="司机姓名" label="司机" width="90" />
            <el-table-column prop="路线名称" label="路线" min-width="120" show-overflow-tooltip />
            <el-table-column width="125">
              <template #header>
                <span>客户 / 瓶数</span>
                <el-tooltip
                  content="计划阶段是库过程预填的候选（含历史已开送气单的未派订单）；转任务只吸收已审核且未开送气单的订单，数字可能变小"
                  placement="top"
                >
                  <el-icon class="ml-1"><InfoFilled /></el-icon>
                </el-tooltip>
              </template>
              <template #default="{ row }">
                {{ 数字(row.任务编号 ? row.计划客户数 : row.预估客户数) }} 户 /
                {{ 数字(row.任务编号 ? row.计划瓶数 : row.预估瓶数) }} 瓶
              </template>
            </el-table-column>
            <el-table-column label="实际送达" width="120">
              <template #default="{ row }">
                <span v-if="row.任务编号">{{ 数字(row.实际客户数) }} 户 / {{ 数字(row.实际瓶数) }} 瓶</span>
                <span v-else class="none">—</span>
              </template>
            </el-table-column>
            <el-table-column label="阶段" width="100">
              <template #default="{ row }">
                <el-tag :type="配送阶段标签(row.阶段)" size="small" effect="light">
                  {{ 配送阶段名称(row.阶段) }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="出车 / 回场" width="160">
              <template #default="{ row }">
                <div class="time-stack">
                  <span>出 {{ 时间(row.出车时间) }}</span>
                  <span>回 {{ 时间(row.回场时间) }}</span>
                </div>
              </template>
            </el-table-column>

            <el-table-column label="操作" width="190" fixed="right">
              <template #default="{ row }">
                <div v-loading="操作中 === `convert-${row.计划编号}` || 操作中 === `load-${row.任务编号}` || 操作中 === `out-${row.任务编号}` || 操作中 === `return-${row.任务编号}`" class="ops">
                  <el-button link type="primary" @click="详情(row)">详情</el-button>
                  <el-button v-if="显示动作(row, 'convert')" link type="warning" @click="执行(row, 'convert')">转任务</el-button>
                  <el-button v-if="显示动作(row, 'load')" link type="primary" @click="执行(row, 'load')">装车</el-button>
                  <el-button v-if="显示动作(row, 'out')" link type="success" @click="执行(row, 'out')">出车</el-button>
                  <el-button v-if="显示动作(row, 'return')" link type="warning" @click="执行(row, 'return')">回场</el-button>
                </div>
              </template>
            </el-table-column>
          </el-table>
        </div>

        <el-pagination
          class="mt-3"
          style="justify-content: flex-end"
          background
          layout="total, sizes, prev, pager, next"
          :total="总数"
          v-model:current-page="查询.page"
          v-model:page-size="查询.size"
          :page-sizes="[20, 50, 100]"
          @current-change="加载"
          @size-change="查询.page = 1; 加载()"
        />
      </DataState>

      <div class="caliber-note">
        <b>口径</b>：列表一行 = 一趟配送（计划 + 可选任务）。装车量由库过程按
        <b>订单未出数量汇总到商品后跨批次 FIFO</b> 分配；回场余货自动调回同一实体仓。
        「计划」阶段的客户/瓶数是库过程预填的候选（包含历史已开送气单的未派订单）；
        转任务只吸收<b>已审核且未开送气单</b>的订单，所以转入后的计划客户/瓶数可能小于预填值。
      </div>
    </el-card>

    <el-dialog v-model="计划可见" title="新建配送计划" width="620px" destroy-on-close>
      <el-form :model="计划表单" label-width="110px">
        <el-form-item label="所属网点" required>
          <el-select v-model="计划表单.网点编号" placeholder="选择网点" style="width: 100%">
            <el-option v-for="o in 网点选项" :key="o.value" :label="o.label" :value="o.value" />
          </el-select>
        </el-form-item>
        <el-form-item label="车辆" required>
          <el-select v-model="计划表单.车辆编号" placeholder="选择车辆" style="width: 100%" @change="选车辆">
            <el-option v-for="o in 车辆选项" :key="o.value" :label="o.label" :value="o.value" />
          </el-select>
        </el-form-item>
        <el-form-item label="出车仓库" required>
          <el-select v-model="计划表单.出车仓库编号" placeholder="装车来源实体仓" style="width: 100%">
            <el-option v-for="o in 仓库选项" :key="o.value" :label="o.label" :value="o.value" />
          </el-select>
        </el-form-item>
        <el-form-item label="计划日期">
          <el-date-picker v-model="计划表单.计划日期" type="date" value-format="YYYY-MM-DD" style="width: 100%" />
        </el-form-item>
        <el-form-item label="路线">
          <el-select v-model="计划表单.路线编号" clearable placeholder="可选：按路线排序送达" style="width: 100%">
            <el-option v-for="o in 路线选项" :key="o.value" :label="o.label" :value="o.value" />
          </el-select>
        </el-form-item>
        <el-form-item label="司机">
          <el-select v-model="计划表单.司机编号" clearable placeholder="默认取车辆绑定司机" style="width: 100%">
            <el-option v-for="o in 员工选项" :key="o.value" :label="o.label" :value="o.value" />
          </el-select>
        </el-form-item>
        <el-form-item label="跟车">
          <el-select v-model="计划表单.跟车编号" clearable placeholder="可选" style="width: 100%">
            <el-option v-for="o in 员工选项" :key="o.value" :label="o.label" :value="o.value" />
          </el-select>
        </el-form-item>
        <el-form-item label="计划出车时间">
          <el-date-picker
            v-model="计划表单.计划出车时间"
            type="datetime"
            value-format="YYYY-MM-DD HH:mm:ss"
            placeholder="可选"
            style="width: 100%"
          />
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="计划表单.备注" type="textarea" :rows="2" placeholder="可选" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="计划可见 = false">取消</el-button>
        <el-button type="primary" :loading="提交中" @click="提交计划">创建计划</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
@media (min-width: 768px) {
  .filter-collapsible {
    display: flex;
  }
}

.doc-no {
  font-family: var(--font-num);
  font-weight: var(--fw-medium);
  padding: 0;
}

.ops {
  display: flex;
  flex-wrap: wrap;
  gap: 0 6px;
  min-height: 24px;
}

.ops .el-button {
  margin-left: 0;
}

.none {
  color: var(--c-ink-300);
}

.time-stack {
  display: flex;
  flex-direction: column;
  line-height: 1.45;
  font-size: 12px;
  color: var(--c-ink-500);
}
</style>
