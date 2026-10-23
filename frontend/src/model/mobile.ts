import type { CenterAction, CenterSection } from './center.ts'

/**
 * 手机列表对应的审批中心只读分组。
 * @param section 手机路由的分组标识。
 * @returns 受支持的审批中心分组；“我的”不读取审批列表。
 */
export function mobileCenterSection(section: string): CenterSection | null {
  return section === 'todo' || section === 'done' || section === 'cc' ? section : null
}

/**
 * 为一张已经授权的审批摘要生成手机详情地址。
 * @param id 审批单标识。
 * @returns 编码后的手机 hash 地址。
 */
export function mobileDetailHash(id: string): string {
  return `#/m/approvals/${encodeURIComponent(id)}`
}

/**
 * 是否把下拉手势解释为列表刷新；输入元素内的滑动不触发。
 * @param startY 手势起点纵坐标。
 * @param endY 手势终点纵坐标。
 * @param scrollTop 页面开始触摸时的滚动位置。
 * @param withinInput 是否从可编辑控件开始。
 * @returns 到达顶部且下拉超过阈值时为真。
 */
export function shouldRefreshOnPull(startY: number, endY: number, scrollTop: number, withinInput: boolean): boolean {
  return !withinInput && scrollTop <= 0 && endY - startY >= 72
}

/**
 * 已声明可用但尚不能提交的审批操作标签。
 */
export const mobileActionLabels: Readonly<Record<CenterAction, string>> = {
  approve: '同意', reject: '拒绝', return: '退回', transfer: '转交', addSign: '加签', removeSign: '减签',
  consult: '征询意见', supplement: '要求补充', copy: '抄送', undoApproval: '撤销同意', comment: '评论', remind: '催办',
}
