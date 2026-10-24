import { adminAccessToken, canReadAdminSection, loginAdmin, logoutAdmin } from '../src/api/admin-session.ts'
import { createManagedFlow, exportManagedFlow, getManagedFlow, importManagedFlow, listManagedFlows,
  previewManagedImport, publishManagedFlow, saveManagedFlow, setManagedFlowEnabled } from '../src/api/admin-flow-management.ts'
import { createEmptyFlow } from '../src/designer/workbench/workbench-model.ts'

/**
 * 用假 HTTP 服务核对流程管理员会话、修订前提和文件传输契约。
 * @param check 统一的场景断言入口。
 * @returns 所有场景执行完成。
 */
export async function runFlowManagementChecks(check: (name: string, value: boolean) => void): Promise<void> {
  const originalFetch = globalThis.fetch
  const seen: { path: string; method: string; authorization: string | null; body: unknown }[] = []
  const definition = createEmptyFlow('FLOW_A')
  let revision = 2
  globalThis.fetch = async (input, init) => {
    const path = String(input)
    const method = init?.method ?? 'GET'
    const authorization = new Headers(init?.headers).get('Authorization')
    const body: unknown = typeof init?.body === 'string' ? JSON.parse(init.body) : null
    seen.push({ path, method, authorization, body })
    if (path === '/api/v1/identity/login') return Response.json({
      personId: 'flow-manager', accessToken: 'flow-token', accessExpiresAt: '2099-01-01T00:00:00Z',
      refreshToken: 'refresh-token', refreshExpiresAt: '2099-02-01T00:00:00Z', mustChangePassword: false,
    })
    if (path === '/api/v1/admin/session') return Response.json({ code: 'FORBIDDEN', message: '非组织管理员。' }, { status: 403 })
    if (authorization !== 'Bearer flow-token') throw new Error('流程请求必须带本地 JWT。')
    if (path === '/api/v1/admin/flows' && method === 'GET') return Response.json([{
      code: 'FLOW_A', name: '流程 A', isEnabled: true, draftRevision: revision,
    }])
    if (path === '/api/v1/admin/flows/FLOW_A' && method === 'GET') return Response.json({
      code: 'FLOW_A', name: '流程 A', isEnabled: true, draftRevision: revision, draft: definition,
      publishedVersionId: 'version-1', publishedVersion: 1,
    })
    if (path === '/api/v1/admin/flows' && method === 'POST') return Response.json({
      code: 'FLOW_A', name: '流程 A', isEnabled: true, draftRevision: 1, draft: definition,
    })
    if (path === '/api/v1/admin/flows/FLOW_A/draft') {
      if ((body as { expectedRevision: number }).expectedRevision !== revision) {
        return Response.json({ code: 'CONFLICT', message: '草稿已被修改。' }, { status: 409 })
      }
      revision++
      return Response.json({ code: 'FLOW_A', name: '流程 A', isEnabled: true,
        draftRevision: revision, draft: definition, publishedVersionId: 'version-1', publishedVersion: 1 })
    }
    if (path === '/api/v1/admin/flows/FLOW_A/publish') return Response.json({
      code: 'FLOW_A', name: '流程 A', isEnabled: true, draftRevision: revision,
      draft: definition, publishedVersionId: 'version-2', publishedVersion: 2,
    })
    if (path === '/api/v1/admin/flows/FLOW_A/enabled') return Response.json({
      code: 'FLOW_A', name: '流程 A', isEnabled: false, draftRevision: revision,
    })
    if (path === '/api/v1/admin/flows/import/preview') return Response.json({
      code: 'FLOW_A', name: '流程 A', nodeCount: 2, hasCodeConflict: true, canOverwrite: true,
      existingDraftRevision: revision, publishIssues: [{ message: '审批节点尚未设置', elementId: 'review' }],
    })
    if (path === '/api/v1/admin/flows/import') return Response.json({
      code: 'FLOW_A', name: '流程 A', isEnabled: true, draftRevision: revision + 1, draft: definition,
    })
    if (path.startsWith('/api/v1/admin/flows/FLOW_A/export?')) {
      return new Response('<definitions exporter="ApprovalService"/>',
        { headers: { 'Content-Type': 'application/xml' } })
    }
    throw new Error(`未登记的流程管理请求：${method} ${path}`)
  }
  try {
    const session = await loginAdmin('FM1', 'password', 'flows')
    check('部门流程管理员登录只核对流程范围且不能进入其他管理页', session.scope === 'flows'
      && !seen.some(item => item.path === '/api/v1/admin/session')
      && canReadAdminSection(session, 'flows') && !canReadAdminSection(session, 'org'))
    let blockedOtherScope = false
    try { await adminAccessToken('admin') } catch { blockedOtherScope = true }
    check('流程范围会话不能调用组织管理员接口', blockedOtherScope)
    const list = await listManagedFlows()
    const detail = await getManagedFlow('FLOW_A')
    check('管理目录与详情允许省略空属性且保留草稿修订和发布指针',
      list[0]?.group === null && list[0]?.draft === null && detail.draft?.code === 'FLOW_A'
      && detail.draftRevision === 2 && detail.publishedVersionId === 'version-1')
    await createManagedFlow(definition, { group: '采购', ownerDepartmentId: 'dept-1', administratorId: null }, '创建流程')
    check('新建流程提交完整定义、直属归属和审计原因',
      (seen.at(-1)?.body as { definition?: { code: string }; ownerDepartmentId?: string; reason?: string })
        .definition?.code === 'FLOW_A' && (seen.at(-1)?.body as { ownerDepartmentId?: string }).ownerDepartmentId === 'dept-1')
    const saved = await saveManagedFlow('FLOW_A', 2, definition,
      { group: '采购', ownerDepartmentId: 'dept-1', administratorId: null }, '调整节点')
    check('保存草稿以预期修订号 CAS 并读取服务端新修订',
      saved.draftRevision === 3 && seen.at(-1)?.method === 'PUT'
      && (seen.at(-1)?.body as { expectedRevision?: number }).expectedRevision === 2)
    let conflict = false
    try { await saveManagedFlow('FLOW_A', 2, definition,
      { group: null, ownerDepartmentId: null, administratorId: null }, '旧修订') }
    catch (cause) { conflict = cause instanceof Error && cause.message.includes('已被修改') }
    check('并发覆盖被服务端 409 拒绝且保留中文原因', conflict)
    const published = await publishManagedFlow('FLOW_A', 3, 'version-1', '发布第二版')
    check('发布同时提交草稿修订和当前发布版双重前提',
      published.publishedVersion === 2 && (seen.at(-1)?.body as { expectedRevision?: number }).expectedRevision === 3
      && (seen.at(-1)?.body as { expectedPublishedVersionId?: string }).expectedPublishedVersionId === 'version-1')
    await setManagedFlowEnabled(detail, false, '临时停用')
    check('停用需要预期旧状态且不改变发布版',
      (seen.at(-1)?.body as { expectedEnabled?: boolean; enabled?: boolean }).expectedEnabled === true
      && (seen.at(-1)?.body as { enabled?: boolean }).enabled === false)
    const preview = await previewManagedImport('<definitions/>')
    check('兼容文件先预检同编码冲突和当前可覆盖修订', preview.hasCodeConflict
      && preview.canOverwrite && preview.existingDraftRevision === 3 && preview.publishIssues[0]?.elementId === 'review')
    await importManagedFlow('<definitions/>',
      { group: null, ownerDepartmentId: null, administratorId: null }, '迁入流程', 'FLOW_A', 3)
    check('覆盖导入显式提交目标流程和预期修订',
      (seen.at(-1)?.body as { targetCode?: string; expectedRevision?: number }).targetCode === 'FLOW_A'
      && (seen.at(-1)?.body as { expectedRevision?: number }).expectedRevision === 3)
    const xml = await exportManagedFlow('FLOW_A', 'published', 2)
    check('发布版导出明确选择版本并保持 JWT 不落入 URL', xml.includes('<definitions')
      && seen.at(-1)?.path.endsWith('source=published&version=2') === true
      && !seen.at(-1)?.path.includes('flow-token'))
  } finally {
    logoutAdmin()
    globalThis.fetch = originalFetch
  }
}
