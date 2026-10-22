/**
 * 自由画布原型的持久检查：直接载入模板与图逻辑，不启动浏览器、不下载依赖。
 * 运行方式：node docs/prototypes/checks.cjs。
 */
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const vm = require('node:vm');

/**
 * 已通过的检查数量。
 */
let passed = 0;

/**
 * 记录一个有明确预期的检查，失败时终止后续执行。
 * @param {string} name 场景名称。
 * @param {boolean} condition 条件是否成立。
 * @returns {void} 无返回值。
 */
function check(name, condition) {
  if (!condition) throw new Error('FAIL: ' + name);
  passed += 1;
  console.log('PASS: ' + name);
}

/**
 * 在没有 DOM 的独立环境载入实际产品原型模块。
 * @returns {object} 受测的模板与图处理模块。
 */
function loadModules() {
  const context = vm.createContext({ structuredClone });
  for (const [file, variable] of [
    ['designer-templates.js', 'DesignerTemplates'],
    ['designer-graph.js', 'DesignerGraph']
  ]) {
    vm.runInContext(readFileSync(join(__dirname, file), 'utf8') + '\n;globalThis.' + variable + '=' + variable + ';', context, { filename: file });
  }
  return { templates: context.DesignerTemplates, graph: context.DesignerGraph };
}

/**
 * 验证四套模板的真实节点结构与默认校验结果。
 * @param {object} templates 模板模块。
 * @param {object} graph 图处理模块。
 * @returns {void} 无返回值。
 */
function checkTemplates(templates, graph) {
  for (const key of ['purchase', 'expense', 'contract', 'quality']) {
    const state = templates.create(key, { settings: {} });
    const errors = graph.validate(state);
    check(key + ' 默认图通过完整结构校验：' + errors.join('；'), errors.length === 0);
    check(key + ' 使用独立自由画布及显式开始、结束', state.mode === 'canvas' && state.schemaVersion === 2
      && state.nodes.filter(node => node.type === 'start').length === 1
      && state.nodes.filter(node => node.type === 'end').length === 1);
    const other = templates.create(key, { settings: {} });
    state.nodes[0].name = '只修改当前草稿';
    state.scenarios[0].input.reviewMarker = '仅当前场景';
    check(key + ' 修改一份草稿不会污染另一份节点和场景', other.nodes[0].name !== state.nodes[0].name
      && !Object.hasOwn(other.scenarios[0].input, 'reviewMarker'));
  }
}

/**
 * 验证金额判断实际影响可到达路线，含临界值场景。
 * @param {object} templates 模板模块。
 * @param {object} graph 图处理模块。
 * @returns {void} 无返回值。
 */
function checkAmountRoutes(templates, graph) {
  const purchase = templates.create('purchase', { settings: {} });
  const purchaseCase = id => purchase.scenarios.find(item => item.id === id).input;
  const low = graph.trace(purchase, purchaseCase('purchase-small'));
  const high = graph.trace(purchase, purchaseCase('purchase-large'));
  const boundary = graph.trace(purchase, purchaseCase('purchase-ten-thousand'));
  check('小额采购只走采购经理，不固定启动专项与合同子流程', low.complete
    && ['committee', 'finance', 'contractReview', 'purchaseLeader', 'management'].every(id => !low.waves.flat().includes(id))
    && ['specialJoin', 'procurement', 'createOrder', 'end'].every(id => low.waves.flat().includes(id)));
  check('一万元增加分管领导，十二万元再增加高额会签', boundary.complete && high.complete
    && boundary.waves.flat().includes('purchaseLeader') && !boundary.waves.flat().includes('management')
    && high.waves.flat().includes('management'));
  const belowContract = graph.trace(purchase, purchaseCase('purchase-contract-below'));
  const contractBoundary = graph.trace(purchase, purchaseCase('purchase-contract-boundary'));
  check('五万元合同边界严格区分49999和50000', belowContract.complete && contractBoundary.complete
    && !belowContract.waves.flat().includes('contractReview') && contractBoundary.waves.flat().includes('contractReview'));
  check('采购三路专项审核汇合及后续采购经理只执行一次', high.waves.flat().filter(id => id === 'specialJoin').length === 1
    && high.waves.flat().filter(id => id === 'procurement').length === 1);
  const committee = purchase.nodes.find(node => node.id === 'committee');
  check('单一来源票签配置为四人总权重五、采购经理两票、通过比例60%', committee.method === '票签' && committee.ratio === 60
    && Object.keys(committee.voteWeights).length === 4 && committee.voteWeights['吴芳'] === 2
    && Object.values(committee.voteWeights).reduce((sum, value) => sum + value, 0) === 5);
  const expense = templates.create('expense', { settings: {} });
  const expenseCase = id => expense.scenarios.find(item => item.id === id).input;
  const normal = graph.trace(expense, expenseCase('expense-travel'));
  const twentyThousand = graph.trace(expense, expenseCase('expense-twenty-thousand'));
  const large = graph.trace(expense, expenseCase('expense-large'));
  check('两万元报销只需分管，超过两万元再走总经理和财务复核', twentyThousand.complete && large.complete
    && twentyThousand.waves.flat().includes('expenseLeader') && !twentyThousand.waves.flat().includes('management')
    && !twentyThousand.waves.flat().includes('financeReview')
    && ['seniorLeader', 'management', 'financeReview'].every(id => large.waves.flat().includes(id)));
  check('普通报销保留直属上级、部门、财务或签和出纳，财务禁止批量', normal.complete
    && ['supervisor', 'department', 'finance', 'payment', 'end'].every(id => normal.waves.flat().includes(id))
    && expense.nodes.find(node => node.id === 'finance').denied.includes('批量同意'));
}

/**
 * 对全部预置场景执行真实图路径检查，覆盖包容分支零、单、双、三路命中。
 * @param {object} templates 模板模块。
 * @param {object} graph 图处理模块。
 * @returns {void} 无返回值。
 */
function checkScenarioPaths(templates, graph) {
  let scenarios = 0;
  for (const key of ['purchase', 'expense', 'contract', 'quality']) {
    const state = templates.create(key, { settings: {} });
    for (const scenario of state.scenarios) {
      const result = graph.trace(state, scenario.input);
      const visited = result.waves.flat();
      check(scenario.title + '：命中与跳过的路径符合预期', scenario.expect.includeNodes.every(id => visited.includes(id))
        && scenario.expect.excludeNodes.every(id => !visited.includes(id)) && result.complete === scenario.expect.complete);
      scenarios += 1;
    }
  }
  check('提供超过十个独立的常用业务路径场景', scenarios > 10);
  const expense = templates.create('expense', { settings: {} });
  const three = graph.trace(expense, expense.scenarios.find(item => item.id === 'expense-all').input);
  const branchNodes = ['projectReview', 'costReview', 'entertainmentReview'];
  check('报销三路会审同一批次启动，汇合只出现一次', three.waves.some(wave => branchNodes.every(id => wave.includes(id)))
    && three.waves.flat().filter(id => id === 'reviewJoin').length === 1);
  const purchase = templates.create('purchase', { settings: {} });
  const all = graph.trace(purchase, purchase.scenarios.find(item => item.id === 'purchase-all').input);
  check('采购票签、预算外财务、合同三路同批启动并汇合', all.waves.some(wave => ['committee', 'finance', 'contractReview'].every(id => wave.includes(id)))
    && all.waves.flat().filter(id => id === 'specialJoin').length === 1);
}
/**
 * 用长短不同的真实分支验证合同终审不会抢在慢分支之前执行。
 * @param {object} templates 模板模块。
 * @param {object} graph 图处理模块。
 * @returns {void} 无返回值。
 */
function checkParallelWaiting(templates, graph) {
  const contract = templates.create('contract', { settings: {} });
  const legalEdge = contract.edges.find(edge => edge[0] === 'management' && edge[1] === 'parallelJoin');
  if (!legalEdge) throw new Error('合同模板缺少法务到并行汇合的连线。');
  // 增加第二轮法务审批，使财务先到汇合点；终审必须等待法务分支真正抵达。
  contract.nodes.push({ id: 'legalSecondReview', name: '法务复核', type: 'approval', people: '法务负责人', method: '依次审批', x: 700, y: 180 });
  legalEdge[1] = 'legalSecondReview';
  contract.edges.push(['legalSecondReview', 'parallelJoin', '']);
  const result = graph.trace(contract, {});
  const waveOf = id => result.waves.findIndex(wave => wave.includes(id));
  check('合同长短分支图仍通过结构校验并最终完成', graph.validate(contract).length === 0 && result.complete);
  check('并行汇合等待法务复核和财务两条分支', waveOf('parallelJoin') > waveOf('legalSecondReview') && waveOf('parallelJoin') > waveOf('finance'));
  check('合同终审位于汇合之后且只发生一次', waveOf('finalApproval') > waveOf('parallelJoin')
    && result.waves.flat().filter(id => id === 'finalApproval').length === 1);
}

/**
 * 验证返工回路和未知条件不会伪装成已审批通过。
 * @param {object} templates 模板模块。
 * @param {object} graph 图处理模块。
 * @returns {void} 无返回值。
 */
function checkPausedRoutes(templates, graph) {
  const quality = templates.create('quality', { settings: {} });
  const rework = graph.trace(quality, { rework: '需要返工' });
  const accepted = graph.trace(quality, { rework: '无需返工' });
  check('需要返工沿真实回路暂停，不假装到达结束', !rework.complete && rework.waves.flat().includes('finance')
    && !rework.waves.flat().includes('end') && rework.notes.some(note => note.includes('回路')));
  check('无需返工走双部门汇合、归档和显式结束', accepted.complete && !accepted.waves.flat().includes('finance')
    && ['cc', 'production', 'parallelJoin', 'archive', 'end'].every(id => accepted.waves.flat().includes(id)));
  const missingInput = graph.trace(templates.create('purchase', { settings: {} }), {});
  check('缺少条件字段时暂停，不误走默认分支', !missingInput.complete && missingInput.notes.length > 0 && !missingInput.waves.flat().includes('end'));
  const unknown = templates.create('purchase', { settings: {} });
  unknown.edges.find(edge => edge[0] === 'amountGateway' && !graph.isDefault(edge[2]))[2] = 'amount >= 10000 && department == "采购部"';
  const unsupported = graph.trace(unknown, { ...unknown.scenarios[0].input, amount: 12800, department: '采购部' });
  check('超出演示条件语法的表达式明确暂停', !unsupported.complete && unsupported.notes.length > 0 && !unsupported.waves.flat().includes('end'));
}

/**
 * 验证发布前能拦截结构损坏、缺失兜底以及错误网关配对。
 * @param {object} templates 模板模块。
 * @param {object} graph 图处理模块。
 * @returns {void} 无返回值。
 */
function checkInvalidGraphs(templates, graph) {
  const dangling = templates.create('purchase', { settings: {} });
  dangling.edges.push(['department', 'missingNode', '']);
  check('拦截指向不存在节点的悬空连线', graph.validate(dangling).some(error => error.includes('悬空')));
  const isolated = templates.create('purchase', { settings: {} });
  isolated.nodes.push({ id: 'isolated', name: '孤立审批', type: 'approval', people: '审核员', x: 100, y: 100 });
  check('拦截没有路径连接的孤立节点', graph.validate(isolated).some(error => error.includes('孤立审批') && error.includes('无法从开始')));
  const noDefault = templates.create('purchase', { settings: {} });
  noDefault.edges = noDefault.edges.filter(edge => !(edge[0] === 'amountGateway' && graph.isDefault(edge[2])));
  check('条件网关缺少默认分支时禁止发布', graph.validate(noDefault).some(error => error.includes('默认分支')));
  const wrongType = templates.create('contract', { settings: {} });
  wrongType.nodes.find(node => node.id === 'parallelSplit').pairedGatewayId = 'department';
  check('并行网关不能与普通审批节点配对', graph.validate(wrongType).some(error => error.includes('配对')));
  const wrongPair = templates.create('contract', { settings: {} });
  wrongPair.nodes.find(node => node.id === 'parallelJoin').pairedGatewayId = 'department';
  check('并行汇合必须指回对应分叉，错误配对被拦截', graph.validate(wrongPair).some(error => error.includes('配对')));
  const noEnd = templates.create('expense', { settings: {} });
  noEnd.nodes = noEnd.nodes.filter(node => node.type !== 'end');
  noEnd.edges = noEnd.edges.filter(edge => edge[1] !== 'end');
  check('缺少显式结束节点时禁止发布', graph.validate(noEnd).some(error => error.includes('结束节点')));
}

/**
 * 验证旧树形草稿转换时保留业务配置并引入显式条件网关及结束节点。
 * @param {object} graph 图处理模块。
 * @returns {void} 无返回值。
 */
function checkMigration(graph) {
  const legacy = {
    mode: 'tree', flowKey: 'purchase', version: 4, owner: '流程管理员',
    settings: { self: '转交直属上级', printTitle: '采购审批凭证' },
    fields: [{ key: 'amount', name: '采购金额', type: '金额', required: true, summary: true }],
    nodes: [
      { id: 'start', name: '业务系统发起', type: 'start', people: '业务系统', x: 20, y: 180 },
      { id: 'department', name: '部门审核', type: 'approval', people: '李娜', source: '部门负责人', method: '会签', denied: ['转交'], signature: true, x: 240, y: 180 },
      { id: 'management', name: '大额审核', type: 'approval', people: '周总', method: '依次审批', x: 460, y: 30 },
      { id: 'finance', name: '财务确认', type: 'approval', people: '赵敏', method: '或签', x: 680, y: 180 },
      { id: 'cc', name: '抄送', type: 'cc', people: '吴芳', x: 680, y: 380 }
    ],
    edges: [['start', 'department', ''], ['department', 'management', '金额 ≥ 10,000'], ['department', 'finance', '其他情况'], ['management', 'finance', ''], ['finance', 'cc', '']]
  };
  const snapshot = JSON.stringify(legacy);
  const migrated = graph.migrate(legacy);
  check('转换旧草稿不修改原始备份数据', JSON.stringify(legacy) === snapshot);
  check('旧草稿转为版本二自由画布且通过完整校验', migrated.mode === 'canvas' && migrated.schemaVersion === 2 && graph.validate(migrated).length === 0);
  check('转换保留字段、流程设置及审批人规则', JSON.stringify(migrated.fields) === JSON.stringify(legacy.fields)
    && JSON.stringify(migrated.settings) === JSON.stringify(legacy.settings)
    && JSON.stringify(migrated.nodes.find(node => node.id === 'department')) === JSON.stringify(legacy.nodes[1]));
  check('隐含分叉变为显式网关，并保留金额路线语义', migrated.nodes.some(node => node.type === 'exclusive')
    && migrated.nodes.some(node => node.type === 'end')
    && !graph.trace(migrated, { amount: 8000 }).waves.flat().includes('management')
    && graph.trace(migrated, { amount: 10000 }).waves.flat().includes('management'));
}

/**
 * 顺序运行所有场景并输出汇总。
 * @returns {void} 无返回值。
 */
function main() {
  const { templates, graph } = loadModules();
  checkTemplates(templates, graph);
  checkAmountRoutes(templates, graph);
  checkScenarioPaths(templates, graph);
  checkParallelWaiting(templates, graph);
  checkPausedRoutes(templates, graph);
  checkInvalidGraphs(templates, graph);
  checkMigration(graph);
  console.log('原型图检查全部通过：' + passed + ' 项。');
}

main();
