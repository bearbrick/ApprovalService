import http from 'k6/http'
import { check } from 'k6'
import { Trend } from 'k6/metrics'

/** 发起接口耗时，单位毫秒。 */
const startDuration = new Trend('approval_start_duration', true)
/** 人工审批接口耗时，单位毫秒。 */
const approveDuration = new Trend('approval_approve_duration', true)
/** 待办数量接口耗时，单位毫秒。 */
const countDuration = new Trend('approval_count_duration', true)

/**
 * 压测参数；目标流程必须是无同步业务钩子的单人审批流程。
 */
export const options = {
  vus: Number(__ENV.APPROVAL_VUS || 10),
  duration: __ENV.APPROVAL_DURATION || '1m',
  thresholds: {
    approval_start_duration: ['p(95)<300'],
    approval_approve_duration: ['p(95)<300'],
    approval_count_duration: ['p(95)<50'],
    checks: ['rate>0.99'],
  },
}

/**
 * 生成不输出 API Key 的公共请求头。
 * @param {string} requestId 本次命令的稳定请求 ID。
 * @returns {object} k6 请求头。
 */
function headers(requestId) {
  return {
    'Content-Type': 'application/json',
    'X-Api-Key': __ENV.APPROVAL_API_KEY,
    'X-Request-Id': requestId,
  }
}

/**
 * 每次先发起一张唯一业务单，再审批该单，最后读取同一人的待办数量。
 * @returns {void} 完成一次三接口场景。
 */
export default function () {
  const base = (__ENV.APPROVAL_BASE_URL || '').replace(/\/$/, '')
  const seed = `${__VU}-${__ITER}-${Date.now()}-${Math.random().toString(16).slice(2)}`
  const body = {
    flowCode: __ENV.APPROVAL_FLOW_CODE,
    businessKey: `LOAD-${seed}`,
    title: `压测单 ${seed}`,
    initiator: __ENV.APPROVAL_INITIATOR,
    data: JSON.parse(__ENV.APPROVAL_DATA_JSON || '{}'),
  }

  const started = http.post(`${base}/api/v1/instances`, JSON.stringify(body),
    { headers: headers(`start-${seed}`), tags: { operation: 'start' } })
  startDuration.add(started.timings.duration)
  if (!check(started, { '发起成功': response => response.status === 200 })) return
  const instance = started.json()
  const pending = (instance.tasks || []).find(task => task.status === 'pending')
  if (!check(instance, { '发起后出现待办': () => Boolean(pending) })) return

  const acted = http.post(`${base}/api/v1/tasks/${encodeURIComponent(pending.id)}/actions`,
    JSON.stringify({ user: __ENV.APPROVAL_APPROVER, action: 'approve', opinion: '压测同意' }),
    { headers: headers(`approve-${seed}`), tags: { operation: 'approve' } })
  approveDuration.add(acted.timings.duration)
  check(acted, { '审批成功': response => response.status === 200 })

  const counted = http.get(`${base}/api/v1/tasks/count?user=${encodeURIComponent(__ENV.APPROVAL_APPROVER)}`,
    { headers: headers(`count-${seed}`), tags: { operation: 'count' } })
  countDuration.add(counted.timings.duration)
  check(counted, { '待办数量成功': response => response.status === 200 })
}
