<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import api, { type 科目行, type 期间行 } from '@/api'
import PageHead from '@/components/PageHead.vue'

/**
 * 凭证录入（批次 16）—— 手工凭证的唯一入口。
 *
 * 三条规矩，都写在界面上而不是藏在代码里：
 *   1. **借贷必须平**：差额实时算，不平就禁用保存按钮（不是保存后弹一句"借贷不平"）；
 *   2. **科目只列"启用 + 明细"**：汇总科目不能直接记账（后端 /finance/subjects 已过滤），
 *      录错方向时人只能在明细科目里选，少了"挂到 1002 还是 1002.01"这种事后才发现的问题；
 *   3. **日期落在已结账期间不许存**：保存前就用期间列表判断并提示，不用等后端 54441。
 *
 * 保存走 usp_财务_手工凭证 + 逐行 usp_财务_加分录（后端一个事务里做完）；
 * 「保存并过账」再多调一次 usp_财务_凭证过账 —— 否则草稿留在未结账期间，
 * 一旦月结就再也过不了账（期间锁是库里的硬规则，不是我们加的）。
 */

const router = useRouter()

interface 编辑行 {
  key: number
  科目编码: string
  借贷方向: '借' | '贷'
  金额: number | undefined
  摘要: string
}

let 行号 = 0
function 新行(方向: '借' | '贷' = '借'): 编辑行 {
  return { key: ++行号, 科目编码: '', 借贷方向: 方向, 金额: undefined, 摘要: '' }
}

const 凭证日期 = ref(new Date().toISOString().slice(0, 10))
const 摘要 = ref('')
const 行 = ref<编辑行[]>([新行('借'), 新行('贷')])
const 科目 = ref<科目行[]>([])
const 期间 = ref<期间行[]>([])
const 保存中 = ref(false)
const 加载中 = ref(false)
const 错误 = ref('')

/** 金额统一按两位小数参与平衡判断：0.1 + 0.2 的浮点误差不该让人看到"差 0.01" */
function 两位(n: number) {
  return Math.round((Number(n) || 0) * 100) / 100
}

const 借合计 = computed(() => 两位(行.value.filter((l) => l.借贷方向 === '借').reduce((a, l) => a + (Number(l.金额) || 0), 0)))
const 贷合计 = computed(() => 两位(行.value.filter((l) => l.借贷方向 === '贷').reduce((a, l) => a + (Number(l.金额) || 0), 0)))
const 差额 = computed(() => 两位(借合计.value - 贷合计.value))
const 平衡 = computed(() => 差额.value === 0 && 借合计.value > 0)

const 有金额 = computed(() => 行.value.some((l) => Number(l.金额) > 0))

/** 提交前逐行体检：只提示，不擅自删行（删行会让人以为金额丢了） */
const 行问题 = computed(() => {
  const 问题: Record<number, string> = {}
  for (const l of 行.value) {
    const 有内容 = l.科目编码 || Number(l.金额) > 0 || l.摘要
    if (!有内容) continue
    if (!l.科目编码) 问题[l.key] = '请选择科目'
    else if (!(Number(l.金额) > 0)) 问题[l.key] = '金额需大于 0'
  }
  return 问题
})

const 可以保存 = computed(() => 平衡.value && Object.keys(行问题.value).length === 0)

/** 日期对应的会计期间（用于"这期已结账"的前置提示） */
const 所属期间 = computed(() => {
  const m = /^(\d{4})-(\d{2})/.exec(凭证日期.value)
  if (!m) return null
  return 期间.value.find((p) => p.年度 === Number(m[1]) && p.月份 === Number(m[2])) ?? null
})
const 期间已结账 = computed(() => 所属期间.value?.期间状态 === 1)

function 加行() {
  行.value.push(新行('借'))
}
function 删行(k: number) {
  if (行.value.length <= 2) {
    ElMessage.warning('一借一贷是最少的两行，不能再删')
    return
  }
  行.value = 行.value.filter((l) => l.key !== k)
}

function 金额文本(v: number) {
  return v.toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    const [科目表, 期间表] = await Promise.all([api.finance.科目(), api.finance.期间()])
    科目.value = 科目表 ?? []
    期间.value = 期间表?.items ?? []
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '科目/期间加载失败，无法录入凭证'
  } finally {
    加载中.value = false
  }
}

async function 保存(并过账: boolean) {
  if (!可以保存.value) {
    ElMessage.warning(平衡.value ? '有分录还没填完整' : `借贷不平，差额 ${金额文本(差额.value)}`)
    return
  }
  if (!凭证日期.value) {
    ElMessage.warning('请选择凭证日期')
    return
  }

  const 分录 = 行.value
    .filter((l) => l.科目编码 && Number(l.金额) > 0)
    .map((l) => ({
      科目编码: l.科目编码,
      借贷方向: l.借贷方向,
      金额: 两位(Number(l.金额)),
      摘要: l.摘要 || 摘要.value || undefined
    }))

  保存中.value = true
  try {
    const r = await api.finance.新建凭证({ 凭证日期: 凭证日期.value, 摘要: 摘要.value || undefined, 分录 })
    if (并过账) {
      try {
        await api.finance.凭证过账(r.凭证编号)
        ElMessage.success(`凭证 ${r.凭证字号} 已保存并过账`)
      } catch {
        // 过账失败时草稿已经落库：说清楚它去哪了，别让人以为白录一遍
        ElMessage.warning(`凭证 ${r.凭证字号} 已保存为草稿，但过账未成功；可在凭证台账再点「过账」`)
      }
    } else {
      ElMessage.success(`凭证 ${r.凭证字号} 已保存为草稿（过账后金额才计入期间）`)
    }
    router.push('/ledger/voucher')
  } catch {
    // 校验类错误（借贷不平/期间已结账/科目停用）由 http 拦截器统一提示，这里只保留现场
  } finally {
    保存中.value = false
  }
}

onMounted(加载)
</script>

<template>
  <div>
    <PageHead
      title="录入凭证"
      icon="EditPen"
      sub="手工凭证 · 借贷必须平衡后才能保存 · 科目只列启用中的明细科目"
    >
      <template #actions>
        <el-button @click="router.push('/ledger/voucher')">
          <el-icon><Back /></el-icon> 返回台账
        </el-button>
      </template>
    </PageHead>

    <div v-if="错误" class="error-panel">
      <el-icon><CircleCloseFilled /></el-icon>
      <div class="error-panel__body">{{ 错误 }}</div>
      <el-button class="error-panel__retry" size="small" @click="加载">重试</el-button>
    </div>

    <el-card v-loading="加载中" shadow="never">
      <div class="filter-bar">
        <span class="filter-bar__label">凭证日期</span>
        <el-date-picker
          v-model="凭证日期"
          type="date"
          value-format="YYYY-MM-DD"
          placeholder="凭证日期"
          style="width: 160px"
        />
        <span class="filter-bar__label">摘要</span>
        <el-input v-model="摘要" placeholder="整张凭证的摘要（分录空着时沿用它）" style="width: 320px" />
        <span class="spacer" />
        <span class="hint">
          所属期间：
          <b v-if="所属期间">{{ 所属期间.期间名称 }}</b>
          <b v-else>（该月还没有会计期间，保存时自动补建）</b>
        </span>
        <el-tag v-if="期间已结账" type="danger" effect="light" size="small">已结账，不能录入</el-tag>
      </div>

      <div class="section-title mt-4 mb-3">
        凭证分录
        <span class="hint">｜一借一贷是最少的两行；金额按两位小数参与平衡判断</span>
      </div>

      <div class="table-wrap table-scroll-hint">
        <el-table :data="行" size="small" border row-key="key">
          <el-table-column label="#" type="index" width="52" align="center" />

          <el-table-column label="科目" min-width="260">
            <template #default="{ row }">
              <el-select
                v-model="row.科目编码"
                filterable
                clearable
                placeholder="输入编码或名称筛选"
                style="width: 100%"
              >
                <el-option
                  v-for="s in 科目"
                  :key="s.科目编码"
                  :label="`${s.科目编码} ${s.科目名称}`"
                  :value="s.科目编码"
                >
                  <span class="num">{{ s.科目编码 }}</span>
                  <span style="margin-left: 8px">{{ s.科目名称 }}</span>
                  <span class="flat" style="float: right">{{ s.科目类别 }}</span>
                </el-option>
              </el-select>
            </template>
          </el-table-column>

          <el-table-column label="方向" width="110" align="center">
            <template #default="{ row }">
              <el-radio-group v-model="row.借贷方向" size="small">
                <el-radio-button value="借">借</el-radio-button>
                <el-radio-button value="贷">贷</el-radio-button>
              </el-radio-group>
            </template>
          </el-table-column>

          <el-table-column label="金额" width="170" align="right">
            <template #default="{ row }">
              <el-input-number
                v-model="row.金额"
                :controls="false"
                :precision="2"
                :min="0"
                :step="100"
                size="small"
                placeholder="0.00"
                style="width: 100%"
              />
            </template>
          </el-table-column>

          <el-table-column label="分录摘要" min-width="220">
            <template #default="{ row }">
              <el-input v-model="row.摘要" size="small" :placeholder="摘要 || '可留空，沿用凭证摘要'" />
            </template>
          </el-table-column>

          <el-table-column label="校验" width="120">
            <template #default="{ row }">
              <span v-if="行问题[row.key]" class="err">{{ 行问题[row.key] }}</span>
              <span v-else class="flat">—</span>
            </template>
          </el-table-column>

          <el-table-column label="" width="70" align="center">
            <template #default="{ row }">
              <el-button link type="danger" @click="删行(row.key)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <div class="mt-3">
        <el-button size="small" @click="加行">
          <el-icon><Plus /></el-icon> 增加一行
        </el-button>
      </div>

      <div class="sum-bar mt-3">
        <span>借方合计 <b class="num">{{ 金额文本(借合计) }}</b></span>
        <span>贷方合计 <b class="num">{{ 金额文本(贷合计) }}</b></span>
        <span>
          差额
          <b class="num" :class="差额 === 0 ? '' : 'sum-bar__danger'">{{ 金额文本(差额) }}</b>
        </span>
        <span class="spacer" />
        <el-tag v-if="平衡" type="success" effect="light">
          <el-icon><Select /></el-icon> 借贷平衡
        </el-tag>
        <el-tag v-else-if="有金额" type="danger" effect="light">
          <el-icon><WarningFilled /></el-icon> 借贷不平，不能保存
        </el-tag>
        <el-tag v-else type="info" effect="plain">还没填金额</el-tag>
      </div>

      <div class="action-bar no-print mt-4">
        <span class="hint">
          保存后是<b>草稿</b>（金额还不计入期间统计，到台账点「过账」才生效）。
          草稿<span class="err">不支持改分录、也不支持删除</span> —— 库里的规则是「已记账只能红冲」；
          所以<b>保存前请核对清楚</b>，真录错了就过账后立刻红冲，再重新录一张。
        </span>
        <span class="spacer" />
        <el-button @click="router.push('/ledger/voucher')">取消</el-button>
        <el-button
          :disabled="!可以保存 || 期间已结账"
          :loading="保存中"
          @click="保存(false)"
        >
          保存草稿
        </el-button>
        <el-button
          type="primary"
          :disabled="!可以保存 || 期间已结账"
          :loading="保存中"
          @click="保存(true)"
        >
          保存并过账
        </el-button>
      </div>

      <div class="caliber-note">
        <b>口径</b>：保存时调用 <code>usp_财务_手工凭证</code> 建表头（<code>业务类型代码 = MANUAL</code>），
        再逐行 <code>usp_财务_加分录</code>（它会再校验一次科目是否停用、金额是否 &gt; 0）；
        两者在后端是<b>同一个事务</b>，任一行被拒则整张凭证不落库。
        过账由 <code>usp_财务_凭证过账</code> 复核借贷平衡（容差 0.01）后写回合计与期间统计。
      </div>
    </el-card>
  </div>
</template>

<style scoped>
@media (min-width: 768px) {
  .filter-bar {
    flex-wrap: wrap;
  }
}
/* 表内的行级校验提示：用危险色，别用 .down —— 那在财务配色里是"跌"（绿/红相反） */
.err {
  color: var(--c-danger-500, #dc2626);
}
</style>
