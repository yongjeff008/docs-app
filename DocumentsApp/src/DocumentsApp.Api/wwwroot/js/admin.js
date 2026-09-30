/**
 * 系统管理页：用户 / 部门 / 操作日志（仅管理员）
 */
(function () {
  'use strict';

  const $ = (sel, root = document) => root.querySelector(sel);
  const esc = (s) => Util.escapeHtml(s);
  const { Toast, Modal, Menu } = window;

  /** 完整日期时间：2026-09-28 14:05 */
  function formatDateTime(value) {
    if (!value) return '';
    const d = new Date(value);
    if (isNaN(d.getTime())) return '';
    const pad = (n) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }

  const Admin = {
    me: null,
    users: [],
    departments: [],
    logPage: 1,
    logKeyword: '',

    async init() {
      let me;
      try {
        me = await Api.me();
      } catch (_) {
        return; // Api 已处理跳转
      }
      if (!me.isAdmin) {
        location.href = '/';
        return;
      }
      this.me = me;
      $('#userName').textContent = me.displayName || me.username;
      $('#userAvatar').textContent = (me.displayName || me.username || '?').slice(0, 1);
      $('#userMenuName').textContent = me.username + '（' + me.roleName + '）';

      document.querySelectorAll('[data-icon]').forEach((el) => {
        const icon = Icons[el.dataset.icon];
        if (icon) el.insertAdjacentHTML('afterbegin', icon);
      });

      this._bindEvents();
      const tab = (location.hash || '').slice(1);
      this.switchTab(['users', 'departments', 'permissions', 'logs'].includes(tab) ? tab : 'users');
      document.body.classList.add('ready');
    },

    _bindEvents() {
      // 用户菜单
      $('#userBtn').addEventListener('click', (e) => {
        e.stopPropagation();
        $('#userDropdown').classList.toggle('open');
      });
      document.addEventListener('click', () => $('#userDropdown').classList.remove('open'));
      $('#logoutBtn').addEventListener('click', async () => {
        try { await Api.logout(); } catch (_) { /* 忽略 */ }
        location.href = '/login.html';
      });
      $('#changePwdBtn').addEventListener('click', () => UI.changeOwnPassword());

      // 标签页
      document.querySelector('.tabs').addEventListener('click', (e) => {
        const tab = e.target.closest('.tab');
        if (tab) this.switchTab(tab.dataset.tab);
      });

      // 用户
      $('#newUserBtn').addEventListener('click', () => this.createUser());
      $('#userSearch').addEventListener('input', Util.debounce(() => this.renderUsers(), 200));
      $('#userList').addEventListener('click', (e) => {
        const btn = e.target.closest('[data-action]');
        if (!btn) return;
        const user = this.users.find((u) => u.id === Number(btn.closest('[data-id]').dataset.id));
        if (!user) return;
        if (btn.dataset.action === 'edit') this.editUser(user);
        else if (btn.dataset.action === 'more') {
          e.stopPropagation();
          const r = btn.getBoundingClientRect();
          this.showUserMenu(user, r.right - 168, r.bottom + 4);
        }
      });

      // 部门
      $('#newDeptBtn').addEventListener('click', () => this.createDepartment());
      $('#deptList').addEventListener('click', (e) => {
        const btn = e.target.closest('[data-action]');
        if (!btn) return;
        const dept = this.departments.find((d) => d.id === Number(btn.closest('[data-id]').dataset.id));
        if (!dept) return;
        if (btn.dataset.action === 'rename') this.renameDepartment(dept);
        else if (btn.dataset.action === 'delete') this.deleteDepartment(dept);
      });

      // 权限总览
      $('#permSearch').addEventListener('input', Util.debounce(() => this.renderPermissions(), 200));
      $('#permFilter').addEventListener('change', () => this.renderPermissions());

      // 日志
      $('#logSearch').addEventListener('input', Util.debounce(() => {
        this.logKeyword = $('#logSearch').value.trim();
        this.logPage = 1;
        this.loadLogs();
      }, 300));
      $('#logPager').addEventListener('click', (e) => {
        const btn = e.target.closest('[data-page]');
        if (!btn || btn.disabled) return;
        this.logPage = Number(btn.dataset.page);
        this.loadLogs();
      });

      window.addEventListener('resize', () => Menu.hide());
      document.addEventListener('keydown', (e) => { if (e.key === 'Escape') Menu.hide(); });
    },

    switchTab(name) {
      document.querySelectorAll('.tab').forEach((t) => t.classList.toggle('active', t.dataset.tab === name));
      document.querySelectorAll('.admin-panel').forEach((p) => { p.hidden = p.dataset.panel !== name; });
      history.replaceState(null, '', '#' + name);
      if (name === 'users') this.loadUsers();
      else if (name === 'departments') this.loadDepartments();
      else if (name === 'permissions') this.loadPermissions();
      else this.loadLogs();
    },

    _loading(el) {
      el.innerHTML = '<div class="list-loading"><div class="spinner"></div></div>';
    },

    // ================= 用户 =================
    async loadUsers() {
      if (!this.users.length) this._loading($('#userList'));
      try {
        [this.users, this.departments] = await Promise.all([Api.getUsers(), Api.getDepartments()]);
        this.renderUsers();
      } catch (err) {
        $('#userList').innerHTML = `<div class="empty-state"><p>${esc('加载失败：' + err.message)}</p></div>`;
      }
    },

    renderUsers() {
      const kw = $('#userSearch').value.trim().toLowerCase();
      const list = this.users.filter((u) => !kw ||
        [u.username, u.displayName, u.departmentName].some((s) => (s || '').toLowerCase().includes(kw)));

      if (!list.length) {
        $('#userList').innerHTML = `<div class="empty-state"><h3>${kw ? '没有匹配的用户' : '还没有用户'}</h3></div>`;
        return;
      }

      $('#userList').innerHTML = `
        <div class="data-row data-head user-grid">
          <span>用户</span><span>部门</span><span>角色</span><span>状态</span><span>最后登录</span><span></span>
        </div>
        ${list.map((u) => `
          <div class="data-row user-grid${u.isEnabled ? '' : ' is-disabled'}" data-id="${u.id}">
            <span class="cell-main">
              <span class="avatar avatar-sm">${esc((u.displayName || u.username).slice(0, 1))}</span>
              <span class="cell-text">
                <b>${esc(u.displayName)}${u.id === this.me.id ? ' <em class="tag tag-blue">我</em>' : ''}</b>
                <small>${esc(u.username)}</small>
              </span>
            </span>
            <span class="cell" data-label="部门">${esc(u.departmentName || '—')}</span>
            <span class="cell" data-label="角色"><span class="tag${u.role === 'Admin' ? ' tag-blue' : ''}">${esc(u.roleName)}</span></span>
            <span class="cell" data-label="状态"><span class="status ${u.isEnabled ? 'on' : 'off'}">${u.isEnabled ? '启用' : '已禁用'}</span></span>
            <span class="cell" data-label="最后登录">${u.lastLoginAt ? esc(formatDateTime(u.lastLoginAt)) : '从未登录'}</span>
            <span class="cell-actions">
              <button class="icon-btn" data-action="edit" title="编辑" aria-label="编辑">${Icons.edit}</button>
              <button class="icon-btn" data-action="more" title="更多操作" aria-label="更多操作">${Icons.more}</button>
            </span>
          </div>`).join('')}`;
    },

    _deptOptions() {
      return [{ value: '', label: '（无部门）' }].concat(this.departments.map((d) => ({ value: d.id, label: d.name })));
    },

    _roleOptions() {
      return [{ value: 'User', label: '普通成员（按文件夹授权访问）' }, { value: 'Admin', label: '管理员（全部权限）' }];
    },

    async createUser() {
      const created = await Modal.form({
        title: '新建用户',
        okText: '创建',
        fields: [
          { name: 'username', label: '用户名（登录用）', required: true, placeholder: '字母、数字或 _ . -', maxlength: 32 },
          { name: 'displayName', label: '姓名', required: true, maxlength: 64 },
          { name: 'password', label: '初始密码', type: 'password', required: true, autocomplete: 'new-password', hint: '至少 6 位，建议用户登录后自行修改' },
          { name: 'departmentId', label: '部门', type: 'select', options: this._deptOptions(), value: '' },
          { name: 'role', label: '角色', type: 'select', options: this._roleOptions(), value: 'User' }
        ],
        validate: (v) => v.password.length < 6 ? '密码至少需要 6 位' : null,
        submit: (v) => Api.createUser({
          username: v.username,
          displayName: v.displayName,
          password: v.password,
          role: v.role,
          departmentId: v.departmentId ? Number(v.departmentId) : null
        })
      });
      if (created) {
        Toast.success('用户已创建');
        await this.loadUsers();
      }
    },

    async editUser(user) {
      const isSelf = user.id === this.me.id;
      const ok = await Modal.form({
        title: '编辑用户 · ' + user.username,
        fields: [
          { name: 'displayName', label: '姓名', required: true, value: user.displayName, maxlength: 64 },
          { name: 'departmentId', label: '部门', type: 'select', options: this._deptOptions(), value: user.departmentId ?? '' },
          { name: 'role', label: '角色', type: 'select', options: this._roleOptions(), value: user.role, disabled: isSelf,
            hint: isSelf ? '不能修改自己的角色' : '' }
        ],
        submit: (v) => Api.updateUser(user.id, {
          displayName: v.displayName,
          departmentId: v.departmentId ? Number(v.departmentId) : null,
          role: isSelf ? user.role : v.role
        })
      });
      if (ok) {
        Toast.success('已保存');
        await this.loadUsers();
      }
    },

    showUserMenu(user, x, y) {
      const isSelf = user.id === this.me.id;
      const items = [{ label: '编辑', icon: Icons.edit, action: () => this.editUser(user) }];
      if (!isSelf) {
        items.push(
          { label: '重置密码', icon: Icons.key, action: () => this.resetPassword(user) },
          { label: user.isEnabled ? '禁用账号' : '启用账号', icon: Icons.toggle, action: () => this.toggleUser(user) },
          { label: '删除用户', icon: Icons.trash, danger: true, action: () => this.deleteUser(user) }
        );
      }
      Menu.show(items, x, y, user.displayName + '（' + user.username + '）');
    },

    async resetPassword(user) {
      const ok = await Modal.form({
        title: '重置密码 · ' + user.username,
        okText: '重置',
        fields: [
          { name: 'password', label: '新密码', type: 'password', required: true, autocomplete: 'new-password', hint: '至少 6 位。重置后该用户需使用新密码重新登录' }
        ],
        validate: (v) => v.password.length < 6 ? '密码至少需要 6 位' : null,
        submit: (v) => Api.resetPassword(user.id, v.password)
      });
      if (ok) Toast.success('密码已重置');
    },

    async toggleUser(user) {
      const enable = !user.isEnabled;
      if (!enable) {
        const ok = await Modal.confirm({
          title: '禁用账号',
          message: `确定要禁用“${user.displayName}（${user.username}）”吗？禁用后该用户将无法登录，已登录的会话会立即失效。`,
          okText: '禁用',
          danger: true
        });
        if (!ok) return;
      }
      try {
        // 部门字段按当前值一并提交（后端 null 表示无部门）
        await Api.updateUser(user.id, { isEnabled: enable, departmentId: user.departmentId });
        Toast.success(enable ? '账号已启用' : '账号已禁用');
        await this.loadUsers();
      } catch (err) {
        Toast.error(err.message);
      }
    },

    async deleteUser(user) {
      const ok = await Modal.confirm({
        title: '删除用户',
        message: `确定要删除“${user.displayName}（${user.username}）”吗？该用户的文件夹授权将一并移除，其上传的文档会保留。此操作不可恢复，如只是暂停使用，建议改为“禁用账号”。`,
        okText: '删除',
        danger: true
      });
      if (!ok) return;
      try {
        await Api.deleteUser(user.id);
        Toast.success('用户已删除');
        await this.loadUsers();
      } catch (err) {
        Toast.error(err.message);
      }
    },

    // ================= 部门 =================
    async loadDepartments() {
      if (!this.departments.length) this._loading($('#deptList'));
      try {
        this.departments = await Api.getDepartments();
        this.renderDepartments();
      } catch (err) {
        $('#deptList').innerHTML = `<div class="empty-state"><p>${esc('加载失败：' + err.message)}</p></div>`;
      }
    },

    renderDepartments() {
      if (!this.departments.length) {
        $('#deptList').innerHTML = `
          <div class="empty-state">${Icons.emptyFolder}<h3>还没有部门</h3><p>创建部门后，可在用户编辑中为成员设置所属部门</p></div>`;
        return;
      }
      $('#deptList').innerHTML = `
        <div class="data-row data-head dept-grid"><span>部门名称</span><span>成员数</span><span></span></div>
        ${this.departments.map((d) => `
          <div class="data-row dept-grid" data-id="${d.id}">
            <span class="cell-main"><span class="perm-icon">${Icons.building}</span><b>${esc(d.name)}</b></span>
            <span class="cell" data-label="成员数">${d.userCount} 人</span>
            <span class="cell-actions">
              <button class="icon-btn" data-action="rename" title="重命名" aria-label="重命名">${Icons.edit}</button>
              <button class="icon-btn" data-action="delete" title="删除" aria-label="删除">${Icons.trash}</button>
            </span>
          </div>`).join('')}`;
    },

    async createDepartment() {
      const ok = await Modal.form({
        title: '新建部门',
        okText: '创建',
        fields: [{ name: 'name', label: '部门名称', required: true, maxlength: 64 }],
        submit: (v) => Api.createDepartment(v.name)
      });
      if (ok) {
        Toast.success('部门已创建');
        await this.loadDepartments();
      }
    },

    async renameDepartment(dept) {
      const ok = await Modal.form({
        title: '重命名部门',
        fields: [{ name: 'name', label: '部门名称', required: true, value: dept.name, maxlength: 64 }],
        submit: (v) => v.name === dept.name ? null : Api.renameDepartment(dept.id, v.name)
      });
      if (ok) {
        Toast.success('已保存');
        await this.loadDepartments();
      }
    },

    async deleteDepartment(dept) {
      if (dept.userCount > 0) {
        Toast.info(`该部门还有 ${dept.userCount} 名成员，请先在“用户”中调整他们的部门`);
        return;
      }
      const ok = await Modal.confirm({
        title: '删除部门',
        message: `确定要删除部门“${dept.name}”吗？该部门的文件夹授权将一并移除。`,
        okText: '删除',
        danger: true
      });
      if (!ok) return;
      try {
        await Api.deleteDepartment(dept.id);
        Toast.success('部门已删除');
        await this.loadDepartments();
      } catch (err) {
        Toast.error(err.message);
      }
    },

    // ================= 权限总览 =================
    permItems: [],

    async loadPermissions() {
      this._loading($('#permList'));
      try {
        this.permItems = await Api.getPermissionOverview();
        this.renderPermissions();
      } catch (err) {
        $('#permList').innerHTML = `<div class="empty-state"><p>${esc('加载失败：' + err.message)}</p></div>`;
      }
    },

    renderPermissions() {
      const kw = $('#permSearch').value.trim().toLowerCase();
      const filter = $('#permFilter').value;
      const list = this.permItems.filter((it) =>
        (!filter || (filter === 'broken' ? !it.inheritPermissions : it.inheritPermissions)) &&
        (!kw || it.path.toLowerCase().includes(kw) || it.grants.some((g) => g.subjectName.toLowerCase().includes(kw))));

      if (!list.length) {
        $('#permList').innerHTML = `<div class="empty-state"><h3>${this.permItems.length ? '没有匹配的项目' : '所有文件夹和文档都完全继承上级权限'}</h3></div>`;
        return;
      }

      $('#permList').innerHTML = `
        <div class="data-row data-head perm-grid"><span>文件夹 / 文档</span><span>继承状态</span><span>单独授权</span><span></span></div>
        ${list.map((it) => {
          const isFolder = it.resourceType === 'Folder';
          const type = isFolder ? 'folder' : 'document';
          const href = `/#folder=${it.folderId}&perm=${type}:${it.resourceId}`;
          return `
            <div class="data-row perm-grid">
              <span class="cell-main">
                <span class="perm-type-icon">${isFolder ? Icons.folder : Icons.file(it.name)}</span>
                <span class="cell-text">
                  <b title="${esc(it.path)}">${esc(it.name)}</b>
                  <small title="${esc(it.path)}">${esc(it.path)}</small>
                </span>
              </span>
              <span class="cell" data-label="状态">
                ${it.inheritPermissions
                  ? `<span class="perm-state own">${Icons.shield}继承 + 单独授权</span>`
                  : `<span class="perm-state broken">${Icons.unlink}已断开继承</span>`}
              </span>
              <span class="cell perm-grant-cell" data-label="授权">
                ${it.grants.length ? it.grants.map((g) => `
                  <span class="source-chip" title="${esc(g.subjectName)}：${esc(g.levelName)}${g.inheritToChildren === false ? '（仅本文件夹）' : ''}">
                    ${esc(g.subjectName)} · ${esc(g.levelName)}${g.inheritToChildren === false ? ' · 仅本级' : ''}
                  </span>`).join('') : '<span class="muted">无（仅管理员可访问）</span>'}
              </span>
              <span class="cell-actions">
                <a class="btn btn-sm" href="${href}" title="打开并查看权限明细">查看</a>
              </span>
            </div>`;
        }).join('')}`;
    },

    // ================= 操作日志 =================
    async loadLogs() {
      this._loading($('#logList'));
      $('#logPager').innerHTML = '';
      try {
        const data = await Api.getAuditLogs(this.logPage, this.logKeyword);
        this.renderLogs(data);
      } catch (err) {
        $('#logList').innerHTML = `<div class="empty-state"><p>${esc('加载失败：' + err.message)}</p></div>`;
      }
    },

    renderLogs(data) {
      if (!data.items.length) {
        $('#logList').innerHTML = `<div class="empty-state"><h3>${this.logKeyword ? '没有匹配的日志' : '暂无操作日志'}</h3></div>`;
        return;
      }
      $('#logList').innerHTML = `
        <div class="data-row data-head log-grid"><span>时间</span><span>操作人</span><span>操作</span><span>对象</span><span>IP</span></div>
        ${data.items.map((l) => `
          <div class="data-row log-grid">
            <span class="cell log-time">${esc(formatDateTime(l.createdAt))}</span>
            <span class="cell" data-label="操作人">${esc(l.userName || '—')}</span>
            <span class="cell" data-label="操作"><span class="tag${/失败|删除|禁用/.test(l.action) ? ' tag-red' : ''}">${esc(l.action)}</span></span>
            <span class="cell log-target" data-label="对象">
              <span>${esc(l.targetName || '—')}</span>
              ${l.detail ? `<small title="${esc(l.detail)}">${esc(l.detail)}</small>` : ''}
            </span>
            <span class="cell log-ip" data-label="IP">${esc(l.ipAddress || '')}</span>
          </div>`).join('')}`;

      const pages = Math.max(1, Math.ceil(data.total / data.pageSize));
      $('#logPager').innerHTML = pages <= 1 ? `<span>共 ${data.total} 条</span>` : `
        <button class="btn" data-page="${data.page - 1}" ${data.page <= 1 ? 'disabled' : ''}>上一页</button>
        <span>第 ${data.page} / ${pages} 页 · 共 ${data.total} 条</span>
        <button class="btn" data-page="${data.page + 1}" ${data.page >= pages ? 'disabled' : ''}>下一页</button>`;
    }
  };

  document.addEventListener('DOMContentLoaded', () => Admin.init());
})();
