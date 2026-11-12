<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { 菜单 as 本地菜单, type 菜单组, type 菜单项 } from '@/config/modules'
import { useAuthStore } from '@/stores/auth'
import { useLookupStore } from '@/stores/lookup'
import {
  应用主题,
  初始主题,
  读侧栏折叠,
  写侧栏折叠,
  读展开分组,
  写展开分组,
  监听断点,
  type 主题
} from '@/utils/主题'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const lookup = useLookupStore()

/* ------------------------------------------------------------------ 布局态 */
const 折叠 = ref(读侧栏折叠())
const 断点 = ref<'mobile' | 'tablet' | 'desktop'>('desktop')
const 抽屉开 = ref(false)
const 菜单已从后端加载 = ref(false)

const 是移动端 = computed(() => 断点.value === 'mobile' || 断点.value === 'tablet')
/** 图标窄栏：只剩一列图标，分组子项这时没有显示位置 */
const 窄栏 = computed(() => 折叠.value && !是移动端.value)

/**
 * store 里导出的是 ref，模板里直接写 auth.必须改密 会被当成"取值后再取属性"，
 * Vue 会告警（坑 R：warning 也算失败），这里显式展开成 computed。
 */
const 必须改密 = computed(() => auth.必须改密)

/** 断点变化：进入移动端就自动收起抽屉；回到桌面端恢复用户此前选择 */
watch(断点, (b, 旧) => {
  if (b === 'mobile' || b === 'tablet') {
    抽屉开.value = false
        if (旧 === 'desktop') 折叠.value = true
  } else if (旧 && 旧 !== 'desktop') {
    折叠.value = 读侧栏折叠()
  }
})

function 切换侧栏() {
  if (是移动端.value) {
    抽屉开.value = !抽屉开.value
  } else {
    折叠.value = !折叠.value
    写侧栏折叠(折叠.value)
  }
}

/* ------------------------------------------------------------------ 主题 */
const 主题模式 = ref<主题>(初始主题())
应用主题(主题模式.value)

function 切换主题() {
  主题模式.value = 主题模式.value === 'dark' ? 'light' : 'dark'
  应用主题(主题模式.value)
}

/* 跟随系统：用户没手动选过时，系统切换也跟着变 */
let 解除系统监听: (() => void) | null = null

function 挂系统主题监听() {
  const mq = window.matchMedia?.('(prefers-color-scheme: dark)')
  if (!mq) return
  const 触发 = (e: MediaQueryListEvent) => {
    // 只在用户没有显式选择时跟随（localStorage 里没有记录）
    let 有记录 = true
    try {
      有记录 = localStorage.getItem('gas.theme') !== null
    } catch {
      有记录 = false
    }
    if (有记录) return
    主题模式.value = e.matches ? 'dark' : 'light'
    应用主题(主题模式.value)
  }
  mq.addEventListener('change', 触发)
  解除系统监听 = () => mq.removeEventListener('change', 触发)
}

/* ------------------------------------------------------------- 后端菜单 */
const 后端菜单索引 = computed(() => {
  const m = new Map<string, { title: string; 资源: string; 模块: string; 排序号: number }>()
  auth.后端菜单.forEach((x) => {
    if (!x?.路由) return
    m.set(String(x.路由), {
      title: x.名称 ?? String(x.资源),
      资源: String(x.资源 ?? ''),
      模块: String(x.模块 ?? ''),
      排序号: Number(x.排序号 ?? 0)
    })
  })
  return m
})

/** 只有一个入口的组（首页看板）不该被折成"点开才看得见"，直接平铺 */
type 导航组 = 菜单组 & { 独立?: boolean }

const 显示菜单 = computed<导航组[]>(() => {
  const 后端 = 后端菜单索引.value.size > 0
  const 结果: 导航组[] = []
  本地菜单.forEach((n) => {
    if ('children' in n) {
      const g = n as 菜单组
      let 子项 = g.children
      if (后端) {
        子项 = 子项.filter((c) => 后端菜单索引.value.has(c.path))
      } else {
        子项 = 子项.filter((c) => auth.有资源(c.资源))
      }
      if (!子项.length) return
      const 排好 = 后端
        ? [...子项].sort(
            (a, b) =>
              (后端菜单索引.value.get(a.path)?.排序号 ?? 0) -
              (后端菜单索引.value.get(b.path)?.排序号 ?? 0)
          )
        : 子项
      结果.push({ title: g.title, icon: g.icon, children: 排好 })
    } else {
      const it = n as 菜单项
      const 有 = 后端 ? 后端菜单索引.value.has(it.path) : auth.有资源(it.资源)
      if (有) 结果.push({ title: it.title, icon: it.icon ?? 'CircleCheck', children: [it], 独立: true })
    }
  })
  return 结果
})

/* ------------------------------------------------------------- 分组折叠 */
/**
 * 13 个组一次性摊开时，侧栏比正文还长，找入口靠滚动而不是靠归类，
 * 所以默认全部收起；只把"当前页所在的那一组"自动打开（见下面的 watch），
 * 其余由用户自己点。展开状态存 localStorage，刷新后还是原来那副样子。
 */
const 展开表 = ref<Record<string, boolean>>(读展开分组())

function 已展开(组名: string) {
  return 展开表.value[组名] === true
}

function 存展开表(下一个: Record<string, boolean>) {
  展开表.value = 下一个
  写展开分组(下一个)
}

function 展开分组(组名: string) {
  if (展开表.value[组名]) return
  存展开表({ ...展开表.value, [组名]: true })
}

function 切换分组(组名: string) {
  if (窄栏.value) {
    // 窄栏里子项没有落脚处：点分组 = 撑回宽栏并展开这一组，比悬停浮层少一种意外
    折叠.value = false
    写侧栏折叠(false)
    展开分组(组名)
    return
  }
  const 下一个 = { ...展开表.value }
  if (下一个[组名]) delete 下一个[组名]
  else 下一个[组名] = true
  存展开表(下一个)
}

/** 收起全部（窄栏开关只收放宽度，这个负责收放内容） */
function 收起全部() {
  存展开表({})
}

/** 底部"全部收起"只在真的有组开着时出现 */
const 有展开的组 = computed(() => 显示菜单.value.some((m) => !m.独立 && 已展开(m.title)))

/** 扁平化菜单项，供快捷搜索用 */
const 全部菜单项 = computed(() => {
  const out: { title: string; path: string; 组: string; icon?: string }[] = []
  显示菜单.value.forEach((g) => {
    g.children.forEach((c) => out.push({ title: c.title, path: c.path, 组: g.title, icon: g.icon }))
  })
  return out
})

const 当前标题 = computed(() => String(route.meta.title ?? ''))

/** 路由是否落在该组内：单据详情页 /orders/delivery/12 也算送气单那一组的 */
function 含当前页(m: 导航组) {
  return m.children.some((c) => route.path === c.path || route.path.startsWith(`${c.path}/`))
}

const 当前组 = computed(() => 显示菜单.value.find(含当前页)?.title ?? '')
const 当前菜单 = computed(() => route.path)

/** 当前页所在的"可折叠"组：独立入口（首页看板）没有展开这回事，不参与 */
const 当前所在组 = computed(() => {
  const 命中 = 显示菜单.value.find(含当前页)
  return 命中 && !命中.独立 ? 命中.title : ''
})

/* 走进某一组里的页面就把这一组打开（其余组仍是用户自己的选择），
   immediate 是因为刷新时菜单可能已经就位、watch 等不到"变化" */
watch(当前所在组, (组名) => 组名 && 展开分组(组名), { immediate: true })

/* 路由变化后：移动端自动关抽屉；切页回到顶部 */
watch(
  () => route.path,
  () => {
    if (是移动端.value) 抽屉开.value = false
    内容区.value?.scrollTo({ top: 0, behavior: 'smooth' })
  }
)

/* ------------------------------------------------------------ 快捷搜索 */
const 搜索开 = ref(false)
const 搜索词 = ref('')
const 搜索激活 = ref(0)
const 搜索结果 = computed(() => {
  const k = 搜索词.value.trim().toLowerCase()
  if (!k) return 全部菜单项.value.slice(0, 12)
  return 全部菜单项.value
    .filter((x) => x.title.toLowerCase().includes(k) || x.组.toLowerCase().includes(k))
    .slice(0, 20)
})

watch(搜索结果, () => (搜索激活.value = 0))

function 打开搜索() {
  搜索词.value = ''
  搜索激活.value = 0
  搜索开.value = true
}

function 跳转(item: { path: string }) {
  搜索开.value = false
  if (item.path !== route.path) router.push(item.path)
}

function 搜索键盘(e: KeyboardEvent) {
  if (e.key === 'ArrowDown') {
    e.preventDefault()
    搜索激活.value = Math.min(搜索激活.value + 1, 搜索结果.value.length - 1)
  } else if (e.key === 'ArrowUp') {
    e.preventDefault()
    搜索激活.value = Math.max(搜索激活.value - 1, 0)
  } else if (e.key === 'Enter' && 搜索结果.value[搜索激活.value]) {
    e.preventDefault()
    跳转(搜索结果.value[搜索激活.value])
  }
}

/* Cmd/Ctrl+K 打开搜索，Esc 关闭；输入框里不劫持 */
function 全局键(e: KeyboardEvent) {
  if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
    e.preventDefault()
    打开搜索()
    return
  }
  if (e.key === 'Escape' && 搜索开.value) 搜索开.value = false
}

/* ------------------------------------------------------------ 内容区滚动 */
const 内容区 = ref<HTMLElement | null>(null)
const 显示回顶 = ref(false)

function 处理滚动() {
  const el = 内容区.value
  if (!el) return
  显示回顶.value = el.scrollTop > 400
}

function 回到顶部() {
  内容区.value?.scrollTo({ top: 0, behavior: 'smooth' })
}

/* ------------------------------------------------------------------ 杂项 */
const 姓名缩写 = computed(() => {
  const n = auth.用户?.姓名 ?? ''
  return n ? n.slice(-2) : '?'
})

const 岗位描述 = computed(() => {
  const u = auth.用户
  if (!u) return ''
  const 部门 = u.部门名称 || '未设部门'
  const 岗位 = u.岗位名称 || '未设岗位'
  return `${部门} · ${岗位}`
})

async function 退出() {
  await auth.退出()
  ElMessage.success('已退出登录')
  router.push('/login')
}

async function 初始化菜单() {
  await auth.拉取菜单()
  if (auth.后端菜单.length) 菜单已从后端加载.value = true
}

/** 菜单换血后把已经不存在的组名从展开表里清掉，别让它越存越胖 */
function 清理展开表() {
  const 有效 = new Set(显示菜单.value.filter((m) => !m.独立).map((m) => m.title))
  const 下一个: Record<string, boolean> = {}
  Object.keys(展开表.value).forEach((组名) => {
    if (有效.has(组名)) 下一个[组名] = true
  })
  if (Object.keys(下一个).length !== Object.keys(展开表.value).length) 存展开表(下一个)
}

onMounted(async () => {
  解除断点监听 = 监听断点((b) => (断点.value = b))
  挂系统主题监听()
  window.addEventListener('keydown', 全局键)
  await lookup.加载()
  await 初始化菜单()
  清理展开表()
})

let 解除断点监听: (() => void) | null = null

onBeforeUnmount(() => {
  解除断点监听?.()
  解除系统监听?.()
  window.removeEventListener('keydown', 全局键)
})
</script>

<template>
  <div
    class="app-shell"
    :class="{ 'is-collapsed': 窄栏, 'is-drawer-open': 抽屉开 }"
  >
    <!-- 移动端抽屉遮罩 -->
    <div class="app-side__mask" @click="抽屉开 = false" />

    <!-- ============================ 侧边导航 ============================ -->
    <aside class="app-side">
      <div class="app-side__brand">
        <div class="app-side__logo">气</div>
        <div v-show="!折叠 || 是移动端" class="min-w-0">
          <div class="app-side__name">瓶装气进销存</div>
          <div class="app-side__sub">LPG INVENTORY v3</div>
        </div>
      </div>

      <nav class="app-side__scroll">
        <template v-for="(m, i) in 显示菜单" :key="i">
          <!-- 独立入口（首页看板）：只有一个页面的组不必再折一层 -->
          <router-link
            v-if="m.独立"
            :to="m.children[0].path"
            class="nav-item nav-solo"
            :class="{ 'is-active': 当前菜单 === m.children[0].path }"
            :title="m.children[0].title"
          >
            <el-icon><component :is="m.children[0].icon ?? m.icon" /></el-icon>
            <span v-show="!窄栏" class="nav-item__text">{{ m.children[0].title }}</span>
          </router-link>

          <template v-else>
            <button
              type="button"
              class="nav-group"
              :class="{ 'is-open': 已展开(m.title), 'is-current': 含当前页(m) }"
              :aria-expanded="已展开(m.title) ? 'true' : 'false'"
              :title="m.title"
              @click="切换分组(m.title)"
            >
              <el-icon class="nav-group__icon"><component :is="m.icon" /></el-icon>
              <span v-show="!窄栏" class="nav-group__title">{{ m.title }}</span>
              <span v-show="!窄栏" class="nav-group__count">{{ m.children.length }}</span>
              <el-icon v-show="!窄栏" class="nav-group__arrow"><ArrowRight /></el-icon>
              <!-- 折起来时也得看得见"我正在这一组里"：留一颗暖色光点 -->
              <span v-if="含当前页(m) && !已展开(m.title)" class="nav-group__spot" />
            </button>

            <!-- 折叠高度按子项实际条数给（一行 36px + 上下留白），
                 不写死一个"够大"的 max-height：那样收起的缓动会拖一段空尾巴 -->
            <div
              class="nav-fold"
              :class="{ 'is-open': 已展开(m.title) }"
              :style="`--fold-h:${m.children.length * 36 + 14}px`"
            >
              <div class="nav-children">
                <router-link
                  v-for="(c, ci) in m.children"
                  :key="c.path"
                  :to="c.path"
                  class="nav-item nav-child"
                  :class="{ 'is-active': 当前菜单 === c.path }"
                  :style="`--dab:${ci}`"
                >
                  <el-icon><component :is="c.icon ?? m.icon ?? 'Document'" /></el-icon>
                  <span class="nav-item__text">{{ c.title }}</span>
                </router-link>
              </div>
            </div>
          </template>
        </template>

        <div v-if="!显示菜单.length" class="empty" style="padding: 40px 16px">
          <el-icon class="empty-icon"><Lock /></el-icon>
          <div class="text-xs">当前岗位没有可用菜单</div>
        </div>
      </nav>

      <div class="app-side__foot">
        <span v-if="!菜单已从后端加载">菜单来自本地配置</span>
        <span v-else>菜单由权限服务下发</span>
        <span class="spacer" />
        <button v-if="有展开的组" type="button" class="nav-reset" @click="收起全部">全部收起</button>
      </div>
    </aside>

    <!-- ============================ 主区域 ============================ -->
    <div class="app-body">
      <header class="app-header">
        <div class="app-header__left">
          <button class="icon-btn" :title="折叠 ? '展开菜单' : '收起菜单'" @click="切换侧栏">
            <el-icon><Fold v-if="!折叠 && !是移动端" /><Expand v-else /></el-icon>
          </button>

          <div class="app-header__title">
            <h1>{{ 当前标题 }}</h1>
            <span v-if="当前组" class="app-header__crumb">{{ 当前组 }}</span>
          </div>
        </div>

        <div class="app-header__right">
          <!-- 必须改密：提示而非强制拦截（全局拦截会把人锁死在改密页，见 router/index.ts） -->
          <span
            v-if="必须改密"
            class="must-change"
            title="首次登录建议尽快修改初始密码"
            @click="router.push('/system/password')"
          >
            <el-icon><WarningFilled /></el-icon>
            初始密码未修改
          </span>

          <!-- 快捷搜索：Cmd/Ctrl + K -->
          <button class="icon-btn" title="搜索功能（Ctrl+K）" @click="打开搜索">
            <el-icon><Search /></el-icon>
          </button>

          <button
            class="icon-btn"
            :title="主题模式 === 'dark' ? '切换到亮色' : '切换到暗色'"
            @click="切换主题"
          >
            <el-icon><Moon v-if="主题模式 === 'light'" /><Sunny v-else /></el-icon>
          </button>

          <!-- 用户信息 + 下拉 -->
          <el-dropdown trigger="click">
            <div class="user-chip">
              <div class="user-chip__avatar">{{ 姓名缩写 }}</div>
              <div class="min-w-0">
                <div class="user-chip__name">{{ auth.用户?.姓名 }}</div>
              </div>
            </div>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item disabled>
                  <div class="text-xs text-muted">
                    {{ auth.用户?.工号 }} · {{ 岗位描述 }}
                  </div>
                </el-dropdown-item>
                <el-dropdown-item divided @click="router.push('/system/password')">
                  <el-icon><Key /></el-icon> 修改密码
                </el-dropdown-item>
                <el-dropdown-item @click="router.push('/system/permissions')">
                  <el-icon><Lock /></el-icon> 权限矩阵
                </el-dropdown-item>
                <el-dropdown-item divided @click="退出">
                  <el-icon><SwitchButton /></el-icon> 退出登录
                </el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>
      </header>

      <main ref="内容区" class="app-main" @scroll.passive="处理滚动">
        <div class="app-main__inner">
          <router-view v-slot="{ Component }">
            <transition name="page" mode="out-in">
              <component :is="Component" />
            </transition>
          </router-view>
          <div class="app-main__footer">瓶装气进销存系统 · v3</div>
        </div>
      </main>
    </div>

    <!-- 回到顶部 -->
    <button class="back-top" :class="{ 'is-hidden': !显示回顶 }" title="回到顶部" @click="回到顶部">
      <el-icon><Top /></el-icon>
    </button>

    <!-- ============================ 快捷搜索面板 ============================ -->
    <el-dialog
      v-model="搜索开"
      width="520px"
      :show-close="false"
      top="12vh"
      class="quick-search"
      append-to-body
    >
      <template #header>
        <el-input
          v-model="搜索词"
          placeholder="搜索功能 / 单据 / 报表…"
          size="large"
          clearable
          autofocus
          @keydown="搜索键盘"
        >
          <template #prefix><el-icon><Search /></el-icon></template>
        </el-input>
      </template>

      <div class="quick-search__results">
        <template v-for="(g, gi) in [...new Set(搜索结果.map((x) => x.组))]" :key="gi">
          <div class="quick-search__group">{{ g }}</div>
          <div
            v-for="item in 搜索结果.filter((x) => x.组 === g)"
            :key="item.path"
            class="quick-search__item"
            :class="{ 'is-active': 搜索结果[搜索激活]?.path === item.path }"
            @click="跳转(item)"
            @mouseenter="搜索激活 = 搜索结果.findIndex((x) => x.path === item.path)"
          >
            <el-icon><component :is="item.icon ?? 'Document'" /></el-icon>
            <span class="quick-search__label">{{ item.title }}</span>
            <span class="quick-search__path">{{ item.path }}</span>
          </div>
        </template>

        <div v-if="!搜索结果.length" class="empty" style="padding: 32px">
          <el-icon class="empty-icon"><Search /></el-icon>
          <div class="text-sm">没有匹配的功能</div>
        </div>
      </div>

      <template #footer>
        <div class="row text-xs text-muted">
          <span><kbd>↑</kbd><kbd>↓</kbd> 选择</span>
          <span><kbd>Enter</kbd> 打开</span>
          <span><kbd>Esc</kbd> 关闭</span>
          <span class="spacer" />
          <span>{{ 搜索结果.length }} 项</span>
        </div>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
/* 主区域纵向布局：顶栏固定 + 内容滚动。
   .app-body 是 grid 的第二列，必须能收缩（min-width:0），否则宽表格会撑破布局。 */
.app-body {
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
  height: 100%;
}

.min-w-0 {
  min-width: 0;
}

kbd {
  display: inline-block;
  min-width: 16px;
  padding: 1px 4px;
  margin-right: 2px;
  border: 1px solid var(--c-border);
  border-bottom-width: 2px;
  border-radius: var(--r-xs);
  background: var(--bg-surface-alt);
  font-family: var(--font-num);
  font-size: 10px;
  color: var(--c-ink-500);
  text-align: center;
}

:deep(.quick-search .el-dialog__header) {
  padding: var(--sp-4) var(--sp-4) var(--sp-3);
}

:deep(.quick-search .el-dialog__body) {
  padding: 0;
}

:deep(.quick-search .el-dialog__footer) {
  padding: var(--sp-2) var(--sp-4);
}
</style>
