/**
 * 权限设置弹窗（文件夹 / 文档通用）
 *   标签页：授权设置 / 生效明细 / 查询成员
 *   继承状态：继承上级 ⇄ 断开继承（断开时可复制现有权限，恢复时可选择保留或清除单独授权）
 */
(function () {
  'use strict';

  const esc = (s) => Util.escapeHtml(s);
  const { Toast, Modal } = window;

  const TYPE_NAMES = { folder: '文件夹', document: '文档' };
  const subjectKey = (type, id) => type + ':' + id;
  const kindOf = (type) => type === 'Role' ? '全员' : type === 'Department' ? '部门' : '成员';
  const iconOf = (type) => type === 'Role' ? Icons.users : type === 'Department' ? Icons.building : Icons.user;
  const kindOrder = (type) => type === 'Role' ? 0 : type === 'Department' ? 1 : 2;

  /** 授权来源说明 */
  function sourceText(g) {
    if (g.source === 'Owner') return '所有者';
    if (g.source === 'Direct') return '单独授权';
    return (g.isOwner ? '所有者 · ' : '') + '继承自“' + (g.fromFolderName || '上级') + '”';
  }

  const PermissionDialog = {
    /**
     * @param {{type: 'folder'|'document', id: number, name: string}} target
     * @returns {Promise<boolean>} 是否已保存
     */
    async open(target) {
      let data, dir;
      try {
        [data, dir] = await Promise.all([Api.getPermissions(target.type, target.id), Api.getDirectory()]);
      } catch (err) {
        Toast.error(err.message);
        return false;
      }

      const typeName = TYPE_NAMES[target.type];
      const isFolder = target.type === 'folder';

      // ---------- 可编辑状态 ----------
      const state = {
        inherit: data.inheritPermissions,
        entries: data.entries.map((e) => ({
          type: e.subjectType, id: e.subjectId, name: e.subjectName,
          actions: Perm.levelValue(e.actions), inherit: e.inheritToChildren
        })),
        confirm: null,   // 'break' | 'restore'：正在确认的继承操作
        pane: 'edit'
      };
      const originalInherit = data.inheritPermissions;

      // 授权对象：所有成员 / 部门 / 用户
      const subjects = new Map();
      subjects.set(subjectKey('Role', 'User'), { type: 'Role', id: 'User', name: '所有成员', kind: '全员' });
      dir.departments.forEach((d) => subjects.set(subjectKey('Department', d.id),
        { type: 'Department', id: String(d.id), name: d.name, kind: '部门' }));
      dir.users.forEach((u) => subjects.set(subjectKey('User', u.id), {
        type: 'User', id: String(u.id), name: u.displayName || u.username, kind: '成员',
        sub: u.username + (u.departmentName ? ' · ' + u.departmentName : '')
      }));

      const levelOptions = (selected) => Perm.levels.map((l) =>
        `<option value="${l.value}"${l.value === selected ? ' selected' : ''}>${l.name}</option>`).join('');

      const subjectCell = (type, name, extra = '', kind = kindOf(type)) => `
        <span class="perm-subject">
          <span class="perm-icon">${iconOf(type)}</span>
          <span class="perm-name" title="${esc(name)}">${esc(name)}</span>
          <span class="tag">${kind}</span>${extra}
        </span>`;

      // ---------- 渲染：授权设置 ----------
      function renderStatus(body) {
        const el = body.querySelector('.perm-status');
        const inheritedCount = data.inherited.length;
        const changed = state.inherit !== originalInherit
          ? '<em class="perm-unsaved">未保存</em>' : '';

        if (!data.canInherit) {
          el.className = 'perm-status top';
          el.innerHTML = `${Icons.folder}<div class="perm-status-text"><b>顶层文件夹</b><span>没有上级文件夹，权限完全由本文件夹的授权决定</span></div>`;
          return;
        }

        if (state.inherit) {
          el.className = 'perm-status inherit';
          el.innerHTML = state.confirm === 'break' ? `
            ${Icons.unlink}
            <div class="perm-status-text">
              <b>断开继承</b>
              <span>断开后，来自“${esc(data.parentName)}”的 ${inheritedCount} 条授权将不再对本${typeName}生效。是否把它们复制为本${typeName}的单独授权？</span>
              <div class="perm-status-actions">
                <button class="btn btn-primary btn-sm" type="button" data-inherit="break-copy">复制现有权限并断开</button>
                <button class="btn btn-sm" type="button" data-inherit="break-empty">不复制，直接断开</button>
                <button class="btn btn-ghost btn-sm" type="button" data-inherit="cancel">取消</button>
              </div>
            </div>` : `
            ${Icons.link}
            <div class="perm-status-text">
              <b>继承上级权限 ${changed}</b>
              <span>正在继承“${esc(data.parentName)}”的权限；本${typeName}的单独授权会在此基础上叠加</span>
            </div>
            <button class="btn btn-sm" type="button" data-inherit="ask-break">${Icons.unlink}<span>断开继承</span></button>`;
        } else {
          const ownCount = state.entries.length;
          el.className = 'perm-status broken';
          el.innerHTML = state.confirm === 'restore' ? `
            ${Icons.link}
            <div class="perm-status-text">
              <b>恢复继承</b>
              <span>恢复后将重新继承“${esc(data.parentName)}”的 ${inheritedCount} 条授权。${ownCount ? `本${typeName}现有的 ${ownCount} 条单独授权要保留吗？` : ''}</span>
              <div class="perm-status-actions">
                ${ownCount ? `
                  <button class="btn btn-primary btn-sm" type="button" data-inherit="restore-clear">清除单独授权并恢复</button>
                  <button class="btn btn-sm" type="button" data-inherit="restore-keep">保留单独授权并恢复</button>` : `
                  <button class="btn btn-primary btn-sm" type="button" data-inherit="restore-keep">恢复继承</button>`}
                <button class="btn btn-ghost btn-sm" type="button" data-inherit="cancel">取消</button>
              </div>
            </div>` : `
            ${Icons.unlink}
            <div class="perm-status-text">
              <b>已断开继承 ${changed}</b>
              <span>不再受“${esc(data.parentName)}”的权限影响，只有下方的授权生效</span>
            </div>
            <button class="btn btn-sm" type="button" data-inherit="ask-restore">${Icons.link}<span>恢复继承</span></button>`;
        }
      }

      function renderInherited(body) {
        const el = body.querySelector('.perm-inherited');
        if (!data.canInherit || !state.inherit) { el.innerHTML = ''; return; }
        el.innerHTML = `
          <div class="perm-section-title">继承的权限 <small>来自上级，需到上级修改或断开继承</small></div>
          <div class="perm-list">
            ${data.inherited.length ? data.inherited.map((g) => `
              <div class="perm-row inherited">
                ${subjectCell(g.subjectType, g.subjectName)}
                <span class="perm-level">${esc(g.levelName)}</span>
                <span class="perm-from" title="${esc(sourceText(g))}">${esc(sourceText(g))}</span>
              </div>`).join('') : '<div class="perm-empty">上级没有传递下来的授权</div>'}
          </div>`;
      }

      function renderEntries(body) {
        body.querySelector('.perm-own-title').innerHTML = `本${typeName}的授权 <small>${
          !data.canInherit || !state.inherit ? '只有以下授权生效' : '在继承的基础上额外授予'}</small>`;

        const list = body.querySelector('.perm-entries');
        list.innerHTML = state.entries.length ? state.entries.map((e, i) => `
          <div class="perm-row${isFolder ? '' : ' no-inherit'}" data-index="${i}">
            ${subjectCell(e.type, e.name)}
            <select class="input input-sm" data-field="actions" aria-label="权限级别">${levelOptions(e.actions)}</select>
            ${isFolder ? `
              <label class="perm-inherit" title="勾选后，子文件夹也会继承这条授权">
                <input type="checkbox" data-field="inherit"${e.inherit ? ' checked' : ''}><span>含子文件夹</span>
              </label>` : ''}
            <button class="icon-btn" type="button" data-field="remove" aria-label="移除">${Icons.close}</button>
          </div>`).join('')
          : `<div class="perm-empty">${state.inherit && data.canInherit ? '没有单独授权，可在下方添加' : '还没有任何授权，除管理员外无人可以访问'}</div>`;

        // 已授权的对象不再出现在“添加”下拉中
        const used = new Set(state.entries.map((e) => subjectKey(e.type, e.id)));
        const groups = { '全员': [], '部门': [], '成员': [] };
        subjects.forEach((s, key) => { if (!used.has(key)) groups[s.kind].push({ key, s }); });
        body.querySelector('.perm-add-subject').innerHTML = '<option value="">选择成员 / 部门…</option>' +
          Object.entries(groups).filter(([, arr]) => arr.length).map(([label, arr]) =>
            `<optgroup label="${label}">` + arr.map(({ key, s }) =>
              `<option value="${esc(key)}">${esc(s.name)}${s.sub ? '（' + esc(s.sub) + '）' : ''}</option>`).join('') +
            '</optgroup>').join('');
      }

      // ---------- 渲染：生效明细（根据当前未保存的状态实时计算） ----------
      function renderEffective(body) {
        const rows = new Map();
        const add = (type, id, name, actions, source) => {
          const key = subjectKey(type, id);
          const row = rows.get(key) || { type, name, actions: 0, sources: [] };
          row.actions |= actions;
          row.sources.push(source);
          rows.set(key, row);
        };
        if (data.owner) add('User', String(data.owner.id), data.owner.name, 31, { text: '所有者', level: '管理者' });
        if (data.canInherit && state.inherit) {
          data.inherited.forEach((g) => add(g.subjectType, g.subjectId, g.subjectName, g.actions,
            { text: sourceText(g), level: g.levelName, inherited: true }));
        }
        state.entries.forEach((e) => add(e.type, e.id, e.name, e.actions,
          { text: '单独授权', level: Perm.levelName(e.actions) }));

        const list = [...rows.values()].sort((a, b) => kindOrder(a.type) - kindOrder(b.type) || a.name.localeCompare(b.name, 'zh'));
        const dirty = state.inherit !== originalInherit || JSON.stringify(state.entries) !== initialEntries;

        body.querySelector('.perm-pane[data-pane="effective"]').innerHTML = `
          <p class="perm-tip">以下是${dirty ? '<b>按当前（未保存）设置</b>计算的' : ''}最终生效权限。部门、所有成员的授权对其中每个人生效，同一人有多条授权时取最高级别。</p>
          <div class="perm-list">
            <div class="perm-row effective admin-row">
              ${subjectCell('Role', '管理员', `<small class="perm-count">${data.adminCount} 人</small>`, '角色')}
              <span class="perm-level">全部权限</span>
              <span class="perm-sources"><span class="source-chip">系统角色</span></span>
            </div>
            ${list.map((r) => `
              <div class="perm-row effective">
                ${subjectCell(r.type, r.name)}
                <span class="perm-level"><b>${Perm.levelName(r.actions)}</b></span>
                <span class="perm-sources">${r.sources.map((s) => `
                  <span class="source-chip${s.inherited ? ' inherited' : ''}" title="${esc(s.text)}：${esc(s.level)}">
                    ${esc(s.text)}${r.sources.length > 1 ? ' · ' + esc(s.level) : ''}
                  </span>`).join('')}
                </span>
              </div>`).join('')}
          </div>
          ${list.length ? '' : '<p class="perm-empty">除管理员外，没有人可以访问</p>'}`;
      }

      // ---------- 渲染：查询成员 ----------
      function renderCheck(body) {
        const pane = body.querySelector('.perm-pane[data-pane="check"]');
        if (pane.dataset.ready) return;
        pane.dataset.ready = '1';
        pane.innerHTML = `
          <p class="perm-tip">选择一名成员，查看他对本${typeName}的最终权限以及权限来自哪里（基于<b>已保存</b>的设置）。</p>
          <select class="input perm-check-user" aria-label="选择成员">
            <option value="">选择成员…</option>
            ${dir.users.map((u) => `<option value="${u.id}">${esc(u.displayName || u.username)}（${esc(u.username)}${u.departmentName ? ' · ' + esc(u.departmentName) : ''}）</option>`).join('')}
          </select>
          <div class="perm-check-result"></div>`;
        pane.querySelector('.perm-check-user').addEventListener('change', async (e) => {
          const out = pane.querySelector('.perm-check-result');
          if (!e.target.value) { out.innerHTML = ''; return; }
          out.innerHTML = '<div class="list-loading"><div class="spinner"></div></div>';
          try {
            const r = await Api.checkPermission(target.type, target.id, Number(e.target.value));
            out.innerHTML = `
              <div class="perm-check-level${r.actions ? '' : ' none'}">
                <span>${esc(r.userName)}</span>
                <b>${esc(r.levelName)}</b>
              </div>
              ${r.reasons.length ? `
                <div class="perm-section-title">权限来源</div>
                <div class="perm-list">
                  ${r.reasons.map((g) => `
                    <div class="perm-row inherited">
                      ${subjectCell(g.subjectType, g.subjectName)}
                      <span class="perm-level">${esc(g.levelName)}</span>
                      <span class="perm-from" title="${esc(sourceText(g))}">${esc(sourceText(g))}</span>
                    </div>`).join('')}
                </div>` : `<p class="perm-empty">没有任何授权适用于该成员${r.isDisabled ? '' : '，因此无法访问'}</p>`}`;
          } catch (err) {
            out.innerHTML = `<p class="perm-error">${esc(err.message)}</p>`;
          }
        });
      }

      function renderAll(body) {
        renderStatus(body);
        renderInherited(body);
        renderEntries(body);
        if (state.pane === 'effective') renderEffective(body);
      }

      const initialEntries = JSON.stringify(state.entries);

      // ---------- 继承操作 ----------
      function applyInheritAction(body, action) {
        if (action === 'ask-break') state.confirm = 'break';
        else if (action === 'ask-restore') state.confirm = 'restore';
        else if (action === 'cancel') state.confirm = null;
        else if (action === 'break-copy' || action === 'break-empty') {
          if (action === 'break-copy') {
            // 把继承的授权复制为单独授权（同一对象取最高级别）
            data.inherited.forEach((g) => {
              const existing = state.entries.find((e) => e.type === g.subjectType && e.id === g.subjectId);
              const level = Perm.levelValue(g.actions);
              if (existing) existing.actions = Perm.levelValue(existing.actions | level);
              else state.entries.push({ type: g.subjectType, id: g.subjectId, name: g.subjectName, actions: level, inherit: true });
            });
          }
          state.inherit = false;
          state.confirm = null;
        } else if (action === 'restore-keep' || action === 'restore-clear') {
          if (action === 'restore-clear') state.entries = [];
          state.inherit = true;
          state.confirm = null;
        }
        renderAll(body);
      }

      const saved = await Modal.custom({
        title: '权限 · ' + target.name,
        okText: '保存',
        wide: true,
        build: (body) => {
          body.innerHTML = `
            <div class="perm-path" title="${esc(data.path)}">${isFolder ? Icons.folder : Icons.file(target.name)}<span>${esc(data.path)}${isFolder ? '' : ' / ' + esc(target.name)}</span></div>
            <div class="perm-tabs" role="tablist">
              <button type="button" class="perm-tab active" data-pane="edit">授权设置</button>
              <button type="button" class="perm-tab" data-pane="effective">生效明细</button>
              <button type="button" class="perm-tab" data-pane="check">查询成员</button>
            </div>
            <div class="perm-pane" data-pane="edit">
              <div class="perm-status"></div>
              ${data.owner ? `<div class="perm-owner">${Icons.user}<span>所有者：${esc(data.owner.name)}（创建者，自动拥有管理权限）</span></div>` : ''}
              <div class="perm-inherited"></div>
              <div class="perm-section-title perm-own-title"></div>
              <div class="perm-list perm-entries"></div>
              <div class="perm-add">
                <select class="input perm-add-subject" aria-label="选择授权对象"></select>
                <select class="input perm-add-level" aria-label="权限级别">${levelOptions(1)}</select>
                <button class="btn" type="button" data-field="add">${Icons.plus}<span>添加</span></button>
              </div>
              <details class="perm-help">
                <summary>权限级别与图标说明</summary>
                <ul>
                  ${Perm.levels.map((l) => `<li><b>${l.name}</b>：${isFolder ? l.desc : l.docDesc}</li>`).join('')}
                  <li><span class="perm-badge own">${Icons.shield}</span> 继承上级，并有单独授权</li>
                  <li><span class="perm-badge broken">${Icons.unlink}</span> 已断开继承，只有自身授权生效</li>
                  <li>无图标：完全继承上级权限</li>
                </ul>
              </details>
            </div>
            <div class="perm-pane" data-pane="effective" hidden></div>
            <div class="perm-pane" data-pane="check" hidden></div>
            <p class="field-error" hidden></p>`;
          renderAll(body);

          body.addEventListener('change', (e) => {
            const row = e.target.closest('.perm-row[data-index]');
            if (!row) return;
            const entry = state.entries[Number(row.dataset.index)];
            if (e.target.dataset.field === 'actions') entry.actions = Number(e.target.value);
            if (e.target.dataset.field === 'inherit') entry.inherit = e.target.checked;
          });

          body.addEventListener('click', (e) => {
            const tab = e.target.closest('.perm-tab');
            if (tab) {
              state.pane = tab.dataset.pane;
              body.querySelectorAll('.perm-tab').forEach((t) => t.classList.toggle('active', t === tab));
              body.querySelectorAll('.perm-pane').forEach((p) => { p.hidden = p.dataset.pane !== state.pane; });
              if (state.pane === 'effective') renderEffective(body);
              if (state.pane === 'check') renderCheck(body);
              return;
            }
            const inheritBtn = e.target.closest('[data-inherit]');
            if (inheritBtn) { applyInheritAction(body, inheritBtn.dataset.inherit); return; }

            const btn = e.target.closest('button[data-field]');
            if (!btn) return;
            if (btn.dataset.field === 'remove') {
              state.entries.splice(Number(btn.closest('.perm-row').dataset.index), 1);
              renderEntries(body);
            } else if (btn.dataset.field === 'add') {
              const key = body.querySelector('.perm-add-subject').value;
              if (!key) { Modal.showError(body, '请选择要授权的成员或部门'); return; }
              const s = subjects.get(key);
              state.entries.push({ type: s.type, id: s.id, name: s.name, actions: Number(body.querySelector('.perm-add-level').value), inherit: true });
              body.querySelector('.field-error').hidden = true;
              renderEntries(body);
            }
          });
          return null;
        },
        submit: async (body) => {
          if (state.confirm) throw new Error('请先确认或取消“' + (state.confirm === 'break' ? '断开继承' : '恢复继承') + '”操作');
          // 已选对象但忘记点“添加”时提醒
          if (body.querySelector('.perm-add-subject').value) {
            throw new Error('已选择授权对象但尚未添加，请点击“添加”或清空选择');
          }
          if (!state.inherit && !state.entries.length && !data.owner && !state.emptyConfirmed) {
            // 断开且无任何授权：只有管理员可访问，这是合法的，但需要再次确认
            state.emptyConfirmed = true;
            throw new Error('当前没有任何授权，保存后除管理员外无人可以访问该' + typeName + '。确认无误请再次点击“保存”');
          }
          await Api.savePermissions(target.type, target.id, state.inherit, state.entries.map((e) => ({
            subjectType: e.type, subjectId: e.id, actions: e.actions, inheritToChildren: isFolder ? e.inherit : true
          })));
        }
      });
      if (saved) Toast.success('权限已保存');
      return !!saved;
    }
  };

  window.PermissionDialog = PermissionDialog;
})();
