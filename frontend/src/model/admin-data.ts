import type { AdminRow, AdminViewData } from './admin.ts'

/**
 * 管理目录接口中的部门安全摘要。
 */
export interface AdminDepartment {
  /**
   * 内部 ID。
   */
  id: string
  /**
   * 部门编码。
   */
  code: string
  /**
   * 部门名称。
   */
  name: string
  /**
   * 上级部门名称。
   */
  parentName?: string | null
  /**
   * 负责人姓名。
   */
  managerName?: string | null
  /**
   * 可信数据来源。
   */
  source: string
}

/**
 * 管理目录接口中的人员安全摘要。
 */
export interface AdminPerson {
  /**
   * 内部 ID。
   */
  id: string
  /**
   * 工号。
   */
  employeeNumber: string
  /**
   * 姓名。
   */
  name: string
  /**
   * 所属部门名称。
   */
  departments: string[]
  /**
   * 岗位。
   */
  position?: string | null
  /**
   * 在职状态。
   */
  isActive: boolean
}

/**
 * 组织目录的管理端响应。
 */
export interface AdminDirectory {
  /**
   * 部门。
   */
  departments: AdminDepartment[]
  /**
   * 人员。
   */
  people: AdminPerson[]
}

/**
 * 不含任何密钥或出站地址的应用摘要。
 */
export interface AdminClientApp {
  /**
   * 应用编码。
   */
  code: string
  /**
   * 应用名称。
   */
  name: string
  /**
   * 已授权流程编码。
   */
  allowedFlowCodes: string[]
  /**
   * 启用状态。
   */
  isEnabled: boolean
}

/**
 * 把受保护目录响应映射到部门、人员列表已有的安全列。
 * @param viewKey 当前管理页面编码。
 * @param directory 服务端目录。
 * @returns 只含列定义允许字段的页面数据。
 */
export function directoryViewData(viewKey: string, directory: AdminDirectory): AdminViewData {
  if (viewKey === 'org/departments') {
    return {
      metrics: [],
      rows: directory.departments.map((item): AdminRow => ({
        id: item.id,
        cells: {
          code: item.code, name: item.name, parent: item.parentName ?? '—',
          manager: item.managerName ?? '—', source: item.source,
        },
      })),
    }
  }
  if (viewKey === 'org/people') {
    return {
      metrics: [],
      rows: directory.people.map((item): AdminRow => ({
        id: item.id,
        cells: {
          name: item.name, employeeNo: item.employeeNumber,
          departments: item.departments.join('、') || '—', position: item.position ?? '—',
          status: item.isActive ? '在职' : '离职',
        },
      })),
    }
  }
  throw new Error('不支持的组织管理页面。')
}

/**
 * 把接入应用安全摘要映射为列表，不访问密钥字段。
 * @param apps 管理员已授权的应用摘要。
 * @returns 页面可展示的安全行。
 */
export function clientAppsViewData(apps: readonly AdminClientApp[]): AdminViewData {
  return {
    metrics: [],
    rows: apps.map((item): AdminRow => ({
      id: item.code,
      cells: {
        name: item.name, code: item.code, flows: item.allowedFlowCodes.join('、') || '—',
        status: item.isEnabled ? '启用' : '停用', updatedAt: '—',
      },
    })),
  }
}
