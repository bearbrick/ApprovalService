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
    return `<div class="config-row"><label for="cfg-${key}">${label}</label><input id="cfg-${key}" class="inp" value="${UI.escape(D.state.settings[key] ?? value)}" onchange="D.setting('${key}',this.value)"></div>`;
  },
  /**
   * 绘制业务系统传入数据的定义表。
   * @returns {string} 页面内容。
   */
  fields() {
    const types = ['文本','数字','金额','日期','人员','部门','附件','明细表','计算字段'];
    return `<div class="notice">定义业务系统需要传入的审批数据。业务单据在业务系统创建和修改，审批服务保存提交时的数据快照。</div><div class="toolbar"><b>数据字段</b><span class="pill">${D.state.fields.length} 个字段</span><span style="flex:1"></span><button class="btn" onclick="D.importSchema()">从 JSON 示例生成</button><button class="btn primary" onclick="D.addField()">＋ 添加字段</button></div><div class="table-wrap"><table class="designer-table"><thead><tr><th style="width:35px">#</th><th style="width:19%">字段标识 key</th><th style="width:17%">显示名称</th><th style="width:13%">数据类型</th><th>必传</th><th>摘要</th><th>来源 / 说明</th><th></th></tr></thead><tbody>${D.state.fields.map((f,i) => `<tr><td class="muted">${i+1}</td><td><input aria-label="字段标识 ${i+1}" class="inp mono" value="${UI.escape(f.key)}" onchange="D.field(${i},'key',this.value)"></td><td><input aria-label="字段名称 ${i+1}" class="inp" value="${UI.escape(f.name)}" onchange="D.field(${i},'name',this.value)"></td><td><select aria-label="字段类型 ${i+1}" class="sel" onchange="D.field(${i},'type',this.value)">${types.map(t => `<option ${f.type===t?'selected':''}>${t}</option>`).join('')}</select></td><td><input aria-label="必传 ${f.name}" type="checkbox" ${f.required?'checked':''} onchange="D.field(${i},'required',this.checked)"></td><td><input aria-label="摘要 ${f.name}" type="checkbox" ${f.summary?'checked':''} onchange="D.field(${i},'summary',this.checked)"></td><td>${f.type==='计算字段'?'<span class="mono">SUM(items.amount)</span>':f.source==='审批填写'?'<span class="pill accent">审批人填写</span>':'<span class="muted">业务系统传入</span>'}</td><td><button class="btn sm ghost" onclick="D.editField(${i})">设置</button><button class="btn sm danger" aria-label="删除 ${f.name}" onclick="D.removeField(${i})">×</button></td></tr>`).join('')}</tbody></table></div><div class="design-footer"><span>摘要字段建议选择 3～5 个，在待办、邮件与提醒中展示</span><span>审批人填写的字段随回调交由业务系统处理</span></div><div class="config-card" style="margin-top:18px"><h3>发起校验与数据更新</h3><div class="config-grid"><div>${this.select('dataUpdate','审批中数据更新',['仅允许约定字段更新','不允许更新'])}${this.input('updateFields','可更新字段','attachments, remark')}</div><div>${this.select('missingField','字段缺失时',['拒绝发起并返回字段原因'])}${this.input('serial','审批编号规则','SP-{yyyyMM}-{0000}')}</div></div></div>`;
  },
  /**
   * 绘制完整审批规则配置。
   * @returns {string} 页面内容。
   */
  rules() {
    return `<div class="config-grid"><section class="config-card"><h3>自动处理</h3>${this.select('empty','找不到审批人',['不允许发起，提示原因','自动通过','转交流程管理员','转交指定成员'])}${this.select('self','审批人是发起人',['自动跳过','由本人审批','转交直属上级'])}${this.select('repeat','同一人多次出现',['相邻的只审一次','整个流程只审一次','每次都审'])}${this.select('disabled','审批人离职 / 停用',['转交直属上级','转交流程管理员'])}<p class="hint">到达节点时按最新组织关系计算审批人，与预览不一致时在操作记录中注明。</p></section><section class="config-card"><h3>退回、撤回与重新提交</h3>${this.select('return','允许退回到',['发起人、上一步及已处理节点','仅发起人','仅上一步'])}${this.select('resubmit','退回后重新提交',['从头开始审批','回到退回的节点'])}${this.select('withdraw','允许业务系统撤回',['审批结束前','第一个人处理前'])}${this.select('reject','会签中有人拒绝',['立即结束','等所有人处理后决定'])}<p class="hint">并行分支内仅可退回同一分支或分叉之前；补充材料后回到当前节点。</p></section><section class="config-card"><h3>时限与提醒</h3>${this.input('limit','节点处理时限','2 个工作日')}${this.input('before','到期前提醒','4 小时')}${this.select('reminder','超时提醒对象',['审批人和发起人','仅审批人'])}${this.select('daily','每日待办汇总',['每天 09:00 邮件汇总','关闭'])}<p class="hint">按管理后台的工作日历计算。超时自动同意、自动转交为二期功能。</p></section><section class="config-card"><h3>可见范围与接入授权</h3>${this.select('app','允许接入应用',['ERP 业务系统、示例业务系统','ERP 业务系统','示例业务系统'])}${this.input('departments','允许发起部门','所有部门')}${this.input('roles','允许发起角色 / 岗位','不限')}${this.select('view','查看权限',['发起人、参与人、抄送人','仅参与人（保密流程）'])}${this.input('extraView','额外查看人','本部门负责人')}${this.input('managers','流程管理员',D.state.owner)}</section></div>`;
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
    return `<div class="notice">已发布版本不可修改。在途审批单继续按发起时的版本流转；当前修改保存为新草稿。</div><div class="config-grid"><section class="config-card"><h3>版本历史</h3><div class="version-timeline"><article><h4>V${D.state.version+1} 草稿 <span class="pill warn">编辑中</span></h4><p class="hint">基于 V${D.state.version} 创建 · ${D.state.nodes.length} 个节点 · ${D.state.fields.length} 个字段</p><button class="btn sm" onclick="D.publish()">校验并发布</button></article>${D.state.history.map(h=>`<article><h4>V${h.version} <span class="pill ok">已发布</span></h4><p>${UI.escape(h.note)}</p><p class="hint">${h.date} · ${D.state.owner}</p></article>`).join('')}</div></section><section class="config-card"><h3>草稿与当前发布版本</h3><table class="designer-table"><thead><tr><th>配置项</th><th>发布版本</th><th>当前草稿</th></tr></thead><tbody><tr><td>流程节点</td><td>${D.state.publishedNodes??5}</td><td>${D.state.nodes.length}</td></tr><tr><td>审批数据字段</td><td>${D.state.publishedFields??8}</td><td>${D.state.fields.length}</td></tr><tr><td>画法</td><td>${D.state.publishedMode==='canvas'?'自由画布':'钉钉式'}</td><td>${D.state.mode==='canvas'?'自由画布':'钉钉式'}</td></tr></tbody></table><p class="hint" style="margin-top:16px">此处展示原型中的配置摘要。生产版本会记录完整差异及发布审计。</p></section></div>`;
  }
};
