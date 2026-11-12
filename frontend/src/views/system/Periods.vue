<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import api, { type 期间行 } from '@/api'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'

/**
 * 期间管理（批次 16）—— 月结 / 反月结 / 结转损益 / 成本结转 的唯一入口。
 *
 * **只放合法按钮**：四个按钮的显示条件全部来自后端算好的 可月结 / 可反月结 / 可结转，
 * 后端又是照 `usp_财务_月结` / `usp_财务_反月结` / `usp_财务_结转损益` / `usp_财务_成本结转`
 * 四个过程里的校验写的。这样"按钮能点"与"过程会放行"是同一套口径，
 * 不会出现批次 15 那种"按钮点一次必 409"的死按钮。
 *
 * 不在这里做任何"跳过前置校验"的快捷方式：月结前该平的账必须平，
 * 该结的损益必须结 —— 那是会计规则，不是可以商量的性能问题。
 */

const 行 = ref<期间行[]>([])
const 加载中 = ref(false)
const 错误 = ref('')
const 忙碌 = ref<number | 0>(0)
const 只看未结账 = ref(false)

const 已结账数 = computed(() => 行.value.filter((r) => r.期间状态 === 1).length)
const 未结账数 = computed(() => 行.value.filter((r) => r.期间状态 === 0).length)
const 可月结数 = computed(() => 行.value.filter((r) => r.可月结 === 1).length)
const 可反月结期 = computed(() => 行.value.find((r) => r.可反月结 === 1) ?? null)

const 显示行 = computed(() => (只看未结账.value ? 行.value.filter((r) => r.期间状态 === 0) : 行.value))

function 金额(v: any) {
  const n = Number(v)
  if (!Number.isFinite(n)) return '—'
  return n.toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}
function 日期(v: any) {
  return v ? String(v).slice(0, 10) : '—'
}

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    const r = await api.finance.期间()
    行.value = r?.items ?? []
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '期间列表加载失败，请稍后重试'
    行.value = []
  } finally {
    加载中.value = false
  }
}

/** 四个动作共用一套"确认 → 调接口 → 重载"，差别只在文案与接口 */
async function 执行(
  row: 期间行,
  kind: '月结' | '反月结' | '结转损益' | '成本结转',
  说明: string
) {
  await ElMessageBox.confirm(说明, `${kind}：${row.期间名称}`, {
    type: kind === '反月结' ? 'warning' : 'info',
    confirmButtonText: kind,
    cancelButtonText: '取消'
  })

  忙碌.value = row.期间编号
  try {
    if (kind === '月结') {
      const r: any = await api.finance.月结(row.期间编号)
      ElMessage.success(
        r?.结转损益凭证字号
          ? `月结完成，已生成结转损益凭证 ${r.结转损益凭证字号}`
          : '月结完成（本期无损益余额，未生成结转凭证）'
      )
    } else if (kind === '反月结') {
      await api.finance.反月结(row.期间编号)
      ElMessage.success(`${row.期间名称} 已反结账，可以继续在本期补录/红冲凭证`)
    } else if (kind === '结转损益') {
      const r: any = await api.finance.结转损益(row.期间编号)
      ElMessage.success(r?.说明 ?? '结转损益完成')
    } else {
      const r: any = await api.finance.成本结转(row.期间编号)
      ElMessage.success(`成本结转完成：本期 ${r?.结转行数 ?? 0} 行`)
    }
    await 加载()
  } catch {
    // 失败原因（已结账/之前有期间未反结/借贷不平…）由拦截器就地弹出，无需重复提示
  } finally {
    忙碌.value = 0
  }
}

const 月结说明 = (r: 期间行) =>
  `月结会依次执行：\n` +
  `① 重算本期移动加权成本（${r.已记账张数} 张已记账凭证的归属期间）\n` +
  `② 把损益科目本期余额结转到本年利润\n` +
  `③ 试算平衡并把期间统计写回\n` +
  `④ 锁定期间 —— 此后本期不能再录凭证、红冲凭证\n\n` +
  (r.草稿张数 > 0 ? `注意：本期还有 ${r.草稿张数} 张草稿未过账，锁定后就再也过不了账了。\n\n` : '') +
  `确认对「${r.期间名称}」月结？`

const 反月结说明 = (r: 期间行) =>
  `反月结会把「${r.期间名称}」退回未结账（期间状态 0），并解除本期凭证的锁定。\n` +
  `已经生成的结转损益凭证**不会删除**（留痕），再次月结时会按当时余额重新判断。\n\n` +
  `只允许对**最后一个已结账期间**反结账。确认反结账？`

onMounted(加载)
</script>

<template>
  <div>
    <PageHead
      title="期间管理"
      icon="Calendar"
      sub="会计期间与月结 · 只显示当前状态允许的动作 · 月结会锁定本期凭证"
    >
      <template #actions>
        <el-button :loading="加载中" @click="加载">
          <el-icon><Refresh /></el-icon> 刷新
        </el-button>
      </template>
    </PageHead>

    <div class="stat-grid">
      <StatCard
        label="会计期间"
        icon="Calendar"
        tone="brand"
        :value="行.length"
        unit="期"
        sub="按年月自动补建，不需要手工维护"
      />
      <StatCard label="已结账" icon="Lock" tone="success" :value="已结账数" unit="期" sub="期间内凭证已锁定" />
      <StatCard label="未结账" icon="Unlock" :tone="未结账数 ? 'warning' : 'success'" :value="未结账数" unit="期" sub="可以继续录凭证" />
      <StatCard
        label="可反结账"
        icon="RefreshLeft"
        tone="info"
        :value="可反月结期 ? 可反月结期.期间名称 : '—'"
        :sub="可反月结期 ? '只能反结最后一个已结账期间' : '没有可反结的期间'"
      />
    </div>

    <el-card shadow="never">
      <div class="filter-bar">
        <el-checkbox v-model="只看未结账" border>只看未结账（{{ 未结账数 }}）</el-checkbox>
        <span class="spacer" />
        <span class="hint">
          当前有 <b>{{ 可月结数 }}</b> 个期间满足月结条件（未结账 + 本期已记账凭证借贷平衡）
        </span>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!显示行.length"
        empty-title="没有会计期间"
        empty-desc="会计期间由 usp_财务_确保期间 在录凭证时自动补建；空表说明还没有任何凭证。"
        empty-icon="Calendar"
        :skeleton-rows="8"
        @retry="加载"
      >
        <div class="table-wrap table-scroll-hint">
          <el-table :data="显示行" size="small" stripe border max-height="calc(100vh - 400px)">
            <el-table-column prop="期间名称" label="期间" width="130" fixed>
              <template #default="{ row }">
                <span class="num">{{ row.期间名称 }}</span>
              </template>
            </el-table-column>

            <el-table-column label="起止" width="190">
              <template #default="{ row }">{{ 日期(row.开始日期) }} ~ {{ 日期(row.结束日期) }}</template>
            </el-table-column>

            <el-table-column prop="期间状态" label="状态" width="96" align="center">
              <template #default="{ row }">
                <el-tag :type="row.期间状态 === 1 ? 'success' : 'warning'" size="small" effect="light">
                  {{ row.期间状态 === 1 ? '已结账' : '未结账' }}
                </el-tag>
              </template>
            </el-table-column>

            <el-table-column label="凭证" width="120" align="center">
              <template #default="{ row }">
                <span class="num">{{ row.已记账张数 }}</span>
                <span class="flat"> 张</span>
                <el-tooltip
                  v-if="row.草稿张数 > 0"
                  content="本期还有草稿未过账：月结锁定期间后，这些草稿将无法再过账"
                  placement="top"
                >
                  <el-tag type="warning" size="small" effect="plain" style="margin-left: 4px">
                    +{{ row.草稿张数 }} 草稿
                  </el-tag>
                </el-tooltip>
              </template>
            </el-table-column>

            <el-table-column label="借方合计" width="140" align="right">
              <template #default="{ row }"><span class="num">{{ 金额(row.已记账借方) }}</span></template>
            </el-table-column>

            <el-table-column label="贷方合计" width="140" align="right">
              <template #default="{ row }"><span class="num">{{ 金额(row.已记账贷方) }}</span></template>
            </el-table-column>

            <el-table-column label="借贷差" width="110" align="right">
              <template #default="{ row }">
                <span class="num" :class="Math.abs(Number(row.借贷差)) > 0.01 ? 'err' : 'flat'">
                  {{ 金额(row.借贷差) }}
                </span>
              </template>
            </el-table-column>

            <el-table-column label="结转" width="150" align="center">
              <template #default="{ row }">
                <el-tag v-if="row.是否结转损益" type="info" size="small" effect="plain">损益已结转</el-tag>
                <span v-else class="flat">未结转</span>
                <el-tooltip v-if="row.成本结转行数" :content="`成本结转账 ${row.成本结转行数} 行，已结账 ${row.成本结转已结账行数} 行`" placement="top">
                  <el-tag size="small" effect="plain" style="margin-left: 4px">成本 {{ row.成本结转行数 }}</el-tag>
                </el-tooltip>
              </template>
            </el-table-column>

            <el-table-column label="结账人 / 时间" width="180" show-overflow-tooltip>
              <template #default="{ row }">
                <span v-if="row.结账人">{{ row.结账人 }} · {{ 日期(row.结账时间) }}</span>
                <span v-else class="flat">—</span>
              </template>
            </el-table-column>

            <el-table-column label="提示" min-width="200" show-overflow-tooltip>
              <template #default="{ row }">
                <span v-if="row.月结受阻原因" class="err">{{ row.月结受阻原因 }}</span>
                <span v-else-if="row.期间状态 === 0 && row.后续已结账数 > 0" class="warn">
                  后续 {{ row.后续已结账数 }} 个期间已结账，倒序结账请谨慎
                </span>
                <span v-else class="flat">—</span>
              </template>
            </el-table-column>

            <el-table-column label="操作" width="280" fixed="right" align="center">
              <template #default="{ row }">
                <template v-if="row.期间状态 === 0">
                  <el-button
                    v-if="row.可结转 === 1"
                    link
                    type="primary"
                    :loading="忙碌 === row.期间编号"
                    @click="执行(row, '成本结转', `按期间重算移动加权成本台账（幂等覆盖，重跑结果一致）。\n\n确认对「${row.期间名称}」结转成本？`)"
                  >
                    成本结转
                  </el-button>
                  <el-button
                    v-if="row.可结转 === 1"
                    link
                    type="primary"
                    :loading="忙碌 === row.期间编号"
                    @click="执行(row, '结转损益', `把本期损益类科目的余额结转到本年利润（本期损益为 0 时不会生成凭证）。\n\n确认对「${row.期间名称}」结转损益？`)"
                  >
                    结转损益
                  </el-button>
                  <el-button
                    v-if="row.可月结 === 1"
                    link
                    type="success"
                    :loading="忙碌 === row.期间编号"
                    @click="执行(row, '月结', 月结说明(row))"
                  >
                    月结
                  </el-button>
                  <el-tooltip v-else :content="row.月结受阻原因 || '不满足月结条件'" placement="left">
                    <span class="flat">月结不可用</span>
                  </el-tooltip>
                </template>

                <template v-else>
                  <el-button
                    v-if="row.可反月结 === 1"
                    link
                    type="warning"
                    :loading="忙碌 === row.期间编号"
                    @click="执行(row, '反月结', 反月结说明(row))"
                  >
                    反月结
                  </el-button>
                  <el-tooltip v-else content="只能对最后一个已结账期间反结账（后续期间已结账，请先反结后面的）" placement="left">
                    <span class="flat">不可反结账</span>
                  </el-tooltip>
                </template>
              </template>
            </el-table-column>
          </el-table>
        </div>

        <div class="caliber-note">
          <b>口径</b>：状态与四个按钮的合法性都来自库里的过程 ——
          <code>usp_财务_月结</code>（含成本结转 → 结转损益 → 试算平衡 → 锁账）、
          <code>usp_财务_反月结</code>（只许反结最后一个已结账期间）、
          <code>usp_财务_结转损益</code>（本期损益为 0 则不生成凭证）、
          <code>usp_财务_成本结转</code>（幂等覆盖）。
          页面<b>不做任何绕过校验的快捷操作</b>：借贷不平、期间已结账这类前置条件一律由过程拦。
          历史凭证要红冲，必须先在这里<b>反月结</b>打开期间。
        </div>
      </DataState>
    </el-card>
  </div>
</template>

<style scoped>
.err {
  color: var(--c-danger-500, #dc2626);
}
.warn {
  color: var(--c-warning-600, #d97706);
}
@media (min-width: 768px) {
  .filter-bar {
    display: flex;
  }
}
</style>
