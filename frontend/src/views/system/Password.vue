<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import api from '@/api'
import { useAuthStore } from '@/stores/auth'
import PageHead from '@/components/PageHead.vue'

/**
 * 修改密码 —— 两种进入方式：
 *   1) 首次登录（初始密码未改）：顶栏"初始密码未修改"横幅点进来，页面切到"首次登录"口径
 *   2) 平时主动改（用户菜单进来）
 *
 * 为什么不再由路由守卫强制：批次 6 第 1 步实测发现，"守卫无条件拦到本页"会把人锁死 ——
 * 改密成功后 store 里的用户对象仍旧是旧快照（必须改密 仍为 true），任何菜单都会被弹回来。
 * 现在守卫不再拦截，改密成功后这里用 标记已改密() 清标记，横幅随之消失。
 *
 * 本页曾经的问题（批次 2 修复）：
 *   `强制 = auth.必须改密 || route.query.force === '1'` 里的 query 是"粘"的：
 *   登录页跳到 /system/password（不带 force）时，守卫把它改写成 ?force=1，
 *   改密成功后 标记已改密() 只清掉了 auth 标记，query 还在 → 强制恒为真，
 *   页面永远像"还没改完"，再点任何菜单又被弹回来。
 *   现在：query 只作为提示用，成功改密后立即把 force 从地址栏摘掉。
 */

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

const 表单 = reactive({ 旧密码: '', 新密码: '', 确认: '' })
const 保存中 = ref(false)
const 错误 = ref('')
/** 改密成功后展示"下一步"卡片，而不是直接催人跳走 */
const 已完成 = ref(false)

/** 首次登录（仍在用初始密码）：只认 auth 标记，不认 query（query 会残留，见文件头注释） */
const 强制 = computed(() => auth.必须改密)

const 账号文本 = computed(
  () => [auth.用户?.工号, auth.用户?.姓名].filter(Boolean).join(' · ') || '未登录'
)

/* -------------------------------------------------------------- 强度评估 */

/** 密码强度：长度 + 字符种类，只做提示不做强约束（后端规则才是准绳） */
const 强度 = computed(() => {
  const p = 表单.新密码
  if (!p) return { 分: 0, 名: '未输入', 类型: 'info' as 'info' | 'warning' | 'success' | 'danger' }
  let 分 = 0
  if (p.length >= 8) 分 += 1
  if (p.length >= 12) 分 += 1
  if (/[a-z]/.test(p) && /[A-Z]/.test(p)) 分 += 1
  if (/\d/.test(p)) 分 += 1
  if (/[^A-Za-z0-9]/.test(p)) 分 += 1
  if (分 <= 2) return { 分, 名: '偏弱', 类型: 'danger' as const }
  if (分 <= 3) return { 分, 名: '一般', 类型: 'warning' as const }
  if (分 <= 4) return { 分, 名: '较强', 类型: 'success' as const }
  return { 分, 名: '很强', 类型: 'success' as const }
})

const 强度百分比 = computed(() => Math.min(100, 强度.value.分 * 20))

/** 逐条校验提示：用"要求列表"代替点击后才弹 toast */
const 规则 = computed(() => [
  { 名: '至少 6 位', 过: 表单.新密码.length >= 6 },
  { 名: '包含字母', 过: /[A-Za-z]/.test(表单.新密码) },
  { 名: '包含数字', 过: /\d/.test(表单.新密码) },
  { 名: '与确认一致', 过: !!表单.确认 && 表单.新密码 === 表单.确认 },
  { 名: '不同于原密码', 过: !!表单.新密码 && 表单.新密码 !== 表单.旧密码 }
])

const 可提交 = computed(
  () =>
    !!表单.旧密码 &&
    !!表单.新密码 &&
    !!表单.确认 &&
    表单.新密码.length >= 6 &&
    表单.新密码 === 表单.确认 &&
    表单.新密码 !== 表单.旧密码
)

/* ---------------------------------------------------------------- 提交 */

async function 提交() {
  错误.value = ''
  if (!表单.旧密码) return (错误.value = '请输入原密码')
  if (表单.新密码.length < 6) return (错误.value = '新密码至少 6 位')
  if (表单.新密码 !== 表单.确认) return (错误.value = '两次输入的新密码不一致')
  if (表单.新密码 === 表单.旧密码) return (错误.value = '新密码不能与原密码相同')
  if (/^\d{6}$/.test(表单.新密码) && 表单.新密码 === '123456')
    return (错误.value = '新密码不能使用系统初始密码')

  保存中.value = true
  try {
    await api.auth.改密码(表单.旧密码, 表单.新密码)
    ElMessage.success('密码已修改，下次登录请使用新密码')
    表单.旧密码 = 表单.新密码 = 表单.确认 = ''

    // 无论从哪进来，成功后都要清掉"仍在用初始密码"的标记：
    // 顶栏横幅（MainLayout 的 必须改密）据此消失，否则用户会一直看到"初始密码未修改"。
    auth.标记已改密()

    // 用副本算出口：route.query 在 replace 之后才是新的，这里别读第二次
    const 去向 = String(route.query.redirect || '')
    const q = { ...route.query }
    delete q.force
    delete q.redirect
    await router.replace({ path: route.path, query: q })
    已完成.value = true
    if (去向) ElMessage.success('可以开始使用了')
  } catch (e: any) {
    // 就地显示：原密码错误这类原因必须留在页面上，而不是 toast 一闪
    错误.value = e?.response?.data?.message ?? '密码修改失败，请检查原密码后重试'
  } finally {
    保存中.value = false
  }
}

function 再去改一次() {
  已完成.value = false
  错误.value = ''
}
</script>

<template>
  <div class="pwd-page">
    <PageHead
      :title="强制 ? '首次登录 · 请修改初始密码' : '修改密码'"
      icon="Lock"
      :sub="
        强制
          ? '初始密码是公开的，不修改就无法使用系统其他功能'
          : '建议定期更换；改完立即生效，下次登录用新密码'
      "
    />

    <!-- 强制模式：顶部强提示，说明"为什么必须改"，而不是看起来像可选页 -->
    <el-alert
      v-if="强制"
      type="warning"
      :closable="false"
      show-icon
      class="mb-3"
      title="当前仍在用初始密码，系统已限制进入其他页面"
    >
      <template #default>
        为保障账号安全，必须先把初始密码改成自己的密码。改密期间左侧菜单与其他页面暂时不可用。
      </template>
    </el-alert>

    <div class="pwd-grid">
      <!-- 左：表单 -->
      <el-card shadow="never" class="pwd-card">
        <template #header>
          <div class="pwd-card__head">
            <span class="section-title">{{ 已完成 ? '密码已修改' : '设置新密码' }}</span>
            <span class="hint">{{ 账号文本 }}<template v-if="auth.用户?.岗位名称">（{{ auth.用户.岗位名称 }}）</template></span>
          </div>
        </template>

        <!-- 成功态：给"下一步"出口（强制模式下看不见菜单，必须有按钮） -->
        <div v-if="已完成" class="done">
          <el-icon class="done__icon"><CircleCheckFilled /></el-icon>
          <div class="done__title">密码已更新</div>
          <div class="done__desc">新密码已生效，下次登录请使用新密码。</div>
          <div class="done__ops">
            <el-button type="primary" @click="router.push('/dashboard')">
              <el-icon><HomeFilled /></el-icon> 进入首页
            </el-button>
            <el-button @click="再去改一次">再改一次</el-button>
          </div>
        </div>

        <el-form v-else label-width="96px" label-position="right" @submit.prevent>
          <el-form-item label="当前账号">
            <span class="text-strong">{{ 账号文本 }}</span>
            <small v-if="auth.用户?.岗位名称" class="text-muted ml">{{ auth.用户.岗位名称 }}</small>
          </el-form-item>

          <el-form-item label="原密码">
            <el-input
              v-model="表单.旧密码"
              type="password"
              show-password
              autocomplete="current-password"
              :disabled="保存中"
              @keyup.enter="提交"
            />
          </el-form-item>

          <el-form-item label="新密码">
            <!-- 用自带的 show-password 眼睛图标，不再叠一个"显示/隐藏"按钮（会和它抢后缀位） -->
            <el-input
              v-model="表单.新密码"
              type="password"
              show-password
              autocomplete="new-password"
              placeholder="至少 6 位"
              :disabled="保存中"
              @keyup.enter="提交"
            />
            <!-- 强度条：只用语义令牌色 -->
            <div v-if="表单.新密码" class="strength">
              <div class="strength__bar">
                <div
                  class="strength__fill"
                  :class="`strength__fill--${强度.类型}`"
                  :style="{ width: 强度百分比 + '%' }"
                />
              </div>
              <span class="text-xs" :class="`text-${强度.类型 === 'info' ? 'muted' : 强度.类型}`">
                强度：{{ 强度.名 }}
              </span>
            </div>
          </el-form-item>

          <el-form-item label="确认新密码">
            <el-input
              v-model="表单.确认"
              type="password"
              show-password
              autocomplete="new-password"
              :disabled="保存中"
              @keyup.enter="提交"
            />
          </el-form-item>

          <!-- 失败就地显示 + 可直接重试（提交按钮就是重试） -->
          <div v-if="错误" class="pwd-error">
            <el-icon><CircleCloseFilled /></el-icon>
            <span>{{ 错误 }}</span>
          </div>

          <div class="pwd-ops">
            <el-button
              type="primary"
              :loading="保存中"
              :disabled="!可提交"
              @click="提交"
            >
              <el-icon><Check /></el-icon> 确认修改
            </el-button>
            <el-button v-if="!强制" @click="router.back()">取消</el-button>
          </div>
        </el-form>
      </el-card>

      <!-- 右：密码规则与说明（把"为什么被拒"前置，减少来回试） -->
      <el-card shadow="never" class="pwd-card">
        <template #header>
          <span class="section-title">密码要求</span>
        </template>

        <ul class="rules">
          <li v-for="r in 规则" :key="r.名" :class="{ 'rules__ok': r.过 }">
            <el-icon><CircleCheckFilled v-if="r.过" /><CircleClose v-else /></el-icon>
            <span>{{ r.名 }}</span>
          </li>
        </ul>

        <div class="caliber-note">
          <b>口径</b>：本页只做<b>提交前自检</b>（长度 / 一致 / 不同于原密码），
          密码是否合规最终以<b>后端 /api/auth/password 的校验</b>为准；界面上的"强度"由长度与字符种类估算，
          仅是提示，不是准入条件。改密成功后<b>立即生效</b>，当前会话不受影响，下次登录用新密码。
        </div>
      </el-card>
    </div>
  </div>
</template>

<style scoped>
/* 本页独有布局：表单 + 规则两栏，窄屏落成单栏 */
.pwd-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 340px);
  gap: var(--sp-4);
  align-items: start;
}

@media (max-width: 991px) {
  .pwd-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}

.pwd-card {
  border-radius: var(--r-lg);
}

.pwd-card__head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: var(--sp-3);
  flex-wrap: wrap;
}

.pwd-ops {
  display: flex;
  gap: var(--sp-2);
  flex-wrap: wrap;
  margin-top: var(--sp-4);
  padding-left: 96px; /* 与 el-form 的 label-width 对齐 */
}

@media (max-width: 767px) {
  .pwd-ops {
    padding-left: 0;
  }
}

.ml {
  margin-left: var(--sp-2);
}

/* 就地错误条 */
.pwd-error {
  display: flex;
  align-items: center;
  gap: var(--sp-2);
  width: auto;
  margin-left: 96px;
  margin-top: var(--sp-4);
  padding: var(--sp-2) var(--sp-3);
  background: var(--c-danger-50);
  border: 1px solid var(--c-danger-500);
  border-radius: var(--r-md);
  color: var(--c-danger-700);
  font-size: var(--fs-sm);
}

@media (max-width: 767px) {
  .pwd-error {
    margin-left: 0;
  }
}

/* 强度条 */
.strength {
  display: flex;
  align-items: center;
  gap: var(--sp-2);
  width: 100%;
  margin-top: var(--sp-1);
}

.strength__bar {
  flex: 1 1 auto;
  height: 6px;
  background: var(--c-ink-100);
  border-radius: var(--r-full);
  overflow: hidden;
}

.strength__fill {
  height: 100%;
  border-radius: var(--r-full);
  transition: width var(--dur-base) var(--ease-out), background var(--dur-base) var(--ease-out);
}

.strength__fill--danger { background: var(--c-danger-500); }
.strength__fill--warning { background: var(--c-warning-500); }
.strength__fill--success { background: var(--c-success-500); }
.strength__fill--info { background: var(--c-ink-300); }

/* 规则清单：未满足灰、满足绿 */
.rules {
  display: flex;
  flex-direction: column;
  gap: var(--sp-2);
  margin: 0;
  padding: 0;
  list-style: none;
}

.rules li {
  display: flex;
  align-items: center;
  gap: var(--sp-2);
  font-size: var(--fs-sm);
  color: var(--c-ink-400);
}

.rules li .el-icon {
  color: var(--c-ink-300);
}

.rules__ok {
  color: var(--c-success-700) !important;
}

.rules__ok .el-icon {
  color: var(--c-success-500) !important;
}

/* 成功态 */
.done {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--sp-2);
  padding: var(--sp-8) var(--sp-4);
  text-align: center;
}

.done__icon {
  font-size: 44px;
  color: var(--c-success-500);
}

.done__title {
  font-size: var(--fs-lg);
  font-weight: var(--fw-semibold);
  color: var(--c-ink-900);
}

.done__desc {
  font-size: var(--fs-sm);
  color: var(--c-ink-400);
}

.done__ops {
  display: flex;
  gap: var(--sp-2);
  margin-top: var(--sp-2);
  flex-wrap: wrap;
  justify-content: center;
}
</style>
