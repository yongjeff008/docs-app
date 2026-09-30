/**
 * 主界面逻辑：文件夹树 / 文档列表 / 上传 / 预览 / 弹窗 / 提示
 */
(function () {
  'use strict';

  const $ = (sel, root = document) => root.querySelector(sel);
  const esc = (s) => Util.escapeHtml(s);

  const MAX_FILE_MB = 100;
  const LAST_FOLDER_KEY = 'docs.lastFolderId';

  // 通用组件见 ui.js
  const { Toast, Modal, Menu } = window;

  // ======================================================
  // 应用主体
  // ======================================================
  const App = {
    tree: null,
    viewer: null,
    currentFolderId: null,
    docs: [],
    searchKeyword: '',
    listAbort: null,
    previewDoc: null,
    me: null,

    async init() {
      // 校验登录状态
      let me;
      try {
        me = await Api.me();
      } catch (_) {
        return; // Api 已处理跳转
      }
      this.me = me;
      $('#userName').textContent = me.displayName || me.username;
      $('#userAvatar').textContent = (me.displayName || me.username || '?').slice(0, 1);
      $('#userMenuName').textContent = me.username + '（' + me.roleName +
        (me.departmentName ? ' · ' + me.departmentName : '') + '）';
      $('#adminLink').hidden = !me.isAdmin;
      // 只有管理员可以在根目录新建文件夹
      $('#newRootFolderBtn').parentElement.hidden = !me.isAdmin;

      this._fillIcons();

      this.tree = new FolderTree($('#tree'), {
        onSelect: (node) => this.openFolder(node.id),
        onContextMenu: (node, x, y) => this.showFolderMenu(node, x, y)
      });
      if (!me.isAdmin) {
        this.tree.emptyHtml = '<div class="tree-empty">暂无可访问的文件夹<br><span>请联系管理员为你授权</span></div>';
      }
      this.viewer = new Viewer($('#previewBody'));

      this._bindEvents();
      await this.loadTree();

      // 链接指定文件夹（如权限总览跳转：#folder=12&perm=folder:12）优先，其次恢复上次打开的文件夹
      const hash = new URLSearchParams(location.hash.slice(1));
      const linked = Number(hash.get('folder'));
      const last = linked && this.tree.get(linked) ? linked : Number(localStorage.getItem(LAST_FOLDER_KEY));
      if (hash.has('folder')) history.replaceState(null, '', location.pathname);
      if (last && this.tree.get(last)) {
        this.tree.select(last);
      } else if (this.tree.roots.length) {
        this.tree.select(this.tree.roots[0].id);
      } else {
        this.renderList();
      }
      document.body.classList.add('ready');

      // 链接要求直接打开权限明细
      const perm = /^(folder|document):(\d+)$/.exec(hash.get('perm') || '');
      if (perm) {
        const id = Number(perm[2]);
        if (perm[1] === 'folder' && this.tree.get(id)) this.openPermissions('folder', this.tree.get(id));
        if (perm[1] === 'document') {
          await this.docsReady;
          const doc = this.docs.find((d) => d.id === id);
          if (doc) this.openPermissions('document', doc);
        }
      }
    },

    /** 当前用户对文件夹是否拥有某权限（界面控制用，真正的校验在后端） */
    can(folderId, flag) {
      if (this.me && this.me.isAdmin) return true;
      const node = folderId ? this.tree.get(folderId) : null;
      return !!node && (node.actions & flag) === flag;
    },

    /** 当前用户对文档是否拥有某权限（文档可能断开继承，以后端返回的 actions 为准） */
    canDoc(doc, flag) {
      if (this.me && this.me.isAdmin) return true;
      return ((doc.actions || 0) & flag) === flag;
    },

    /** 是否可以删除某文档：有删除权限，或是自己上传的且有上传权限 */
    canDeleteDoc(doc) {
      return this.canDoc(doc, Perm.Delete) || (doc.createdBy === this.me.id && this.canDoc(doc, Perm.Write));
    },

    /** 填充静态按钮上的图标 */
    _fillIcons() {
      document.querySelectorAll('[data-icon]').forEach((el) => {
        const icon = Icons[el.dataset.icon];
        if (icon) el.insertAdjacentHTML('afterbegin', icon);
      });
    },

    // ---------------- 事件绑定 ----------------
    _bindEvents() {
      // 抽屉
      $('#menuBtn').addEventListener('click', () => this.toggleDrawer(true));
      $('#drawerMask').addEventListener('click', () => this.toggleDrawer(false));

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

      // 新建文件夹
      $('#newRootFolderBtn').addEventListener('click', () => this.createFolder(null));
      $('#newSubFolderBtn').addEventListener('click', () => this.createFolder(this.currentFolderId));

      // 上传
      $('#uploadBtn').addEventListener('click', () => {
        if (!this.currentFolderId) { Toast.info('请先选择一个文件夹'); return; }
        if (!this.can(this.currentFolderId, Perm.Write)) { Toast.info('你没有向该文件夹上传文档的权限'); return; }
        $('#fileInput').click();
      });
      $('#fileInput').addEventListener('change', (e) => {
        const files = [...e.target.files];
        e.target.value = '';
        this.uploadFiles(files);
      });
      this._bindDragDrop();

      // 搜索
      const onSearch = Util.debounce(() => this.search($('#searchInput').value.trim()), 300);
      $('#searchInput').addEventListener('input', onSearch);
      $('#searchInput').addEventListener('keydown', (e) => {
        if (e.key === 'Escape') { e.target.value = ''; this.search(''); }
      });
      $('#searchClear').addEventListener('click', () => {
        $('#searchInput').value = '';
        this.search('');
      });

      // 面包屑
      $('#breadcrumb').addEventListener('click', (e) => {
        const a = e.target.closest('[data-folder]');
        if (a) this.tree.select(Number(a.dataset.folder));
      });

      // 文档列表
      $('#docList').addEventListener('click', (e) => {
        const row = e.target.closest('.doc-row');
        if (!row) return;
        const doc = this.docs.find((d) => d.id === Number(row.dataset.id));
        if (!doc) return;
        const action = e.target.closest('[data-action]')?.dataset.action;
        if (action === 'download') return; // 由 <a download> 处理
        if (action === 'more') {
          e.stopPropagation();
          const r = e.target.closest('[data-action]').getBoundingClientRect();
          this.showDocMenu(doc, r.right - 160, r.bottom + 4);
          return;
        }
        if (action === 'folder') {
          e.stopPropagation();
          this.clearSearch();
          this.tree.select(doc.folderId);
          return;
        }
        this.openPreview(doc);
      });
      $('#docList').addEventListener('keydown', (e) => {
        if (e.key !== 'Enter' || !e.target.classList.contains('doc-row')) return;
        const doc = this.docs.find((d) => d.id === Number(e.target.dataset.id));
        if (doc) this.openPreview(doc);
      });
      $('#docList').addEventListener('contextmenu', (e) => {
        const row = e.target.closest('.doc-row');
        if (!row) return;
        const doc = this.docs.find((d) => d.id === Number(row.dataset.id));
        if (!doc) return;
        e.preventDefault();
        this.showDocMenu(doc, e.clientX, e.clientY);
      });

      // 预览区
      $('#previewBack').addEventListener('click', () => this.closePreview());
      // 权限标签：查看 / 设置当前文件夹权限
      $('#permChip').addEventListener('click', () => {
        const node = this.tree.get(this.currentFolderId);
        if (node && this.can(node.id, Perm.Manage)) this.openPermissions('folder', node);
      });

      $('#previewMore').addEventListener('click', (e) => {
        e.stopPropagation();
        if (!this.previewDoc) return;
        const r = e.currentTarget.getBoundingClientRect();
        this.showDocMenu(this.previewDoc, r.right - 160, r.bottom + 4);
      });

      // 浏览器返回键关闭预览（手机体验）
      window.addEventListener('popstate', () => {
        if (this.previewDoc) this.closePreview(true);
      });

      // 键盘：Esc 关闭预览 / 菜单
      document.addEventListener('keydown', (e) => {
        if (e.key !== 'Escape') return;
        if (Menu.el) { Menu.hide(); return; }
        if (this.previewDoc && !$('.modal-overlay')) this.closePreview();
      });

      window.addEventListener('resize', () => Menu.hide());
    },

    _bindDragDrop() {
      const main = $('#main');
      const overlay = $('#dropOverlay');
      let depth = 0;
      const hasFiles = (e) => e.dataTransfer && [...e.dataTransfer.types].includes('Files');

      main.addEventListener('dragenter', (e) => {
        if (!hasFiles(e)) return;
        e.preventDefault();
        depth++;
        overlay.classList.add('show');
        overlay.querySelector('p').textContent = !this.currentFolderId
          ? '请先在左侧选择一个文件夹'
          : !this.can(this.currentFolderId, Perm.Write)
            ? '你没有向该文件夹上传文档的权限'
            : '松开鼠标，上传到“' + (this.tree.get(this.currentFolderId)?.name || '') + '”';
      });
      main.addEventListener('dragover', (e) => {
        if (!hasFiles(e)) return;
        e.preventDefault();
        e.dataTransfer.dropEffect = this.can(this.currentFolderId, Perm.Write) ? 'copy' : 'none';
      });
      main.addEventListener('dragleave', (e) => {
        if (!hasFiles(e)) return;
        depth = Math.max(0, depth - 1);
        if (depth === 0) overlay.classList.remove('show');
      });
      main.addEventListener('drop', (e) => {
        if (!hasFiles(e)) return;
        e.preventDefault();
        depth = 0;
        overlay.classList.remove('show');
        if (!this.currentFolderId) { Toast.info('请先选择一个文件夹'); return; }
        if (!this.can(this.currentFolderId, Perm.Write)) { Toast.info('你没有向该文件夹上传文档的权限'); return; }
        const files = [...e.dataTransfer.files].filter((f) => f.size > 0 || f.type);
        if (!files.length) { Toast.error('不支持上传文件夹，请选择文件'); return; }
        this.uploadFiles(files);
      });
      // 防止拖到其他区域时浏览器直接打开文件
      window.addEventListener('dragover', (e) => e.preventDefault());
      window.addEventListener('drop', (e) => e.preventDefault());
    },

    toggleDrawer(open) {
      document.body.classList.toggle('drawer-open', open);
    },

    // ---------------- 文件夹 ----------------
    async loadTree() {
      try {
        const roots = await Api.getFolderTree();
        this.tree.setData(roots);
      } catch (err) {
        Toast.error('加载文件夹失败：' + err.message);
      }
    },

    async openFolder(id) {
      this.currentFolderId = id;
      localStorage.setItem(LAST_FOLDER_KEY, String(id));
      if (this.previewDoc) this.closePreview();
      if (this.searchKeyword) this.clearSearch(true);
      if (Util.isMobile()) this.toggleDrawer(false);
      this.docsReady = this.loadDocs();
      await this.docsReady;
    },

    async createFolder(parentId) {
      const parent = parentId ? this.tree.get(parentId) : null;
      const name = await Modal.prompt({
        title: parent ? '新建子文件夹' : '新建文件夹',
        label: parent ? '在“' + parent.name + '”下新建' : '文件夹名称',
        placeholder: '请输入文件夹名称',
        okText: '创建'
      });
      if (!name) return;
      try {
        const folder = await Api.createFolder(name, parentId);
        Toast.success('已创建文件夹“' + folder.name + '”');
        if (parentId) this.tree.expanded.add(parentId);
        await this.loadTree();
        this.tree.select(folder.id);
      } catch (err) {
        Toast.error(err.message);
      }
    },

    async renameFolder(node) {
      const name = await Modal.prompt({ title: '重命名文件夹', label: '新名称', value: node.name, okText: '保存' });
      if (!name || name === node.name) return;
      try {
        await Api.renameFolder(node.id, name);
        Toast.success('重命名成功');
        await this.loadTree();
        if (this.currentFolderId) this.renderHeader();
      } catch (err) {
        Toast.error(err.message);
      }
    },

    async moveFolder(node) {
      const target = await Modal.pickFolder({
        title: '移动“' + node.name + '”到…',
        tree: this.tree,
        excludeId: node.id,
        currentParentId: this.tree.parentOf.get(node.id),
        allowRoot: this.me.isAdmin,
        canSelect: (n) => this.can(n.id, Perm.Write)
      });
      if (!target) return;
      try {
        await Api.moveFolder(node.id, target.id);
        Toast.success('移动成功');
        if (target.id) this.tree.expanded.add(target.id);
        await this.loadTree();
        if (this.currentFolderId) this.tree.select(this.currentFolderId, true);
        this.renderHeader();
      } catch (err) {
        Toast.error(err.message);
      }
    },

    async deleteFolder(node) {
      const ok = await Modal.confirm({
        title: '删除文件夹',
        message: '确定要删除文件夹“' + node.name + '”吗？此操作不可恢复。',
        okText: '删除',
        danger: true
      });
      if (!ok) return;
      try {
        await Api.deleteFolder(node.id);
        Toast.success('已删除文件夹');
        const parentId = this.tree.parentOf.get(node.id);
        const wasCurrent = this.tree.isDescendantOrSelf(this.currentFolderId, node.id);
        await this.loadTree();
        if (wasCurrent) {
          this.currentFolderId = null;
          localStorage.removeItem(LAST_FOLDER_KEY);
          if (parentId && this.tree.get(parentId)) this.tree.select(parentId);
          else this.renderList();
        }
      } catch (err) {
        Toast.error(err.message);
      }
    },

    showFolderMenu(node, x, y) {
      if (!node) return;
      const items = [];
      if (this.can(node.id, Perm.Write)) {
        items.push({ label: '新建子文件夹', icon: Icons.folderPlus, action: () => this.createFolder(node.id) });
      }
      if (this.can(node.id, Perm.Manage)) {
        items.push(
          { label: '重命名', icon: Icons.edit, action: () => this.renameFolder(node) },
          { label: '移动到…', icon: Icons.move, action: () => this.moveFolder(node) },
          { label: '权限设置', icon: Icons.shield, action: () => this.openPermissions('folder', node) },
          { label: '删除', icon: Icons.trash, danger: true, action: () => this.deleteFolder(node) }
        );
      }
      if (!items.length) {
        Toast.info('你对“' + node.name + '”只有查看权限');
        return;
      }
      Menu.show(items, x, y, node.name);
    },

    // ---------------- 权限设置 ----------------
    /** 打开权限弹窗（type: 'folder' | 'document'），保存后刷新树与列表 */
    async openPermissions(type, item) {
      Menu.hide();
      const saved = await PermissionDialog.open({
        type, id: item.id, name: type === 'folder' ? item.name : item.fileName
      });
      if (!saved) return;
      await this.loadTree();
      if (this.currentFolderId && this.tree.get(this.currentFolderId)) {
        this.renderHeader();
        if (!this.searchKeyword) await this.loadDocs();
      } else {
        this.currentFolderId = null;
        this.renderList();
      }
    },

    // ---------------- 文档列表 ----------------
    async loadDocs() {
      if (this.listAbort) this.listAbort.abort();
      const abort = new AbortController();
      this.listAbort = abort;
      this.renderHeader();
      $('#docList').innerHTML = '<div class="list-loading"><div class="spinner"></div></div>';

      try {
        const docs = this.searchKeyword
          ? await Api.searchDocuments(this.searchKeyword, abort.signal)
          : await Api.getDocuments(this.currentFolderId, abort.signal);
        if (abort.signal.aborted) return;
        this.docs = docs;
        this.renderList();
      } catch (err) {
        if (err.name === 'AbortError') return;
        this.docs = [];
        $('#docList').innerHTML = `<div class="empty-state"><p>${esc('加载失败：' + err.message)}</p></div>`;
      }
    },

    /** 更新顶部面包屑与按钮状态 */
    renderHeader() {
      const bc = $('#breadcrumb');
      const hasFolder = !!this.currentFolderId && !!this.tree.get(this.currentFolderId);

      if (this.searchKeyword) {
        bc.innerHTML = `<span class="crumb current">搜索“${esc(this.searchKeyword)}”的结果</span>`;
      } else if (hasFolder) {
        const path = this.tree.getPath(this.currentFolderId);
        bc.innerHTML = path.map((n, i) => i === path.length - 1
          ? `<span class="crumb current">${esc(n.name)}</span>`
          : `<button class="crumb" data-folder="${n.id}">${esc(n.name)}</button><span class="crumb-sep">/</span>`
        ).join('');
      } else {
        bc.innerHTML = '<span class="crumb current">文档</span>';
      }

      const disabled = !hasFolder || !!this.searchKeyword || !this.can(this.currentFolderId, Perm.Write);
      $('#uploadBtn').disabled = disabled;
      $('#newSubFolderBtn').disabled = disabled;
      $('#uploadBtn').title = hasFolder && disabled && !this.searchKeyword ? '你对该文件夹只有查看权限' : '';

      // 权限标签：继承状态 + 我的权限，可管理时点击查看明细
      const chip = $('#permChip');
      const node = hasFolder && !this.searchKeyword ? this.tree.get(this.currentFolderId) : null;
      chip.hidden = !node;
      if (node) {
        const myLevel = this.me.isAdmin ? '管理员' : Perm.levelName(node.actions);
        const status = node.inheritPermissions === false ? '已断开继承'
          : node.hasOwnGrants ? '继承 + 单独授权' : this.tree.parentOf.get(node.id) ? '继承上级' : '顶层文件夹';
        chip.className = 'perm-chip' + (node.inheritPermissions === false ? ' broken' : node.hasOwnGrants ? ' own' : '');
        chip.innerHTML = `${node.inheritPermissions === false ? Icons.unlink : node.hasOwnGrants ? Icons.shield : Icons.link}` +
          `<span class="perm-chip-status">${status}</span><span class="perm-chip-me">我的权限：${myLevel}</span>`;
        chip.title = this.can(node.id, Perm.Manage) ? '点击查看 / 设置权限' : '你没有设置该文件夹权限的权限';
        chip.disabled = !this.can(node.id, Perm.Manage);
      }
    },

    renderList() {
      this.renderHeader();
      const list = $('#docList');

      if (!this.searchKeyword && !this.currentFolderId) {
        const hasFolders = this.tree.roots.length > 0;
        const isAdmin = this.me.isAdmin;
        list.innerHTML = `
          <div class="empty-state">
            ${Icons.emptyFolder}
            <h3>${hasFolders ? '请选择左侧文件夹' : isAdmin ? '还没有任何文件夹' : '暂无可访问的文件夹'}</h3>
            <p>${hasFolders ? '选择文件夹后即可查看文档' : isAdmin ? '先新建一个文件夹，再上传文档' : '请联系管理员为你授权'}</p>
            ${!hasFolders && isAdmin ? `<button class="btn btn-primary" id="emptyNewFolder">${Icons.folderPlus}<span>新建文件夹</span></button>` : ''}
          </div>`;
        $('#emptyNewFolder')?.addEventListener('click', () => this.createFolder(null));
        return;
      }

      if (!this.docs.length) {
        const canUpload = this.can(this.currentFolderId, Perm.Write);
        list.innerHTML = this.searchKeyword
          ? `<div class="empty-state"><h3>没有找到相关文档</h3><p>换个关键词试试</p></div>`
          : !canUpload
            ? `<div class="empty-state">${Icons.emptyFolder}<h3>这个文件夹还是空的</h3><p>你对该文件夹只有查看权限</p></div>`
            : `<div class="empty-state">
               ${Icons.emptyFolder}
               <h3>这个文件夹还是空的</h3>
               <p class="only-desktop">将文件拖拽到这里，或点击下方按钮上传</p>
               <p class="only-mobile">点击下方按钮选择文件上传</p>
               <button class="btn btn-primary" id="emptyUpload">${Icons.upload}<span>上传文件</span></button>
             </div>`;
        $('#emptyUpload')?.addEventListener('click', () => $('#fileInput').click());
        return;
      }

      list.innerHTML = `
        <div class="doc-table-head">
          <span>名称</span><span>大小</span><span>上传时间</span><span></span>
        </div>
        ${this.docs.map((d) => `
          <div class="doc-row" data-id="${d.id}" tabindex="0">
            <div class="doc-name">
              ${Icons.file(d.fileName)}
              <div class="doc-name-text">
                <span class="doc-title-line">
                  <span class="doc-title" title="${esc(d.fileName)}">${esc(d.fileName)}</span>${Icons.permBadge(d)}
                </span>
                <span class="doc-meta">
                  ${Util.formatSize(d.size)} · ${Util.formatDate(d.createdAt)}
                  ${this.searchKeyword ? ` · <button class="link" data-action="folder">${esc(d.folderName || '')}</button>` : ''}
                </span>
              </div>
            </div>
            <span class="doc-size">${Util.formatSize(d.size)}</span>
            <span class="doc-date">${Util.formatDate(d.createdAt)}</span>
            <div class="doc-actions">
              <a class="icon-btn" data-action="download" href="${Api.downloadUrl(d.id)}" download title="下载" aria-label="下载">${Icons.download}</a>
              <button class="icon-btn" data-action="more" title="更多操作" aria-label="更多操作">${Icons.more}</button>
            </div>
          </div>`).join('')}`;
    },

    showDocMenu(doc, x, y) {
      const items = [
        { label: '预览', icon: Icons.eye, action: () => this.openPreview(doc) },
        { label: '下载', icon: Icons.download, action: () => this.download(doc) }
      ];
      if (this.canDoc(doc, Perm.Write)) {
        items.push({ label: '重命名', icon: Icons.edit, action: () => this.renameDoc(doc) });
      }
      if (this.canDoc(doc, Perm.Manage)) {
        items.push({ label: '权限设置', icon: Icons.shield, action: () => this.openPermissions('document', doc) });
      }
      if (this.canDeleteDoc(doc)) {
        items.push({ label: '删除', icon: Icons.trash, danger: true, action: () => this.deleteDoc(doc) });
      }
      Menu.show(items, x, y, doc.fileName);
    },

    download(doc) {
      const a = document.createElement('a');
      a.href = Api.downloadUrl(doc.id);
      a.download = doc.fileName;
      document.body.appendChild(a);
      a.click();
      a.remove();
    },

    async renameDoc(doc) {
      const name = await Modal.prompt({
        title: '重命名文档', label: '新文件名', value: doc.fileName, okText: '保存', selectBaseName: true
      });
      if (!name || name === doc.fileName) return;
      try {
        const updated = await Api.renameDocument(doc.id, name);
        Object.assign(doc, updated);
        Toast.success('重命名成功');
        this.renderList();
        if (this.previewDoc && this.previewDoc.id === doc.id) $('#previewTitle').textContent = doc.fileName;
      } catch (err) {
        Toast.error(err.message);
      }
    },

    async deleteDoc(doc) {
      const ok = await Modal.confirm({
        title: '删除文档',
        message: '确定要删除“' + doc.fileName + '”吗？此操作不可恢复。',
        okText: '删除',
        danger: true
      });
      if (!ok) return;
      try {
        await Api.deleteDocument(doc.id);
        Toast.success('已删除');
        if (this.previewDoc && this.previewDoc.id === doc.id) this.closePreview();
        this.docs = this.docs.filter((d) => d.id !== doc.id);
        this.renderList();
        this.loadTree(); // 刷新文档计数
      } catch (err) {
        Toast.error(err.message);
      }
    },

    // ---------------- 搜索 ----------------
    async search(keyword) {
      $('#searchClear').hidden = !keyword;
      if (keyword === this.searchKeyword) return;
      this.searchKeyword = keyword;
      if (this.previewDoc) this.closePreview();
      if (!keyword && !this.currentFolderId) { this.renderList(); return; }
      await this.loadDocs();
    },

    clearSearch(silent) {
      $('#searchInput').value = '';
      $('#searchClear').hidden = true;
      this.searchKeyword = '';
      if (!silent) this.renderHeader();
    },

    // ---------------- 上传 ----------------
    async uploadFiles(files) {
      if (!files.length) return;
      const folderId = this.currentFolderId;
      if (!folderId) { Toast.info('请先选择一个文件夹'); return; }

      const panel = $('#uploadPanel');
      panel.hidden = false;

      let success = 0, failed = 0;
      for (const file of files) {
        const item = document.createElement('div');
        item.className = 'upload-item';
        item.innerHTML = `
          <div class="upload-info">
            <span class="upload-name" title="${esc(file.name)}">${esc(file.name)}</span>
            <span class="upload-status">等待上传</span>
          </div>
          <div class="progress"><div class="progress-bar"></div></div>`;
        panel.appendChild(item);
        const status = item.querySelector('.upload-status');
        const bar = item.querySelector('.progress-bar');

        // 前端预检大小
        if (file.size > MAX_FILE_MB * 1024 * 1024) {
          status.textContent = '文件超过 ' + MAX_FILE_MB + ' MB';
          item.classList.add('failed');
          failed++;
          continue;
        }
        if (file.size === 0) {
          status.textContent = '不能上传空文件';
          item.classList.add('failed');
          failed++;
          continue;
        }

        try {
          status.textContent = '0%';
          const { promise } = Api.uploadFile(file, folderId, (p) => {
            const pct = Math.round(p * 100);
            bar.style.width = pct + '%';
            status.textContent = pct < 100 ? pct + '%' : '处理中…';
          });
          await promise;
          bar.style.width = '100%';
          status.innerHTML = Icons.check + '完成';
          item.classList.add('done');
          success++;
          setTimeout(() => { item.remove(); if (!panel.children.length) panel.hidden = true; }, 2500);
        } catch (err) {
          status.textContent = err.message;
          item.classList.add('failed');
          failed++;
        }
      }

      // 失败项保留，点击可关闭
      panel.querySelectorAll('.upload-item.failed').forEach((el) => {
        el.title = '点击关闭';
        el.onclick = () => { el.remove(); if (!panel.children.length) panel.hidden = true; };
      });

      if (success && !failed) Toast.success(success === 1 ? '上传成功' : `已成功上传 ${success} 个文件`);
      else if (success && failed) Toast.error(`上传完成：成功 ${success} 个，失败 ${failed} 个`);
      else if (failed) Toast.error(failed === 1 ? '上传失败' : `${failed} 个文件上传失败`);

      if (success) {
        if (this.currentFolderId === folderId && !this.searchKeyword) await this.loadDocs();
        this.loadTree();
      }
    },

    // ---------------- 预览 ----------------
    openPreview(doc) {
      Menu.hide();
      const wasOpen = !!this.previewDoc;
      this.previewDoc = doc;
      $('#previewTitle').textContent = doc.fileName;
      $('#previewDownload').href = Api.downloadUrl(doc.id);
      $('#listView').hidden = true;
      $('#previewView').hidden = false;
      document.body.classList.add('previewing');
      if (!wasOpen) history.pushState({ preview: doc.id }, '');
      this.viewer.open(doc);
    },

    /** @param {boolean} fromPopState 是否由浏览器返回触发 */
    closePreview(fromPopState) {
      if (!this.previewDoc) return;
      this.previewDoc = null;
      this.viewer.close();
      $('#previewView').hidden = true;
      $('#listView').hidden = false;
      document.body.classList.remove('previewing');
      if (!fromPopState && history.state && history.state.preview) history.back();
    }
  };

  document.addEventListener('DOMContentLoaded', () => App.init());
})();
