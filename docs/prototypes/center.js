/**
 * 审批中心原型的页面状态。
 */
const center = { rows:ApprovalDemo.load(), tab:'todo', detail:null, detailTab:'data', search:'', flow:'', date:'', status:'', selected:new Set() };
/**
 * 转义演示数据。
 * @param {string} value 原始内容。
 * @returns {string} 安全的页面文本。
 */
function esc(value) { return UI.escape(String(value ?? '')); }
/**
 * 保存并刷新当前页面。
 * @returns {void} 无返回值。
 */
function saveCenter() { ApprovalDemo.save(center.rows); renderCenter(); }
/**
 * 根据当前筛选条件获取列表。
 * @returns {Array} 满足条件的记录。
 */
function filteredCenter() { return center.rows.filter(r=>r.category===center.tab && (!center.flow || r.flow===center.flow) && (!center.status || r.status===center.status) && (!center.date || r.time.startsWith(center.date)) && (!center.search || JSON.stringify(r).toLowerCase().includes(center.search.toLowerCase()))); }
/**
 * 绘制审批中心。
 * @returns {void} 无返回值。
 */
function renderCenter() {
  const body=center.detail ? centerDetail() : centerList();
  UI.shell('center', center.detail ? '审批详情' : '审批中心', center.detail ? '审批中心 / '+center.detail.id : '集中处理审批，随时掌握进度',body,`<button class="btn" onclick="centerNotifications()">${UI.icon('alert')} 消息 <span class="pill accent">3</span></button><a class="btn" href="mobile.html">${UI.icon('preview')} 手机审批</a><button class="btn" onclick="centerSettings()">${UI.icon('gear')} 个人设置</button>`);
}
/**
 * 生成列表页面。
 * @returns {string} 列表内容。
 */
function centerList() {
  const tabs=[['todo','待我处理'],['done','我已处理'],['cc','抄送我的'],['mine','我发起的']];
  const rows=filteredCenter();
  return `<div class="center-stats">${[['待我处理',center.rows.filter(r=>r.category==='todo').length,'今天有 2 项新待办','cards'],['加急待办',center.rows.filter(r=>r.category==='todo'&&r.urgent).length,'优先处理重要事项','alert'],['我已处理',center.rows.filter(r=>r.category==='done').length,'本月已处理审批','check'],['抄送未读',center.rows.filter(r=>r.category==='cc'&&!r.read).length,'及时了解审批结果','send']].map(s=>`<div class="center-stat"><span class="stat-symbol">${UI.icon(s[3])}</span><div><small>${s[0]}</small><strong>${s[1]}</strong><small>${s[2]}</small></div></div>`).join('')}</div>
  <section class="panel"><div class="center-tabs">${tabs.map(t=>`<button class="${center.tab===t[0]?'active':''}" onclick="center.tab='${t[0]}';center.selected.clear();center.status='';renderCenter()">${t[1]} <span class="count">${center.rows.filter(r=>r.category===t[0]).length}</span></button>`).join('')}</div><div style="padding:0 16px"><div class="center-filter"><input class="inp" aria-label="搜索审批" placeholder="搜索标题、业务单号、摘要内容" value="${esc(center.search)}" oninput="center.search=this.value;updateCenterRows()"><select class="sel" aria-label="流程筛选" onchange="center.flow=this.value;updateCenterRows()"><option value="">全部流程</option>${['费用报销','采购申请'].map(v=>`<option ${center.flow===v?'selected':''}>${v}</option>`).join('')}</select><select class="sel" aria-label="时间筛选" onchange="center.date=this.value;updateCenterRows()"><option value="">全部时间</option><option value="2026-10-13" ${center.date?'selected':''}>今天</option></select><select class="sel" aria-label="状态筛选" onchange="center.status=this.value;updateCenterRows()"><option value="">全部状态</option>${['待处理','审批中','已通过','已拒绝','已退回','待补充'].map(v=>`<option ${center.status===v?'selected':''}>${v}</option>`).join('')}</select><button class="btn ghost" onclick="center.search='';center.flow='';center.date='';center.status='';renderCenter()">重置</button></div>
  <div class="toolbar" style="padding:0 0 13px"><span class="hint">${center.tab==='todo'?'加急优先 · 未读审批以粗体显示':center.tab==='mine'?'业务系统发起的审批，仅查看进度与催办':'已按最新时间排序'}</span><div class="center-batch"><span id="selected-count" class="hint"></span>${center.tab==='todo'?'<button class="btn primary sm" onclick="centerBatch()">批量同意</button>':''}<button class="btn sm" onclick="center.rows=ApprovalDemo.load();renderCenter();UI.toast(\'列表已刷新\')">${UI.icon('refresh')} 刷新</button></div></div></div><div id="center-rows">${centerTable(rows)}</div></section><p class="hint" style="margin-top:14px">审批数据由业务系统提供。所有审批操作均留存记录；业务单据的修改、撤回与重新提交请在来源系统完成。</p>`;
}
/**
 * 渲染表格行。
 * @param {Array} rows 记录列表。
 * @returns {string} 表格内容。
 */
function centerTable(rows) {
  return `<div class="table-wrap"><table class="center-table"><thead><tr><th><input type="checkbox" aria-label="全选当前列表" onchange="selectCenterAll(this.checked)"></th><th>审批主题 / 业务单号</th><th>流程</th><th>发起人</th><th class="amount">摘要金额</th><th>当前节点</th><th>状态</th><th>提交时间</th><th></th></tr></thead><tbody>${rows.map(r=>`<tr><td><input type="checkbox" aria-label="选择${esc(r.id)}" ${center.selected.has(r.id)?'checked':''} onchange="toggleCenterSelection('${r.id}',this.checked)"></td><td><a href="#${r.id}" class="subject ${r.read?'':'unread'}" onclick="openCenter('${r.id}');return false">${esc(r.title)}</a>${r.urgent?'<span class="center-tag">加急</span>':''}<span class="sub mono">${r.key} · ${r.id}</span></td><td>${r.flow}</td><td><span class="center-avatar">${r.person.slice(-2)}</span>${r.person}<span class="sub" style="margin-left:35px">${r.dept}</span></td><td class="amount">${ApprovalDemo.money(r.amount)}</td><td>${esc(r.node)}</td><td><span class="pill ${r.status==='待处理'?'accent':r.status==='已通过'?'ok':r.status==='已拒绝'?'danger':'warn'}">${r.status}</span></td><td><span class="sub" style="font-size:12px">${r.time}</span></td><td><button class="btn ghost sm" onclick="openCenter('${r.id}')">${r.category==='todo'?'去处理':'查看'}</button></td></tr>`).join('') || '<tr><td colspan="9"><div class="empty">没有符合条件的审批<button class="btn sm" onclick="center.search=\'\';center.flow=\'\';center.date=\'\';center.status=\'\';renderCenter()">清除筛选</button></div></td></tr>'}</tbody></table></div><div class="center-foot"><span>共 ${rows.length} 条 · 已展示全部结果</span><span><button class="btn sm" disabled>上一页</button> <button class="btn sm primary">1</button> <button class="btn sm" disabled>下一页</button></span></div>`;
}
/**
 * 仅刷新搜索结果，保持输入焦点。
 * @returns {void} 无返回值。
 */
function updateCenterRows() { document.getElementById('center-rows').innerHTML=centerTable(filteredCenter()); }
/**
 * 切换勾选项。
 * @param {string} id 审批编号。
 * @param {boolean} checked 是否选中。
 * @returns {void} 无返回值。
 */
function toggleCenterSelection(id,checked) { checked?center.selected.add(id):center.selected.delete(id); document.getElementById('selected-count').textContent=`已选择 ${center.selected.size} 项`; }
/**
 * 全选当前筛选结果。
 * @param {boolean} checked 是否选中。
 * @returns {void} 无返回值。
 */
function selectCenterAll(checked) { filteredCenter().forEach(r=>toggleCenterSelection(r.id,checked)); updateCenterRows(); }
/**
 * 打开审批详情。
 * @param {string} id 审批编号。
 * @returns {void} 无返回值。
 */
function openCenter(id) { center.detail=center.rows.find(r=>r.id===id); center.detail.read=true; center.detailTab='data'; saveCenter(); centerScrollTop(); }
/**
 * 返回列表时重置导航滚动位置。
 * @returns {void} 无返回值。
 */
function backCenter() { center.detail=null;renderCenter();centerScrollTop(); }
/**
 * 页面导航后滚动至顶部，保留输入时的滚动位置。
 * @returns {void} 无返回值。
 */
function centerScrollTop() { window.scrollTo(0,0);document.getElementById('page-content')?.scrollTo(0,0); }
/**
 * 生成业务数据与明细内容。
 * @param {object} r 审批记录。
 * @returns {string} 业务数据页面内容。
 */
function centerBusiness(r) {
  const purchase=r.flow==='采购申请';
  return `<section class="center-section"><h3>基本信息 <span class="pill">业务数据快照 · 只读</span></h3><div class="center-section-body center-fields">${[['发起人',r.person],['所属部门',r.dept],['提交时间',r.time],['业务单号',r.key],['来源系统','示例业务系统'],[purchase?'供应商':'费用承担部门',purchase?'深圳市启辰科技有限公司':r.dept]].map(f=>`<div class="center-field"><label>${f[0]}</label><div>${esc(f[1])}</div></div>`).join('')}<div class="center-field wide"><label>${purchase?'采购用途':'报销事由'}</label><div>${esc(r.title)}</div></div></div></section>
  <section class="center-section"><h3>${purchase?'采购':'费用'}明细 <span class="hint">${purchase?'2':'3'} 条明细</span></h3><div class="table-wrap"><table class="center-table"><thead><tr>${(purchase?['物料名称','规格型号','数量','单价（元）','金额（元）']:['发生日期','费用类型','费用说明','金额（元）']).map(t=>`<th>${t}</th>`).join('')}</tr></thead><tbody>${(purchase && r.amount!==12800?[[r.title,'按业务单据','1 批',r.amount.toFixed(2),r.amount.toFixed(2)]]:!purchase && r.amount!==1334.5?[[r.time.slice(0,10),'业务费用',r.title,r.amount.toFixed(2)]]:purchase?[['笔记本电脑','商用 14 英寸','2 台','5,600.00','11,200.00'],['显示器','27 英寸 2K','2 台','800.00','1,600.00']]:[['2026-10-08','交通费','深圳往返高铁','686.00'],['2026-10-08','住宿费','深圳酒店 · 2 晚','560.00'],['2026-10-09','交通费','客户现场往返打车','88.50']]).map(row=>`<tr>${row.map(v=>`<td>${v}</td>`).join('')}</tr>`).join('')}</tbody></table></div><div class="center-total"><span class="muted">${purchase?'采购':'报销'}总金额</span><strong>${ApprovalDemo.money(r.amount)}</strong></div></section>
  <section class="center-section"><h3>附件 <span class="hint">查看权限随审批单控制</span></h3><div class="center-section-body center-attachments"><button class="center-file" onclick="centerAttachment('电子发票.pdf')"><span class="file-icon">PDF</span><span>电子发票.pdf<small>238 KB · 点击预览</small></span></button><button class="center-file" onclick="centerAttachment('费用凭证.jpg')"><span class="file-icon" style="color:#50856b;background:#eef8f4">JPG</span><span>费用凭证.jpg<small>1.2 MB · 点击预览</small></span></button></div></section>`;
}
/**
 * 生成审批详情。
 * @returns {string} 详情内容。
 */
function centerDetail() {
  const r=center.detail;
  return `<div class="center-detail-header"><button class="btn" aria-label="返回审批列表" onclick="backCenter()">${UI.icon('chevL')} 返回</button><span class="detail-icon">${UI.icon('logs')}</span><div><h2>${esc(r.title)} ${r.urgent?'<span class="center-tag">加急</span>':''}</h2><span class="hint mono">${r.id} · ${r.flow} v3 · ${r.time}</span></div><span class="pill ${r.status==='待处理'?'accent':'warn'}" style="margin-left:auto">${r.status}</span><button class="btn" onclick="centerPrintFull()">${UI.icon('download')} 打印审批单</button></div>
  <div class="center-tabs center-data-tabs">${[['data','审批数据'],['graph','流程进度'],['history','操作记录'],['comments','评论与沟通']].map(t=>`<button class="${center.detailTab===t[0]?'active':''}" onclick="center.detailTab='${t[0]}';renderCenter()">${t[1]}</button>`).join('')}<button onclick="centerOriginal()">查看业务原单 ${UI.icon('chevR')}</button></div>
  <div class="center-detail-layout"><div>${center.detailTab==='data'?centerBusiness(r):center.detailTab==='graph'?centerGraph(r):center.detailTab==='history'?centerHistory(r):centerComments(r)}${r.category==='todo'?`<section class="center-section"><h3>财务意见 <span class="hint">当前处理人：赵敏</span></h3><div class="center-section-body"><label class="hint" for="center-opinion">审批意见</label><textarea id="center-opinion" class="inp center-opinion-area" placeholder="请填写审批意见，拒绝、退回时必填" oninput="localStorage.setItem('approval-draft-${r.id}',this.value)">${esc(localStorage.getItem('approval-draft-'+r.id)||'')}</textarea><div>${(JSON.parse(localStorage.getItem('approval-phrases')||'null')||['同意，按制度执行。','情况属实，同意。','请补充相关凭证。']).map(t=>`<button class="center-phrase" onclick="document.getElementById('center-opinion').value=this.textContent;localStorage.setItem('approval-draft-${r.id}',this.textContent)">${esc(t)}</button>`).join('')}<button class="btn ghost sm" onclick="centerSignature()">${UI.icon('pen')} 添加签名</button></div><span class="hint">意见自动保存为草稿，仅本人可见。</span></div></section>`:''}</div><aside><section class="center-section"><h3>审批进度 <span class="pill accent">${esc(r.node)}</span></h3>${centerTimeline(r)}</section><div class="notice"><span>${UI.icon('alert')}</span><span>下一步尚未处理时，可在「我已处理」中撤销同意。所有操作均追加到审批记录。</span></div></aside></div>
  <div class="center-bottom"><span class="hint">${r.category==='todo'?'请核实业务数据后处理审批':'当前状态：'+r.status}</span>${r.category==='todo'?`<button class="btn" onclick="UI.toast('意见草稿已保存')">暂存意见</button><button class="btn" onclick="centerMore()">更多操作 ▾</button><button class="btn" onclick="centerAction('退回')">退回</button><button class="btn danger" onclick="centerAction('拒绝')">拒绝</button><button class="btn primary" onclick="centerAction('同意')">${UI.icon('check')} 同意</button>`:r.category==='mine'?'<button class="btn primary" onclick="centerRemind()">催办</button>':r.category==='done'&&r.canRevoke?'<button class="btn" onclick="centerAction(\'撤销同意\')">撤销同意</button>':'<button class="btn" onclick="center.detailTab=\'comments\';renderCenter()">发表评论</button>'}</div>`;
}
/**
 * 生成进度时间线。
 * @param {object} r 审批记录。
 * @returns {string} 时间线内容。
 */
function centerTimeline(r) { return `<div class="center-timeline"><div class="center-step"><b>提交申请</b><p>${r.person} · ${r.dept}</p><p>${r.time}</p></div><div class="center-step"><b>部门负责人审批</b><p>李娜 · 已同意</p><p>10-13 09:45</p><div class="opinion">情况属实，同意报销。</div></div><div class="center-step ${r.category==='todo'?'current':''}"><b>财务审核</b><p>赵敏 ${r.added?'· 加签 '+esc(r.added):''}</p><p>${r.category==='todo'?'待处理 · 剩余 1 个工作日':'已处理'}</p>${r.history?.length?`<div class="opinion">${esc(r.history.at(-1).action)} · ${esc(r.history.at(-1).opinion)}</div>`:''}</div><div class="center-step ${r.node==='出纳办理'?'current':'future'}"><b>出纳办理</b><p>刘洋 · ${r.node==='出纳办理'?'待处理':'等待前序节点'}</p></div><div class="center-step future"><b>审批结束</b><p>结果同步至业务系统</p></div></div>`; }
/**
 * 绘制流程节点视图。
 * @param {object} r 当前审批单。
 * @returns {string} 流程视图。
 */
function centerGraph(r) { return `<section class="center-section"><h3>流程图 <span class="hint">当前版本 v3 · 蓝色为当前节点</span></h3><div class="center-graph">${['发起','部门负责人','财务审核','出纳办理','结束'].map((n,i)=>`${i?'→':''}<div class="node ${n===r.node?'active':''}">${n}<div class="hint">${['王强','李娜','赵敏','刘洋',''][i]}</div></div>`).join('')}</div><div class="center-section-body hint">审批人到达节点时重新计算。与发起时预览不一致时，变更原因将保留在操作记录。</div></section>`; }
/**
 * 渲染操作审计记录。
 * @param {object} r 当前审批单。
 * @returns {string} 审计记录。
 */
function centerHistory(r) { return `<section class="center-section"><h3>操作记录 <span class="hint">只追加 · 不可修改</span></h3><div class="table-wrap"><table class="center-table"><thead><tr><th>时间</th><th>操作人</th><th>操作</th><th>意见 / 说明</th></tr></thead><tbody><tr><td>${r.time}</td><td>${r.person}</td><td>发起</td><td>示例业务系统提交</td></tr><tr><td>2026-10-13 09:45</td><td>李娜</td><td>同意</td><td>情况属实，同意。</td></tr>${(r.history||[]).map(h=>`<tr><td>${esc(h.at)}</td><td>赵敏</td><td>${esc(h.action)}</td><td>${esc(h.opinion)} ${esc(h.person)}</td></tr>`).join('')}</tbody></table></div></section>`; }
/**
 * 渲染评论区域。
 * @param {object} r 当前审批单。
 * @returns {string} 评论区域。
 */
function centerComments(r) { return `<section class="center-section"><h3>评论与沟通</h3><div class="center-section-body"><div class="center-comment"><span class="center-avatar">王强</span><div><b>王强</b><span class="hint"> · 10-13 09:15</span><p>电子发票已附在附件中，请审核。</p></div></div>${(r.comments||[]).map(c=>`<div class="center-comment"><span class="center-avatar">赵敏</span><div><b>赵敏</b><p>${esc(c)}</p></div></div>`).join('')}<textarea class="inp" id="center-comment" rows="3" placeholder="添加评论，输入 @ 提醒相关人员" style="margin-top:18px"></textarea><div class="toolbar"><select class="sel" style="width:160px" aria-label="提醒人员" onchange="document.getElementById('center-comment').value+='@'+this.value+' '"><option value="">@ 提醒人员</option><option>王强</option><option>李娜</option><option>刘洋</option></select><label class="btn sm">添加附件<input type="file" hidden onchange="UI.toast('附件已加入本次评论：'+this.files[0].name)"></label><button class="btn primary" style="margin-left:auto" onclick="centerPostComment()">发布评论</button></div></div></section>`; }
/**
 * 发布一条评论。
 * @returns {void} 无返回值。
 */
function centerPostComment() { const text=document.getElementById('center-comment').value.trim(); if(!text)return UI.toast('请先填写评论'); (center.detail.comments ||= []).push(text);saveCenter();UI.toast('评论已发布，提及人员将收到提醒'); }
/**
 * 打开审批操作确认表单。
 * @param {string} action 操作名称。
 * @returns {void} 无返回值。
 */
function centerAction(action) {
  UI.closeModal();
  const draft=document.getElementById('center-opinion')?.value || '';
  const person=['转交','加签','征询意见','抄送'].includes(action);
  UI.modal(action+'审批', `<div class="notice">${action==='同意'?'审批通过后将进入出纳办理节点。':action==='要求补充'?'将通知业务系统。发起人在业务系统补充材料后，回到当前节点继续审批。':action==='撤销同意'?'下一步尚未处理，可以撤销本次同意，审批回到财务审核。':action==='退回'?'只显示当前分支或分叉前已处理的节点。':action==='减签'?'仅能移除由您加签且尚未处理的人员。':'操作将记录在审批历史中。'}</div>${person?`<label class="field">${action==='转交'?'转交给':'选择人员'}<select id="action-person" class="sel"><option>刘洋 · 财务部</option><option>刘洋 · 采购部</option><option>李娜 · 生产部</option></select></label>`:''}${action==='退回'?'<label class="field">退回目标<select id="action-person" class="sel"><option>发起人 · 王强</option><option>部门负责人 · 李娜</option></select></label>':''}${action==='加签'?'<label class="field">加签方式<select class="sel"><option>前加签 · 他人处理后回到我</option><option>后加签 · 我处理后交给他人</option><option>并行加签 · 与我一起审批</option></select></label>':''}${action==='减签'?`<label class="field">待移除人员<select class="sel"><option>${esc(center.detail.added||'暂无可减签人员')}</option></select></label>`:''}<label class="field">${action==='同意'?'审批意见':'操作原因（必填）'}<textarea class="inp" id="action-opinion" rows="3" placeholder="填写意见，便于后续追溯">${esc(draft)}</textarea></label>${action==='同意'?'<label class="field">财务核定金额（审批填写字段）<input type="number" class="inp" value="'+center.detail.amount+'" min="0" step="0.01"></label>':''}<p class="center-modal-note">这是演示操作，仅更新本浏览器的虚构记录。</p>`, dialog=>{const opinion=dialog.querySelector('#action-opinion').value.trim();if(action!=='同意'&&!opinion){UI.toast('请填写操作原因');return false;}if(action==='减签'&&!center.detail.added){UI.toast('当前没有可减签人员');return false;}ApprovalDemo.act(center.detail,action,opinion||'同意',dialog.querySelector('#action-person')?.value||'');saveCenter();UI.toast(action+'操作成功');},'确认'+action);
}
/**
 * 展示更多操作。
 * @returns {void} 无返回值。
 */
function centerMore() { UI.modal('更多审批操作',`<div class="center-actions-grid">${['转交','加签','减签','征询意见','要求补充','抄送'].map(a=>`<button class="btn" onclick="centerAction('${a}')">${a}</button>`).join('')}</div><p class="center-modal-note">可用操作由当前节点配置决定。减签仅适用于本人加签的待处理人员。</p>`,()=>{},'关闭'); }
/**
 * 批量处理选中的待办。
 * @returns {void} 无返回值。
 */
function centerBatch() { const rows=center.rows.filter(r=>center.selected.has(r.id)&&r.category==='todo');if(!rows.length)return UI.toast('请先勾选需要同意的待办');UI.modal('批量同意 '+rows.length+' 项审批',`<p>请确认已核实以下业务数据：</p>${rows.map(r=>`<p>${esc(r.title)} · ${ApprovalDemo.money(r.amount)}</p>`).join('')}<label class="field">统一审批意见<textarea id="batch-opinion" class="inp" rows="3">同意，按制度执行。</textarea></label>`,d=>{const opinion=d.querySelector('#batch-opinion').value.trim();if(!opinion){UI.toast('请填写审批意见');return false;}rows.forEach(r=>ApprovalDemo.act(r,'同意',opinion));center.selected.clear();saveCenter();UI.toast(rows.length+' 项审批已同意');},'确认同意'); }
/**
 * 查看审批附件的演示预览。
 * @param {string} name 附件名称。
 * @returns {void} 无返回值。
 */
function centerAttachment(name) { UI.modal('附件预览 · '+name,`<div style="background:#f2f5fa;padding:24px"><div style="background:white;border:1px solid #ddd;padding:26px;text-align:center;color:#755077"><h2>电子发票（演示）</h2><p>深圳市差旅服务有限公司</p><hr><p style="text-align:left">购买方：示例科技有限公司</p><p style="text-align:left">项目名称：差旅服务</p><h3>价税合计　¥ 1,334.50</h3><p class="hint">此处为原型预览，非真实票据</p></div></div>`,()=>{},'关闭预览'); }
/**
 * 显示来源业务系统的只读入口。
 * @returns {void} 无返回值。
 */
function centerOriginal() { UI.modal('来源业务单据',`<p>业务单号：<b>${center.detail.key}</b></p><p class="hint">查看原单、补充材料、撤回及重新提交均在业务系统完成。</p><a class="btn primary" href="demo.html">打开业务系统对接示例 ${UI.icon('chevR')}</a>`,()=>{},'关闭'); }
/**
 * 发起人催办，并模拟一小时限流。
 * @returns {void} 无返回值。
 */
function centerRemind() { if(center.detail.reminded&&Date.now()-center.detail.reminded<3600000)return UI.toast('该节点已催办，请 1 小时后再试');center.detail.reminded=Date.now();ApprovalDemo.act(center.detail,'催办','已提醒当前审批人');saveCenter();UI.toast('催办已发送给当前审批人'); }
/**
 * 打开站内消息。
 * @returns {void} 无返回值。
 */
function centerNotifications() { UI.modal('站内消息',`<div class="center-inbox" onclick="UI.closeModal();openCenter('SP-202610-0007')"><b>新待办 · 王强的费用报销</b><p>10 月深圳客户现场服务差旅 · ¥ 1,334.50</p><span class="hint">今天 09:45</span></div><div class="center-inbox" onclick="UI.closeModal();openCenter('SP-202610-0006')"><b>即将到期 · 费用报销</b><p>请在 1 个工作日内完成审批。</p></div><div class="center-inbox" onclick="UI.closeModal();openCenter('SP-202610-0003')"><b>抄送 · 仓储手持终端采购已通过</b><p>点击查看审批详情</p></div>`,()=>UI.toast('消息已全部标记为已读'),'全部标为已读'); }
/**
 * 个人设置的入口。
 * @returns {void} 无返回值。
 */
function centerSettings() { UI.modal('个人设置 · 赵敏',`<div class="center-setting-row"><div><b>委托代理</b><small>${esc(localStorage.getItem('approval-delegation')||'出差或休假时，将审批委托给同事')}</small></div><button class="btn sm" onclick="centerDelegation()">设置委托</button></div><div class="center-setting-row"><div><b>个人签名</b><small>审批意见中使用手写签名或上传图片</small></div><button class="btn sm" onclick="centerSignature()">维护签名</button></div><div class="center-setting-row"><div><b>常用审批语</b><small>提高重复审批效率</small></div><button class="btn sm" onclick="centerPhrases()">编辑</button></div><div class="center-setting-row"><div><b>消息偏好</b><small>站内消息、邮件与每日待办汇总</small></div><button class="btn sm" onclick="centerPreferences()">设置</button></div>`,()=>{},'完成'); }
/**
 * 设置委托日期、范围和代理人。
 * @returns {void} 无返回值。
 */
function centerDelegation() { UI.closeModal();UI.modal('委托代理',`<label class="field">代理人<select id="delegate" class="sel"><option>刘洋 · 财务部</option><option>李娜 · 生产部</option></select></label><div class="form-grid"><label class="field">开始时间<input id="delegate-start" class="inp" type="datetime-local" value="2026-10-14T09:00"></label><label class="field">结束时间<input id="delegate-end" class="inp" type="datetime-local" value="2026-10-16T18:00"></label></div><label class="field">委托范围<select class="sel"><option>全部流程</option><option>仅费用报销</option><option>仅采购申请</option></select></label><p><label><input type="checkbox" checked> 将现有待办一并交给代理人</label></p><p class="hint">代理期间本人仍能查看和处理。审批记录将注明「刘洋（代赵敏）」，不连环委托。</p>`,d=>{if(d.querySelector('#delegate-start').value>=d.querySelector('#delegate-end').value){UI.toast('结束时间必须晚于开始时间');return false;}localStorage.setItem('approval-delegation',d.querySelector('#delegate').value+' · '+d.querySelector('#delegate-start').value.slice(0,10)+' 至 '+d.querySelector('#delegate-end').value.slice(0,10));UI.toast('委托代理已保存');}); }
/**
 * 编辑常用语。
 * @returns {void} 无返回值。
 */
function centerPhrases() { UI.closeModal();UI.modal('常用审批语',`<label class="field">每行一条<textarea id="phrases" class="inp" rows="6">${esc((JSON.parse(localStorage.getItem('approval-phrases')||'null')||['同意，按制度执行。','情况属实，同意。','请补充相关凭证。']).join('\n'))}</textarea></label>`,d=>{localStorage.setItem('approval-phrases',JSON.stringify(d.querySelector('#phrases').value.split('\n').filter(Boolean)));renderCenter();UI.toast('常用语已保存');}); }
/**
 * 维护个人签名，支持鼠标或触屏绘制。
 * @returns {void} 无返回值。
 */
function centerSignature() { UI.closeModal();UI.modal('个人签名',`<p class="hint">在下方区域手写，或上传已有的个人签名图片。</p><canvas id="signature" class="center-signature" width="560" height="120"></canvas><div class="toolbar"><button class="btn sm" onclick="document.getElementById('signature').getContext('2d').clearRect(0,0,560,120)">清空</button><label class="btn sm">上传图片<input type="file" accept="image/*" hidden onchange="centerUploadSignature(this.files[0])"></label></div>`,()=>{localStorage.setItem('approval-signature',document.getElementById('signature').toDataURL());UI.toast('签名已保存');});const canvas=document.getElementById('signature');const ctx=canvas.getContext('2d');ctx.lineWidth=2;ctx.strokeStyle='#17315a';let drawing=false;canvas.onpointerdown=e=>{drawing=true;canvas.setPointerCapture(e.pointerId);ctx.beginPath();ctx.moveTo(e.offsetX*560/canvas.clientWidth,e.offsetY*120/canvas.clientHeight);};canvas.onpointermove=e=>{if(drawing){ctx.lineTo(e.offsetX*560/canvas.clientWidth,e.offsetY*120/canvas.clientHeight);ctx.stroke();}};canvas.onpointerup=()=>{drawing=false;}; }
/**
 * 将选择的签名图片绘制到签名板。
 * @param {File} file 用户选择的文件。
 * @returns {void} 无返回值。
 */
function centerUploadSignature(file) { if(!file)return;const url=URL.createObjectURL(file);const img=new Image();img.onload=()=>{const c=document.getElementById('signature');c.getContext('2d').clearRect(0,0,560,120);c.getContext('2d').drawImage(img,0,0,Math.min(560,img.width*120/img.height),120);URL.revokeObjectURL(url);};img.src=url; }
/**
 * 保存通知偏好。
 * @returns {void} 无返回值。
 */
function centerPreferences() { UI.closeModal();const saved=JSON.parse(localStorage.getItem('approval-preferences')||'null');UI.modal('消息偏好',`<div class="center-settings">${['新待办与催办','到期前与超时提醒','审批结果','抄送与 @ 提醒','每天 08:30 邮件汇总'].map((t,i)=>`<div class="center-setting-row"><span>${t}</span><label><input type="checkbox" name="pref" ${saved?saved[i]?'checked':'':'checked'}> 接收</label></div>`).join('')}</div>`,d=>{localStorage.setItem('approval-preferences',JSON.stringify([...d.querySelectorAll('[name=pref]')].map(c=>c.checked)));UI.toast('消息偏好已保存');}); }
renderCenter();

if (location.hash === '#settings') { centerSettings(); }
