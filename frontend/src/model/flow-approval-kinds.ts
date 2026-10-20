/**
 * 后端 FlowApprovalKinds 的前端镜像；值用于设计器配置和流程 JSON。
 */

/**
 * 审批人来源。
 */
export const flowAssigneeSources = {
  /**
   * 指定成员。
   */
  Member: 'member',
  /**
   * 角色。
   */
  Role: 'role',
  /**
   * 岗位。
   */
  Position: 'position',
  /**
   * 职级。
   */
  Grade: 'grade',
  /**
   * 直属上级。
   */
  Manager: 'manager',
  /**
   * 连续多级上级。
   */
  ManagerChain: 'managerChain',
  /**
   * 部门负责人。
   */
  DepartmentHead: 'departmentHead',
  /**
   * 业务数据中的部门负责人。
   */
  DataDepartmentHead: 'dataDepartmentHead',
  /**
   * 分管领导。
   */
  DivisionLeader: 'divisionLeader',
  /**
   * 业务数据中的人员。
   */
  DataPerson: 'dataPerson',
  /**
   * 审批矩阵。
   */
  Matrix: 'matrix',
  /**
   * 业务系统接口。
   */
  BusinessApi: 'businessApi',
  /**
   * 上一步指定。
   */
  PreviousSelection: 'previousSelection',
  /**
   * 发起人自选。
   */
  InitiatorSelection: 'initiatorSelection',
  /**
   * 发起人本人。
   */
  Initiator: 'initiator',
} as const;

/**
 * 多人审批方式。
 */
export const flowApprovalModes = {
  /**
   * 依次审批。
   */
  Sequential: 'sequential',
  /**
   * 会签。
   */
  All: 'all',
  /**
   * 或签。
   */
  Any: 'any',
  /**
   * 票签。
   */
  Vote: 'vote',
} as const;

/**
 * 会签拒绝后的处理。
 */
export const flowRejectBehaviors = {
  /**
   * 立即拒绝。
   */
  Immediate: 'immediate',
  /**
   * 等全部人员处理。
   */
  WaitAll: 'waitAll',
} as const;

/**
 * 找不到审批人时的处理。
 */
export const flowMissingAssigneeBehaviors = {
  /**
   * 阻止发起或进入节点。
   */
  Block: 'block',
  /**
   * 自动通过。
   */
  Approve: 'approve',
  /**
   * 转交管理员。
   */
  Administrator: 'administrator',
  /**
   * 转交指定人员。
   */
  Specified: 'specified',
} as const;

/**
 * 审批人即发起人时的处理。
 */
export const flowSelfApprovalBehaviors = {
  /**
   * 跳过本人。
   */
  Skip: 'skip',
  /**
   * 本人审批。
   */
  Self: 'self',
  /**
   * 转交直属上级。
   */
  Manager: 'manager',
} as const;

/**
 * 同一人重复出现时的处理。
 */
export const flowDuplicateAssigneeBehaviors = {
  /**
   * 相邻出现只审一次。
   */
  AdjacentOnce: 'adjacentOnce',
  /**
   * 全流程只审一次。
   */
  Once: 'once',
  /**
   * 每次都审。
   */
  EveryTime: 'everyTime',
} as const;

/**
 * 审批人离职或停用时的处理。
 */
export const flowInactiveAssigneeBehaviors = {
  /**
   * 转交直属上级。
   */
  Manager: 'manager',
  /**
   * 转交管理员。
   */
  Administrator: 'administrator',
} as const;
