import { nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { resolveRoute } from '../model/routes.ts'

/**
 * 提供 hash 导航、浏览器历史、标题和页面焦点管理。
 * @returns 当前路由的响应式引用。
 */
export function useRouteNavigation() {
  const route = ref(resolveRoute(location.hash))
  const positions = new Map<string, number>()
  const previousRestoration = history.scrollRestoration
  let generation = 0
  let entryKey = ''

  /**
   * 为每个历史条目分配独立标识，重复访问相同地址也能分别恢复位置。
   * @returns 当前历史条目的标识。
   */
  function ensureEntryKey(): string {
    const state = history.state
    if (typeof state?.approvalNavigationKey === 'string') return state.approvalNavigationKey
    const key = crypto.randomUUID()
    history.replaceState({ ...(state && typeof state === 'object' ? state : {}), approvalNavigationKey: key }, '', location.href)
    return key
  }

  /**
   * 更新页面焦点并恢复历史位置。
   * @param restore 是否恢复该地址之前的滚动位置。
   * @returns 完成页面更新的异步结果。
   */
  async function update(restore: boolean): Promise<void> {
    positions.set(entryKey, window.scrollY)
    entryKey = ensureEntryKey()
    route.value = resolveRoute(location.hash)
    document.title = `${route.value.title} · 审批服务`
    const current = ++generation
    await nextTick()
    if (current !== generation) return
    document.querySelector<HTMLElement>('#page-title')?.focus({ preventScroll: true })
    window.scrollTo(0, restore ? positions.get(entryKey) ?? 0 : 0)
  }

  /**
   * 响应历史和 hash 变化，避免两个事件重复渲染。
   */
  function onHistory(): void {
    if (route.value.hash !== location.hash || entryKey !== history.state?.approvalNavigationKey) void update(true)
  }

  /**
   * 接管普通 hash 链接，保留新标签页和辅助键行为。
   * @param event 点击事件。
   */
  function onLink(event: MouseEvent): void {
    if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return
    const anchor = event.target instanceof Element ? event.target.closest<HTMLAnchorElement>('a[href^="#/"]') : null
    if (!anchor || anchor.target || anchor.hasAttribute('download')) return
    event.preventDefault()
    const hash = anchor.getAttribute('href')!
    if (hash === route.value.hash) return
    history.pushState(null, '', hash)
    void update(false)
  }

  onMounted(() => {
    entryKey = ensureEntryKey()
    history.scrollRestoration = 'manual'
    document.title = `${route.value.title} · 审批服务`
    window.addEventListener('popstate', onHistory)
    window.addEventListener('hashchange', onHistory)
    document.addEventListener('click', onLink)
  })
  onBeforeUnmount(() => {
    generation++
    history.scrollRestoration = previousRestoration
    window.removeEventListener('popstate', onHistory)
    window.removeEventListener('hashchange', onHistory)
    document.removeEventListener('click', onLink)
  })
  return route
}
