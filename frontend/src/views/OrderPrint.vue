<script setup lang="ts">
/**
 * 单据打印页（批次 14）。
 *
 * 为什么单独开一页而不是在 OrderEdit 里 `window.print()`：
 *   录单页是「表单 + 表格 + 吸底条」，用 @media print 隐藏它们要么漏、要么把只读表格也打进去，
 *   而这里只渲染一张正式单据 —— 打印范围天然干净，也便于从列表页直接开新标签打印。
 *
 * 路由 `/print/orders/:kind/:id`（在 MainLayout 之外，无侧栏/顶栏）。
 * 数据走同一个 `GET /api/orders/{kind}/{id}`，不新增接口、不改契约。
 */
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import api from '@/api'
import { 单据表, 状态名称 } from '@/config/modules'
import { useLookupStore } from '@/stores/lookup'
import DataState from '@/components/DataState.vue'

const route = useRoute()
const router = useRouter()
const lookup = useLookupStore()

const kind = computed(() => String(route.params.kind))
const id = computed(() => Number(route.params.id ?? 0))
const 配置 = computed(() => 单据表[kind.value])

const 主表 = ref<any>({})
const 明细 = ref<any[]>([])
const 加载中 = ref(false)
const 错误 = ref('')

/** 三联：存根 / 客户 / 记账（送气单默认三联，其余默认单联） */
const 三联 = ref(kind.value === 'delivery')
const 联次 = computed(() => (三联.value ? ['存根', '客户', '记账'] : ['']))

/** 单号：模板里不做 `配置!.noField`（模板表达式不支持非空断言），统一在这里取 */
const 单据号码 = computed(() => {
  const k = 配置.value?.noField
  return k ? (主表.value[k] ?? '') : ''
})

const 公司 = computed<any>(() => lookup.列表('公司')[0] ?? {})
const 公司名 = computed(() => 公司.value.公司名称 || 公司.value.公司简称 || '瓶装气进销存')
const 网点名 = computed(() => {
  const 编号 = 主表.value['所属网点编号']
  return 编号 ? lookup.名称('经营网点', 编号) : ''
})

/* ------------------------------------------------------------------ 版式定义 */

type 列定义 = {
  label: string
  key: string
  lookup?: string
  options?: { label: string; value: any }[]
  类型?: 'money' | 'num' | 'text'
  宽?: string
}

/** 合计项：三种单据共用同一个形状（单位可空），免得模板里按 type 收窄 */
type 合计项 = { label: string; 值: number; 类型: 'money' | 'num'; 单位?: string }

const 库存状态选项 = [
  { label: '可用', value: 'AVAIL' },
  { label: '报废', value: 'SCRAP' },
  { label: '在途', value: 'TRANSIT' }
]
const 权属选项 = [
  { label: '自有', value: 'OWN' },
  { label: '客户', value: 'CUSTOMER' }
]

const 数字 = (v: any) => Number(v ?? 0)

/** 客户/供应商主档行：打印要地址电话，下拉数据里已经带全 */
function 伙伴行(表: string, 编号: any) {
  return 编号 ? lookup.行(表, 编号) : undefined
}

const 打印定义 = computed(() => {
  const m = 主表.value
  const 客 = 伙伴行('客户', m['客户编号'])
  const 供 = 伙伴行('供应商', m['供应商编号'])

  if (kind.value === 'delivery') {
    return {
      标题: '送气单',
      抬头: [
        { label: '客户', 值: lookup.名称('客户', m['客户编号']) },
        { label: '联系人', 值: 客?.联系人 ?? '' },
        { label: '电话', 值: 客?.联系电话 ?? '' },
        { label: '收货地址', 值: m['收货地址'] || 客?.客户地址 || '' },
        { label: '送货日期', 值: 日期文本(m['送货日期']) },
        { label: '业务员', 值: lookup.名称('员工', m['业务员编号']) },
        { label: '出库仓库', 值: lookup.名称('仓库', m['出库仓库编号']) },
        { label: '结算方式', 值: m['是否月结'] ? '月结' : '现结' },
        { label: '单据状态', 值: 状态名称(m['单据状态']) }
      ],
      列: [
        { label: '商品 / 规格', key: '商品编号', lookup: '商品', 宽: '40%' },
        { label: '数量', key: '数量', 类型: 'num' },
        { label: '单价', key: '单价', 类型: 'money' },
        { label: '金额', key: '行金额', 类型: 'money' },
        { label: '回收空瓶', key: '回收空瓶数', 类型: 'num' },
        { label: '备注', key: '备注', 类型: 'text' }
      ] as 列定义[],
      合计: [
        { label: '数量合计', 值: 和('数量'), 类型: 'num', 单位: '瓶' },
        { label: '金额合计', 值: 和('行金额') || 数字(m['销售金额']), 类型: 'money' }
      ] as 合计项[],
      金额情况: [
        { label: '销售金额', 值: 数字(m['销售金额']) },
        { label: '优惠金额', 值: 数字(m['优惠金额']) },
        { label: '应收金额', 值: 数字(m['销售金额']) - 数字(m['优惠金额']) },
        { label: '实收现金', 值: 数字(m['实收现金']) },
        { label: '挂账金额', 值: 数字(m['挂账金额']) }
      ],
      大写: 数字(m['销售金额']) - 数字(m['优惠金额']),
      签字: ['客户签收', '送货人', '制单']
    }
  }

  if (kind.value === 'intake') {
    return {
      标题: '到货单',
      抬头: [
        { label: '供应商', 值: lookup.名称('供应商', m['供应商编号']) },
        { label: '到货日期', 值: 日期文本(m['到货日期']) },
        { label: '收货仓库', 值: lookup.名称('仓库', m['收货仓库编号']) },
        { label: '车牌', 值: m['车牌'] ?? '' },
        { label: '司机', 值: m['司机姓名'] ?? '' },
        { label: '过磅单号', 值: m['过磅单号'] ?? '' },
        { label: '车次', 值: m['到货车次'] ?? '' },
        { label: '毛重 / 皮重', 值: `${数字(m['毛重公斤'])} / ${数字(m['皮重公斤'])} kg` },
        { label: '单据状态', 值: 状态名称(m['单据状态']) }
      ],
      列: [
        { label: '商品 / 规格', key: '商品编号', lookup: '商品', 宽: '34%' },
        { label: '应收', key: '应收数量', 类型: 'num' },
        { label: '实收', key: '实收数量', 类型: 'num' },
        { label: '拒收', key: '拒收数量', 类型: 'num' },
        { label: '含税单价', key: '含税单价', 类型: 'money' },
        { label: '不含税金额', key: '不含税金额', 类型: 'money' },
        { label: '备注', key: '备注', 类型: 'text' }
      ] as 列定义[],
      合计: [
        { label: '实收合计', 值: 和('实收数量'), 类型: 'num', 单位: '瓶' },
        { label: '拒收合计', 值: 和('拒收数量'), 类型: 'num', 单位: '瓶' },
        { label: '不含税金额', 值: 和('不含税金额'), 类型: 'money' }
      ] as 合计项[],
      金额情况: [],
      大写: 0,
      签字: ['送货人', '收货人', '质检员']
    }
  }

  if (kind.value === 'stocktake') {
    return {
      标题: '库存盘点单',
      抬头: [
        { label: '盘点仓库', 值: lookup.名称('仓库', m['仓库编号']) },
        { label: '盘点日期', 值: 日期文本(m['盘点日期']) },
        { label: '盘点范围', 值: m['盘点范围'] ?? '' },
        { label: '盘点人', 值: lookup.名称('员工', m['盘点人编号']) },
        { label: '盲盘', 值: m['是否盲盘'] ? '是' : '否' },
        { label: '差异行数', 值: m['差异行数'] ?? 0 },
        { label: '单据状态', 值: 状态名称(m['单据状态']) }
      ],
      列: [
        { label: '商品 / 规格', key: '商品编号', lookup: '商品', 宽: '32%' },
        { label: '批次', key: '批次编号', 类型: 'text' },
        { label: '库存状态', key: '库存状态代码', options: 库存状态选项 },
        { label: '权属', key: '权属代码', options: 权属选项 },
        { label: '账面', key: '账面数量', 类型: 'num' },
        { label: '实盘', key: '实盘数量', 类型: 'num' },
        { label: '差异', key: '差异数量', 类型: 'num' },
        { label: '差异原因', key: '差异原因', 类型: 'text' }
      ] as 列定义[],
      合计: [
        { label: '账面合计', 值: 和('账面数量'), 类型: 'num', 单位: '瓶' },
        { label: '实盘合计', 值: 和('实盘数量'), 类型: 'num', 单位: '瓶' },
        { label: '差异合计', 值: 和('差异数量'), 类型: 'num', 单位: '瓶' }
      ] as 合计项[],
      金额情况: [],
      大写: 0,
      签字: ['盘点人', '复核人', '仓管员']
    }
  }

  return null
})

/** 整列都没有内容（或被内部备注过滤掉）的「备注 / 差异原因」列不打出来，省纸也省眼 */
const 显示列 = computed<列定义[]>(() => {
  const 列 = 打印定义.value?.列 ?? []
  return 列.filter((c) => {
    if (!/备注|原因/.test(c.label)) return true
    return 明细.value.some((r) => {
      const v = r[c.key]
      return v !== null && v !== undefined && String(v).trim() !== '' && !内部备注.test(String(v).trim())
    })
  })
})

function 和(key: string) {
  return 明细.value.reduce((s, r) => s + Number(r[key] ?? 0), 0)
}

function 日期文本(v: any) {
  return v ? String(v).slice(0, 10) : ''
}

function 金额文本(v: any) {
  return Number(v ?? 0).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

function 数量文本(v: any) {
  return Number(v ?? 0).toLocaleString('zh-CN', { maximumFractionDigits: 3 })
}

/** 明细备注里的系统流水（「按订单 11441 生成」/「按订单预填」）属于内部信息，不印在给客户/供应商的单据上 */
const 内部备注 = /^按订单/

function 单元格(c: 列定义, r: any) {
  const v = r[c.key]
  if (c.类型 === 'text' && typeof v === 'string' && 内部备注.test(v.trim())) return '—'
  if (v === null || v === undefined || v === '') return c.类型 === 'money' ? '0.00' : '—'
  if (c.lookup) return lookup.名称(c.lookup, v) || String(v)
  if (c.options) return c.options.find((o) => String(o.value) === String(v))?.label ?? String(v)
  if (c.类型 === 'money') return 金额文本(v)
  if (c.类型 === 'num') return 数量文本(v)
  return String(v)
}

/* ------------------------------------------------------------------ 人民币大写 */

/** 金额转人民币大写（财务凭证/三联单必须印大写，避免手改数字） */
function 大写金额(v: any): string {
  const 分总额 = Math.round(Number(v ?? 0) * 100)
  const 负 = 分总额 < 0
  let n = Math.abs(分总额)
  if (n === 0) return '零元整'
  const 字 = '零壹贰叁肆伍陆柒捌玖'.split('')
  const 位 = ['', '拾', '佰', '仟']
  const 节 = ['', '万', '亿', '万亿']

  const 分 = n % 100
  n = Math.floor(n / 100)
  const 角 = Math.floor(分 / 10)
  const 分位 = 分 % 10

  const 节数组: number[] = []
  let 整 = n
  while (整 > 0) {
    节数组.push(整 % 10000)
    整 = Math.floor(整 / 10000)
  }

  let 结果 = ''
  for (let i = 节数组.length - 1; i >= 0; i--) {
    const 位串 = String(节数组[i]).padStart(4, '0')
    let 段 = ''
    let 待补零 = false
    for (let j = 0; j < 4; j++) {
      const d = Number(位串[j])
      if (d === 0) {
        if (段) 待补零 = true
      } else {
        if (待补零) {
          段 += '零'
          待补零 = false
        }
        段 += 字[d] + 位[3 - j]
      }
    }
    if (段) 结果 += 段 + 节[i]
    else if (结果 && !结果.endsWith('零')) 结果 += '零'
  }
  结果 = 结果.replace(/零+$/, '') || '零'

  let 小数 = ''
  if (角 === 0 && 分位 === 0) 小数 = '整'
  else {
    if (角 > 0) 小数 += 字[角] + '角'
    else if (分位 > 0) 小数 += '零'
    if (分位 > 0) 小数 += 字[分位] + '分'
  }
  return `${负 ? '负' : ''}${结果}元${小数}`
}

/* ------------------------------------------------------------------ 数据 */

async function 加载() {
  if (!配置.value || !(id.value > 0)) return
  加载中.value = true
  错误.value = ''
  try {
    await lookup.加载()
    const r: any = await api.orders.详情(kind.value, id.value)
    主表.value = r?.主表 ?? {}
    明细.value = r?.明细 ?? []
    document.title = `${配置.value.title} ${主表.value[配置.value.noField] ?? ''}`
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '单据加载失败，请返回列表重试'
  } finally {
    加载中.value = false
  }
}

function 打印() {
  window.print()
}

function 返回() {
  if (window.history.length > 1) router.back()
  else router.push(`/orders/${kind.value}/${id.value}`)
}

onMounted(async () => {
  await 加载()
  // 从录单页「打印」进来时带 ?print=1，自动打印机（浏览器仍需用户确认一次）
  if (route.query.print === '1' && !错误.value) setTimeout(打印, 400)
})
</script>

<template>
  <div class="print-page">
    <!-- 屏幕上的工具条：打印时由全局 @media print 里的 .no-print 隐藏 -->
    <div class="print-toolbar no-print">
      <span class="print-toolbar__t">{{ 打印定义?.标题 ?? 配置?.title ?? '单据' }}打印</span>
      <span class="print-toolbar__hint">A4 纵向 · 打印前请在浏览器里关掉「页眉页脚」</span>
      <span class="spacer" />
      <el-checkbox v-if="kind === 'delivery' || kind === 'intake'" v-model="三联">
        三联（存根 / 客户 / 记账）
      </el-checkbox>
      <el-button @click="返回">返回</el-button>
      <el-button type="primary" :disabled="加载中 || !打印定义" @click="打印">打印</el-button>
    </div>

    <DataState :loading="加载中" :error="错误" :empty="!打印定义" @retry="加载">
      <div v-if="打印定义" class="sheets">
        <section
          v-for="(联, i) in 联次"
          :key="i"
          class="sheet"
          :class="{ 'sheet--break': i < 联次.length - 1 }"
        >
          <header class="doc-head">
            <div class="doc-head__org">
              <div class="doc-head__name">{{ 公司名 }}</div>
              <div class="doc-head__sub">
                <span v-if="网点名">{{ 网点名 }}</span>
                <span v-if="公司.联系电话">电话 {{ 公司.联系电话 }}</span>
                <span v-if="公司.经营地址">{{ 公司.经营地址 }}</span>
              </div>
            </div>
            <h1 class="doc-head__title">{{ 打印定义.标题 }}</h1>
            <div class="doc-head__no">
              <span>单号</span>
              <b>{{ 单据号码 || '—' }}</b>
            </div>
            <!-- 联次标记：打印后裁切分单用，必须留在抬头流内（绝对定位会压住单号） -->
            <div v-if="联" class="doc-head__copy">{{ 联 }}联</div>
          </header>

          <div class="doc-meta">
            <div v-for="f in 打印定义.抬头" :key="f.label" class="doc-meta__item">
              <span class="doc-meta__k">{{ f.label }}</span>
              <span class="doc-meta__v">{{ f.值 || '—' }}</span>
            </div>
          </div>

          <table class="doc-table">
            <colgroup>
              <col style="width: 42px" />
              <col v-for="c in 显示列" :key="c.key" :style="c.宽 ? { width: c.宽 } : undefined" />
            </colgroup>
            <thead>
              <tr>
                <th class="c">#</th>
                <th v-for="c in 显示列" :key="c.key" :class="c.类型 === 'money' || c.类型 === 'num' ? 'r' : ''">
                  {{ c.label }}
                </th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(r, i) in 明细" :key="i">
                <td class="c">{{ i + 1 }}</td>
                <td
                  v-for="c in 显示列"
                  :key="c.key"
                  :class="c.类型 === 'money' || c.类型 === 'num' ? 'r num' : ''"
                >
                  {{ 单元格(c, r) }}
                </td>
              </tr>
              <tr v-if="!明细.length">
                <td :colspan="显示列.length + 1" class="c muted">本单无明细行</td>
              </tr>
            </tbody>
          </table>

          <div class="doc-sums">
            <span v-for="s in 打印定义.合计" :key="s.label" class="doc-sums__item">
              {{ s.label }}
              <b>{{ s.类型 === 'money' ? `¥${金额文本(s.值)}` : `${数量文本(s.值)}${s.单位 ?? ''}` }}</b>
            </span>
          </div>

          <div v-if="打印定义.金额情况.length" class="doc-money">
            <span v-for="x in 打印定义.金额情况" :key="x.label">
              {{ x.label }} <b>¥{{ 金额文本(x.值) }}</b>
            </span>
            <span class="doc-money__cn">
              应收大写：<b>{{ 大写金额(打印定义.大写) }}</b>
            </span>
          </div>

          <div v-if="主表['备注']" class="doc-note">备注：{{ 主表['备注'] }}</div>

          <footer class="doc-sign">
            <span v-for="s in 打印定义.签字" :key="s">{{ s }}：______________</span>
            <span>日期：________年____月____日</span>
          </footer>

          <div class="doc-foot">
            打印时间 {{ new Date().toLocaleString('zh-CN', { hour12: false }) }} ·
            本单由系统生成，金额以财务过账结果为准
          </div>
        </section>
      </div>
    </DataState>
  </div>
</template>

<style scoped>
.print-page {
  min-height: 100vh;
  padding: var(--sp-4);
  background: var(--bg-page);
}

.print-toolbar {
  display: flex;
  align-items: center;
  gap: var(--sp-3);
  max-width: 210mm;
  margin: 0 auto var(--sp-4);
  padding: var(--sp-2) var(--sp-3);
  background: var(--bg-surface);
  border: 1px solid var(--c-border-light);
  border-radius: var(--r-lg);
}

.print-toolbar__t {
  font-weight: var(--fw-semibold);
  color: var(--c-ink-900);
}

.print-toolbar__hint {
  font-size: var(--fs-xs);
  color: var(--c-ink-400);
}

.print-toolbar .spacer {
  flex: 1;
}

.sheets {
  display: flex;
  flex-direction: column;
  gap: var(--sp-4);
  align-items: center;
}

/* A4 单据：屏幕上是一张白纸卡片，打印时去掉阴影和圆角 */
.sheet {
  position: relative;
  box-sizing: border-box;
  width: 210mm;
  min-height: 297mm;
  padding: 14mm 12mm;
  background: #fff;
  color: #17212f;
  border-radius: var(--r-md);
  box-shadow: var(--sh-lg);
  font-size: 12px;
  line-height: 1.5;
}

.doc-head__copy {
  align-self: flex-start;
  padding: 2px 6px;
  font-size: 12px;
  letter-spacing: 2px;
  color: #666;
  border: 1px solid #bbb;
  white-space: nowrap;
}

.doc-head {
  display: flex;
  align-items: flex-end;
  gap: 8px;
  padding-bottom: 6px;
  border-bottom: 2px solid #17212f;
}

.doc-head__org {
  flex: 1;
  min-width: 0;
}

.doc-head__name {
  font-size: 17px;
  font-weight: 700;
  letter-spacing: 1px;
}

.doc-head__sub {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 2px;
  font-size: 11px;
  color: #555;
}

.doc-head__sub span + span::before {
  content: '·';
  margin-right: 8px;
  color: #999;
}

.doc-head__title {
  margin: 0;
  font-size: 22px;
  font-weight: 700;
  letter-spacing: 8px;
  white-space: nowrap;
}

.doc-head__no {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  font-size: 11px;
  color: #555;
}

.doc-head__no b {
  font-family: var(--font-mono, ui-monospace, Consolas, monospace);
  font-size: 13px;
  color: #17212f;
}

.doc-meta {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 2px 12px;
  margin: 8px 0 10px;
  font-size: 12px;
}

.doc-meta__item {
  display: flex;
  gap: 6px;
  min-width: 0;
  padding: 2px 0;
  border-bottom: 1px dotted #ddd;
}

.doc-meta__k {
  flex: none;
  width: 56px;
  color: #666;
}

.doc-meta__v {
  flex: 1;
  min-width: 0;
  overflow-wrap: anywhere;
  color: #17212f;
}

.doc-table {
  width: 100%;
  border-collapse: collapse;
  table-layout: fixed;
  font-size: 12px;
}

.doc-table th,
.doc-table td {
  padding: 5px 6px;
  border: 1px solid #999;
  overflow-wrap: anywhere;
}

.doc-table thead th {
  background: #f1f3f5;
  font-weight: 600;
}

.doc-table .c {
  text-align: center;
}

.doc-table .r {
  text-align: right;
}

.doc-table .num {
  font-family: var(--font-mono, ui-monospace, Consolas, monospace);
  font-variant-numeric: tabular-nums;
}

.doc-table .muted {
  color: #888;
  padding: 14px 0;
}

.doc-sums {
  display: flex;
  justify-content: flex-end;
  gap: 18px;
  margin-top: 6px;
  font-size: 12px;
}

.doc-sums__item b {
  font-family: var(--font-mono, ui-monospace, Consolas, monospace);
  font-size: 13px;
}

.doc-money {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 18px;
  margin-top: 6px;
  padding: 6px 8px;
  background: #f7f8f9;
  border: 1px solid #ddd;
  font-size: 12px;
}

.doc-money__cn {
  flex-basis: 100%;
  padding-top: 2px;
  border-top: 1px dashed #ccc;
}

.doc-note {
  margin-top: 8px;
  font-size: 11px;
  color: #444;
}

.doc-sign {
  display: flex;
  flex-wrap: wrap;
  gap: 10px 28px;
  margin-top: 26px;
  font-size: 12px;
}

.doc-foot {
  position: absolute;
  left: 12mm;
  right: 12mm;
  bottom: 8mm;
  font-size: 10px;
  color: #999;
  border-top: 1px solid #eee;
  padding-top: 4px;
}

@media print {
  .print-page {
    padding: 0;
    background: #fff;
  }

  .sheets {
    display: block;
  }

  .sheet {
    width: auto;
    min-height: 0;
    padding: 0;
    border-radius: 0;
    box-shadow: none;
  }

  /* 三联：每联单独一页 */
  .sheet--break {
    break-after: page;
    page-break-after: always;
  }

  .doc-foot {
    position: static;
    margin-top: 10px;
  }

  .doc-money {
    background: transparent;
  }
}
</style>
