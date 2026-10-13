/**
 * WinUI 3 客户端 HTML 原型控制器；仅操作独立的浏览器演示数据。
 */
const Win = {
  /** 当前页签。 */
  page:'expense',
  /** 当前详情编号。 */
  selected:null,
  /** 搜索文本。 */
  search:'',
  /** 状态筛选。 */
  filter:'全部状态',
  /** 虚构员工列表。 */
  users:['王强','李娜','赵敏','吴芳','郑涛'],
  /** 当前员工。 */
  user:'王强',
  /** 模拟网络状态。 */
  connected:true,
  /** 模拟本机业务后端状态。 */
  host:true,
  /** 同步模式。 */
  mode:'callback',
  /** 本地单据。 */
  docs:[],
  /** 待同步事件。 */
  events:[],
  /** 最近的模拟日志。 */
  logs:[],
  /**
   * 创建虚构演示单据。
   * @returns {Array<object>} 种子数据。
   */
  seed() {
    return [
      {id:'BX-202610-001',type:'expense',title:'上海客户现场差旅报销',owner:'王强',department:'生产一部',amount:2680,description:'上海客户现场设备调试，报销往返交通与住宿费用。',item:'上海出差交通与住宿',status:'草稿'},
      {id:'BX-202610-002',type:'expense',title:'十月项目差旅费用',owner:'王强',department:'生产一部',amount:4860,description:'项目验收期间差旅费用，票据已整理。',item:'项目验收差旅',status:'审批中',instance:'INS-DEMO-002'},
      {id:'BX-202610-003',type:'expense',title:'九月生产现场交通费',owner:'王强',department:'生产一部',amount:1280,approvedAmount:1280,description:'九月生产现场交通费。',item:'市内交通',status:'已通过',instance:'INS-DEMO-003'},
      {id:'BX-202610-004',type:'expense',title:'供应商走访费用',owner:'王强',department:'生产一部',amount:3500,description:'请补充走访安排后重新提交。',item:'供应商走访差旅',status:'已退回',instance:'INS-DEMO-004'},
      {id:'CG-202610-001',type:'purchase',title:'生产线传感器采购',owner:'王强',department:'生产一部',amount:18000,description:'更换生产线老旧传感器，采购需求已确认。',item:'工业传感器 / 20 件',status:'草稿'},
      {id:'CG-202610-002',type:'purchase',title:'设备维护备件采购',owner:'郑涛',department:'采购部',amount:32600,description:'第四季度设备维护备件补充。',item:'设备维护备件 / 1 批',status:'审批中',instance:'INS-DEMO-006'}
    ].map(d=>({...d,serviceStatus:d.status,stage:0,history:[{title:d.status==='草稿'?'已保存草稿':'已提交审批',note:`${d.owner} · 2026-10-13 09:30`},...(d.status==='审批中'?[{title:'直属上级审批',note:'等待李娜处理'}]:d.status==='已通过'?[{title:'审批通过',note:'赵敏 · 核定金额已回写'}]:d.status==='已退回'?[{title:'退回申请人',note:'李娜 · 请补充走访安排'}]:[])]}));
  },
  /**
   * 初始化独立的本地数据。
   * @returns {void} 无返回值。
   */
  init() {
    this.docs=this.seed();
    try {
      const saved=JSON.parse(localStorage.getItem('approval-winui-prototype-v1')||'null');
      if(saved&&Array.isArray(saved.docs)&&saved.docs.every(d=>d.id&&Array.isArray(d.history))){this.docs=saved.docs;this.events=saved.events||[];this.logs=saved.logs||[];this.user=this.users.includes(saved.user)?saved.user:'王强';this.connected=saved.connected!==false;this.host=saved.host!==false;this.mode=saved.mode==='query'?'query':'callback';}
    } catch { UI.toast('本地演示数据无法读取，已载入默认示例。'); }
    this.go(location.hash.slice(1)||'expense');
  },
  /**
   * 保存本地演示数据。
   * @returns {void} 无返回值。
   */
  persist() {
    try { localStorage.setItem('approval-winui-prototype-v1',JSON.stringify({docs:this.docs,events:this.events,logs:this.logs,user:this.user,connected:this.connected,host:this.host,mode:this.mode})); }
    catch { UI.toast('浏览器无法保存演示数据，本轮操作仅在当前页面保留。'); }
  },
  /**
   * 切换工作区。
   * @param {string} page 页签名称。
   * @returns {void} 无返回值。
   */
  go(page) { this.page=['expense','purchase','tasks','webview','integration','settings'].includes(page)?page:'expense';this.selected=null;this.search='';this.filter='全部状态';history.replaceState(null,'','#'+this.page);this.render(); },
  /**
   * 切换虚构员工。
   * @param {string} name 员工姓名。
   * @returns {void} 无返回值。
   */
  switchUser(name) { if(!this.users.includes(name))return;this.user=name;this.selected=null;this.persist();this.render();UI.toast(`已切换为${name}，当前待办 ${this.tasks().length} 项。`); },
  /**
   * 获取样例当前审批人。
   * @param {object} d 单据。
   * @returns {string} 员工姓名。
   */
  assignee(d) { return d.stage===0?(d.firstApprover||'李娜'):d.type==='expense'?'赵敏':'吴芳'; },
  /**
   * 获取当前员工待办。
   * @returns {Array<object>} 待审批单据。
   */
  tasks() { return this.docs.filter(d=>d.serviceStatus==='审批中'&&this.assignee(d)===this.user); },
  /**
   * 渲染桌面工作区。
   * @returns {void} 无返回值。
   */
  render() {
    const doc=this.docs.find(d=>d.id===this.selected);
    const body=doc?WinViews.detail(this,doc):['expense','purchase'].includes(this.page)?WinViews.list(this):WinViews[this.page](this);
    document.querySelector('#app').innerHTML=WinViews.shell(this,body);
  },
  /**
   * 打开业务单据；查询模式模拟打开时刷新。
   * @param {string} id 单据编号。
   * @returns {void} 无返回值。
   */
  open(id) { this.selected=id;if(this.mode==='query'&&this.connected){const d=this.docs.find(x=>x.id===id);this.sync(d);this.persist();}this.render(); },
  /**
   * 新建或编辑业务单据。
   * @param {string} id 可选单据编号。
   * @returns {void} 无返回值。
   */
  edit(id) {
    const d=this.docs.find(x=>x.id===id),type=d?.type||this.page;
    if(d&&(!['草稿','已退回','已撤回'].includes(d.status)||d.owner!==this.user)){UI.toast('此状态不能编辑业务数据。');return;}
    UI.modal(d?'编辑业务单据':`新建${type==='expense'?'费用报销单':'采购申请单'}`,`<div class="form-grid"><label class="field full">单据标题<input class="inp" id="title" required maxlength="80" value="${UI.escape(d?.title||'')}"></label><label class="field">申请人<input class="inp" value="${UI.escape(this.user)}" readonly></label><label class="field">所属部门<select class="sel" id="department">${['生产一部','采购部','财务部'].map(x=>`<option ${x===d?.department?'selected':''}>${x}</option>`).join('')}</select></label><label class="field full">${type==='expense'?'费用项目':'物料 / 数量'}<input class="inp" id="item" required value="${UI.escape(d?.item||'')}"></label><label class="field">总金额（元）<input class="inp" id="amount" type="number" min="0.01" step="0.01" max="999999999" required value="${d?.amount||''}"></label>${type==='expense'?`<label class="field">票据附件名称（模拟）<input class="inp" id="attachment" value="${UI.escape(d?.attachment||'差旅票据.pdf')}"></label>`:''}<label class="field full">${type==='expense'?'报销事由':'采购用途'}<textarea class="inp" id="description" required>${UI.escape(d?.description||'')}</textarea></label></div><p class="hint" style="margin-top:16px">仅在业务系统编辑草稿；原型只保存一条汇总明细与附件名称，不保存真实票据。</p>`,dialog=>{
      const values=Object.fromEntries(['title','department','item','amount','description','attachment'].map(key=>[key,dialog.querySelector('#'+key)?.value||'']));values.amount=Number(values.amount);
      if(!values.title.trim()||!values.description.trim()||!values.item.trim()){UI.toast('标题、明细和事由不能为空。');return false;}
      if(d)Object.assign(d,values);else{const newId=`${type==='expense'?'BX':'CG'}-DEMO-${Date.now()}`;this.docs.unshift({...values,id:newId,type,owner:this.user,status:'草稿',serviceStatus:'草稿',stage:0,history:[{title:'已保存草稿',note:`${this.user} · 刚刚`} ]});this.selected=newId;}
      this.persist();this.render();UI.toast('业务草稿已保存在本浏览器。');
    },'保存草稿');
  },
  /**
   * 检查模拟网络。
   * @returns {boolean} 是否连接。
   */
  online() { if(!this.connected){UI.toast('连接失败（模拟）：审批数据未发送。请在连接设置中恢复网络后重试。');return false;}return true; },
  /**
   * 预览并提交简化审批路径。
   * @param {string} id 单据编号。
   * @returns {void} 无返回值。
   */
  submit(id) {
    const d=this.docs.find(x=>x.id===id);if(!d||!this.online()||d.owner!==this.user||!['草稿','已退回','已撤回'].includes(d.status))return;
    UI.modal('提交前预览',`<div class="notice">${UI.escape(d.title)} · ¥ ${WinViews.money(d.amount)}<br>以下是简化的固定路径演示；正式预览应调用审批引擎，按流程定义返回节点和候选人。</div><div class="win-preview-path"><span>申请人</span> → <span>直属上级</span> → <span>${d.type==='expense'?'财务审核':'采购经理'}</span> → <span>完成</span></div><label class="field">直属上级（模拟候选人）<select class="sel" id="approver"><option>李娜</option></select></label><p class="hint" style="margin-top:15px">提交快照：单号、发起人、部门、明细及金额。重新提交保留业务单号并增加本地提交次数。不会产生真实审批单。</p>`,dialog=>{
      if(!this.online())return false;
      d.firstApprover=dialog.querySelector('#approver').value;d.stage=0;d.submissions=(d.submissions||0)+1;d.instance=`INS-DEMO-${d.id}-${d.submissions}`;d.status=d.serviceStatus='审批中';delete d.approvedAmount;delete d.orderNo;
      d.history.push({title:d.submissions>1?'重新提交审批':'已提交审批',note:`${this.user} · 刚刚`},{title:'直属上级审批',note:'等待李娜处理'});
      this.log(`提交 ${d.id}：本地模拟审批单已生成。`);this.persist();this.render();UI.toast('提交成功（本地模拟）。切换李娜可处理待办。');
    },'确认提交');
  },
  /**
   * 撤回本地模拟审批。
   * @param {string} id 单据编号。
   * @returns {void} 无返回值。
   */
  withdraw(id) {
    const d=this.docs.find(x=>x.id===id);if(!d||!this.online()||d.owner!==this.user||d.serviceStatus!=='审批中')return;
    UI.modal('撤回审批',`<p>撤回“${UI.escape(d.title)}”后，可在业务系统修改并重新提交。</p><label class="field" style="margin-top:16px">撤回原因<textarea class="inp" id="reason" required></textarea></label>`,dialog=>{if(!this.online())return false;d.status=d.serviceStatus='已撤回';d.history.push({title:'申请人撤回',note:dialog.querySelector('#reason').value});this.persist();this.render();UI.toast('已撤回本地模拟审批。');},'确认撤回');
  },
  /**
   * 打开业务客户端自己的审批窗口。
   * @param {string} id 单据编号。
   * @returns {void} 无返回值。
   */
  approve(id) {
    const d=this.tasks().find(x=>x.id===id);if(!d||!this.online())return;
    UI.modal('处理审批 · 原生窗口示意',`<div class="notice">${UI.escape(d.title)}<br>${UI.escape(d.owner)} · ¥ ${WinViews.money(d.amount)}</div><div class="form-grid" style="margin-top:18px"><label class="field full">处理结果<select class="sel" id="result"><option value="approve">同意</option><option value="return">退回申请人</option></select></label>${d.type==='expense'&&d.stage===1?`<label class="field full">核定金额（审批字段）<input class="inp" id="approved" type="number" min="0" max="${d.amount}" step="0.01" required value="${d.amount}"></label>`:''}<label class="field full">审批意见<textarea class="inp" id="opinion" required placeholder="请输入处理意见"></textarea></label></div><p class="hint" style="margin-top:16px">业务字段只读；审批人只填写意见与当前节点授权的审批字段。此处仅模拟两级流转。</p>`,dialog=>{
      if(!this.online())return false;
      if(!this.tasks().some(x=>x.id===id)){UI.toast('该待办已处理，请刷新。');return false;}
      const opinion=dialog.querySelector('#opinion').value.trim();if(!opinion){UI.toast('请输入审批意见。');return false;}
      const returned=dialog.querySelector('#result').value==='return';d.history.push({title:returned?'退回申请人':`${this.user} · 同意`,note:opinion});
      if(returned)d.serviceStatus='已退回';else if(d.stage===0){d.stage=1;d.history.push({title:d.type==='expense'?'财务审核':'采购经理审核',note:`等待${this.assignee(d)}处理`});}else{d.serviceStatus='已通过';if(d.type==='expense')d.serviceAmount=Number(dialog.querySelector('#approved').value);else d.serviceOrder='PO-DEMO-'+d.id;}
      this.events.push({id:'EVT-'+Date.now(),docId:id,delivered:false});this.log(`${this.user} 已处理 ${id}（模拟）。`);
      if(this.mode==='callback'&&this.host)this.deliver(false);else this.log('等待业务系统同步：可主动查询或投递模拟回调。');
      this.persist();this.render();UI.toast('审批已处理（本地模拟），待办已更新。');
    },'确认处理');
  },
  /**
   * 同步一张单据的结果。
   * @param {object} d 业务单据。
   * @returns {void} 无返回值。
   */
  sync(d) { d.status=d.serviceStatus;if(d.serviceStatus==='已通过'){if(d.serviceAmount!==undefined)d.approvedAmount=d.serviceAmount;if(d.serviceOrder)d.orderNo=d.serviceOrder;} },
  /**
   * 主动查询最新模拟结果。
   * @returns {void} 无返回值。
   */
  refresh() { if(!this.online())return;this.docs.forEach(d=>this.sync(d));this.log('主动查询完成：已将模拟审批状态与结果回写业务单据。');this.persist();this.render();UI.toast('模拟查询完成，业务状态已刷新。'); },
  /**
   * 投递未处理的模拟回调。
   * @param {boolean} notify 是否刷新页面并提示。
   * @returns {void} 无返回值。
   */
  deliver(notify=true) {
    if(!this.online())return;
    if(!this.host){this.log('投递失败：本机业务后端已关闭。事件仍待同步。');this.persist();this.render();UI.toast('请启动本机后端，或使用查询同步。');return;}
    const pending=this.events.filter(e=>!e.delivered);pending.forEach(e=>{const d=this.docs.find(x=>x.id===e.docId);if(d)this.sync(d);e.delivered=true;this.log(`${e.id}：首次接收，已回写业务单据（模拟）。`);});
    this.persist();if(notify){this.render();UI.toast(pending.length?`已投递 ${pending.length} 个模拟事件。`:'暂无待投递事件。');}
  },
  /**
   * 演示相同事件不重复回写。
   * @returns {void} 无返回值。
   */
  duplicate() {
    if(!this.online())return;
    if(!this.host){UI.toast('本机业务后端已关闭，无法接收回调。');return;}
    const event=[...this.events].reverse().find(e=>e.delivered);
    if(!event){UI.toast('请先处理一项待办并投递一次模拟回调。');return;}
    this.log(`${event.id}：重复事件，已识别并忽略；未重复回写（模拟）。`);this.persist();this.render();UI.toast('同一事件重复投递，业务数据保持不变。');
  },
  /**
   * 追加有限长度日志。
   * @param {string} message 日志文本。
   * @returns {void} 无返回值。
   */
  log(message) { this.logs.unshift(`${new Date().toLocaleTimeString('zh-CN')} ${message}`);this.logs=this.logs.slice(0,20); },
  /**
   * 切换模拟环境开关。
   * @param {string} key 开关名称。
   * @returns {void} 无返回值。
   */
  toggle(key) { if(!['connected','host'].includes(key))return;this[key]=!this[key];this.log(`${key==='connected'?'网络':'本机业务后端'}已${this[key]?'恢复':'关闭'}（模拟）。`);this.persist();this.render(); },
  /**
   * 重置独立演示环境。
   * @returns {void} 无返回值。
   */
  prepare() {
    UI.modal('准备本地演示环境','<p>将重置本页的虚构单据、员工、待办与同步记录。其他原型页面的数据不受影响。</p><div class="notice" style="margin-top:16px">正式版准备流程：组织与人员绑定 → 导入费用和采购流程 → 准备业务数据。本原型只重置浏览器样例。</div>',()=>{this.docs=this.seed();this.user='王强';this.events=[];this.logs=[];this.connected=true;this.host=true;this.mode='callback';this.log('演示环境已准备：两类业务单据、员工与主管待办（本地模拟）。');this.persist();this.render();UI.toast('本地示例已重置。');},'重置并准备');
  }
};
Win.init();
