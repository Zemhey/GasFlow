<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { useAuthStore } from '@/stores/auth'
import { 应用主题, 初始主题 } from '@/utils/主题'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()
const 加载中 = ref(false)
const 表单 = reactive({ 工号: '', 密码: '' })
const 错误 = ref('')
/** 密码是否可见（自己控制，避免 show-password 的图标与主题不搭） */
const 见密码 = ref(false)

/** 演示账号：点一下直接填入，省去手工输入。
 *  口径来自 db/v3/20_v3_种子_组织与主体.sql 的种子员工（v3 只有 E001~E008）。
 *  以前的 YG001/YG002/YG003 是 v2 库的号，v3 里不存在 —— 点一下就会登录失败。 */
const 演示账号 = [
  { 工号: 'G001', 姓名: '游客', 角色: '游客账号 · 全功能演示' }
]

function 填入(工号: string) {
  表单.工号 = 工号
  表单.密码 = '123456'
  错误.value = ''
}

async function 提交() {
  if (!表单.工号 || !表单.密码) {
    ElMessage.warning('请输入工号和密码')
    return
  }
  错误.value = ''
  加载中.value = true
  try {
    const u = await auth.登录(表单.工号.trim(), 表单.密码)
    ElMessage.success(`欢迎回来，${u.姓名}`)
    // 首次登录（初始密码未改）不在这里拦：交给路由守卫与 MainLayout 的横幅，
    // 否则"守卫把人推向 /system/password、本页又把人推向 /dashboard"会来回弹（见 router/index.ts）。
    router.push((route.query.redirect as string) || '/dashboard')
  } catch (e: any) {
    // 登录失败不弹 toast（会盖住表单），就地显示原因，便于连续重试
    错误.value = e?.response?.data?.message ?? '工号或密码不正确，请重试'
  } finally {
    加载中.value = false
  }
}

const 主题 = ref(初始主题())

function 切换主题() {
  主题.value = 主题.value === 'dark' ? 'light' : 'dark'
  应用主题(主题.value)
}

onMounted(() => {
  // 登录页也应用用户上次选择的主题，避免"登录页亮、进去是暗"的割裂感
  应用主题(主题.value)
})
</script>

<template>
  <div class="login-page">
    <!-- 左侧品牌区：桌面端显示，手机上隐藏（手机上屏幕要留给表单） -->
    <aside class="login-brand">
      <div class="login-brand__inner">
        <div class="login-brand__logo">气</div>
        <h1>瓶装气进销存系统</h1>
        <p class="login-brand__slogan">LPG Inventory &amp; Distribution</p>

        <ul class="login-brand__features">
          <li><el-icon><Van /></el-icon> 销售 · 配送 · 签收闭环</li>
          <li><el-icon><Coin /></el-icon> 钢瓶一物一码资产台账</li>
          <li><el-icon><Wallet /></el-icon> 凭证 · 应收应付 · 月结</li>
          <li><el-icon><DataAnalysis /></el-icon> 提成薪资 · 报表驾驶舱</li>
        </ul>

        <div class="login-brand__foot">宜昌江城燃气有限公司 · v3</div>
      </div>
    </aside>

    <!-- 右侧表单区 -->
    <main class="login-form">
      <button class="login-theme icon-btn" :title="主题 === 'dark' ? '切换到亮色' : '切换到暗色'" @click="切换主题">
        <el-icon><Moon v-if="主题 === 'light'" /><Sunny v-else /></el-icon>
      </button>

      <div class="login-form__box">
        <div class="login-form__head">
          <div class="login-form__logo">气</div>
          <div>
            <h2>登录</h2>
            <p>请使用您的工号登录系统</p>
          </div>
        </div>

        <!-- 失败原因就地展示，不用 toast 遮挡表单 -->
        <div v-if="错误" class="error-panel">
          <el-icon><WarningFilled /></el-icon>
          <div class="error-panel__body">{{ 错误 }}</div>
        </div>

        <el-form label-position="top" size="large" @keyup.enter="提交">
          <el-form-item label="工号">
            <el-input v-model="表单.工号" placeholder="如 E001" autofocus clearable @input="错误 = ''">
              <template #prefix><el-icon><User /></el-icon></template>
            </el-input>
          </el-form-item>

          <el-form-item label="密码">
            <el-input
              v-model="表单.密码"
              :type="见密码 ? 'text' : 'password'"
              placeholder="默认 123456"
              @input="错误 = ''"
            >
              <template #prefix><el-icon><Lock /></el-icon></template>
              <template #suffix>
                <el-icon class="pw-toggle" @click="见密码 = !见密码">
                  <View v-if="!见密码" /><Hide v-else />
                </el-icon>
              </template>
            </el-input>
          </el-form-item>

          <el-button type="primary" size="large" class="login-submit" :loading="加载中" @click="提交">
            登 录
          </el-button>
        </el-form>

        <div class="login-demo">
          <div class="login-demo__title">
            <el-icon><InfoFilled /></el-icon> 演示账号（点击自动填入）
          </div>
          <div class="login-demo__list">
            <button
              v-for="a in 演示账号"
              :key="a.工号"
              class="login-demo__item"
              type="button"
              @click="填入(a.工号)"
            >
              <span class="login-demo__no">{{ a.工号 }}</span>
              <span class="login-demo__name">{{ a.姓名 }}</span>
              <span class="login-demo__role">{{ a.角色 }}</span>
            </button>
          </div>
          <p class="login-demo__tip">密码均为 123456</p>
        </div>
      </div>
    </main>
  </div>
</template>

<style scoped>
/* 左右分栏：≥992px 显示品牌区，以下只留表单 */
.login-page {
  display: grid;
  grid-template-columns: 1fr 1fr;
  height: 100vh;
  height: 100dvh;
  /* 透明：吃 body 上那层大气（暖阳光 + 远处天光 + 笔触），表单才浮在空气上 */
  background: transparent;
}

/* ------------------------------------------------------------- 品牌区 */
/* 一块"户外光"的底：天光从左上、炉火从右下，两块互补色都只用极低的浓度，
   中间那层点彩由 ::before 负责 —— 颜色不预先混好，留给眼睛去混。 */
.login-brand {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  background:
    radial-gradient(62% 46% at 78% 82%, rgba(245, 166, 35, 0.22), transparent 62%),
    radial-gradient(70% 54% at 12% 6%, rgba(96, 154, 250, 0.22), transparent 60%),
    linear-gradient(168deg, #2a4f70 0%, #1f3550 48%, #17212f 100%);
  color: #fff;
}

/* 点彩：三套错开的细小色点（暖金 + 天蓝 + 一丝紫），密处像被晒亮的空气，疏处退回夜色。
   第三套用质数级间距（41px）专门破掉前两层的规律，否则整片会读成半色调网点。 */
.login-brand::before {
  content: '';
  position: absolute;
  inset: 0;
  background-image:
    radial-gradient(circle, rgba(255, 201, 77, 0.18) 0 1.5px, transparent 2.4px),
    radial-gradient(circle, rgba(147, 187, 253, 0.18) 0 1.5px, transparent 2.4px),
    radial-gradient(circle, rgba(142, 124, 195, 0.16) 0 1.2px, transparent 2px);
  background-size: 26px 26px, 34px 34px, 41px 41px;
  background-position: 0 0, 13px 17px, 27px 8px;
  mask-image: radial-gradient(78% 70% at 46% 42%, #000 18%, transparent 100%);
  -webkit-mask-image: radial-gradient(78% 70% at 46% 42%, #000 18%, transparent 100%);
}

/* 地平线上的反光：慢慢横移的一层光，让静止的登录页也有"某一瞬间"的感觉 */
.login-brand::after {
  content: '';
  position: absolute;
  left: -18%;
  right: -18%;
  bottom: -22%;
  height: 54%;
  background: radial-gradient(58% 100% at 50% 100%, rgba(255, 214, 130, 0.3), transparent 72%);
  animation: light-drift 22s var(--ease-in-out) infinite alternate;
}

@keyframes light-drift {
  from { transform: translate3d(-5%, 0, 0); opacity: 0.75; }
  to { transform: translate3d(6%, -3%, 0); opacity: 1; }
}

.login-brand__inner {
  position: relative;
  z-index: 1;
  max-width: 420px;
  padding: var(--sp-10);
}

.login-brand__logo {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 56px;
  height: 56px;
  margin-bottom: var(--sp-5);
  border-radius: var(--r-lg);
  background: linear-gradient(145deg, var(--c-accent-400), var(--c-accent-600));
  color: #17212f;
  font-size: 28px;
  font-weight: var(--fw-bold);
  box-shadow: var(--glow-accent);
}

.login-brand h1 {
  font-size: var(--fs-3xl);
  font-weight: var(--fw-bold);
  color: #fff;
  letter-spacing: -0.02em;
  margin-bottom: var(--sp-2);
}

.login-brand__slogan {
  font-size: var(--fs-sm);
  color: rgba(255, 255, 255, 0.55);
  letter-spacing: var(--ls-wider);
  text-transform: uppercase;
}

.login-brand__features {
  margin: var(--sp-8) 0 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: var(--sp-3);
}

.login-brand__features li {
  display: flex;
  align-items: center;
  gap: var(--sp-3);
  font-size: var(--fs-sm);
  color: rgba(255, 255, 255, 0.78);
}

.login-brand__features .el-icon {
  color: var(--c-accent-400);
  font-size: 17px;
}

.login-brand__foot {
  margin-top: var(--sp-10);
  font-size: var(--fs-xs);
  color: rgba(255, 255, 255, 0.4);
  letter-spacing: var(--ls-wide);
}

/* ------------------------------------------------------------- 表单区 */
.login-form {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: var(--sp-6);
  overflow-y: auto;
}

.login-theme {
  position: absolute;
  top: var(--sp-4);
  right: var(--sp-4);
}

.login-form__box {
  width: 100%;
  max-width: 400px;
  padding: var(--sp-8) var(--sp-6);
  /* 表单收进一张受光的纸：顶边亮、影有色，直接摊在页面底上会显得没着落 */
  border: 1px solid var(--c-border-light);
  border-radius: var(--r-xl);
  background: var(--surface-sheen), var(--bg-surface);
  box-shadow: var(--sh-lg), var(--rim-lit);
}

.login-form__head {
  display: flex;
  align-items: center;
  gap: var(--sp-3);
  margin-bottom: var(--sp-6);
}

.login-form__logo {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 46px;
  height: 46px;
  flex-shrink: 0;
  border-radius: var(--r-md);
  background: linear-gradient(145deg, var(--c-brand-400), var(--c-brand-700));
  color: #fff;
  font-size: 22px;
  font-weight: var(--fw-bold);
  box-shadow: var(--glow-brand);
}

.login-form__head h2 {
  font-size: var(--fs-xl);
  color: var(--c-ink-900);
  margin-bottom: 2px;
}

.login-form__head p {
  margin: 0;
  font-size: var(--fs-xs);
  color: var(--c-ink-400);
}

.login-submit {
  width: 100%;
  margin-top: var(--sp-2);
  letter-spacing: var(--ls-wider);
}

.pw-toggle {
  cursor: pointer;
  color: var(--c-ink-400);
  transition: color var(--dur-fast) var(--ease-out);
}

.pw-toggle:hover {
  color: var(--c-brand-600);
}

/* --------------------------------------------------------- 演示账号 */
.login-demo {
  margin-top: var(--sp-6);
  padding-top: var(--sp-4);
  border-top: 1px dashed var(--c-border);
}

.login-demo__title {
  display: flex;
  align-items: center;
  gap: var(--sp-1);
  margin-bottom: var(--sp-3);
  font-size: var(--fs-xs);
  color: var(--c-ink-400);
}

.login-demo__list {
  display: flex;
  flex-direction: column;
  gap: var(--sp-2);
}

.login-demo__item {
  display: flex;
  align-items: center;
  gap: var(--sp-3);
  width: 100%;
  padding: var(--sp-2) var(--sp-3);
  border: 1px solid var(--c-border-light);
  border-radius: var(--r-md);
  background: var(--bg-surface-alt);
  cursor: pointer;
  text-align: left;
  transition:
    background var(--dur-fast) var(--ease-out),
    border-color var(--dur-fast) var(--ease-out),
    transform var(--dur-fast) var(--ease-out);
}

.login-demo__item:hover {
  /* 光从左边照进来的一小片，比整块翻成品牌色轻 */
  background: linear-gradient(100deg, var(--c-brand-50) 0%, transparent 76%), var(--bg-surface);
  border-color: var(--c-brand-200);
  box-shadow: var(--sh-xs), var(--rim-lit);
  transform: translateX(2px);
}

.login-demo__no {
  font-family: var(--font-num);
  font-size: var(--fs-xs);
  font-weight: var(--fw-semibold);
  color: var(--c-brand-600);
  min-width: 52px;
}

.login-demo__name {
  font-size: var(--fs-sm);
  font-weight: var(--fw-medium);
  color: var(--c-ink-800);
}

.login-demo__role {
  font-size: var(--fs-xs);
  color: var(--c-ink-400);
  margin-left: auto;
}

.login-demo__tip {
  margin: var(--sp-3) 0 0;
  font-size: var(--fs-xs);
  color: var(--c-ink-300);
}

/* --------------------------------------------------------------- 响应式 */
@media (max-width: 991px) {
  .login-page {
    grid-template-columns: 1fr;
  }

  .login-brand {
    display: none;
  }
}

@media (max-width: 767px) {
  .login-form {
    align-items: flex-start;
    padding: var(--sp-6) var(--sp-3) var(--sp-4);
  }

  /* 手机上卡片自己就是页边距，内部留白收回一档 */
  .login-form__box {
    padding: var(--sp-6) var(--sp-4);
  }

  .login-demo__role {
    display: none;
  }
}
</style>
