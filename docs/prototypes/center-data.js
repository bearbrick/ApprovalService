/**
 * 桌面与手机共用的虚构审批记录；所有操作只保存到浏览器。
 */
window.ApprovalDemo = {
  key: 'approval-prototype-records-v2',
  seed: [
    { id:'SP-202610-0007', key:'FY20261013001', title:'10 月深圳客户现场服务差旅', flow:'费用报销', person:'王强', dept:'生产部', amount:1334.50, time:'2026-10-13 09:12', node:'财务审核', status:'待处理', urgent:true, category:'todo', read:false },
    { id:'SP-202610-0008', key:'CG20261013002', title:'研发中心办公设备采购', flow:'采购申请', person:'李娜', dept:'研发部', amount:12800, time:'2026-10-13 09:36', node:'财务审核', status:'待处理', urgent:false, category:'todo', read:false },
    { id:'SP-202610-0006', key:'FY20261012004', title:'华东客户拜访差旅报销', flow:'费用报销', person:'陈晨', dept:'销售部', amount:2860, time:'2026-10-12 16:40', node:'财务审核', status:'待处理', urgent:false, category:'todo', read:true },
    { id:'SP-202610-0005', key:'CG20261012003', title:'生产线检修备件采购', flow:'采购申请', person:'赵磊', dept:'生产部', amount:6400, time:'2026-10-12 15:08', node:'财务审核', status:'待处理', urgent:false, category:'todo', read:true },
    { id:'SP-202610-0004', key:'FY20261012002', title:'客户现场调试交通费用', flow:'费用报销', person:'周敏', dept:'技术支持部', amount:458, time:'2026-10-12 11:22', node:'出纳办理', status:'审批中', category:'done', read:true, canRevoke:true },
    { id:'SP-202610-0003', key:'CG20261011003', title:'仓储手持终端采购', flow:'采购申请', person:'王强', dept:'生产部', amount:9600, time:'2026-10-11 14:36', node:'审批结束', status:'已通过', category:'cc', read:false },
    { id:'SP-202610-0002', key:'FY20261011002', title:'财务部月度培训费用', flow:'费用报销', person:'赵敏', dept:'财务部', amount:800, time:'2026-10-11 10:05', node:'部门负责人', status:'审批中', category:'mine', read:true },
    { id:'SP-202610-0001', key:'FY20261010001', title:'供应商现场验收差旅', flow:'费用报销', person:'刘洋', dept:'采购部', amount:1780, time:'2026-10-10 08:50', node:'审批结束', status:'已拒绝', category:'done', read:true }
  ],
  /**
   * 读取演示记录。
   * @returns {Array} 浏览器保存的记录或初始记录。
   */
  load() { try { return JSON.parse(localStorage.getItem(this.key)) || structuredClone(this.seed); } catch { return structuredClone(this.seed); } },
  /**
   * 保存演示记录。
   * @param {Array} rows 审批记录。
   * @returns {void} 无返回值。
   */
  save(rows) { localStorage.setItem(this.key, JSON.stringify(rows)); },
  /**
   * 格式化人民币金额。
   * @param {number} value 金额。
   * @returns {string} 带千分位的金额。
   */
  money(value) { return '¥ ' + value.toLocaleString('zh-CN', { minimumFractionDigits:2, maximumFractionDigits:2 }); },
  /**
   * 应用演示审批操作，保留操作记录。
   * @param {object} row 当前审批单。
   * @param {string} action 操作名称。
   * @param {string} opinion 审批意见。
   * @param {string} person 操作对象。
   * @returns {void} 无返回值。
   */
  act(row, action, opinion, person = '') {
    row.history ||= [];
    row.history.push({ action, opinion, person, at: new Date().toLocaleString('zh-CN', { hour12:false }) });
    if (['同意','拒绝','退回','转交'].includes(action)) { row.category='done'; row.status=({同意:'审批中',拒绝:'已拒绝',退回:'已退回',转交:'审批中'})[action]; row.node=({同意:'出纳办理',拒绝:'审批结束',退回:'发起人补正',转交:person+' · 待处理'})[action]; }
    if (['同意','拒绝','退回','转交','撤销同意','要求补充'].includes(action)) { row.canRevoke=action==='同意'; }
    if (action==='要求补充') { row.status='待补充'; row.node='等待业务系统补充'; row.category='done'; }
    if (action==='撤销同意') { row.category='todo'; row.status='待处理'; row.node='财务审核'; }
    if (action==='加签') { row.added=person; }
    if (action==='减签') { row.added=''; }
    row.read=true;
  }
};
