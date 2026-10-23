/**
 * WinUI 桌面原型的只读视图，所有状态由本地演示控制器提供。
 */
const WinViews = {
  /**
   * 输出文档状态标签。
   * @param {string} status 状态名称。
   * @returns {string} 标签标记。
   */
  badge(status) { return `<span class="pill ${({'审批中':'accent','已通过':'ok','已退回':'warn','已撤回':'warn'})[status]||''}">${UI.escape(status)}</span>`; },
  /**
   * 输出金额。
   * @param {number} amount 金额。
   * @returns {string} 格式化文本。
   */
  money(amount) { return Number(amount).toLocaleString('zh-CN',{minimumFractionDigits:2,maximumFractionDigits:2}); },
  /**
   * 创建桌面导航与状态栏。
   * @param {object} app 原型控制器。
   * @param {string} body 工作区内容。
   * @returns {string} 完整界面。
   */
  shell(app,body) {
    const count=app.tasks().length;
    const nav=[['expense','table','费用报销'],['purchase','box','采购申请'],['tasks','check','我的待办'],['webview','preview','嵌入审批中心'],['integration','braces','对接改动'],['settings','gear','连接与演示环境']];
    return `<header class="win-title"><span class="win-logo">${UI.icon('box')}</span><strong>智云 · 业务工作台</strong><span class="pill">WinUI 3 客户端交互原型</span><span class="window-glyphs" aria-hidden="true">— □ ×</span></header><aside class="win-nav" aria-label="桌面客户端导航"><div class="win-brand"><strong>业务工作台</strong><small>连接业务与每一次审批</small></div>${nav.map(([key,icon,label],i)=>`${i===3?'<div class="nav-label">集成与环境</div>':''}<button class="${app.page===key?'active':''}" onclick="Win.go('${key}')">${UI.icon(icon)}${label}${key==='tasks'&&count?`<span class="count">${count}</span>`:''}</button>`).join('')}<div class="nav-bottom"><a href="demo.html">${UI.icon('chevL')}网页业务示例</a><a href="index.html">${UI.icon('cards')}原型导航</a></div><div class="win-user"><label><span class="avatar">${UI.escape(app.user.slice(0,1))}</span><select aria-label="切换当前员工" onchange="Win.switchUser(this.value)">${app.users.map(name=>`<option ${name===app.user?'selected':''}>${name}</option>`).join('')}</select></label><div class="hint" style="margin:8px 0 0 39px">虚构员工 · 本地身份切换</div></div></aside><main class="win-main" id="win-main">${body}</main><footer class="win-status"><span><i class="status-dot ${app.connected?'':'off'}"></i>${app.connected?'审批服务已连接（模拟）':'审批服务离线（模拟）'}</span><span class="optional">本机业务后端：${app.host?'运行中':'已关闭'}（模拟）</span><span class="optional">状态同步：${app.mode==='callback'?'回调':'查询'}</span><a href="winui-coverage.md" target="_blank">HTML 原型 · 无真实接口调用</a></footer>`;
  },
  /**
   * 创建单据列表。
   * @param {object} app 原型控制器。
   * @returns {string} 列表界面。
   */
  list(app) {
    const type=app.page,title=type==='expense'?'费用报销':'采购申请', docs=app.docs.filter(d=>d.type===type&&d.owner===app.user);
    const shown=docs.filter(d=>(app.filter==='全部状态'||d.status===app.filter)&&`${d.id} ${d.title}`.includes(app.search));
    return `<div class="win-heading"><div><h1>${title}</h1><p>管理我的${title}单，随时跟进审批进度。</p></div><div class="win-actions"><button class="btn" onclick="Win.refresh()">${UI.icon('refresh')}刷新状态</button><button class="btn primary" onclick="Win.edit()">${UI.icon('plus')}新建${title}</button></div></div><div class="win-stats">${[['table','我的单据',docs.length],['clock','审批中',docs.filter(d=>d.status==='审批中').length],['check','已通过',docs.filter(d=>d.status==='已通过').length]].map(([icon,label,n])=>`<div class="win-stat"><span class="stat-icon">${UI.icon(icon)}</span><div><small>${label}</small><b>${n}<small style="font-size:12px;margin-left:5px">张</small></b></div></div>`).join('')}</div><div class="win-tools"><input class="inp" aria-label="搜索单据" placeholder="搜索单据名称或编号" value="${UI.escape(app.search)}" onchange="Win.search=this.value;Win.render()"><select class="sel" aria-label="筛选单据状态" onchange="Win.filter=this.value;Win.render()">${['全部状态','草稿','审批中','已通过','已退回','已撤回'].map(s=>`<option ${app.filter===s?'selected':''}>${s}</option>`).join('')}</select><span class="hint">${UI.escape(app.user)} · 共 ${shown.length} 张单据</span></div><div class="document-grid">${shown.map(d=>`<article class="document-card"><header><span class="doc-id">${UI.escape(d.id)}</span>${WinViews.badge(d.status)}</header><div><h2>${UI.escape(d.title)}</h2><div class="doc-meta"><span>${UI.escape(d.department)}</span><span>2026-10-13</span></div></div><div class="doc-money"><small>¥</small>${WinViews.money(d.amount)}</div><footer><span class="hint">${d.status==='审批中'?`当前审批人：${UI.escape(app.assignee(d))}`:d.status==='草稿'?'尚未提交审批':UI.escape(d.status)}</span><button class="btn sm" onclick="Win.open('${d.id}')">查看单据 ${UI.icon('chevR')}</button></footer></article>`).join('')||'<div class="win-empty">暂无符合条件的单据。可新建单据或切换员工。</div>'}</div><div class="win-bottom-note">单据由本示例业务系统创建、编辑。审批服务仅保存审批快照与处理结果。<br>这里展示桌面端的交互设计；表单、金额与状态保存在当前浏览器，不会提交到真实系统。</div>`;
  },
  /**
   * 创建单据详情与审批时间线。
   * @param {object} app 原型控制器。
   * @param {object} d 当前单据。
   * @returns {string} 详情界面。
   */
  detail(app,d) {
    const editable=['草稿','已退回','已撤回'].includes(d.status)&&d.owner===app.user;
    return `<button class="win-back" onclick="Win.selected=null;Win.render()">${UI.icon('chevL')}返回${d.type==='expense'?'报销':'采购'}列表</button><div class="win-heading"><div><h1>${UI.escape(d.title)}</h1><p>${UI.escape(d.id)} · ${UI.escape(d.owner)} · ${UI.escape(d.department)}</p></div><div class="win-actions">${editable?`<button class="btn" onclick="Win.edit('${d.id}')">编辑</button><button class="btn primary" onclick="Win.submit('${d.id}')">${d.status==='草稿'?'提交审批':'重新提交'}</button>`:''}${d.status==='审批中'&&d.owner===app.user?`<button class="btn" onclick="Win.withdraw('${d.id}')">撤回</button>`:''}<button class="btn" onclick="Win.refresh()">查询状态</button><button class="btn" onclick="window.print()">${UI.icon('logs')}打印</button></div></div>${d.status!==d.serviceStatus?'<div class="notice warn">审批结果尚未同步到业务单据。请运行本机后端接收模拟回调，或点击“查询状态”。</div>':''}<div class="win-detail"><div><section class="win-card"><h2>业务单据</h2><dl class="win-kv"><div><dt>审批状态</dt><dd>${WinViews.badge(d.status)}</dd></div><div><dt>审批单 ID</dt><dd>${d.instance||'提交后生成'}</dd></div><div><dt>${d.type==='expense'?'报销金额':'采购金额'}</dt><dd class="doc-money">¥ ${WinViews.money(d.amount)}</dd></div><div><dt>${d.type==='expense'?'核定金额':'采购订单号'}</dt><dd>${d.approvedAmount!==undefined?'¥ '+WinViews.money(d.approvedAmount):d.orderNo||'审批完成后回写'}</dd></div><div class="wide"><dt>${d.type==='expense'?'报销事由':'采购用途'}</dt><dd>${UI.escape(d.description)}</dd></div></dl></section><section class="win-card"><h2>${d.type==='expense'?'费用明细与票据':'采购物料明细'}</h2><div class="win-table"><table><thead><tr><th>项目</th><th>${d.type==='expense'?'费用类型':'数量 / 单位'}</th><th>金额</th></tr></thead><tbody><tr><td>${UI.escape(d.item)}</td><td>${d.type==='expense'?'差旅交通':'1 批'}</td><td>¥ ${WinViews.money(d.amount)}</td></tr></tbody></table></div>${d.type==='expense'?`<div class="win-attachment">${UI.icon('logs')}<div><strong>${UI.escape(d.attachment||'差旅票据.pdf')}</strong><div class="hint">附件名称演示 · 不上传或读取真实文件</div></div><button class="btn sm" style="margin-left:auto" onclick="UI.toast('此处展示票据附件入口；原型不保存文件内容。')">查看</button></div>`:''}</section><div class="notice">审批数据在已提交状态只读。退回或撤回后，在业务系统修改，再形成新的提交快照。</div></div><aside><section class="win-card"><h2>审批进度</h2><ol class="win-timeline">${d.history.map((h,i)=>`<li class="${i===d.history.length-1&&d.serviceStatus==='审批中'?'current':'done'}"><strong>${UI.escape(h.title)}</strong><small>${UI.escape(h.note)}</small></li>`).join('')}</ol></section><div class="win-bottom-note">时间线为简化交互样例：直属上级 → 财务 / 采购经理。完整规则由正式审批引擎执行。</div></aside></div>`;
  },
  /**
   * 创建原生待办界面。
   * @param {object} app 原型控制器。
   * @returns {string} 待办界面。
   */
  tasks(app) {
    const tasks=app.tasks();
    return `<div class="win-heading"><div><h1>我的待办 <span class="pill accent">${tasks.length}</span></h1><p>当前员工：${UI.escape(app.user)} · 由业务客户端绘制的原生审批窗口</p></div><button class="btn" onclick="Win.refresh()">${UI.icon('refresh')}刷新</button></div><div class="notice">这条路径由 WinUI 界面调用对接类库，通过 SDK 获取待办和处理审批；审批服务不提供 WinUI 控件。</div><div class="document-grid">${tasks.map(d=>`<article class="document-card"><header><span class="doc-id">${UI.escape(d.id)}</span><span class="pill accent">待我审批</span></header><div><h2>${UI.escape(d.title)}</h2><div class="doc-meta">${UI.escape(d.owner)} · ${UI.escape(d.department)}</div></div><div class="doc-money"><small>¥</small>${WinViews.money(d.amount)}</div><footer><span class="hint">${d.stage===0?'直属上级审批':d.type==='expense'?'财务审核':'采购经理审核'}</span><button class="btn primary" onclick="Win.approve('${d.id}')">处理审批</button></footer></article>`).join('')||'<div class="win-empty">当前员工暂无待办。切换为李娜可处理主管审批；后续切换为赵敏或吴芳。</div>'}</div>`;
  },
  /**
   * 创建嵌入模式说明和独立网页。
   * @returns {string} 嵌入界面。
   */
  webview() { return `<div class="win-heading"><div><h1>嵌入审批中心</h1><p>第二种集成方式 · WinUI 3 WebView2 容器示意</p></div><a class="btn" href="center.html" target="_blank">在浏览器打开 ${UI.icon('chevR')}</a></div><div class="notice warn">此处是 HTML iframe，用于评审嵌入效果，不是真实 WebView2。下方网页使用独立的演示数据和身份，不与桌面单据或左下角员工同步。正式版由业务后端取得短期嵌入令牌，再打开有权限的审批页面。</div><iframe class="win-embed" title="独立审批中心网页原型" src="center.html"></iframe>`; },
  /**
   * 创建对接职责说明。
   * @returns {string} 对接界面。
   */
  integration() {
    const rows=[['业务单据存储','审批状态、审批单 ID、核定金额 / 订单号','本地模拟；正式代码待实现'],['提交与撤回','预览、发起、撤回、重新提交与查询','对接类库规划'],['待办与审批','当前员工待办、同意 / 退回、刷新角标','对接类库规划'],['本机业务后端','收回调、取数、取项目经理、预算钩子、生成订单','对接类库规划'],['状态同步','签名验证、事件去重、状态回写、查询补偿','对接类库规划'],['演示初始化与自检','组织绑定、两个流程、17 个设计场景、--smoke','对接类库规划']];
    return `<div class="win-heading"><div><h1>对接改动</h1><p>把业务系统需要增加的代码，与界面职责分开。</p></div><a class="btn" href="demo.html">查看 17 个演示场景</a></div><div class="win-chain"><div><strong>01 · WinUI 3 界面</strong><small>.NET 10 / Windows<br>只引用对接类库</small></div><div><strong>02 · 对接类库</strong><small>net48 / net10.0<br>无界面依赖，可供 WinForms 参考</small></div><div><strong>03 · .NET SDK</strong><small>net461 / netstandard2.0 / net8.0<br>封装数据接口</small></div><div><strong>04 · 审批服务</strong><small>HTTP / JSON<br>流程、任务、快照与记录</small></div></div><div class="win-card"><h2>结果回到业务系统</h2><div class="notice">审批服务 → 签名回调 → 本机业务后端 / 对接类库 → 更新业务单据 → 刷新 WinUI 界面</div><p class="hint">没有业务后端时：WinUI → 对接类库 → SDK 查询审批状态 → 更新业务单据。正式版的取数、钩子和业务动作需提供可访问的业务接口；关闭本机后端的模式仅演示快照提交与查询同步。</p></div><div class="win-card"><h2>职责清单 · 正式实现时按文件统计改动</h2><div class="win-table"><table><thead><tr><th>改动点</th><th>职责</th><th>当前状态</th></tr></thead><tbody>${rows.map(r=>`<tr>${r.map(x=>`<td>${x}</td>`).join('')}</tr>`).join('')}</tbody></table></div><p class="win-bottom-note">当前仓库 SDK 只实现服务信息接口，不能把本页按钮当作已完成的业务 API。具体文件和行数待正式实现后统计。原型无签名校验、网络重试或实际回调。</p></div><div class="notice">WinUI 3 不运行于 .NET Framework；旧系统兼容由 net48 对接类库与 net461 SDK 保证。正式客户端安装包、Windows App SDK 和 WebView2 运行环境由后续实现任务处理。</div>`;
  },
  /**
   * 创建连接与模拟环境控制面板。
   * @param {object} app 原型控制器。
   * @returns {string} 设置界面。
   */
  settings(app) {
    return `<div class="win-heading"><div><h1>连接与演示环境</h1><p>在一台电脑上评审两种对接方式与异常提示。</p></div><button class="btn primary" onclick="Win.prepare()">一键准备演示环境</button></div><div class="grid-2"><section class="win-card"><h2>连接设置</h2><div class="win-inline"><div><strong>审批服务</strong><p>本页仅切换网络状态，不发起 HTTP 请求</p></div><button class="btn" onclick="Win.toggle('connected')">${app.connected?'模拟断网':'模拟恢复连接'}</button></div><div class="win-inline"><div><strong>本机业务后端</strong><p>模拟取数、取人、预算钩子与回调接收</p></div><button class="btn" onclick="Win.toggle('host')">${app.host?'关闭本机后端':'启动本机后端'}</button></div><div class="win-inline"><div><strong>状态同步方式</strong><p>回调收取结果，或由客户端主动查询</p></div><select class="sel" aria-label="状态同步方式" onchange="Win.mode=this.value;Win.persist();Win.render()"><option value="callback" ${app.mode==='callback'?'selected':''}>回调同步</option><option value="query" ${app.mode==='query'?'selected':''}>查询同步</option></select></div><div class="win-inline"><div><strong>当前员工</strong><p>切换身份后，待办角标随之变化</p></div><select class="sel" aria-label="设置当前员工" onchange="Win.switchUser(this.value)">${app.users.map(n=>`<option ${n===app.user?'selected':''}>${n}</option>`).join('')}</select></div></section><section class="win-card"><h2>同步演示</h2><p class="hint">先在“我的待办”处理一张单据，再观察业务状态回写。关闭后端时会保留待同步结果，可通过主动查询补偿。</p><div class="win-actions" style="margin:20px 0"><button class="btn" onclick="Win.deliver()">投递模拟回调</button><button class="btn" onclick="Win.duplicate()">重复投递同一事件</button><button class="btn" onclick="Win.refresh()">主动查询全部状态</button></div><div class="win-log" aria-live="polite">${app.logs.map(l=>`<div>${UI.escape(l)}</div>`).join('')||'等待模拟操作…'}</div></section></div><div class="notice">“一键准备”重置此页面的本地虚构单据、员工和同步记录，不会创建真实组织、流程或接入应用。不输入或保存 API Key、密码或签名密钥。</div>`;
  }
};
