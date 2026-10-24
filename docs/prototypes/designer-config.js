/**
 * 生成设计器的字段、规则、详情与打印配置页面。
 */
const DesignerConfig = {
  /**
   * 生成一个绑定流程设置的下拉控件。
   * @param {string} key 设置键。
   * @param {string} label 中文名称。
   * @param {string[]} values 可选项。
   * @returns {string} 控件内容。
   */
  select(key, label, values) {
    return `<div class="config-row"><label for="cfg-${key}">${label}</label><select id="cfg-${key}" class="sel" onchange="D.setting('${key}',this.value)">${values.map(value => `<option ${D.state.settings[key] === value ? 'selected' : ''}>${value}</option>`).join('')}</select></div>`;
  },
  /**
   * 生成一个绑定流程设置的输入控件。
   * @param {string} key 设置键。
   * @param {string} label 中文名称。
   * @param {string} value 默认值。
   * @returns {string} 控件内容。
   */
  input(key, label, value) {
    return `<div class="config-row"><label for="cfg-${key}">${label}</label><input id="cfg-${key}" class="inp" value="${UI.escape(D.state.settings[key] ?? value)}" oninput="D.setting('${key}',this.value)"></div>`;
  },
  /**
   * 按职责划分流程级规则；节点级审批方式和操作权限在画布属性面板维护。
   * @returns {object[]} 六组规则定义。
   */
  policyGroups() {
    return [
      {id:'people',title:'人员与去重',icon:'user',description:'审批人到达节点时计算，预览差异会留下记录。',fields:[
        ['empty','找不到审批人','不允许发起，提示原因',['不允许发起，提示原因','自动通过','转交流程管理员','转交指定成员']],
        ['emptyPeople','指定兜底成员','何静'],
        ['disabled','审批人离职 / 停用','转交直属上级',['转交直属上级','转交流程管理员']],
        ['self','审批人是发起人','自动跳过',['自动跳过','由本人审批','转交直属上级']],
        ['repeat','同一人多次出现','相邻的只审一次',['相邻的只审一次','整个流程只审一次','每次都审']],
        ['multiDepartment','多部门人员发起','以业务系统传入部门为准，未传用主部门',['以业务系统传入部门为准，未传用主部门']],
        ['assigneeTime','审批人计算时机','到达节点时按最新组织计算',['到达节点时按最新组织计算']]
      ],note:'14 种审批人来源在节点属性中选择；连续上级、角色范围与取人接口按节点配置。'},
      {id:'return',title:'退回与恢复',icon:'undo',description:'区分退回重走、补充原节点恢复和撤销同意。',fields:[
        ['return','允许退回到','发起人、上一步及已处理节点',['发起人、上一步及已处理节点','仅发起人','仅上一步']],
        ['resubmit','退回后重新提交','从头开始审批',['从头开始审批','回到退回的节点']],
        ['parallelReturn','并行退回目标','仅同分支或分叉前节点',['仅同分支或分叉前节点']],
        ['parallelImpact','跨分叉退回确认','确认目标及受影响分支后提交',['确认目标及受影响分支后提交']],
        ['supplementResume','补充完成后','恢复到要求补充的原节点',['恢复到要求补充的原节点']],
        ['withdraw','业务系统撤回','审批结束前',['审批结束前','第一个人处理前']],
        ['undoApproval','审批人撤销同意','下一步无人处理时允许',['下一步无人处理时允许','不允许']],
        ['revokeFlow','已通过单据撤销','由业务系统另行发起撤销审批',['由业务系统另行发起撤销审批']]
      ],note:'补充、修改业务数据与重新提交均在业务系统完成。并行影响需要预览确认，原型不模拟令牌回滚或分支重激活。'},
      {id:'deadline',title:'时限与催办',icon:'clock',description:'初版按工作日历提醒，不自动替审批人作决定。',fields:[
        ['limit','默认节点时限','2 个工作日'],
        ['calendar','工作日历','公司标准工作日历',['公司标准工作日历']],
        ['before','到期前提醒','4 小时'],
        ['reminder','超时提醒对象','审批人和发起人',['审批人和发起人','仅审批人']],
        ['daily','每日待办汇总','每天 09:00 邮件汇总',['每天 09:00 邮件汇总','关闭']],
        ['remindLimit','手动催办频率','同一步每小时最多一次',['同一步每小时最多一次']],
        ['overdueEscalation','初版超时处理','仅发送提醒，不自动流转',['仅发送提醒，不自动流转']]
      ],note:'工作日历维护节假日与调休。自动同意、自动转交及升级流转属于二期，本页不启用。'},
      {id:'delegation',title:'代理与转交',icon:'users',description:'代理保留双方身份，流程和节点共同限制可执行操作。',fields:[
        ['delegationScope','代理适用范围','允许全部流程或指定流程',['允许全部流程或指定流程']],
        ['delegationExisting','已有待办交给代理','由委托人选择是否一并转交',['由委托人选择是否一并转交']],
        ['delegationChain','连续委托','只到第一层代理，不连环委托',['只到第一层代理，不连环委托']],
        ['delegationSelf','代理人是发起人','沿用本流程的自审规则',['沿用本流程的自审规则']],
        ['transferScope','节点转交默认值','允许转交，由节点配置收窄',['允许转交，由节点配置收窄','默认禁止，由节点明确允许']],
        ['transferReason','转交与代理记录','记录处理人、被代理人和操作意见',['记录处理人、被代理人和操作意见']],
        ['managers','流程管理员',D.state.owner||'流程管理员']
      ],note:'委托开始结束时间、代理人和具体流程在个人委托设置中维护。加签、减签、征询意见和抄送由节点操作开关控制。'},
      {id:'visibility',title:'意见与可见',icon:'preview',description:'意见与签名进入审批记录；业务字段保持来源系统所有。',fields:[
        ['commentPolicy','默认意见要求','拒绝、退回时必填',['拒绝、退回时必填','所有审批操作必填','由节点逐项设置']],
        ['signaturePolicy','签名方式','手写签名或个人签名图片',['手写签名或个人签名图片','不要求签名']],
        ['attachmentPolicy','审批意见附件','允许，受审批单查看权限控制',['允许，受审批单查看权限控制','不允许']],
        ['view','审批单查看权限','发起人、参与人、抄送人',['发起人、参与人、抄送人','仅参与人（保密流程）']],
        ['extraView','额外查看人','本部门负责人'],
        ['confidentialStats','保密流程统计','仅显示数量，不暴露业务明细',['仅显示数量，不暴露业务明细']],
        ['dataVisibility','节点字段可见性','在各节点属性中配置隐藏字段',['在各节点属性中配置隐藏字段']],
        ['signaturePrint','意见与签名打印','随节点意见显示在审批单',['随节点意见显示在审批单']]
      ],note:'电子签章暂不考虑。签名图片不等于电子签章。评论、附件、暂存和常用语在审批中心演示。'},
      {id:'integration',title:'接入与业务联动',icon:'send',description:'校验阻断、自动动作重试和结果回调分别配置。',fields:[
        ['app','允许接入应用','ERP 业务系统、示例业务系统',['ERP 业务系统、示例业务系统','ERP 业务系统','示例业务系统']],
        ['departments','允许发起部门','所有部门'],
        ['roles','允许发起角色 / 岗位','不限'],
        ['hookBeforeStart','发起前业务校验','按接入应用配置调用',['按接入应用配置调用','不调用']],
        ['hookFailure','校验拒绝 / 接口失败','阻止当前操作并显示原因，允许重试',['阻止当前操作并显示原因，允许重试']],
        ['hookTimeout','业务钩子超时','5 秒'],
        ['automaticFailure','自动节点失败','进入重试队列，耗尽后人工处理',['进入重试队列，耗尽后人工处理']],
        ['callbackDedup','结果回调去重','接收方按 eventId 去重',['接收方按 eventId 去重']],
        ['callbackRetry','失败重试间隔','1 分钟、5 分钟、30 分钟、2 小时'],
        ['dataUpdate','审批中业务数据更新','仅允许约定字段更新',['仅允许约定字段更新','不允许更新']]
      ],note:'同意前校验、取人接口和自动业务动作在对应节点选择。外部调用经队列和出站适配层；原型不发送请求，不在数据库事务中等待外部系统。'}
    ];
  },
  /**
   * 初始化缺少的规则值，保留用户已保存的流程草稿。
   * @returns {object[]} 可呈现的规则分组。
   */
  ensurePolicies() {
    D.state.settings ||= {};
    const groups=this.policyGroups(),settings=D.state.settings;
    if(!Array.isArray(settings.policyMigrationNotes))settings.policyMigrationNotes=[];
    const aliases={empty:{'转交指定的人':'转交指定成员'},withdraw:{'首人处理前':'第一个人处理前','第一个人审批前':'第一个人处理前'},resubmit:{'从头走':'从头开始审批','回到退回节点':'回到退回的节点'},disabled:{'转交他的直属上级':'转交直属上级'}};
    Object.entries(aliases).forEach(([key,map])=>{const old=settings[key];if(Object.hasOwn(map,old)){settings[key]=map[old];settings.policyMigrationNotes.push(`“${old}”已按同义项显示为“${map[old]}”。`);}});
    if(typeof settings.limit==='number'||/^\d+(?:\.\d+)?$/.test(String(settings.limit??''))){const old=settings.limit;settings.limit=old+' 个工作日';settings.policyMigrationNotes.push(`旧节点时限“${old}”补充为“${settings.limit}”，请核对工作日单位。`);}
    groups.forEach(group=>group.fields.forEach(([key,,value])=>{if(D.state.settings[key]===undefined)D.state.settings[key]=value;}));
    if(!groups.some(group=>group.id===D.state.settings.policyGroup))D.state.settings.policyGroup='people';
    return groups;
  },
  /**
   * 生成规则分组导航、摘要与当前配置面板。
   * @returns {string} 规则页内容。
   */
  renderPolicies() {
    const groups=this.ensurePolicies(),active=groups.find(group=>group.id===D.state.settings.policyGroup),settings=D.state.settings;
    const summaries={people:`空审批人：${settings.empty}`,return:`重提：${settings.resubmit}`,deadline:`时限：${settings.limit}`,delegation:`${settings.transferScope}`,visibility:`${settings.view}`,integration:`回调按 eventId 去重`};
    return `${settings.policyMigrationNotes.length?`<div class="notice" style="margin-bottom:14px"><b>旧草稿配置已兼容</b><br>${settings.policyMigrationNotes.map(note=>UI.escape(note)).join("<br>")}</div>`:""}<div class="toolbar"><span class="pill accent">流程默认规则</span><span class="hint">节点可进一步限制操作 · 配置只保存在本地草稿</span><span class="spacer"></span><button class="btn" onclick="DesignerConfig.policySummary()">查看规则摘要</button><button class="btn primary" onclick="DesignerConfig.savePolicies()">${UI.icon('check')} 保存规则</button></div><nav class="tabs" aria-label="审批规则分组">${groups.map(group=>`<button class="${group.id===active.id?'active':''}" onclick="DesignerConfig.changePolicyGroup('${group.id}')">${UI.icon(group.icon)}${group.title}</button>`).join('')}</nav><div class="config-grid"><section class="config-card"><h3>${UI.escape(active.title)} <span class="pill">${active.fields.length} 项</span></h3><p class="hint" style="margin-bottom:16px">${active.description}</p>${active.fields.filter(([key])=>(key!=='emptyPeople'||settings.empty==='转交指定成员')&&(key!=='extraView'||settings.view!=='仅参与人（保密流程）')).map(field=>this.policyControl(field)).join('')}<div class="notice" style="margin-top:16px">${active.note}</div>${active.id==='deadline'?'<div class="toolbar" style="margin-top:14px"><button class="btn sm" disabled>超时自动同意 · 二期</button><button class="btn sm" disabled>超时自动转交 · 二期</button><a class="btn sm" href="admin.html#org/calendar">工作日历</a></div>':''}</section><aside><section class="config-card"><h3>当前流程规则摘要</h3>${groups.map(group=>`<div style="display:flex;align-items:center;justify-content:space-between;gap:12px;padding:10px 0;border-bottom:1px solid var(--line)"><div><b style="font-size:12px">${group.title}</b><p class="hint" id="policy-summary-${group.id}">${UI.escape(summaries[group.id])}</p></div><button class="btn ghost sm" onclick="DesignerConfig.changePolicyGroup('${group.id}')">配置</button></div>`).join('')}<p class="hint" id="policy-save-status" style="margin-top:14px">${settings.policySavedAt?'规则保存于 '+UI.escape(settings.policySavedAt):'修改后保存规则，重新打开此流程时恢复配置。'}</p></section><section class="config-card" style="margin-top:16px"><h3>节点审批方式与操作权限</h3>${D.state.nodes.filter(node=>node.type==='approval').map(node=>`<div style="display:flex;align-items:center;gap:10px;justify-content:space-between;padding:9px 0"><span>${UI.escape(node.name)}<small class="hint" style="display:block">${UI.escape(node.method||'依次审批')}</small></span><button class="btn sm" onclick="DesignerConfig.openPolicyNode('${UI.escape(node.id)}')">节点设置</button></div>`).join('')}<p class="hint">会签拒绝策略、或签完成、票签阈值与权重，以及转交、加减签、征询、批量同意等操作在节点面板保存。</p></section></aside></div>`;
  },
  /**
   * 生成单个规则控件，固定业务约束显示为只读文本。
   * @param {Array} field 键、名称、默认值和可选值。
   * @returns {string} 带标签的控件内容。
   */
  policyControl(field) {
    const [key,label,,values]=field,value=D.state.settings[key];
    const unknown=values&&!values.includes(value);
    if(values?.length===1&&!unknown)return `<div class="config-row"><label>${label}</label><div style="font-size:12px;color:var(--ink-2)">${UI.escape(value)} <span class="pill">固定规则</span></div></div>`;
    const change=`DesignerConfig.setPolicy('${key}',this.value)`;
    return `<div class="config-row"><label for="policy-${key}">${label}</label>${values?`<div><select class="sel" id="policy-${key}" data-policy-key="${key}" onchange="${change}">${unknown?`<option value="${UI.escape(value)}" selected>${UI.escape(value)}（旧值待核对）</option>`:''}${values.map(option=>`<option ${option===value?'selected':''}>${UI.escape(option)}</option>`).join('')}</select>${unknown?'<p class="hint">旧值已保留，请选择当前支持的设置后保存。</p>':''}</div>`:`<input class="inp" id="policy-${key}" data-policy-key="${key}" value="${UI.escape(value)}" oninput="DesignerConfig.setPolicy('${key}',this.value,false)">`}</div>`;
  },
  /**
   * 更新草稿中的流程规则并重新计算摘要。
   * @param {string} key 规则键。
   * @param {string} value 配置值。
   * @param {boolean} render 是否重新渲染，文本输入时关闭以保留焦点与保存按钮点击。
   * @returns {void} 无返回值。
   */
  setPolicy(key,value,render=true) {
    D.state.settings[key]=value;delete D.state.settings.policySavedAt;
    if(render){D.render();return;}
    const keys={empty:['people','空审批人：'],resubmit:['return','重提：'],limit:['deadline','时限：'],transferScope:['delegation',''],view:['visibility','']},summary=keys[key];
    const target=summary?document.getElementById('policy-summary-'+summary[0]):null;if(target)target.textContent=summary[1]+value;
    const status=document.getElementById('policy-save-status');if(status)status.textContent='有改动待保存。';
  },
  /**
   * 切换规则分组，保存于该流程草稿的界面状态。
   * @param {string} group 分组键。
   * @returns {void} 无返回值。
   */
  changePolicyGroup(group) {D.state.settings.policyGroup=group;D.render();},
  /**
   * 校验并保存流程规则，不执行审批或访问网络。
   * @returns {void} 无返回值。
   */
  savePolicies() {
    const groups=this.ensurePolicies(),settings=D.state.settings;
    // 保存前读取正在编辑的控件，避免浏览器尚未触发失焦时漏掉最后一次输入。
    document.querySelectorAll('[data-policy-key]').forEach(control=>{settings[control.dataset.policyKey]=control.value;});
    const unknown=groups.flatMap(group=>group.fields).find(([key,,,values])=>values&&!values.includes(settings[key]));
    if(unknown){UI.toast('请核对“'+unknown[1]+'”的旧配置。');this.changePolicyGroup(groups.find(group=>group.fields.some(field=>field[0]===unknown[0])).id);return;}
    const missing=groups.flatMap(group=>group.fields).find(([key])=>!(key==='emptyPeople'&&settings.empty!=='转交指定成员')&&!String(settings[key]??'').trim());
    if(missing){UI.toast('请填写“'+missing[1]+'”。');const group=groups.find(item=>item.fields.some(field=>field[0]===missing[0]));this.changePolicyGroup(group.id);return;}
    for(const key of ['limit','before','hookTimeout']){const value=String(settings[key]);if(!/^[0-9]+(?:\.[0-9]+)?\s*(?:个工作日|工作日|小时|分钟|秒)$/.test(value)||parseFloat(value)<=0){const group=groups.find(item=>item.fields.some(field=>field[0]===key));UI.toast('时长请使用正数及单位，例如“2 个工作日”“4 小时”“5 秒”。');this.changePolicyGroup(group.id);return;}}
    settings.policySavedAt=new Date().toLocaleTimeString('zh-CN',{hour:'2-digit',minute:'2-digit'});D.save();
  },
  /**
   * 展示可复核的全部规则摘要，配置值均来自当前草稿。
   * @returns {void} 无返回值。
   */
  policySummary() {
    const groups=this.ensurePolicies();UI.modal('流程规则摘要',groups.map(group=>`<h3 style="font-size:13px;margin:16px 0 8px">${group.title}</h3><table class="designer-table"><tbody>${group.fields.map(([key,label])=>`<tr><th style="width:40%">${label}</th><td>${UI.escape(D.state.settings[key])}</td></tr>`).join('')}</tbody></table>`).join('')+'<p class="hint" style="margin-top:16px">仅保存原型配置；初版规则执行与跨分支一致性由后续引擎实现验证。</p>',()=>{},'关闭');
  },
  /**
   * 跳到指定审批节点的规则面板。
   * @param {string} id 节点标识。
   * @returns {void} 无返回值。
   */
  openPolicyNode(id) {D.selected=id;D.tab='flow';D.render();},
  /**
   * 绘制业务系统传入数据的定义表。
   * @returns {string} 页面内容。
   */
  fields() {
    const types = ['文本','数字','金额','日期','人员','部门','附件','明细表','计算字段'];
    return `<div class="notice">定义业务系统需要传入的审批数据。业务单据在业务系统创建和修改，审批服务保存提交时的数据快照。</div><div class="toolbar"><b>数据字段</b><span class="pill">${D.state.fields.length} 个字段</span><span style="flex:1"></span><button class="btn" onclick="D.importSchema()">从 JSON 示例生成</button><button class="btn primary" onclick="D.addField()">＋ 添加字段</button></div><div class="table-wrap"><table class="designer-table"><thead><tr><th style="width:35px">#</th><th style="width:19%">字段标识 key</th><th style="width:17%">显示名称</th><th style="width:13%">数据类型</th><th>必传</th><th>摘要</th><th>来源 / 说明</th><th></th></tr></thead><tbody>${D.state.fields.map((f,i) => `<tr><td class="muted">${i+1}</td><td><input aria-label="字段标识 ${i+1}" class="inp mono" value="${UI.escape(f.key)}" oninput="D.field(${i},'key',this.value)"></td><td><input aria-label="字段名称 ${i+1}" class="inp" value="${UI.escape(f.name)}" oninput="D.field(${i},'name',this.value)"></td><td><select aria-label="字段类型 ${i+1}" class="sel" onchange="D.field(${i},'type',this.value)">${types.map(t => `<option ${f.type===t?'selected':''}>${t}</option>`).join('')}</select></td><td><input aria-label="必传 ${f.name}" type="checkbox" ${f.required?'checked':''} onchange="D.field(${i},'required',this.checked)"></td><td><input aria-label="摘要 ${f.name}" type="checkbox" ${f.summary?'checked':''} onchange="D.field(${i},'summary',this.checked)"></td><td>${f.type==='计算字段'?'<span class="mono">SUM(items.amount)</span>':f.source==='审批填写'?'<span class="pill accent">审批人填写</span>':'<span class="muted">业务系统传入</span>'}</td><td><button class="btn sm ghost" onclick="D.editField(${i})">设置</button><button class="btn sm danger" aria-label="删除 ${f.name}" onclick="D.removeField(${i})">×</button></td></tr>`).join('')}</tbody></table></div><div class="design-footer"><span>摘要字段建议选择 3～5 个，在待办、邮件与提醒中展示</span><span>审批人填写的字段随回调交由业务系统处理</span></div><div class="config-card" style="margin-top:18px"><h3>发起校验与数据更新</h3><div class="config-grid"><div>${this.select('dataUpdate','审批中数据更新',['仅允许约定字段更新','不允许更新'])}${this.input('updateFields','可更新字段','attachments, remark')}</div><div>${this.select('missingField','字段缺失时',['拒绝发起并返回字段原因'])}${this.input('serial','审批编号规则','SP-{yyyyMM}-{0000}')}</div></div></div>`;
  },
  /**
   * 绘制完整审批规则配置。
   * @returns {string} 页面内容。
   */
  rules() {
    return this.renderPolicies();
  },
  /**
   * 绘制详情排版，可调整列数、顺序及字段可见。
   * @returns {string} 页面内容。
   */
  layout() {
    return `<div class="toolbar"><b>审批详情排版</b><span class="pill">只读业务数据</span><span style="flex:1"></span><select aria-label="排版方式" class="sel" style="width:160px" onchange="D.setting('layoutMode',this.value);D.render()">${['自动排版','自定义排版','嵌入业务原单'].map(t=>`<option ${D.state.settings.layoutMode===t?'selected':''}>${t}</option>`).join('')}</select><button class="btn" onclick="D.detailPreview()">预览详情</button></div>${D.state.settings.layoutMode==='嵌入业务原单'?`<div class="config-card"><h3>业务原单页面</h3>${this.input('originalUrl','页面地址模板','https://erp.example.invalid/orders/{businessKey}')}${this.select('originalMode','打开方式',['在详情内嵌','在新窗口打开'])}<p class="hint">原型不访问外部业务系统。正式接入需使用可信域名和短时身份。</p></div>`:`<div class="layout-editor"><section class="config-card"><h3>字段与布局</h3>${this.select('columns','每行列数',['2 列','1 列','3 列'])}<p class="hint">拖动右侧字段调整顺序；点击字段右侧按钮控制显示。</p>${D.state.fields.map((f,i)=>`<div class="field-chip"><span>${UI.escape(f.name)}</span><button class="btn sm ghost" onclick="D.field(${i},'hidden',${!f.hidden});D.render()">${f.hidden?'显示':'隐藏'}</button></div>`).join('')}${this.select('uppercase','人民币大写',['显示','不显示'])}</section><section class="layout-preview"><div class="layout-form"><h3 style="font-size:17px;margin:0 0 6px">${UI.escape(D.state.name)}单</h3><p class="hint" style="margin-bottom:20px">业务单号 ${D.state.code}-0026 · 审批数据快照</p><div class="layout-cells" style="grid-template-columns:repeat(${parseInt(D.state.settings.columns||'2')},1fr)">${D.state.fields.filter(f=>!f.hidden).map(f=>`<div class="layout-cell" draggable="true" data-field="${UI.escape(f.key)}"><span>${UI.escape(f.name)}</span>${UI.escape(D.sample(f))}</div>`).join('')}</div><p class="hint" style="margin-top:20px">审批记录与节点意见将自动追加在业务数据下方。</p></div></section></div>`}`;
  },
  /**
   * 生成审批单打印布局及设置。
   * @returns {string} 页面内容。
   */
  print() {
    return `<div class="print-grid"><section class="config-card"><h3>打印模板</h3>${this.select('printMode','模板模式',['自动排版','自定义模板'])}${this.select('paper','纸张',['A4 纵向','A4 横向','A5 纵向'])}${this.input('printTitle','标题','采购申请审批单')}${this.input('company','单位抬头','智云科技有限公司')}${this.select('printFont','中文字体',['使用管理员已上传字体'])}${this.select('printSign','意见和签名',['全部节点','仅已完成节点'])}${this.select('printFiles','附票据页',['图片 / PDF 附件附后','不附加'])}${this.select('watermark','水印',['审批中显示“审批中”','始终显示单位名称','不显示'])}<button class="btn primary" style="width:100%;margin-top:18px" onclick="D.printPreview()">预览打印审批单</button><p class="hint" style="margin-top:10px">预览使用虚构数据。正式 PDF 由服务端生成，中文字体由单位上传。</p></section><div class="paper-stage"><div class="paper">${this.paper()}</div></div></div>`;
  },
  /**
   * 生成统一审批单预览。
   * @returns {string} 审批单内容。
   */
  paper() {
    return `<p style="text-align:center;font-size:12px;margin:0 0 10px">${UI.escape(D.state.settings.company||'智云科技有限公司')}</p><h2>${UI.escape(D.state.settings.printTitle||D.state.name+'审批单')}</h2><div class="paper-sub">SP-202610-0026　｜　流程版本 V${D.state.version}　｜　${D.state.code}</div><table>${D.state.fields.filter(f=>!f.hidden).map(f=>`<tr><th style="width:110px">${UI.escape(f.name)}</th><td>${UI.escape(D.sample(f))}</td></tr>`).join('')}</table><table class="signatures" style="margin-top:18px">${D.state.nodes.filter(n=>['approval','handle'].includes(n.type)).map((n,i)=>`<tr><th style="width:110px">${UI.escape(n.name)}</th><td>${i===0?'同意，按计划执行。<br><span class="hint">张明　2026-10-13 10:24</span>':'<span class="hint">待处理</span>'}</td></tr>`).join('')}</table><p class="hint" style="margin-top:24px;text-align:right">生成时间 2026-10-13 14:30　　1 / 1</p>`;

  },
  /**
   * 生成版本发布记录与版本对比。
   * @returns {string} 页面内容。
   */
  versions() {
    return `<div class="notice">已发布版本不可修改。在途审批单继续按发起时的版本流转；当前修改保存为新草稿。</div><div class="config-grid"><section class="config-card"><h3>版本历史</h3><div class="version-timeline"><article><h4>V${D.state.version+1} 草稿 <span class="pill warn">编辑中</span></h4><p class="hint">基于 V${D.state.version} 创建 · ${D.state.nodes.length} 个节点 · ${D.state.fields.length} 个字段</p><button class="btn sm" onclick="D.publish()">校验并发布</button></article>${D.state.history.map(h=>`<article><h4>V${h.version} <span class="pill ok">已发布</span></h4><p>${UI.escape(h.note)}</p><p class="hint">${h.date} · ${D.state.owner}</p></article>`).join('')}</div></section><section class="config-card"><h3>草稿与当前发布版本</h3><table class="designer-table"><thead><tr><th>配置项</th><th>发布版本</th><th>当前草稿</th></tr></thead><tbody><tr><td>流程节点</td><td>${D.state.publishedNodes??5}</td><td>${D.state.nodes.length}</td></tr><tr><td>审批数据字段</td><td>${D.state.publishedFields??8}</td><td>${D.state.fields.length}</td></tr><tr><td>画法</td><td>自由画布</td><td>自由画布</td></tr></tbody></table><p class="hint" style="margin-top:16px">此处展示原型中的配置摘要。生产版本会记录完整差异及发布审计。</p></section></div>`;
  }
};
