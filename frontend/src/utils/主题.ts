/**
 * 主题（亮/暗）与布局偏好的持久化。
 *
 * 为什么不用 Element Plus 自带的 dark 主题包：
 *   Element Plus 的暗色是"在它自己的变量上再覆盖一遍"，与我们 01 号令牌文件
 *   的分工重叠，容易出现"组件是暗的、页面底还是亮的"这种半吊子状态。
 *   这里统一由 `<html class="dark">` 驱动我们自己的令牌，组件变量在 03 号文件里
 *   已经映射到令牌，所以只切一个 class 就整体生效。
 */

const 键 = {
  主题: 'gas.theme',
  侧栏折叠: 'gas.side.collapsed',
  展开分组: 'gas.side.groups'
} as const

export type 主题 = 'light' | 'dark'

function 读(key: string): string | null {
  try {
    return localStorage.getItem(key)
  } catch {
    return null // 隐私模式 / 禁用存储时静默降级
  }
}

function 写(key: string, value: string) {
  try {
    localStorage.setItem(key, value)
  } catch {
    /* 忽略：存不进去不影响使用 */
  }
}

/** 首次进入时跟随系统偏好，之后以用户选择为准 */
export function 初始主题(): 主题 {
  const 存 = 读(键.主题)
  if (存 === 'dark' || 存 === 'light') return 存
  if (typeof window !== 'undefined' && window.matchMedia?.('(prefers-color-scheme: dark)').matches) {
    return 'dark'
  }
  return 'light'
}

/** 把主题写到 <html> 上；同时告诉浏览器"当前应该用亮还是暗的滚动条/表单控件" */
export function 应用主题(t: 主题) {
  const el = document.documentElement
  el.classList.toggle('dark', t === 'dark')
  el.style.colorScheme = t === 'dark' ? 'dark' : 'light'
  写(键.主题, t)
}

export function 读侧栏折叠(): boolean {
  return 读(键.侧栏折叠) === '1'
}

export function 写侧栏折叠(v: boolean) {
  写(键.侧栏折叠, v ? '1' : '0')
}

/**
 * 侧栏分组的展开状态。
 *
 * 存的是"展开着的组名数组"而不是整个 map：菜单标题由后端下发、可能整批换掉，
 * 只记开着的项就不会越用越胖，读到一个已被下架的组名也只是多一个用不上的键。
 */
export function 读展开分组(): Record<string, boolean> {
  const 原始 = 读(键.展开分组)
  if (!原始) return {}
  try {
    const 名单 = JSON.parse(原始)
    if (!Array.isArray(名单)) return {}
    const 结果: Record<string, boolean> = {}
    名单.forEach((k) => {
      if (typeof k === 'string') 结果[k] = true
    })
    return 结果
  } catch {
    return {} // 存过旧格式或被人手改坏：当作"全部收起"重来，不影响使用
  }
}

export function 写展开分组(展开: Record<string, boolean>) {
  写(
    键.展开分组,
    JSON.stringify(Object.keys(展开).filter((k) => 展开[k]))
  )
}

/**
 * 视口断点判定（与 05-响应式.css 的断点保持一致）。
 * 用于"折叠侧栏"与"变抽屉"这两种不同行为的分界。
 */
export function 当前断点(): 'mobile' | 'tablet' | 'desktop' {
  if (typeof window === 'undefined') return 'desktop'
  const w = window.innerWidth
  if (w <= 767) return 'mobile'
  if (w <= 991) return 'tablet'
  return 'desktop'
}

/** 响应式：断点变化时回调（内部用 matchMedia，比监听 resize 更省性能） */
export function 监听断点(fn: (b: 'mobile' | 'tablet' | 'desktop') => void): () => void {
  if (typeof window === 'undefined') return () => {}
  const mqMobile = window.matchMedia('(max-width: 767px)')
  const mqTablet = window.matchMedia('(max-width: 991px)')
  const 触发 = () => fn(当前断点())
  mqMobile.addEventListener('change', 触发)
  mqTablet.addEventListener('change', 触发)
  触发()
  return () => {
    mqMobile.removeEventListener('change', 触发)
    mqTablet.removeEventListener('change', 触发)
  }
}
