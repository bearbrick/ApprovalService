import { mobileActionLabels, mobileCenterSection, mobileDetailHash, shouldRefreshOnPull } from '../src/model/mobile.ts'
import { resolveRoute } from '../src/model/routes.ts'

/**
 * 验证手机路由、只读适配边界与触摸刷新判定。
 * @param check 统一检查记录器。
 */
export function runMobileChecks(check: (name: string, value: boolean) => void): void {
  check('手机的待办、已办、抄送复用电脑审批中心分组，“我的”不查询审批记录',
    ['todo', 'done', 'cc'].every(value => mobileCenterSection(value) === value) && mobileCenterSection('me') === null)
  check('手机详情地址安全编码审批标识且可以直接解析',
    mobileDetailHash('A 01') === '#/m/approvals/A%2001' && resolveRoute(mobileDetailHash('A 01')).resourceId === 'A 01')
  check('下拉刷新只在页面顶部、非输入控件且超过阈值时触发',
    shouldRefreshOnPull(10, 82, 0, false) && !shouldRefreshOnPull(10, 81, 0, false)
    && !shouldRefreshOnPull(10, 120, 2, false) && !shouldRefreshOnPull(10, 120, 0, true))
  check('手机审批操作名称齐全且不暗含可写接口',
    mobileActionLabels.approve === '同意' && mobileActionLabels.undoApproval === '撤销同意' && mobileActionLabels.supplement === '要求补充')
}
