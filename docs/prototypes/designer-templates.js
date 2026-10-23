/**
 * 四套相互独立的自由画布流程模板，连线是唯一的路径与条件定义来源。
 * 这些定义仅用于原型评审，不调用审批接口，不替代生产审批引擎。
 */
const DesignerTemplates = {
  /**
   * 创建指定流程的独立草稿，并保留基础草稿中的通用展示设置。
   * @param {string} key 流程路由。
   * @param {object} base 基础草稿。
   * @returns {object} 带显式开始、结束和有向连线的流程草稿。
   */
  create(key, base) {
    const flowKey = ['purchase', 'expense', 'contract', 'quality'].includes(key) ? key : 'purchase';
    const state = structuredClone(base);
    const meta = {
      purchase: ['采购申请', 'PURCHASE_REQUEST', '采购管理', 2, '吴芳'],
      expense: ['费用报销', 'EXPENSE', '费用管理', 3, '赵敏'],
      contract: ['合同评审', 'CONTRACT_REVIEW', '采购管理', 1, '吴芳'],
      quality: ['质量异常处理', 'QUALITY_EXCEPTION', '质量管理', 1, '质量经理']
    }[flowKey];
    [state.name, state.code, state.category, state.version, state.owner] = meta;
    state.flowKey = flowKey;
    state.mode = 'canvas';
    state.schemaVersion = 2;
    state.publishedMode = 'canvas';
    delete state.linear;
    state.threshold = flowKey === 'expense' ? 20000 : 10000;
    state.settings = {
      company: '智云科技有限公司', printTitle: state.name + '审批单',
      empty: '不允许发起，提示原因', self: '自动跳过', repeat: '相邻的只审一次',
      disabled: '转交直属上级', return: '发起人、上一步及已处理节点',
      resubmit: '从头开始审批', withdraw: '审批结束前', reject: '立即结束',
      limit: '2 个工作日', before: '4 小时', reminder: '审批人和发起人',
      daily: '每天 09:00 邮件汇总', app: 'ERP 业务系统、示例业务系统',
      departments: '所有部门', roles: '不限', view: '发起人、参与人、抄送人',
      managers: state.owner, dataUpdate: '仅允许约定字段更新', updateFields: 'attachments, remark',
      ...(base.settings || {})
    };
    // 分别创建真实节点和连线，不把并行分支藏在单个审批节点的说明里。
    Object.assign(state, this[flowKey]());
    if (flowKey === 'quality') state.settings.updateFields = 'attachments, remark, rework';
    if (flowKey === 'expense') state.settings.withdraw = '第一个人处理前';
    if (flowKey === 'purchase') {
      state.settings.resubmit = '回到退回的节点';
      state.settings.startHook = '业务系统预算钩子：预算内余额不足时拒绝发起并提示差额';
    }
    state.history = [{ version: state.version, date: '2026-10-13 16:20', note: '自由画布版本：明确开始、结束、条件与分叉汇合结构。' }];
    state.publishedNodes = state.nodes.length;
    state.publishedFields = state.fields.length;
    state.scenarios = this.scenarios(flowKey);
    return state;
  },

  /**
   * 构造节点并填入适合审批场景的公共默认值。
   * @param {string} id 稳定节点标识。
   * @param {string} name 节点名称。
   * @param {string} type 节点类型。
   * @param {number} x 画布横坐标。
   * @param {number} y 画布纵坐标。
   * @param {object} extra 按节点覆盖的人员、规则和业务配置。
   * @returns {object} 独立节点配置。
   */
  node(id, name, type, x, y, extra = {}) {
    return {
      id, name, type, x, y, source: '角色', people: '', method: '依次审批',
      commentRequired: type === 'approval', signature: false,
      denied: type === 'approval' || type === 'handle' ? ['批量同意'] : [],
      ...extra
    };
  },

  /**
   * 创建业务系统发起时共同需要的数据字段。
   * @param {string} amountName 金额字段名称。
   * @param {string} purposeName 事由字段名称。
   * @returns {Array} 不引用其他模板的独立字段列表。
   */
  commonFields(amountName, purposeName) {
    return [
      { key: 'applicant', name: '申请人', type: '人员', required: true, summary: true },
      { key: 'department', name: '申请部门', type: '部门', required: true, summary: true },
      { key: 'amount', name: amountName, type: '计算字段', required: true, summary: true, min: 0.01, expression: 'SUM(items.amount)', description: '明细合计；路径演示使用业务系统传入的预计算金额。' },
      { key: 'purpose', name: purposeName, type: '文本', required: true, summary: true },
      { key: 'date', name: '申请日期', type: '日期', required: true },
      { key: 'attachments', name: '相关附件', type: '附件' },
      { key: 'remark', name: '补充说明', type: '文本' }
    ];
  },

  /**
   * 定义只供原型单字段条件使用的预置布尔结果，不规定正式接入字段。
   * @param {string} key 字段标识。
   * @param {string} name 显示名称。
   * @param {string} description 数据来源与计算含义。
   * @returns {object} 字段定义；JSON 场景值使用 true 或 false。
   */
  booleanField(key, name, description) {
    return { key, name, note: description + ' 这是原型辅助输入，正式字段与规则以契约任务为准。', type: '文本', valueType: 'boolean', required: true, options: ['true', 'false'], description };
  },

  /**
   * 采购申请：类型办理、三路包容专项审核、金额分档、票签会签与业务动作。
   * @returns {object} 采购节点、连线和数据字段。
   */
  purchase() {
    return {
      nodes: [
        this.node('start', '业务系统提交 · 预算钩子', 'start', 20, 250, { source: '发起人本人', people: 'ERP 采购系统 · 先校验预算余额', preSubmitHook: 'budgetCheck' }),
        this.node('department', '部门负责人审批', 'approval', 230, 250, { source: '部门负责人', people: '申请部门 · 第 1 级负责人', denied: [] }),
        this.node('typeGateway', '按采购类型确认', 'exclusive', 440, 250, { people: '生产物料、固定资产分别办理，其他跳过', gatewayRole: 'split' }),
        this.node('materialConfirm', '计划员确认需求', 'handle', 650, 40, { people: '马丽 · 计划员', writableFields: ['demandConfirmed'] }),
        this.node('assetConfirm', '资产管理员登记', 'handle', 650, 440, { people: '林峰 · 资产管理员', writableFields: ['assetNumber'] }),
        this.node('typeMerge', '类型确认汇合', 'exclusive', 860, 250, { people: '任一类型路线到达后继续', gatewayRole: 'join' }),
        this.node('specialSplit', '专项审核 · 包容分支', 'inclusive', 1070, 250, { people: '单一来源、预算外、五万元合同，命中的同时走', gatewayRole: 'split', pairedGatewayId: 'specialJoin' }),
        this.node('committee', '单一来源 · 采购委员会', 'approval', 1290, 40, { people: '吴芳、孙磊、张明、何静', method: '票签', ratio: 60, weights: '吴芳: 2, 孙磊: 1, 张明: 1, 何静: 1', signature: true, voteWeights: { 吴芳: 2, 孙磊: 1, 张明: 1, 何静: 1 }, voteDenominator: 5, voteRequiredWeight: 3 }),
        this.node('finance', '预算外 · 财务经理审核', 'approval', 1290, 250, { people: '何静 · 财务经理', signature: true }),
        this.node('contractReview', '五万元以上 · 合同评审', 'sub', 1290, 460, { people: 'CONTRACT_REVIEW', subFlow: 'CONTRACT_REVIEW', wait: true, inputMapping: { contractName: 'contractName', supplier: 'supplier', amount: 'amount', department: 'department', attachments: 'attachments', parentBusinessKey: 'businessKey' }, outputMapping: { contractConclusion: 'reviewConclusion' } }),
        this.node('specialJoin', '专项审核汇合', 'inclusiveJoin', 1070, 680, { people: '只等待本次命中的分支，零命中走默认路线', gatewayRole: 'join', pairedGatewayId: 'specialSplit' }),
        this.node('procurement', '采购经理审批', 'approval', 860, 680, { people: '吴芳 · 采购经理', denied: ['批量同意'], addSignHint: '允许前加签技术专家' }),
        this.node('amountGateway', '是否达到一万元', 'exclusive', 650, 680, { people: '一万元起追加分管领导', gatewayRole: 'split' }),
        this.node('purchaseLeader', '分管领导审批', 'approval', 440, 470, { source: '分管领导', people: '陈副总 · 分管采购与财务', signature: true }),
        this.node('seniorGateway', '是否超过十万元', 'exclusive', 230, 470, { people: '超过十万元追加总经理和财务总监会签', gatewayRole: 'split' }),
        this.node('management', '总经理、财务总监会签', 'approval', 20, 470, { people: '周总 · 总经理、冯伟 · 财务总监', method: '会签', signature: true, reject: '立即结束' }),
        this.node('amountMerge', '金额分档汇合', 'exclusive', 230, 900, { people: '对应金额路线全部完成后继续', gatewayRole: 'join' }),
        this.node('createOrder', '生成业务采购订单', 'auto', 440, 900, { people: 'ERP · 生成采购订单接口', action: 'createPurchaseOrder', target: 'https://erp.example.invalid/approval/actions/create-purchase-order', retry: '失败自动重试，最终交由管理员', idempotencyKey: 'instanceId + nodeId', outputMapping: { purchaseOrderNumber: 'orderNumber' } }),
        this.node('cc', '抄送采购部', 'cc', 650, 900, { source: '指定成员', people: '吴芳、郑涛 · 采购部' }),
        this.node('end', '采购审批完成', 'end', 860, 900, { people: '订单号及审批结果回调业务系统' })
      ],
      edges: [
        ['start', 'department', ''], ['department', 'typeGateway', ''],
        ['typeGateway', 'materialConfirm', 'purchaseType == "生产物料"'], ['typeGateway', 'assetConfirm', 'purchaseType == "固定资产"'], ['typeGateway', 'typeMerge', '其他情况'],
        ['materialConfirm', 'typeMerge', ''], ['assetConfirm', 'typeMerge', ''], ['typeMerge', 'specialSplit', ''],
        ['specialSplit', 'committee', 'singleSource == true'], ['specialSplit', 'finance', 'overBudget == true'], ['specialSplit', 'contractReview', 'amount >= 50000'], ['specialSplit', 'specialJoin', '其他情况'],
        ['committee', 'specialJoin', ''], ['finance', 'specialJoin', ''], ['contractReview', 'specialJoin', ''],
        ['specialJoin', 'procurement', ''], ['procurement', 'amountGateway', ''],
        ['amountGateway', 'purchaseLeader', 'amount >= 10000'], ['amountGateway', 'amountMerge', '其他情况'],
        ['purchaseLeader', 'seniorGateway', ''], ['seniorGateway', 'management', 'amount > 100000'], ['seniorGateway', 'amountMerge', '其他情况'],
        ['management', 'amountMerge', ''], ['amountMerge', 'createOrder', ''], ['createOrder', 'cc', ''], ['cc', 'end', '']
      ],
      fields: [
        ...this.commonFields('采购合计', '采购用途'),
        { key: 'purchaseType', name: '采购类型', type: '文本', required: true, options: ['生产物料', '固定资产', '办公用品', '服务'] },
        { key: 'supplier', name: '供应商', type: '文本', required: true, summary: true },
        { key: 'contractName', name: '采购合同名称', type: '文本', description: '五万元及以上进入合同子流程时提供。' },
        { key: 'project', name: '所属项目', type: '文本' },
        { key: 'expectedDeliveryDate', name: '期望交货日期', type: '日期', required: true },
        this.booleanField('singleSource', '是否单一来源', '业务系统根据供应商采购方式提供，原型用布尔值比较。'),
        this.booleanField('overBudget', '是否预算外采购', '业务系统将“是否预算内”取反得到；预算内余额不足由发起前钩子阻止。'),
        { key: 'items', name: '采购明细', type: '明细表', required: true },
        { key: 'demandConfirmed', name: '需求确认说明', type: '文本', source: '审批填写' },
        { key: 'assetNumber', name: '资产编号', type: '文本', source: '审批填写' },
        { key: 'contractConclusion', name: '合同评审结论', type: '文本', source: '审批填写' },
        { key: 'purchaseOrderNumber', name: '业务采购订单号', type: '文本', source: '业务动作返回' }
      ]
    };
  },

  /**
   * 费用报销：直属上级、三路包容会审、领导分档、财务核定复核与出纳办理。
   * @returns {object} 报销节点、连线和数据字段。
   */
  expense() {
    return {
      nodes: [
        this.node('start', '业务系统发起报销', 'start', 20, 250, { source: '发起人本人', people: '费用报销系统' }),
        this.node('supervisor', '直属上级审批', 'approval', 230, 250, { source: '直属上级', people: '报销人的直属上级 · 第 1 级', emptyPolicy: '自动通过', denied: [] }),
        this.node('reviewSplit', '费用会审 · 包容分支', 'inclusive', 440, 250, { people: '项目、跨部门、招待费条件命中的同时走', gatewayRole: 'split', pairedGatewayId: 'reviewJoin' }),
        this.node('projectReview', '项目经理会审', 'approval', 650, 40, { source: '业务系统接口', people: '费用系统 · 按 project 返回项目经理', resolver: 'projectManager' }),
        this.node('costReview', '承担部门负责人会审', 'approval', 650, 250, { source: '业务数据里的部门', people: 'costDepartment · 费用承担部门负责人' }),
        this.node('entertainmentReview', '招待费行政会审', 'approval', 650, 460, { people: '黄蕾 · 行政部负责人' }),
        this.node('reviewJoin', '费用会审汇合', 'inclusiveJoin', 860, 250, { people: '只等待本次实际命中的会审分支', gatewayRole: 'join', pairedGatewayId: 'reviewSplit' }),
        this.node('department', '发起部门负责人审批', 'approval', 1070, 250, { source: '部门负责人', people: '发起部门负责人 · 相邻重复人员自动通过', denied: [] }),
        this.node('amountGateway', '领导审批分档', 'exclusive', 1290, 250, { people: '优先判断总经理条件；其次五千元以上分管；其余跳过', gatewayRole: 'split', priority: '按连线顺序，首条满足的路线' }),
        this.node('seniorLeader', '大额 / 大额招待分管审批', 'approval', 1290, 470, { source: '分管领导', people: '发起部门分管领导', signature: true }),
        this.node('management', '总经理审批', 'approval', 1070, 470, { people: '周总', signature: true }),
        this.node('expenseLeader', '普通金额分管审批', 'approval', 1290, 690, { source: '分管领导', people: '发起部门分管领导' }),
        this.node('amountMerge', '领导审批汇合', 'exclusive', 1070, 900, { people: '金额对应的领导路线完成后继续', gatewayRole: 'join' }),
        this.node('finance', '财务票据审核 · 或签', 'approval', 860, 900, { people: '赵敏、钱丽 · 会计', method: '或签', signature: true, writableFields: ['approvedAmount'], denied: ['批量同意'], timeLimit: '2 个工作日' }),
        this.node('financeGateway', '是否需财务经理复核', 'exclusive', 650, 900, { people: '核定金额变化或申请合计超过两万元', gatewayRole: 'split' }),
        this.node('financeReview', '财务经理复核', 'approval', 440, 690, { people: '何静 · 财务经理', signature: true }),
        this.node('paymentMerge', '财务复核汇合', 'exclusive', 230, 900, { people: '核定金额已确认后进入付款办理', gatewayRole: 'join' }),
        this.node('payment', '出纳付款确认', 'handle', 20, 900, { people: '刘洋 · 出纳', writableFields: ['paymentDate', 'paymentReference'], commentRequired: true, calculation: '付款金额 = approvedAmount - loanOffset' }),
        this.node('cc', '抄送费用承担部门', 'cc', 20, 680, { source: '业务数据里的部门', people: 'costDepartment · 费用承担部门负责人' }),
        this.node('end', '报销审批完成', 'end', 230, 680, { people: '付款金额与审批结果回调业务系统' })
      ],
      edges: [
        ['start', 'supervisor', ''], ['supervisor', 'reviewSplit', ''],
        ['reviewSplit', 'projectReview', 'hasProject == true'], ['reviewSplit', 'costReview', 'crossDepartment == true'], ['reviewSplit', 'entertainmentReview', 'hasEntertainment == true'], ['reviewSplit', 'reviewJoin', '其他情况'],
        ['projectReview', 'reviewJoin', ''], ['costReview', 'reviewJoin', ''], ['entertainmentReview', 'reviewJoin', ''],
        ['reviewJoin', 'department', ''], ['department', 'amountGateway', ''],
        ['amountGateway', 'seniorLeader', 'needsGeneralManager == true'], ['amountGateway', 'expenseLeader', 'amount >= 5000'], ['amountGateway', 'amountMerge', '其他情况'],
        ['seniorLeader', 'management', ''], ['management', 'amountMerge', ''], ['expenseLeader', 'amountMerge', ''],
        ['amountMerge', 'finance', ''], ['finance', 'financeGateway', ''],
        ['financeGateway', 'financeReview', 'needsFinanceReview == true'], ['financeGateway', 'paymentMerge', '其他情况'],
        ['financeReview', 'paymentMerge', ''], ['paymentMerge', 'payment', ''], ['payment', 'cc', ''], ['cc', 'end', '']
      ],
      fields: [
        ...this.commonFields('报销合计', '报销事由'),
        { key: 'costDepartment', name: '费用承担部门', type: '部门', required: true },
        { key: 'project', name: '所属项目', type: '文本' },
        { key: 'loanOffset', name: '冲抵借款金额', type: '金额', min: 0 },
        this.booleanField('hasProject', '是否填写项目', '业务系统根据 project 是否为空得到，用于原型单字段判断。'),
        this.booleanField('crossDepartment', '是否跨部门承担', '业务系统比较 costDepartment 与发起部门得到。'),
        this.booleanField('hasEntertainment', '是否包含招待费', '业务系统扫描费用明细类型得到，命中时进入行政会审。'),
        this.booleanField('needsGeneralManager', '是否需总经理审批', '原型预置条件结果：合计 > 20000 或任一张招待费 > 2000；正式版由审批引擎计算。'),
        this.booleanField('needsFinanceReview', '是否需财务经理复核', '原型预置条件结果：核定金额与申请金额不同或合计 > 20000；正式版由审批引擎根据当前核定金额计算。'),
        { key: 'items', name: '费用明细', type: '明细表', required: true },
        { key: 'approvedAmount', name: '财务核定金额', type: '金额', source: '审批填写' },
        { key: 'paymentDate', name: '付款日期', type: '日期', source: '审批填写' },
        { key: 'paymentReference', name: '付款凭证号', type: '文本', source: '审批填写' },
        { key: 'paymentAmount', name: '付款金额', type: '计算字段', expression: 'approvedAmount - loanOffset' }
      ]
    };
  },

  /**
   * 合同评审：独立法务、财务审批节点并行执行，汇合后进入终审。
   * @returns {object} 合同节点、连线和数据字段。
   */
  contract() {
    return {
      nodes: [
        this.node('start', '主流程调用或业务提交', 'start', 20, 255, { source: '发起人本人', people: '采购主流程 / ERP 业务系统' }),
        this.node('department', '合同资料核对', 'approval', 230, 255, { people: '吴芳 · 采购经理' }),
        this.node('parallelSplit', '法务与财务并行', 'parallel', 440, 255, { people: '同时发出法务与财务评审任务', gatewayRole: 'split', pairedGatewayId: 'parallelJoin' }),
        this.node('management', '法务条款评审', 'approval', 650, 65, { people: '法务专员、法务负责人', method: '会签', signature: true, writableFields: ['legalOpinion'], reject: '立即结束' }),
        this.node('finance', '财务条款评审', 'approval', 650, 445, { people: '何静 · 财务经理', signature: true, writableFields: ['financialOpinion'] }),
        this.node('parallelJoin', '专业意见汇合', 'parallelJoin', 860, 255, { people: '两条分支全部完成后进入合同终审', gatewayRole: 'join', pairedGatewayId: 'parallelSplit' }),
        this.node('finalApproval', '合同终审', 'approval', 1070, 255, { people: '周总', signature: true, writableFields: ['reviewConclusion'] }),
        this.node('cc', '抄送合同经办人', 'cc', 1070, 575, { source: '指定成员', people: '吴芳 · 采购经理' }),
        this.node('end', '返回评审结论', 'end', 860, 575, { people: '作为子流程时恢复父流程，独立审批时回调业务系统' })
      ],
      edges: [
        ['start', 'department', ''], ['department', 'parallelSplit', ''],
        ['parallelSplit', 'management', ''], ['parallelSplit', 'finance', ''],
        ['management', 'parallelJoin', ''], ['finance', 'parallelJoin', ''],
        ['parallelJoin', 'finalApproval', ''], ['finalApproval', 'cc', ''], ['cc', 'end', '']
      ],
      fields: [
        { key: 'contractName', name: '合同名称', type: '文本', required: true, summary: true },
        { key: 'supplier', name: '签约单位', type: '文本', required: true, summary: true },
        { key: 'amount', name: '合同金额', type: '金额', required: true, summary: true, min: 0.01 },
        { key: 'department', name: '责任部门', type: '部门', required: true },
        { key: 'attachments', name: '合同正文与附件', type: '附件', required: true },
        { key: 'parentBusinessKey', name: '父流程业务单号', type: '文本' },
        { key: 'legalOpinion', name: '法务意见', type: '文本', source: '审批填写' },
        { key: 'financialOpinion', name: '财务意见', type: '文本', source: '审批填写' },
        { key: 'reviewConclusion', name: '合同终审结论', type: '文本', source: '审批填写' }
      ]
    };
  },

  /**
   * 质量异常：保留返工复检回路，合格后质量与生产共同确认并归档。
   * @returns {object} 质量节点、连线和数据字段。
   */
  quality() {
    return {
      nodes: [
        this.node('start', '业务系统提交异常单', 'start', 20, 300, { source: '发起人本人', people: '质量管理系统' }),
        this.node('department', '质检员复核', 'approval', 230, 300, { people: '质量专员', denied: ['批量同意'], commentRequired: true }),
        this.node('management', '是否需要返工', 'exclusive', 440, 300, { people: '按业务系统提供的复检结果分流', gatewayRole: 'split' }),
        this.node('finance', '生产返工办理', 'handle', 440, 65, { people: '生产负责人', commentRequired: true, writableFields: ['reworkComment'], loopHint: '业务系统补充复检结果后，再由质检员复核' }),
        this.node('parallelSplit', '质量与生产并行确认', 'parallel', 650, 300, { people: '质量、生产两方同时确认处理结果', gatewayRole: 'split', pairedGatewayId: 'parallelJoin' }),
        this.node('cc', '质量经理审批', 'approval', 860, 100, { people: '质量经理', signature: true, writableFields: ['qualityConclusion'] }),
        this.node('production', '生产经理审批', 'approval', 860, 500, { people: '生产经理', signature: true, writableFields: ['productionConclusion'] }),
        this.node('parallelJoin', '双部门意见汇合', 'parallelJoin', 1070, 300, { people: '等待质量、生产两条分支全部完成', gatewayRole: 'join', pairedGatewayId: 'parallelSplit' }),
        this.node('archive', '业务系统归档结果', 'auto', 1070, 720, { people: '质量系统 · 归档接口', action: 'archiveQualityResult', target: 'https://quality.example.invalid/approval/actions/archive', idempotencyKey: 'instanceId + nodeId', retry: '重试失败转管理员，不跳过归档' }),
        this.node('qualityNotice', '抄送相关责任人', 'cc', 860, 720, { source: '指定成员', people: '申请人、质量专员、生产负责人' }),
        this.node('end', '质量异常处理完成', 'end', 650, 720, { people: '返回处理结果' })
      ],
      edges: [
        ['start', 'department', ''], ['department', 'management', ''],
        ['management', 'finance', 'rework == "需要返工"'], ['finance', 'department', '返工后复检'],
        ['management', 'parallelSplit', '其他情况'], ['parallelSplit', 'cc', ''],
        ['parallelSplit', 'production', ''], ['cc', 'parallelJoin', ''],
        ['production', 'parallelJoin', ''], ['parallelJoin', 'archive', ''],
        ['archive', 'qualityNotice', ''], ['qualityNotice', 'end', '']
      ],
      fields: [
        { key: 'defect', name: '异常描述', type: '文本', required: true, summary: true },
        { key: 'batch', name: '产品批次', type: '文本', required: true, summary: true },
        { key: 'department', name: '责任部门', type: '部门', required: true, summary: true },
        { key: 'rework', name: '复检结果', type: '文本', required: true, options: ['需要返工', '无需返工'], description: '由业务系统提供；返工后在来源系统更新，不在审批中心修改业务数据。' },
        { key: 'attachments', name: '检验报告', type: '附件', required: true },
        { key: 'reworkComment', name: '返工处理说明', type: '文本', source: '审批填写' },
        { key: 'qualityConclusion', name: '质量确认意见', type: '文本', source: '审批填写' },
        { key: 'productionConclusion', name: '生产确认意见', type: '文本', source: '审批填写' }
      ]
    };
  },

  /**
   * 提供常用业务场景及可核对的路径预期，不在模板层实现规则执行。
   * @param {string} key 流程路由。
   * @returns {Array} 场景输入和预期节点。
   */
  scenarios(key) {
    const purchaseInput = { amount: 8000, purchaseType: '办公用品', singleSource: false, overBudget: false, department: '采购部', supplier: '示例设备供应商' };
    const expenseInput = { amount: 1334.5, approvedAmount: 1334.5, loanOffset: 0, hasProject: false, crossDepartment: false, hasEntertainment: false, needsGeneralManager: false, needsFinanceReview: false, applicant: '王强', department: '生产一部', costDepartment: '生产一部' };
    const samples = {
      purchase: [
        this.scenario('purchase-small', '小额采购 · 专项零命中', purchaseInput, ['department', 'specialJoin', 'procurement', 'createOrder', 'end'], ['committee', 'finance', 'contractReview', 'purchaseLeader', 'management'], '包容分支零命中走兜底直达汇合；小额不会启动合同子流程。'),
        this.scenario('purchase-single', '单一来源 · 仅命中票签', { ...purchaseInput, singleSource: true }, ['committee', 'specialJoin', 'end'], ['finance', 'contractReview'], '60% 门槛：总权重 5，吴芳权重 2，至少同意权重 3；此处仅走票签节点，不模拟计票结果。'),
        this.scenario('purchase-budget', '预算外 · 仅命中财务', { ...purchaseInput, overBudget: true }, ['finance', 'specialJoin', 'end'], ['committee', 'contractReview'], '只等待本次命中的财务经理分支。'),
        this.scenario('purchase-contract-boundary', '五万元边界 · 仅合同子流程', { ...purchaseInput, amount: 50000, contractName: '设备采购合同' }, ['contractReview', 'specialJoin', 'purchaseLeader', 'end'], ['committee', 'finance', 'management'], '金额等于 50000 已进入合同评审，等它结束后继续。'),
        this.scenario('purchase-contract-below', '合同阈值下方 · 49,999 元', { ...purchaseInput, amount: 49999 }, ['specialJoin', 'purchaseLeader', 'end'], ['committee', 'finance', 'contractReview'], '验证合同子流程不会因默认示例被固定执行。'),
        this.scenario('purchase-all', '三路专项审核 · 80,000 元', { ...purchaseInput, amount: 80000, singleSource: true, overBudget: true, contractName: '生产设备合同' }, ['committee', 'finance', 'contractReview', 'specialJoin', 'purchaseLeader', 'end'], ['management'], '票签、预算外财务、合同子流程同时命中；汇合只推进一次。'),
        this.scenario('purchase-large', '十二万元 · 三路专项与高额会签', { ...purchaseInput, amount: 120000, singleSource: true, overBudget: true, contractName: '大型设备合同' }, ['committee', 'finance', 'contractReview', 'procurement', 'purchaseLeader', 'management', 'createOrder', 'end'], [], '采购经理、分管领导、总经理与财务总监会签依次推进；不把路径到达当作会签通过。'),
        this.scenario('purchase-material', '生产物料 · 计划员确认', { ...purchaseInput, purchaseType: '生产物料' }, ['materialConfirm', 'procurement', 'end'], ['assetConfirm', 'contractReview'], '计划员办理确认需求后进入专项条件判断。'),
        this.scenario('purchase-asset', '固定资产 · 登记资产编号', { ...purchaseInput, purchaseType: '固定资产', amount: 12800 }, ['assetConfirm', 'purchaseLeader', 'end'], ['materialConfirm', 'contractReview'], '资产编号是办理节点填写字段；进入中额领导路线。'),
        this.scenario('purchase-ten-thousand', '一万元边界 · 增加分管领导', { ...purchaseInput, amount: 10000 }, ['procurement', 'purchaseLeader', 'end'], ['management', 'contractReview'], '等于一万元属于中间档。'),
        this.scenario('purchase-hundred-thousand', '十万元边界 · 尚不增加高额会签', { ...purchaseInput, amount: 100000, contractName: '采购合同' }, ['contractReview', 'purchaseLeader', 'end'], ['management'], '设计书为超过十万元才进入总经理和财务总监会签。')
      ],
      expense: [
        this.scenario('expense-travel', '普通差旅 · 包容零命中', expenseInput, ['supervisor', 'reviewJoin', 'department', 'finance', 'payment', 'cc', 'end'], ['projectReview', 'costReview', 'entertainmentReview', 'expenseLeader', 'management', 'financeReview'], '无项目、同部门、无招待费；默认路线直接汇合。'),
        this.scenario('expense-project', '项目报销 · 仅项目经理会审', { ...expenseInput, hasProject: true, project: '深圳现场项目' }, ['projectReview', 'reviewJoin', 'end'], ['costReview', 'entertainmentReview'], '通过业务系统取人接口找到项目经理；不把不存在的分支计入汇合。'),
        this.scenario('expense-cross', '跨部门费用 · 仅承担部门会审', { ...expenseInput, crossDepartment: true, costDepartment: '研发部' }, ['costReview', 'reviewJoin', 'end'], ['projectReview', 'entertainmentReview'], '按费用承担部门找负责人。'),
        this.scenario('expense-entertainment', '含小额招待费 · 仅行政会审', { ...expenseInput, hasEntertainment: true }, ['entertainmentReview', 'reviewJoin', 'end'], ['projectReview', 'costReview', 'management'], '含招待费不等于单张超过 2000，避免误加总经理路线。'),
        this.scenario('expense-all', '三路会审 · 项目 / 跨部门 / 招待', { ...expenseInput, amount: 9000, approvedAmount: 9000, hasProject: true, project: '研发项目', crossDepartment: true, costDepartment: '研发部', hasEntertainment: true }, ['projectReview', 'costReview', 'entertainmentReview', 'reviewJoin', 'expenseLeader', 'end'], ['management'], '三个会审分支同时进行，汇合后进入发起部门负责人。'),
        this.scenario('expense-five-thousand', '五千元边界 · 分管领导', { ...expenseInput, amount: 5000, approvedAmount: 5000 }, ['expenseLeader', 'finance', 'end'], ['management', 'financeReview'], '五千元起需要分管领导。'),
        this.scenario('expense-twenty-thousand', '两万元边界 · 普通分管路线', { ...expenseInput, amount: 20000, approvedAmount: 20000 }, ['expenseLeader', 'finance', 'end'], ['management', 'financeReview'], '合计必须超过两万元，才触发总经理与财务经理复核。'),
        this.scenario('expense-large', '大额报销 · 25,000 元', { ...expenseInput, amount: 25000, approvedAmount: 25000, needsGeneralManager: true, needsFinanceReview: true }, ['seniorLeader', 'management', 'financeReview', 'payment', 'end'], ['expenseLeader'], '先分管领导再总经理；财务核定后追加财务经理复核。'),
        this.scenario('expense-large-entertainment', '单张招待费 2,600 元', { ...expenseInput, amount: 2600, approvedAmount: 2600, hasEntertainment: true, needsGeneralManager: true }, ['entertainmentReview', 'seniorLeader', 'management', 'end'], ['expenseLeader', 'financeReview'], '业务系统预计算“任一张招待费超过 2000”；合计小于 5000 仍走总经理路线。'),
        this.scenario('expense-adjusted', '核定金额改变 · 1,200 元', { ...expenseInput, approvedAmount: 1200, needsFinanceReview: true, loanOffset: 200 }, ['finance', 'financeReview', 'payment', 'end'], ['management'], '核定金额与申请金额不同，复核后付款金额为 1200 − 200 = 1000；路径演示不执行财务计算。'),
        this.scenario('expense-two', '两路会审 · 项目与承担部门', { ...expenseInput, hasProject: true, crossDepartment: true, project: '研发项目', costDepartment: '研发部' }, ['projectReview', 'costReview', 'reviewJoin', 'end'], ['entertainmentReview'], '验证汇合只等待两条命中路线。')
      ],
      contract: [
        this.scenario('contract-parallel', '法务与财务并行评审', { amount: 80000, department: '研发部', contractName: '设备采购合同', supplier: '示例供应商' }, ['management', 'finance', 'parallelJoin', 'finalApproval', 'end'], [], '两个真实分支全部完成才进入终审；演示不替代会签计票。'),
        this.scenario('contract-subflow', '主流程等待合同子流程', { amount: 120000, department: '生产部', parentBusinessKey: 'CG20261013002', contractName: '生产设备合同', supplier: '示例供应商' }, ['department', 'management', 'finance', 'finalApproval', 'cc', 'end'], [], '子流程返回 reviewConclusion 后恢复采购主流程。')
      ],
      quality: [
        this.scenario('quality-direct', '无需返工 · 双部门确认归档', { rework: '无需返工', department: '生产部', defect: '外观异常复核', batch: 'QC20261013A' }, ['department', 'parallelSplit', 'cc', 'production', 'parallelJoin', 'archive', 'end'], ['finance'], '等待质量和生产两方完成后归档。'),
        this.scenario('quality-rework', '需要返工 · 生产处理后复检', { rework: '需要返工', department: '生产部', defect: '尺寸偏差', batch: 'QC20261013B' }, ['department', 'management', 'finance'], ['end'], '同一输入会再次命中返工；模拟器必须暂停，不假装审批结束。', false)
      ]
    };
    return structuredClone(samples[key] || []);
  },

  /**
   * 构造仅描述输入与预期路线的场景，不执行审批政策或复杂条件计算。
   * @param {string} id 场景标识。
   * @param {string} title 场景标题。
   * @param {object} input 已由业务系统预计算的演示输入。
   * @param {string[]} includeNodes 应当访问的节点。
   * @param {string[]} excludeNodes 不应访问的节点。
   * @param {string} note 业务规则与模拟边界说明。
   * @param {boolean} complete 是否预期沿路径到达结束。
   * @returns {object} 独立场景数据。
   */
  scenario(id, title, input, includeNodes, excludeNodes, note, complete = true) {
    return { id, title, description: note, input: structuredClone(input), expect: { includeNodes, excludeNodes, note, complete } };
  }
};