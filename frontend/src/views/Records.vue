<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import api from '@/api'
import { 档案表 } from '@/config/modules'
import { useLookupStore } from '@/stores/lookup'
import PageHead from '@/components/PageHead.vue'
import DataState from '@/components/DataState.vue'

const route = useRoute()
const lookup = useLookupStore()
const 表名 = computed(() => String(route.params.table))
const 配置 = computed(() => 档案表[表名.value])

const 行 = ref<any[]>([])
const 总数 = ref(0)
const 加载中 = ref(false)
const 错误 = ref('')
const 对话框 = ref(false)
const 编辑中 = ref(false)
const 保存中 = ref(false)
const 表单 = ref<any>({})
const 表单错误 = ref('')
const 查询 = reactive({ page: 1, size: 50, keyword: '' })
const 筛选展开 = ref(false)

const 主键名 = computed(() => (行.value[0] ? Object.keys(行.value[0])[0] : ''))

/** 表里展示的字段（textarea 太长，放表单里） */
const 列表字段 = computed(() => (配置.value?.fields ?? []).filter((x) => x.type !== 'textarea'))

/** 是否有筛选条件生效 */
const 有筛选 = computed(() => !!查询.keyword)

function 显示(f: any, row: any) {
  const v = row[f.key]
  if (v === null || v === undefined || v === '') return ''
  if (f.type === 'date') return String(v).slice(0, 10)
  if (f.type === 'money')
    return Number(v).toLocaleString('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
  if (f.type === 'switch') return v ? '是' : '否'
  if (f.type === 'select' && f.lookup) return lookup.名称(f.lookup, v) || v
  if (f.type === 'select' && f.options) {
    const o = f.options.find((x) => String(x.value) === String(v))
    return o ? o.label : v
  }
  if (f.type === 'number' && Math.abs(Number(v)) >= 1000) return Number(v).toLocaleString('zh-CN')
  return v
}

async function 加载() {
  if (!配置.value) return
  加载中.value = true
  错误.value = ''
  try {
    const r: any = await api.records.列表(表名.value, {
      page: 查询.page,
      size: 查询.size,
      keyword: 查询.keyword || undefined
    })
    行.value = r?.items ?? []
    总数.value = r?.total ?? 0
  } catch (e: any) {
    错误.value = e?.response?.data?.message ?? '档案列表加载失败，请稍后重试'
    行.value = []
    总数.value = 0
  } finally {
    加载中.value = false
  }
}

function 新建() {
  表单.value = {}
  配置.value.fields.forEach((f) => (表单.value[f.key] = f.type === 'switch' ? true : null))
  编辑中.value = false
  表单错误.value = ''
  对话框.value = true
}

function 编辑(row: any) {
  表单.value = { ...row }
  编辑中.value = true
  表单错误.value = ''
  对话框.value = true
}

/** 必填校验：在提交前拦一次，错误就地显示（而不是等后端 500） */
function 校验(): string {
  const 缺 = (配置.value?.fields ?? [])
    .filter((f) => f.required && (表单.value[f.key] === null || 表单.value[f.key] === undefined || 表单.value[f.key] === ''))
    .map((f) => f.label)
  if (缺.length) return `请填写：${缺.join('、')}`
  return ''
}

async function 保存() {
  const 错 = 校验()
  if (错) {
    表单错误.value = 错
    return
  }
  表单错误.value = ''
  保存中.value = true
  try {
    const body = { ...表单.value }
    if (编辑中.value) {
      await api.records.修改(表名.value, body[主键名.value], body)
    } else {
      await api.records.新增(表名.value, body)
    }
    ElMessage.success(编辑中.value ? '已保存修改' : '已新增')
    对话框.value = false
    await 加载()
  } catch (e: any) {
    表单错误.value = e?.response?.data?.message ?? '保存失败，请检查填写内容'
  } finally {
    保存中.value = false
  }
}

async function 删除(row: any) {
  const 名 = row[配置.value.fields[0]?.key] ?? row[主键名.value]
  try {
    await ElMessageBox.confirm(
      `确定删除「${名}」吗？删除后不可恢复。若该档案已被单据引用，删除会被数据库外键拦下。`,
      '删除确认',
      { type: 'warning', confirmButtonText: '确认删除', cancelButtonText: '取消' }
    )
  } catch {
    return // 用户取消
  }
  try {
    await api.records.删除(表名.value, row[主键名.value])
    ElMessage.success('已删除')
    await 加载()
  } catch (e: any) {
    // 外键拦截是可预期的业务错误，就地说明，避免用户反复重试
    const msg = e?.response?.data?.message ?? ''
    ElMessage.error(
      String(msg).includes('外键') || String(msg).includes('REFERENCE')
        ? '该档案已被单据或其它档案引用，不能删除。可改为在编辑里停用（是否有效 = 否）。'
        : msg || '删除失败'
    )
  }
}

function 重置() {
  查询.keyword = ''
  查询.page = 1
  加载()
}

/** 导出当前页为 CSV */
function 导出() {
  if (!行.value.length) {
    ElMessage.warning('当前没有可导出的数据')
    return
  }
  const 表头 = 列表字段.value.map((f) => f.label)
  const 体 = 行.value.map((row) =>
    列表字段.value.map((f) => `"${String(显示(f, row) ?? '').replace(/"/g, '""')}"`).join(',')
  )
  const csv = '\uFEFF' + [表头.join(','), ...体].join('\r\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `${配置.value.title}_${new Date().toISOString().slice(0, 10)}.csv`
  a.click()
  URL.revokeObjectURL(url)
  ElMessage.success(`已导出 ${行.value.length} 行`)
}

watch(表名, () => {
  查询.page = 1
  查询.keyword = ''
  加载()
})

onMounted(async () => {
  await lookup.加载()
  await 加载()
})
</script>

<template>
  <div v-if="配置">
    <PageHead :title="配置.title" icon="Collection" :sub="`档案维护 · 共 ${总数.toLocaleString('zh-CN')} 条`">
      <template #actions>
        <el-button :disabled="!行.length" @click="导出">
          <el-icon><Download /></el-icon> 导出
        </el-button>
        <el-button type="success" @click="新建">
          <el-icon><Plus /></el-icon> 新增
        </el-button>
      </template>
    </PageHead>

    <el-card shadow="never">
      <button class="mobile-filter-toggle el-button el-button--default" @click="筛选展开 = !筛选展开">
        <el-icon><Filter /></el-icon> 搜索
      </button>

      <div class="filter-bar filter-collapsible" :class="{ 'is-open': 筛选展开 }">
        <el-input
          v-model="查询.keyword"
          :placeholder="`搜索${配置.title}`"
          clearable
          style="width: 260px"
          @keyup.enter="查询.page = 1; 加载()"
        >
          <template #prefix><el-icon><Search /></el-icon></template>
        </el-input>
        <el-button type="primary" @click="查询.page = 1; 加载()">
          <el-icon><Search /></el-icon> 查询
        </el-button>
        <el-button @click="重置">
          <el-icon><RefreshLeft /></el-icon> 重置
        </el-button>
        <span class="spacer" />
        <span class="hint">共 {{ 总数.toLocaleString('zh-CN') }} 条</span>
      </div>

      <DataState
        :loading="加载中"
        :error="错误"
        :empty="!行.length"
        :empty-title="有筛选 ? `没有符合「${查询.keyword}」的${配置.title}` : `还没有${配置.title}`"
        :empty-desc="
          有筛选 ? '换个关键词，或点「重置」看全部。' : '点击右上角「新增」，录入第一条档案数据。'
        "
        empty-icon="Collection"
        @retry="加载"
      >
        <template #emptyAction>
          <el-button v-if="有筛选" @click="重置">
            <el-icon><RefreshLeft /></el-icon> 清除搜索
          </el-button>
          <el-button v-else type="primary" @click="新建">
            <el-icon><Plus /></el-icon> 新增
          </el-button>
        </template>

        <div class="table-wrap table-scroll-hint">
          <el-table :data="行" size="small" stripe>
            <el-table-column
              v-for="f in 列表字段"
              :key="f.key"
              :label="f.label"
              :min-width="f.width ?? 120"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <el-tag
                  v-if="f.type === 'switch'"
                  :type="row[f.key] ? 'success' : 'info'"
                  size="small"
                  effect="light"
                >
                  {{ row[f.key] ? '是' : '否' }}
                </el-tag>
                <span v-else-if="f.type === 'money'" class="num">{{ 显示(f, row) }}</span>
                <span v-else>{{ 显示(f, row) }}</span>
              </template>
            </el-table-column>

            <el-table-column label="操作" width="130" fixed="right">
              <template #default="{ row }">
                <div class="ops">
                  <el-button link type="primary" @click="编辑(row)">编辑</el-button>
                  <el-button link type="danger" @click="删除(row)">删除</el-button>
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
          :page-sizes="[50, 100, 200]"
          @current-change="加载"
          @size-change="查询.page = 1; 加载()"
        />
      </DataState>
    </el-card>

    <!-- 新增/编辑：宽屏对话框，窄屏自动全屏（05-响应式.css 里控制） -->
    <el-dialog
      v-model="对话框"
      :title="`${编辑中 ? '编辑' : '新增'}${配置.title}`"
      width="640px"
      :close-on-click-modal="false"
    >
      <div v-if="表单错误" class="error-panel">
        <el-icon><WarningFilled /></el-icon>
        <div class="error-panel__body">{{ 表单错误 }}</div>
      </div>

      <el-form label-width="110px" label-position="right" size="default">
        <el-row :gutter="16">
          <el-col v-for="f in 配置.fields" :key="f.key" :xs="24" :sm="f.type === 'textarea' ? 24 : 12">
            <el-form-item :label="f.label" :required="f.required">
              <el-date-picker
                v-if="f.type === 'date'"
                v-model="表单[f.key]"
                type="date"
                value-format="YYYY-MM-DD"
                style="width: 100%"
                :placeholder="`选择${f.label}`"
              />
              <el-select
                v-else-if="f.type === 'select' && f.lookup"
                v-model="表单[f.key]"
                filterable
                clearable
                style="width: 100%"
                :placeholder="`选择${f.label}`"
              >
                <el-option
                  v-for="o in lookup.选项(f.lookup)"
                  :key="o.value"
                  :label="o.label"
                  :value="o.value"
                />
              </el-select>
              <el-select
                v-else-if="f.type === 'select' && f.options"
                v-model="表单[f.key]"
                clearable
                style="width: 100%"
                :placeholder="`选择${f.label}`"
              >
                <el-option v-for="o in f.options" :key="String(o.value)" :label="o.label" :value="o.value" />
              </el-select>
              <el-input-number
                v-else-if="f.type === 'number'"
                v-model="表单[f.key]"
                :controls="false"
                style="width: 100%"
              />
              <el-input-number
                v-else-if="f.type === 'money'"
                v-model="表单[f.key]"
                :controls="false"
                :precision="2"
                style="width: 100%"
              />
              <el-switch v-else-if="f.type === 'switch'" v-model="表单[f.key]" />
              <el-input v-else-if="f.type === 'textarea'" v-model="表单[f.key]" type="textarea" :rows="2" />
              <el-input v-else v-model="表单[f.key]" :placeholder="`请输入${f.label}`" clearable />
            </el-form-item>
          </el-col>
        </el-row>
      </el-form>

      <template #footer>
        <el-button @click="对话框 = false">取消</el-button>
        <el-button type="primary" :loading="保存中" @click="保存">
          {{ 编辑中 ? '保存修改' : '确认新增' }}
        </el-button>
      </template>
    </el-dialog>
  </div>

  <div v-else class="empty-state">
    <div class="empty-state__icon"><el-icon><Collection /></el-icon></div>
    <div class="empty-state__title">未登记的档案表：{{ 表名 }}</div>
    <div class="empty-state__desc">
      该档案还没有在 <code>src/config/modules.ts</code> 的「档案表」里登记字段配置，因此无法渲染。
    </div>
  </div>
</template>

<style scoped>
.ops {
  display: flex;
  gap: 0 6px;
}

.ops .el-button {
  margin-left: 0;
}
</style>
