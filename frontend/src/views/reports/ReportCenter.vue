<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import api from '@/api'
import PageHead from '@/components/PageHead.vue'
import StatCard from '@/components/StatCard.vue'
import DataState from '@/components/DataState.vue'

/**
 * 报表中心目录：GET /api/views
 *
 * v3 把"给人看的口径"全部固化在 120 个 `vw_*` 视图里（凭证分录、科目余额、采购到货、
 * 钢瓶待检、配送送达 …）。后端 `ViewsController` 只做"列目录 + 分页取数"，
 * 所以这里**不写任何字段清单** —— 一张视图有几列、哪列是日期、哪列能筛，
 * 全由 `/api/views/{view}` 现场返回的元数据决定，新增视图不用改这个页面。
 */

interface 视图项 {
  视图: string
  域: string
  列数: number
}
interface 分组 {
  域: string
  项: 视图项[]
}

const 分组 = ref<分组[]>([])
const 加载中 = ref(false)
const 错误 = ref('')
const 关键词 = ref('')

const 全部 = computed(() => 分组.value.flatMap((g) => g.项))
const 命中 = computed(() => {
  const k = 关键词.value.trim().toLowerCase()
  if (!k) return 分组.value
  return 分组.value
    .map((g) => ({ 域: g.域, 项: g.项.filter((x) => x.视图.toLowerCase().includes(k) || g.域.includes(k)) }))
    .filter((g) => g.项.length)
})

/** 域色：让 13 个域在长目录里能靠颜色快速定位 */
const 域色: Record<string, string> = {
  财务: 'danger',
  销售: 'warning',
  采购: 'primary',
  库存: 'success',
  钢瓶: 'info',
  配送: 'primary',
  人力: 'warning',
  集成: 'info',
  审计: 'danger',
  报表中心: 'success',
  系统: 'info',
  移动: 'info',
  其他: ''
}

async function 加载() {
  加载中.value = true
  错误.value = ''
  try {
    const r: any = await api.views.清单()
    分组.value = r?.分组 ?? []
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '报表目录加载失败，请稍后重试'
    分组.value = []
  } finally {
    加载中.value = false
  }
}

onMounted(加载)
</script>

<template>
  <div>
    <PageHead
      title="报表中心"
      icon="Document"
      sub="v3 把统计口径全部固化在数据库视图里 · 本页只列目录，点进去看到的就是库里那张视图 · 新增视图无需改前端"
    >
      <template #actions>
        <el-button type="primary" :loading="加载中" @click="加载">
          <el-icon><Refresh /></el-icon> 刷新目录
        </el-button>
      </template>
    </PageHead>

    <div class="stat-grid">
      <StatCard
        label="可用报表"
        icon="Document"
        tone="brand"
        :value="全部.length.toLocaleString('zh-CN')"
        unit="张"
        :sub="`覆盖 <b>${分组.length}</b> 个业务域`"
      />
      <StatCard
        label="财务类"
        icon="Money"
        tone="info"
        :value="(分组.find((g) => g.域 === '财务')?.项.length ?? 0).toLocaleString('zh-CN')"
        unit="张"
        sub="凭证 / 科目余额 / 应收应付 / 成本 / 资产负债"
      />
      <StatCard
        label="经营类"
        icon="TrendCharts"
        tone="success"
        :value="
          (
            (分组.find((g) => g.域 === '销售')?.项.length ?? 0) +
            (分组.find((g) => g.域 === '采购')?.项.length ?? 0) +
            (分组.find((g) => g.域 === '配送')?.项.length ?? 0) +
            (分组.find((g) => g.域 === '库存')?.项.length ?? 0)
          ).toLocaleString('zh-CN')
        "
        unit="张"
        sub="销售 / 采购 / 配送 / 库存"
      />
      <StatCard
        label="口径来源"
        icon="Coin"
        tone="info"
        value="数据库视图"
        sub="页面不做二次聚合，看到的数就是视图里的数"
      />
    </div>

    <el-card shadow="never">
      <div class="filter-bar filter-collapsible">
        <el-input v-model="关键词" placeholder="搜视图名或业务域（如 凭证 / 钢瓶 / 配送）" clearable style="width: 320px">
          <template #prefix><el-icon><Search /></el-icon></template>
        </el-input>
        <span class="spacer" />
        <span class="hint">共 {{ 全部.length }} 张 · 当前显示 {{ 命中.reduce((s, g) => s + g.项.length, 0) }} 张</span>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!命中.length"
        empty-title="没有匹配的报表"
        empty-desc="换个关键词试试；目录来自数据库里的 vw_* 视图，为空说明库还没建报表视图。"
        empty-icon="Document"
        :skeleton-rows="8"
        @retry="加载"
      >
        <div v-for="g in 命中" :key="g.域" class="域块">
          <div class="域头">
            <el-tag :type="(域色[g.域] as any) || 'info'" effect="dark" size="small">{{ g.域 }}</el-tag>
            <span class="hint">{{ g.项.length }} 张</span>
          </div>
          <div class="卡格">
            <router-link
              v-for="x in g.项"
              :key="x.视图"
              class="报表卡"
              :to="`/reports/view/${encodeURIComponent(x.视图)}`"
            >
              <div class="报表卡__名">{{ x.视图.replace(/^vw_/, '') }}</div>
              <div class="报表卡__底">
                <span class="num">{{ x.列数 }} 列</span>
                <el-icon><ArrowRight /></el-icon>
              </div>
            </router-link>
          </div>
        </div>

        <div class="caliber-note">
          <b>口径</b>：本页不产出任何数字，只列目录。点开一张报表后，列、排序、日期区间、关键词筛选
          都由后端按<b>视图的真实元数据</b>决定（哪个是日期列就按哪列筛），所以口径与库里那张视图<b>永远一致</b>。
          视图由 <code>db/v3/7x_v3_报表_*.sql</code> 建立，改口径请改 SQL、不要在页面上拼。
        </div>
      </DataState>
    </el-card>
  </div>
</template>

<style scoped>
@media (min-width: 768px) {
  .filter-collapsible {
    display: flex;
  }
}

.域块 {
  margin-bottom: var(--sp-4);
}

.域头 {
  display: flex;
  align-items: center;
  gap: var(--sp-2);
  margin-bottom: var(--sp-2);
}

.卡格 {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(190px, 1fr));
  gap: var(--sp-2);
}

.报表卡 {
  display: block;
  padding: var(--sp-3);
  border: 1px solid var(--c-border);
  border-radius: var(--radius-md);
  background: var(--c-bg-elevated);
  text-decoration: none;
  color: inherit;
  transition: border-color var(--dur-fast), transform var(--dur-fast);
}

.报表卡:hover {
  border-color: var(--c-brand-500);
  transform: translateY(-1px);
}

.报表卡__名 {
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.报表卡__底 {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: var(--sp-1);
  color: var(--c-text-3);
  font-size: var(--fs-xs);
}
</style>
