import { loginAdmin, logoutAdmin } from '../src/api/admin-session.ts'
import { deleteFlowSimulation, getFlowSimulation, startFlowSimulation, stepFlowSimulation } from '../src/api/flow-simulation.ts'
import { buildSimulationInput, simulationExampleData, simulationManualNodes } from '../src/designer/workbench/simulation-model.ts'
import { flowTemplates } from '../src/designer/workbench/workbench-model.ts'

/**
 * 验证正式草稿的假输入构造和带 JWT 的内存模拟往返。
 * @param check 统一场景断言。
 * @returns 所有异步场景完成。
 */
export async function runSimulationChecks(check: (name: string, value: boolean) => void): Promise<void> {
  const definition = flowTemplates[0]!.definition
  const manualNodes = simulationManualNodes(definition)
  const peopleText = Object.fromEntries(manualNodes.map(node => [node.id, 'tester-a, tester-b, tester-a']))
  const input = buildSimulationInput(definition, simulationExampleData(definition), ' fake-initiator ', peopleText)
  check('测试模式从十类正式草稿生成假数据并去重每节点人员',
    manualNodes.length > 0 && input.initiatorId === 'fake-initiator'
    && manualNodes.every(node => input.fakeAssignees[node.id]?.join(',') === 'tester-a,tester-b'))
  let invalid = false
  try { buildSimulationInput(definition, '[]', 'fake-initiator', peopleText) } catch { invalid = true }
  check('测试模式拒绝数组业务数据', invalid)
  invalid = false
  try { buildSimulationInput(definition, '{}', 'fake-initiator', {}) } catch { invalid = true }
  check('测试模式拒绝缺失的假处理人', invalid)

  const originalFetch = globalThis.fetch
  const calls: { path: string; method: string; body: unknown }[] = []
  globalThis.fetch = async (resource, init) => {
    const path = String(resource)
    const method = init?.method ?? 'GET'
    const body: unknown = typeof init?.body === 'string' ? JSON.parse(init.body) : null
    calls.push({ path, method, body })
    if (path === '/api/v1/identity/login') return Response.json({
      personId: 'manager', accessToken: 'simulation-token', accessExpiresAt: '2099-01-01T00:00:00Z',
      refreshToken: 'simulation-refresh', refreshExpiresAt: '2099-02-01T00:00:00Z', mustChangePassword: false,
    })
    if (new Headers(init?.headers).get('Authorization') !== 'Bearer simulation-token') {
      throw new Error('模拟接口必须使用本地流程管理员 JWT。')
    }
    if (path === '/api/v1/admin/flows' && method === 'GET') return Response.json([])
    if (path === '/api/v1/admin/flows/simulations' && method === 'POST') return Response.json({
      id: 'memory-1', flowCode: definition.code, expiresAt: '2099-01-01T00:30:00Z', status: 'running',
      tasks: [{ id: 'task-1', nodeId: manualNodes[0]!.id, assigneeId: 'tester-a',
        status: 'pending', completionKind: 'approve' }], waits: [], events: [],
    })
    if (path === '/api/v1/admin/flows/simulations/memory-1' && method === 'GET') return Response.json({
      id: 'memory-1', flowCode: definition.code, expiresAt: '2099-01-01T00:30:00Z', status: 'running',
      tasks: [], waits: [], events: [],
    })
    if (path === '/api/v1/admin/flows/simulations/memory-1/steps' && method === 'POST') return Response.json({
      id: 'memory-1', flowCode: definition.code, expiresAt: '2099-01-01T00:30:00Z', status: 'completed',
      tasks: [], waits: [], events: [{ kind: 'approved', nodeId: manualNodes[0]!.id }],
    })
    if (path === '/api/v1/admin/flows/simulations/memory-1' && method === 'DELETE') {
      return new Response(null, { status: 204 })
    }
    throw new Error(`未登记的模拟请求：${method} ${path}`)
  }
  try {
    await loginAdmin('manager', 'test-password', 'flows')
    const created = await startFlowSimulation(input)
    const refreshed = await getFlowSimulation(created.id)
    const stepped = await stepFlowSimulation(created.id, { kind: created.tasks[0]!.completionKind,
      taskId: created.tasks[0]!.id, actorId: created.tasks[0]!.assigneeId })
    await deleteFlowSimulation(created.id)
    check('测试模式只经流程管理 JWT 发送假草稿并逐步处理内存会话',
      created.tasks.length === 1 && refreshed.id === 'memory-1' && stepped.status === 'completed'
      && calls.some(call => call.path.endsWith('/steps') && call.method === 'POST')
      && calls.some(call => call.path.endsWith('/memory-1') && call.method === 'DELETE')
      && !calls.some(call => call.path.includes('/instances') || call.path.includes('callback')))
  } finally {
    logoutAdmin()
    globalThis.fetch = originalFetch
  }
}
