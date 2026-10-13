/**
 * 流程设计交互原型，仅使用本地虚构数据，不调用审批接口。
 */
const D = {
  /** 当前页签。 */
  tab: 'flow',
  /** 当前节点。 */
  selected: 'department',
  /** 本地演示草稿。 */
  state: {
    mode:'tree', version:3, threshold:10000, settings:{},
    nodes:[
      {id:'start',name:'业务系统提交',type:'start',source:'发起人本人',people:'由 ERP 业务系统发起',method:'依次审批',x:40,y:230},
      {id:'department',name:'部门负责人审批',type:'approval',source:'部门负责人',people:'发起人所在部门 · 第 1 级',method:'依次审批',x:240,y:230},
      {id:'management',name:'总经理 / 财务总监',type:'approval',source:'角色',people:'总经理、财务总监',method:'会签',x:465,y:100},
      {id:'finance',name:'采购执行',type:'handle',source:'角色',people:'采购专员',method:'依次审批',x:680,y:230},
      {id:'cc',name:'抄送采购部',type:'cc',source:'指定成员',people:'陈晓、王强',method:'依次审批',x:680,y:430}
    ],
    edges:[['start','department',''],['department','management','金额 ≥ 10,000'],['department','finance','其他情况'],['management','finance',''],['finance','cc','']],
    fields:[{key:'applicant',name:'申请人',type:'人员',required:true,summary:true},{key:'department',name:'申请部门',type:'部门',required:true,summary:true},{key:'amount',name:'采购金额',type:'金额',required:true,summary:true},{key:'purpose',name:'采购用途',type:'文本',required:true,summary:true},{key:'date',name:'申请日期',type:'日期',required:true},{key:'items',name:'采购明细',type:'明细表',required:true},{key:'attachments',name:'相关附件',type:'附件'},{key:'approvedAmount',name:'核定金额',type:'金额',source:'审批填写'}],
    history:[{version:3,date:'2026-10-12 16:20',note:'调整大额采购阈值，完善财务审批规则。'},{version:2,date:'2026-10-09 11:35',note:'新增采购明细与附件展示。'},{version:1,date:'2026-10-01 09:00',note:'首次发布采购申请流程。'}]
  },
  /** 审批人来源清单。 */
  sources:['指定成员','角色','岗位、职级','直属上级','连续多级上级','部门负责人','业务数据里的部门','分管领导','业务数据里的人员','审批矩阵','业务系统接口','上一步指定','发起人自选','发起人本人'],
  /**
   * 读取本地草稿并开始呈现。
   * @returns {void}
   */
  init() {
    this.base=structuredClone(this.state);this.drafts={};this.loadFlow();
    window.addEventListener('hashchange',()=>{this.drafts[this.state.flowKey]=this.state;this.loadFlow();});
  },
  /**
   * 按路由载入独立模板或该流程的本地草稿。
   * @returns {void}
   */
  loadFlow() {
    const route=location.hash.slice(1),key=['purchase','expense','contract','quality'].includes(route)?route:'purchase';
    this.state=this.drafts[key]||DesignerTemplates.create(key,this.base);
    if(!this.drafts[key]){try{const saved=JSON.parse(localStorage.getItem('approval-prototype-designer-'+key));if(saved?.nodes&&saved?.fields&&saved?.flowKey===key)this.state=saved;}catch{UI.toast('本地草稿无法读取，已载入示例。');}}
    this.selected='department';this.tab='flow';this.render();
  },
  /**
   * 渲染设计器工作台。
   * @returns {void}
   */
  render() {
    const tabs=[['flow','流程设计'],['fields','审批数据'],['layout','详情排版'],['rules','流程规则'],['print','打印设置'],['versions','版本管理']];
    const actions='<div class="designer-actions"><button class="btn" onclick="D.preview()">流程预览</button><button class="btn" onclick="D.test()">测试模式</button><button class="btn" onclick="D.save()">保存草稿</button><button class="btn primary" onclick="D.publish()">发布流程</button></div>';
    const content=`<div class="design-head"><span class="pill warn">草稿</span><span class="pill">基于 V${this.state.version}</span><div class="design-meta"><span class="mono">${this.state.code}</span><span>${this.state.category}</span><span>负责人：${this.state.owner}</span></div><span style="flex:1"></span><button class="btn sm ghost" onclick="D.basic()">基本信息</button></div><div class="tabs design-tabs">${tabs.map(([key,label])=>`<button class="${this.tab===key?'active':''}" onclick="D.tab='${key}';D.render()">${label}</button>`).join('')}</div>${this.tab==='flow'?this.flow():DesignerConfig[this.tab]()}`;
    UI.shell('designer',this.state.name,'流程定义、审批数据与展示配置',content,actions);
    this.bindDrag();
  },
  /**
   * 绘制流程画布与配置面板。
   * @returns {string} 页面内容。
   */
  flow() {
    return `<div class="design-toolbar"><span class="pill accent">${this.state.mode==='tree'?'钉钉式设计':'自由画布 · BPMN 子集'}</span><button class="btn sm" onclick="D.addNode()">＋ 添加节点</button>${this.state.mode==='tree'?'<button class="btn sm" onclick="D.convert()">转为自由画布</button>':'<button class="btn sm" onclick="D.edge()">编辑连线</button>'}<span class="spacer"></span><button class="btn sm ghost" onclick="D.export()">导出</button><button class="btn sm ghost" onclick="D.import()">导入</button><button class="btn sm" onclick="D.validate()">检查流程</button></div><div class="design-board"><div class="design-stage"><div class="stage-caption">${this.state.mode==='tree'?'点击节点配置规则 · 点击 ＋ 插入节点':'拖动节点调整位置 · 点击连线编辑条件'}</div>${this.state.mode==='tree'?this.tree():this.canvas()}</div><aside class="node-inspector">${this.inspector()}</aside></div><div class="design-footer"><span>● 本地演示草稿</span><span>${this.state.nodes.length} 个节点</span><span>${this.state.flowKey==='quality'?'排他分支 + 并行分叉 / 汇合':this.state.linear?'并行评审':'1 组条件分支'}</span><span>当前选中：${UI.escape(this.node()?.name||'无')}</span><span style="margin-left:auto">${this.state.saved?'已保存 '+this.state.saved:'修改后点击保存草稿'}</span></div>`;
  },
  /**
   * 获取当前选中的节点。
   * @returns {object} 节点配置。
   */
  node() { return this.state.nodes.find(n=>n.id===this.selected)||this.state.nodes[1]; },
  /**
   * 绘制可选择的单个节点。
   * @param {object} n 节点。
   * @param {boolean} canvas 是否画布节点。
   * @returns {string} 节点内容。
   */
  card(n,canvas=false) {
    const names={start:'发起人',approval:'审批人',handle:'办理人',cc:'抄送人',sub:'子流程',auto:'自动节点',parallel:'并行分叉',parallelJoin:'并行汇合',inclusive:'包容分支',exclusive:'条件分支'};
    return `<button class="flow-node ${canvas?'canvas-node ':''}${this.selected===n.id?'selected':''}" ${canvas?`style="left:${n.x}px;top:${n.y}px" data-node="${n.id}"`:''} onclick="D.selected='${n.id}';D.render()"><div class="node-cap ${n.type}">${UI.icon(n.type==='start'?'play':n.type==='cc'?'send':n.type==='approval'?'user':'layers')}${names[n.type]||'审批人'}<small>${n.type==='approval'?n.method:UI.icon('chevR')}</small></div><div class="node-body"><b>${UI.escape(n.name)}</b><span>${UI.escape(n.people||'请设置处理人')}</span></div></button>`;
  },
  /**
   * 绘制树形流程。
   * @returns {string} 流程内容。
   */
  tree() {
    const n=this.state.nodes, connector='<div class="connector"><button class="add-node" aria-label="插入节点" onclick="D.addNode()">+</button></div>';
    if(this.state.linear)return `<div class="tree">${n.map(node=>this.card(node)+(['parallel','inclusive','exclusive'].includes(node.type)?this.gateway(node):'')+connector).join('')}<div class="end-node">流程结束</div></div>`;
    return `<div class="tree">${this.card(n[0])}${connector}${this.card(n[1])}${connector}<div class="branch-block"><div class="branch-col"><button class="flow-node" onclick="D.condition()"><div class="node-cap branch">条件 1 <small>优先级 1</small></div><div class="node-body"><b>${this.state.flowKey==='expense'?'报销金额':'采购金额'} ≥ ${Number(this.state.threshold).toLocaleString()}</b><span>满足条件进入管理层审批</span></div></button>${connector}${this.card(n[2])}</div><div class="branch-col"><button class="flow-node" onclick="D.condition()"><div class="node-cap branch">其他情况 <small>默认分支</small></div><div class="node-body"><b>未满足其他条件</b><span>进入后续处理节点</span></div></button><div class="branch-empty">无需额外审批</div></div></div>${connector}${n.slice(3).map(node=>this.card(node)+(['parallel','inclusive','exclusive'].includes(node.type)?this.gateway(node):'')+connector).join('')}<div class="end-node">流程结束</div></div>`;
  },
  /**
   * 绘制自由画布，节点位置与连线随编辑改变。
   * @returns {string} 画布内容。
   */
  canvas() {
    return `<div class="canvas-space" style="width:${this.state.flowKey==='quality'?1200:940}px"><svg aria-label="流程连线"><defs><marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6" markerHeight="6" orient="auto"><path d="M0 0L10 5L0 10Z" fill="#8397b4"/></marker></defs>${this.state.edges.map((e,i)=>{const a=this.state.nodes.find(n=>n.id===e[0]),b=this.state.nodes.find(n=>n.id===e[1]);if(!a||!b)return '';return `<g onclick="D.edge(${i})"><path class="canvas-path" marker-end="url(#arrow)" d="M${a.x+155} ${a.y+40} C${a.x+190} ${a.y+40},${b.x-40} ${b.y+40},${b.x} ${b.y+40}"/><text x="${(a.x+b.x+155)/2}" y="${(a.y+b.y)/2+25}" fill="#7b8ca5" font-size="11">${UI.escape(e[2])}</text></g>`;}).join('')}</svg>${this.state.nodes.map(n=>this.card(n,true)).join('')}</div>`;
  },
  /**
   * 绘制新增分支的两条可配置路线。
   * @param {object} node 分支节点。
   * @returns {string} 分支内容。
   */
  gateway(node) {
    node.branches ||= [{name:'分支一',people:'技术负责人',condition:'amount >= 50000'},{name:'分支二',people:'质量负责人',condition:'其他情况'}];
    return `<div class="connector"></div><div class="branch-block">${node.branches.map((b,i)=>`<div class="branch-col"><button class="flow-node" onclick="D.branch('${node.id}',${i})"><div class="node-cap branch">${UI.escape(b.name)}</div><div class="node-body"><b>${UI.escape(b.people)}</b><span>${node.type==='parallel'?'同时进入此分支':UI.escape(b.condition)}</span></div></button></div>`).join('')}</div>`;
  },
  /**
   * 配置新增分支的标题、条件和处理人。
   * @param {string} id 分支节点标识。
   * @param {number} index 路线序号。
   * @returns {void}
   */
  branch(id,index) {
    const node=this.state.nodes.find(n=>n.id===id),b=node.branches[index];
    UI.modal('分支路线设置',`<label class="field">分支名称<input class="inp" name="name" value="${UI.escape(b.name)}"></label><label class="field">处理人<input class="inp" name="people" value="${UI.escape(b.people)}"></label>${node.type==='parallel'?'<div class="notice">并行分支同时执行，全部完成后汇合。</div>':`<label class="field">条件<input class="inp" name="condition" value="${UI.escape(b.condition)}"></label>`}`,dialog=>{b.name=dialog.querySelector('[name=name]').value;b.people=dialog.querySelector('[name=people]').value;b.condition=dialog.querySelector('[name=condition]')?.value||'';this.render();});
  },
  /**
   * 配置子流程字段映射或自动节点重试。
   * @returns {void}
   */
  extraNode() {
    const n=this.node();
    if(['parallel','parallelJoin','inclusive','exclusive'].includes(n.type)){if(this.state.mode==='canvas'){this.edge();return;}this.branch(n.id,0);return;}
    UI.modal(n.type==='sub'?'子流程字段映射':'自动节点执行策略',n.type==='sub'?`<label class="field">目标流程编码<input class="inp" name="target" value="${UI.escape(n.target||'CONTRACT_REVIEW')}"></label><label class="field">字段映射<textarea class="inp mono" rows="4" name="mapping">${UI.escape(n.mapping||'amount → contractAmount\ndepartment → department\nbusinessKey → parentBusinessKey')}</textarea></label>`:`<label class="field">业务动作名称<input class="inp" name="target" value="${UI.escape(n.target||'创建采购订单')}"></label><label class="field">失败重试间隔<input class="inp" name="mapping" value="${UI.escape(n.mapping||'1 分钟、5 分钟、30 分钟、2 小时')}"></label><div class="notice">重试耗尽后转交管理员，在运行监控中处理。</div>`,dialog=>{n.target=dialog.querySelector('[name=target]').value;n.mapping=dialog.querySelector('[name=mapping]').value;});
  },
  /**
   * 绘制选中节点的属性。
   * @returns {string} 面板内容。
   */
  inspector() {
    const n=this.node();if(!n)return '';
    const sourceHelp={'指定成员':'张明、王强','角色':'总经理、财务总监','岗位、职级':'部门经理 · M3 及以上','直属上级':'第 1 级上级','连续多级上级':'从第 1 级到第 3 级','部门负责人':'发起人所在部门 · 第 1 级','业务数据里的部门':'department','分管领导':'生产制造分管领导','业务数据里的人员':'projectOwner','审批矩阵':'采购金额审批矩阵','业务系统接口':'ERP · 项目负责人接口','上一步指定':'限定在采购部选择','发起人自选':'限定部门负责人角色','发起人本人':'业务系统传入的发起人'};
    const common=`<div class="inspector-title"><h3>节点设置</h3><span class="pill">${UI.escape(n.id)}</span></div><label class="field"><span>节点名称</span><input class="inp" value="${UI.escape(n.name)}" onchange="D.changeNode('name',this.value)"></label>`;
    if(n.type==='start')return common+`<div class="notice">由业务系统通过开放接口发起。此处配置谁能使用这个流程。</div><label class="field"><span>允许的接入应用</span><select class="sel" onchange="D.changeNode('people',this.value)"><option>ERP 业务系统、示例业务系统</option><option>ERP 业务系统</option></select></label><button class="btn" onclick="D.tab='rules';D.render()">配置发起范围与查看权限</button>`;
    if(['auto','sub','parallel','parallelJoin','inclusive','exclusive'].includes(n.type))return common+`<label class="field"><span>${n.type==='sub'?'子流程编码':n.type==='auto'?'业务动作接口':'分支说明'}</span><input class="inp" value="${UI.escape(n.people)}" onchange="D.changeNode('people',this.value)"></label><label class="check-row"><input type="checkbox" checked onchange="D.changeNode('wait',this.checked)">${n.type==='sub'?'等待子流程完成':'全部命中分支完成后继续'}</label><p class="hint">${n.type==='auto'?'接口失败自动重试，最终失败交由管理员处理。':n.type==='sub'?'传入 amount、department 和 businessKey。':'点击编辑连线设置分支目标和条件。'}</p><button class="btn" onclick="D.extraNode()">配置路径 / 字段映射</button>`;
    return common+`<label class="field"><span>审批人来源 <span class="muted">14 种来源</span></span><select class="sel" onchange="D.changeSource(this.value)">${this.sources.map(s=>`<option ${s===n.source?'selected':''}>${s}</option>`).join('')}</select></label><div class="source-detail"><label class="field" style="margin-bottom:0"><span>${n.source}配置</span><input class="inp" value="${UI.escape(n.people)}" placeholder="${sourceHelp[n.source]}" onchange="D.changeNode('people',this.value)"></label><p class="small-label" style="margin-bottom:0">${sourceHelp[n.source]}</p></div>${n.type==='cc'?'<label class="check-row"><input type="checkbox" onchange="D.changeNode(\'readRequired\',this.checked)">要求抄送人点击“已阅”</label>':`<div class="field"><span>多人审批方式</span><div class="option-grid">${['依次审批','会签','或签','票签'].map(m=>`<label><input type="radio" name="method" ${n.method===m?'checked':''} onchange="D.changeNode('method','${m}')">${m}</label>`).join('')}</div><p class="small-label">${{依次审批:'按顺序依次处理，全部同意后通过。',会签:'所有人同意后通过，可设置拒绝后的处理方式。',或签:'任意一人同意即通过，其余任务自动取消。',票签:'赞成票达到设定比例后通过，支持权重。'}[n.method]}</p>${n.method==='票签'?`<label class="field"><span>通过比例（%）</span><input class="inp" type="number" min="1" max="100" value="${n.ratio||67}" onchange="D.changeNode('ratio',this.value)"></label><label class="field"><span>人员权重</span><input class="inp" value="${UI.escape(n.weights||'总经理: 2, 财务总监: 1')}" onchange="D.changeNode('weights',this.value)"></label>`:''}</div><div class="inspector-section"><h4>操作权限</h4><div class="option-grid">${['退回','转交','加签','减签','征询意见','要求补充','抄送','撤销同意','评论与@','暂存','批量同意'].map(a=>`<label><input type="checkbox" ${!(n.denied||[]).includes(a)?'checked':''} onchange="D.operation('${a}',this.checked)">${a}</label>`).join('')}</div></div><div class="inspector-section"><h4>意见与字段</h4><label class="check-row"><input type="checkbox" ${n.commentRequired?'checked':''} onchange="D.changeNode('commentRequired',this.checked)">审批意见必填</label><label class="check-row"><input type="checkbox" ${n.signature?'checked':''} onchange="D.changeNode('signature',this.checked)">要求手写 / 图片签名</label><button class="btn sm" onclick="D.permissions()">配置字段权限</button></div>`}<div class="inspector-section"><button class="btn sm danger" onclick="D.deleteNode()">删除此节点</button></div>`;
  },
  /**
   * 更新节点属性并重绘。
   * @param {string} key 属性名称。
   * @param {unknown} value 属性值。
   * @returns {void}
   */
  changeNode(key,value) {this.node()[key]=value;this.render();},
  /**
   * 切换审批人来源并清空原来源选择。
   * @param {string} value 审批人来源。
   * @returns {void}
   */
  changeSource(value) {this.node().source=value;this.node().people='';this.render();},
  /**
   * 更新节点允许的审批动作。
   * @param {string} name 操作名称。
   * @param {boolean} allowed 是否允许。
   * @returns {void}
   */
  operation(name,allowed) {const n=this.node();n.denied=(n.denied||[]).filter(a=>a!==name);if(!allowed)n.denied.push(name);},
  /**
   * 更新流程设置。
   * @param {string} key 属性名。
   * @param {unknown} value 属性值。
   * @returns {void}
   */
  setting(key,value) {this.state.settings[key]=value;if(['columns','uppercase','printTitle','company','paper','printMode','watermark'].includes(key))this.render();},
  /**
   * 保存独立于生产系统的本地草稿。
   * @returns {void}
   */
  save() {this.state.saved=new Date().toLocaleTimeString('zh-CN',{hour:'2-digit',minute:'2-digit'});localStorage.setItem('approval-prototype-designer-'+this.state.flowKey,JSON.stringify(this.state));this.render();UI.toast('草稿已保存到当前浏览器。');},
  /**
   * 打开新增节点面板。
   * @returns {void}
   */
  addNode() {UI.modal('添加流程节点',`<div class="form-grid"><label class="field">节点类型<select name="type" class="sel"><option value="approval">审批人</option><option value="handle">办理人</option><option value="cc">抄送人</option><option value="sub">子流程</option><option value="auto">自动节点</option><option value="parallel">并行分支</option><option value="inclusive">包容分支</option><option value="exclusive">条件分支</option></select></label><label class="field">节点名称<input name="name" class="inp" placeholder="如：法务审核"></label><label class="field">审批人 / 目标配置<input name="people" class="inp" value="指定成员"></label></div><p class="hint">节点将插入流程结束之前，可继续设置审批规则。</p>`,dialog=>{const name=dialog.querySelector('[name=name]').value.trim();if(!name){UI.toast('请填写节点名称。');return false;}const prev=this.state.nodes.at(-1);const node={id:'node'+Date.now(),name,type:dialog.querySelector('[name=type]').value,people:dialog.querySelector('[name=people]').value,source:'指定成员',method:'依次审批',x:420,y:450};this.state.nodes.push(node);this.state.edges.push([prev.id,node.id,'']);this.selected=node.id;this.render();},'添加节点');},
  /**
   * 删除可选节点，保留示例主路径结构。
   * @returns {void}
   */
  deleteNode() {const n=this.node();if(this.state.nodes.indexOf(n)<5){UI.modal('保留基础流程节点','<p>当前演示流程的基础节点用于展示完整主路径。可以编辑名称、选人和规则；新增的节点可以删除。</p>',()=>{} ,'知道了');return;}this.state.nodes=this.state.nodes.filter(a=>a.id!==n.id);this.state.edges=this.state.edges.filter(e=>!e.includes(n.id));this.selected='department';this.render();},
  /**
   * 修改大额采购条件。
   * @returns {void}
   */
  condition() {UI.modal('条件分支 · 金额判断',`<div class="form-grid"><label class="field">字段<select class="sel"><option>单据金额 amount</option></select></label><label class="field">比较方式<select class="sel"><option>大于等于 ≥</option></select></label><label class="field">金额阈值<input name="amount" class="inp" type="number" min="0" value="${this.state.threshold}"></label></div><div class="notice">从左到右取第一条满足的条件；未命中时进入“其他情况”分支。</div>`,dialog=>{const value=Number(dialog.querySelector('[name=amount]').value);if(!Number.isFinite(value)||value<0){UI.toast('请输入有效金额。');return false;}this.state.threshold=value;this.state.edges[1][2]='金额 ≥ '+value.toLocaleString();this.render();});},
  /**
   * 将树形草稿单向转换为自由画布。
   * @returns {void}
   */
  convert() {UI.modal('转为自由画布','<p>保留现有节点、审批规则与数据字段，转换后可拖动节点、编辑连线与回路。</p><div class="notice warn">自由画布无法转回钉钉式。转换只影响当前草稿，已发布版本不变。</div>',()=>{this.state.mode='canvas';this.render();},'确认转换');},
  /**
   * 编辑或新增自由画布连线。
   * @param {number} index 已有连线序号。
   * @returns {void}
   */
  edge(index=-1) {const e=this.state.edges[index]||['department','management',''];const options=selected=>this.state.nodes.map(n=>`<option value="${n.id}" ${n.id===selected?'selected':''}>${UI.escape(n.name)}</option>`).join('');UI.modal('连线与条件',`<label class="field">起点<select class="sel" name="from">${options(e[0])}</select></label><label class="field">终点<select class="sel" name="to">${options(e[1])}</select></label><label class="field">条件表达式 / 路线说明<input class="inp" name="condition" value="${UI.escape(e[2])}" placeholder="如 amount >= 10000 或其他情况"></label><p class="hint">回路可连接到先前节点。完整执行图校验由正式引擎实现。</p>`,dialog=>{const from=dialog.querySelector('[name=from]').value,to=dialog.querySelector('[name=to]').value;if(from===to){UI.toast('连线起点与终点不能相同。');return false;}const next=[from,to,dialog.querySelector('[name=condition]').value];if(index<0)this.state.edges.push(next);else this.state.edges[index]=next;this.render();});},
  /**
   * 为自由画布节点及详情字段绑定拖动。
   * @returns {void}
   */
  bindDrag() {
    document.querySelectorAll('.canvas-node').forEach(el=>{el.onpointerdown=event=>{if(event.button!==0)return;const n=this.state.nodes.find(a=>a.id===el.dataset.node),x=event.clientX,y=event.clientY,ox=n.x,oy=n.y;let moved=false;el.setPointerCapture(event.pointerId);el.onpointermove=e=>{if(Math.abs(e.clientX-x)+Math.abs(e.clientY-y)>4)moved=true;n.x=Math.max(0,ox+e.clientX-x);n.y=Math.max(0,oy+e.clientY-y);el.style.left=n.x+'px';el.style.top=n.y+'px';};el.onpointerup=()=>{el.onpointermove=null;if(moved){el.onclick=e=>e.preventDefault();this.selected=n.id;this.render();}};};});
    let dragging='';document.querySelectorAll('.layout-cell').forEach(el=>{el.ondragstart=()=>{dragging=el.dataset.field;};el.ondragover=e=>e.preventDefault();el.ondrop=e=>{e.preventDefault();const from=this.state.fields.findIndex(f=>f.key===dragging),to=this.state.fields.findIndex(f=>f.key===el.dataset.field);if(from<0||to<0)return;this.state.fields.splice(to,0,this.state.fields.splice(from,1)[0]);this.render();};});
  },
  /**
   * 检查原型可验证的发布前置条件。
   * @returns {string[]} 校验结果。
   */
  errors() {const errors=[];if(this.state.nodes.some(n=>!n.name.trim()))errors.push('节点名称不能为空。');if(this.state.nodes.some(n=>!n.people.trim()))errors.push('存在未设置处理人或目标的节点。');if(this.state.fields.some(f=>!f.key.trim()||!f.name.trim()))errors.push('字段标识与显示名称不能为空。');if(new Set(this.state.fields.map(f=>f.key)).size!==this.state.fields.length)errors.push('存在重复的字段标识。');if(!this.state.linear&&!this.state.fields.some(f=>f.key==='amount'))errors.push('条件分支引用的 amount 字段不存在。');return errors;},
  /**
   * 展示流程校验报告。
   * @returns {void}
   */
  validate() {const errors=this.errors();UI.modal(errors.length?'发现需要修正的配置':'基础配置检查通过',errors.length?`<ul>${errors.map(e=>`<li>${e}</li>`).join('')}</ul>`:'<div class="notice">节点名称、处理人、字段标识及条件字段检查通过。</div><p class="hint">原型检查仅覆盖基础配置，执行图可达性、网关配对和表达式校验将在正式引擎中完成。</p>',()=>{},'关闭');},
  /**
   * 校验草稿并发布新的本地演示版本。
   * @returns {void}
   */
  publish() {if(this.errors().length){this.validate();return;}UI.modal('发布新版本',`<div class="notice">基础配置检查通过，将发布为 V${this.state.version+1}。</div><label class="field">发布说明<textarea name="note" class="inp" rows="3" placeholder="说明本次流程调整"></textarea></label><p class="hint">在途审批仍使用原版本；此原型仅保存浏览器本地版本。</p>`,dialog=>{const note=dialog.querySelector('[name=note]').value.trim();if(!note){UI.toast('请填写发布说明。');return false;}this.state.version++;this.state.history.unshift({version:this.state.version,note,date:new Date().toLocaleString('zh-CN')});this.state.publishedNodes=this.state.nodes.length;this.state.publishedFields=this.state.fields.length;this.state.publishedMode=this.state.mode;this.tab='versions';this.save();UI.toast('本地演示版本 V'+this.state.version+' 已发布。');},'确认发布');},
  /**
   * 打开预览条件输入与预估审批路径。
   * @returns {void}
   */
  preview() {UI.modal('流程预览 · '+this.state.name,`<div class="form-grid"><label class="field">申请人<select class="sel"><option>王强 · 生产一部</option></select></label><label class="field">${this.state.flowKey==='expense'?'报销金额':'单据金额'}<input class="inp" id="preview-amount" type="number" value="28600" min="0"></label></div><div id="preview-result">${this.pathHtml(28600)}</div><p class="hint">审批人按示例组织关系预估，正式流转到节点时重新计算。</p>`,dialog=>{const amount=Number(dialog.querySelector('#preview-amount').value);if(!Number.isFinite(amount)||amount<0){UI.toast('请输入有效金额。');return false;}dialog.querySelector('#preview-result').innerHTML=this.pathHtml(amount);return false;},'重新计算路径');},
  /**
   * 生成与金额条件匹配的预览路径。
   * @param {number} amount 采购金额。
   * @returns {string} 路径内容。
   */
  pathHtml(amount) {
    const nodes=this.state.nodes.filter(n=>this.state.linear||n.id!=='management'||amount>=this.state.threshold);
    return `${this.state.flowKey==='quality'?'<div class="notice">需要返工时：质检确认 → 生产返工 → 重新质检；合格后由质量、生产经理并行审批。</div>':''}<ol class="simulation-steps">${nodes.map((n,i)=>`<li class="${i===0?'done':''}"><b>${UI.escape(n.name)}</b><div class="hint">${UI.escape(n.people)}${n.type==='parallel'?' · 全部完成后汇合':''}</div></li>`).join('')}<li><b>流程结束</b></li></ol>`;
  },
  /**
   * 启动按节点推进的原型测试。
   * @param {number} step 当前测试步骤。
   * @returns {void}
   */
  test(step=0) {const steps=['业务系统模拟提交',...this.state.nodes.filter(n=>n.type!=='start').map(n=>n.name+' · '+n.people),'流程结束 · 审批已通过'];const end=steps.length-1;UI.modal('测试模式 · 不产生真实审批',`<div class="notice">使用当前流程的虚构示例数据，模拟各节点处理。</div><ol class="simulation-steps">${steps.map((s,i)=>`<li class="${i<step?'done':i===step?'current':''}"><b>${s}</b><div class="hint">${i<step?'已完成':i===step?'当前步骤':'等待流转'}</div></li>`).join('')}</ol>${step>0&&step<end?'<label class="field">测试意见<input class="inp" value="同意，按采购计划执行。"></label><button class="btn danger" onclick="UI.closeModal();D.testRejected()">模拟拒绝</button>':''}`,()=>{if(step<end){setTimeout(()=>this.test(step+1),0);}else UI.toast('测试完成：示例审批通过。');},step===0?'模拟提交':step===end?'结束测试':'同意 / 继续');},
  /**
   * 展示测试中的拒绝终态。
   * @returns {void}
   */
  testRejected() {UI.modal('测试结束 · 已拒绝','<div class="notice warn">审批被拒绝，流程结束。</div><p>模拟结果回调：业务系统收到“已拒绝”状态，可修改原单后重新提交。</p>',()=>{},'关闭测试');},
  /**
   * 设置节点的字段可见及审批填写权限。
   * @returns {void}
   */
  permissions() {const n=this.node();UI.modal('节点字段权限',`<table class="designer-table"><tr><th>字段</th><th>权限</th></tr>${this.state.fields.map((f,i)=>`<tr><td>${UI.escape(f.name)}</td><td><select class="sel" data-permission="${i}">${(f.source==='审批填写'?['可填写','只读','隐藏']:['只读','隐藏']).map(p=>`<option ${n.permissions?.[f.key]===p?'selected':''}>${p}</option>`).join('')}</select></td></tr>`).join('')}</table><p class="hint">业务系统传入的数据不能在审批中心修改。</p>`,dialog=>{n.permissions={};dialog.querySelectorAll('[data-permission]').forEach(el=>n.permissions[this.state.fields[Number(el.dataset.permission)].key]=el.value);});},
  /**
   * 修改业务数据字段。
   * @param {number} index 字段序号。
   * @param {string} key 属性名称。
   * @param {unknown} value 新值。
   * @returns {void}
   */
  field(index,key,value) {this.state.fields[index][key]=value;},
  /**
   * 添加字段并定位到新增行。
   * @returns {void}
   */
  addField() {this.state.fields.push({key:'field'+(this.state.fields.length+1),name:'新字段',type:'文本'});this.render();},
  /**
   * 移除一个数据字段。
   * @param {number} index 字段序号。
   * @returns {void}
   */
  removeField(index) {this.state.fields.splice(index,1);this.render();},
  /**
   * 编辑字段详细来源和计算规则。
   * @param {number} index 字段序号。
   * @returns {void}
   */
  editField(index) {const f=this.state.fields[index];UI.modal('字段设置 · '+f.name,`<label class="field">数据来源<select name="source" class="sel"><option ${f.source!=='审批填写'?'selected':''}>业务系统传入</option><option ${f.source==='审批填写'?'selected':''}>审批填写</option></select></label><label class="field">说明 / 计算表达式 / 明细列<textarea name="note" class="inp" rows="3">${UI.escape(f.note||(f.type==='明细表'?'name: 文本, quantity: 数字, amount: 金额':f.type==='计算字段'?'SUM(items.amount)':''))}</textarea></label><p class="hint">计算字段只用于条件和展示，不回写业务单据。</p>`,dialog=>{f.source=dialog.querySelector('[name=source]').value;f.note=dialog.querySelector('[name=note]').value;this.render();});},
  /**
   * 从本地 JSON 示例生成字段。
   * @returns {void}
   */
  importSchema() {UI.modal('从 JSON 示例生成字段','<label class="field">业务数据示例<textarea name="json" class="inp mono" rows="7">{"projectName":"产线升级","budget":50000,"urgent":false}</textarea></label><p class="hint">追加不存在的字段，同名字段保留现有定义。</p>',dialog=>{let data;try{data=JSON.parse(dialog.querySelector('[name=json]').value);if(!data||Array.isArray(data)||typeof data!=='object')throw new Error();}catch{UI.toast('请输入 JSON 对象。');return false;}Object.entries(data).forEach(([key,v])=>{if(!this.state.fields.some(f=>f.key===key))this.state.fields.push({key,name:key,type:Array.isArray(v)?'明细表':typeof v==='number'?'数字':'文本'});});this.render();},'生成字段');},
  /**
   * 提供详情布局中的只读示例值。
   * @param {object} field 字段。
   * @returns {string} 虚构显示值。
   */
  sample(field) {return {applicant:'王强',department:'生产一部',amount:'¥ 28,600.00',purpose:this.state.flowKey==='expense'?'客户现场服务差旅':'产线设备维护与备件补充',date:'2026-10-13',items:this.state.flowKey==='expense'?'交通费 / 住宿费 / 市内交通':'高精度轴承 ×20 / 液压控制组件 ×5',attachments:this.state.flowKey==='expense'?'行程与票据.pdf':'业务单据附件.pdf',approvedAmount:'由审批人填写',paymentDate:'由出纳填写',contractName:'产线设备年度维护协议',supplier:'华强电子',parentBusinessKey:'PR20261013001',defect:'设备运行振动异常，抽检未通过',batch:'LOT-20261013-A',rework:'是',result:'由质量经理填写',project:'产线升级项目'}[field.key]||'示例数据';},
  /**
   * 展示当前详情排版和字段顺序。
   * @returns {void}
   */
  detailPreview() {UI.modal(this.state.name+' · 详情预览',`<div class="notice">审批人看到的只读业务快照</div><div class="layout-cells" style="grid-template-columns:repeat(${parseInt(this.state.settings.columns||'2')},1fr)">${this.state.fields.filter(f=>!f.hidden).map(f=>`<div class="layout-cell"><span>${UI.escape(f.name)}</span>${UI.escape(this.sample(f))}</div>`).join('')}</div>`,()=>{},'关闭预览');},
  /**
   * 展示可打印审批单。
   * @returns {void}
   */
  printPreview() {UI.modal('打印预览 · 示例审批单',`<div class="paper">${DesignerConfig.paper()}</div>`,()=>{window.print();return false;},'浏览器打印 / 存为 PDF');},
  /**
   * 修改流程基础信息。
   * @returns {void}
   */
  basic() {UI.modal('流程基本信息',`<label class="field">流程编码<input class="inp mono" value="${this.state.code}" disabled></label><label class="field">分类<select class="sel" name="category"><option>采购管理</option><option>财务管理</option><option>人事行政</option></select></label><label class="field">描述<textarea class="inp" name="description" rows="3">${UI.escape(this.state.description||(this.state.name+'的审批规则与处理路径。'))}</textarea></label>`,dialog=>{this.state.description=dialog.querySelector('[name=description]').value;this.state.category=dialog.querySelector('[name=category]').value;this.render();});},
  /**
   * 导出本原型的可恢复草稿。
   * @returns {void}
   */
  export() {const blob=new Blob([JSON.stringify({prototype:'approval-designer',version:1,state:this.state},null,2)],{type:'application/json'});const a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download=this.state.code.toLowerCase()+'.prototype.json';a.click();setTimeout(()=>URL.revokeObjectURL(a.href),1000);UI.toast('原型草稿已导出。正式流程文件格式由契约任务确定。');},
  /**
   * 导入仅由本原型导出的草稿文件。
   * @returns {void}
   */
  import() {UI.modal('导入原型草稿','<label class="field">选择原型 JSON 文件<input class="inp" type="file" accept=".json" name="file"></label><p class="hint">仅支持本原型导出的文件，导入后替换当前草稿；不接受其他工具的流程文件。</p>',dialog=>{const file=dialog.querySelector('[name=file]').files[0];if(!file){UI.toast('请选择文件。');return false;}file.text().then(text=>{try{const data=JSON.parse(text);if(data.prototype!=='approval-designer'||data.version!==1||!Array.isArray(data.state?.nodes)||data.state.nodes.length<5||!Array.isArray(data.state.fields))throw new Error();this.state=data.state;this.selected='department';this.render();UI.closeModal();UI.toast('原型草稿导入完成。');}catch{UI.toast('文件格式或版本不兼容，未修改当前草稿。');}});return false;},'导入草稿');}
};
D.init();
