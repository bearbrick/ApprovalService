/**
 * 沿执行图进行有限场景演示，结果始终由节点与连线决定。
 */
const DesignerSimulation = {
  /**
   * 生成场景与输入数据控件。
   * @returns {string} 场景输入标记。
   */
  scenarioForm() {const scenarios=this.state.scenarios||[];const data=scenarios[0]?.input||{amount:28600,rework:'无需返工'};return `<label class="field">演示场景<select class="sel" id="graph-scenario" onchange="D.selectScenario(this.value)">${scenarios.map((s,i)=>`<option value="${i}">${UI.escape(s.title)}</option>`).join('')||'<option>自定义数据</option>'}</select></label><label class="field" style="margin-top:12px">审批数据 JSON<textarea class="inp mono" id="graph-data" rows="5">${UI.escape(JSON.stringify(data,null,2))}</textarea></label><p class="hint" style="margin-top:8px">场景包含预先准备的条件结果。手动改金额或明细时，needsGeneralManager、needsFinanceReview 等不会自动重算，须一并调整。它们仅用于原型测试；正式版由审批引擎计算业务规则，不要求业务系统传入审批结论。</p>`;},
  /**
   * 切换预置场景的输入，不改变业务快照。
   * @param {string} index 场景序号。
   * @returns {void}
   */
  selectScenario(index) {const s=this.state.scenarios?.[Number(index)];if(s)document.querySelector('#graph-data').value=JSON.stringify(s.input,null,2);},
  /**
   * 从场景对话框读取输入。
   * @param {HTMLDialogElement} dialog 当前对话框。
   * @returns {object|null} 合法数据对象或空值。
   */
  readScenario(dialog) {try{const data=JSON.parse(dialog.querySelector('#graph-data').value);if(!data||Array.isArray(data)||typeof data!=='object')throw new Error();return data;}catch{UI.toast('请输入 JSON 对象形式的审批数据。');return null;}},
  /**
   * 根据节点和连线呈现路径预览。
   * @returns {void}
   */
  preview() {const data=this.state.scenarios?.[0]?.input||{amount:28600,rework:'无需返工'};UI.modal('流程预览 · '+this.state.name,`${this.scenarioForm()}<div id="graph-preview">${this.pathHtml(data)}</div>`,dialog=>{const input=this.readScenario(dialog);if(!input)return false;dialog.querySelector('#graph-preview').innerHTML=this.pathHtml(input);return false;},'重新计算');},
  /**
   * 将按图计算的批次呈现为路径列表。
   * @param {object|number} data 演示数据，数字兼容金额输入。
   * @returns {string} 路径列表。
   */
  pathHtml(data) {const trace=DesignerGraph.trace(this.state,typeof data==='number'?{amount:data}:data);return `<ol class="simulation-steps">${trace.waves.map((wave,i)=>`<li><b>步骤 ${i+1}${wave.length>1?' · 并行处理':''}</b><div>${wave.map(id=>UI.escape(this.state.nodes.find(n=>n.id===id)?.name||id)).join(' ＋ ')}</div></li>`).join('')}</ol>${trace.notes.map(n=>`<div class="notice warn">${UI.escape(n)}</div>`).join('')}<div class="notice ${trace.complete?'':'warn'}">${trace.complete?'当前输入沿连线到达结束事件。':'当前路径没有完成，不判定审批通过。'}</div><p class="hint" style="margin-top:12px">仅演示单字段比较、默认分支、并行批次和简单汇合。遇到无法计算的条件或第二次进入回路会暂停。处理人、会签计票、委托、权限和外部动作未执行。</p>`;},
  /**
   * 选择测试数据后按实际图路径逐批推进。
   * @returns {void}
   */
  test() {UI.modal('测试模式 · 选择场景',`${this.scenarioForm()}<p class="hint" style="margin-top:14px">此模式用于核对图的路线。真实审批规则、计票及外部接口不在本地执行。</p>`,dialog=>{const data=this.readScenario(dialog);if(!data)return false;this.simulation=DesignerGraph.trace(this.state,data);setTimeout(()=>this.testStep(0),0);},'开始路径演示');},
  /**
   * 展示当前按图推进的批次及结果。
   * @param {number} step 当前批次序号。
   * @returns {void}
   */
  testStep(step) {const trace=this.simulation,done=step>=trace.waves.length;UI.modal('测试模式 · '+(done?'结果':'第 '+(step+1)+' 批'),`<div class="notice">${done?(trace.complete?'路径演示已到达结束事件。':'路径演示暂停，未判定通过。'):'当前批次的多个节点表示并行分支；继续后沿实际连线推进。'}</div><ol class="simulation-steps">${trace.waves.map((wave,i)=>`<li class="${i<step?'done':i===step?'current':''}"><b>${wave.map(id=>UI.escape(this.state.nodes.find(n=>n.id===id)?.name||id)).join(' ＋ ')}</b><div class="hint">${i<step?'已模拟处理':i===step?'当前批次':'等待流转'}</div></li>`).join('')}</ol>${done?trace.notes.map(n=>`<div class="notice warn">${UI.escape(n)}</div>`).join(''):'<button class="btn danger" onclick="UI.closeModal();D.testRejected()">模拟拒绝并结束</button>'}<p class="hint">这是图路径演示，不代表生产审批、会签或票签结果。</p>`,()=>{if(!done)setTimeout(()=>this.testStep(step+1),0);},done?'结束测试':'完成当前批次');},
  /**
   * 恢复当前流程的最新样例，先备份已有草稿。
   * @returns {void}
   */
  resetTemplate() {UI.modal('恢复当前流程的示例模板','<p>将替换当前流程的本地草稿，其他流程不受影响。建议先使用“导出”保存一份。</p><div class="notice">确认后会额外保存一份恢复前的浏览器备份。</div>',()=>{const key=this.state.flowKey;localStorage.setItem('approval-prototype-designer-backup-'+key+'-'+Date.now(),JSON.stringify(this.state));this.state=DesignerGraph.migrate(DesignerTemplates.create(key,this.base));this.drafts[key]=this.state;this.selected=this.state.nodes.find(n=>n.type==='approval')?.id||this.state.nodes[0]?.id||'';this.render();this.save();setTimeout(()=>this.fit(),0);},'确认恢复');}
};
