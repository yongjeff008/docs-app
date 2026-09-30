/**
 * 内联 SVG 图标 + 文件夹树组件
 */
(function () {
  'use strict';

  // ---------- 内联 SVG 图标（不依赖图标库） ----------
  const svg = (body, size = 18, extra = '') =>
    `<svg class="icon" width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" ` +
    `stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" ${extra}>${body}</svg>`;

  /** 文件类型图标：带颜色的文档形状 + 类型标签 */
  const fileIcon = (color, label) =>
    `<svg class="file-icon" width="32" height="32" viewBox="0 0 32 32" aria-hidden="true">` +
    `<path d="M7 3h12l7 7v17a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z" fill="${color}" opacity=".12"/>` +
    `<path d="M7 3h12l7 7v17a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z" fill="none" stroke="${color}" stroke-width="1.5"/>` +
    `<path d="M19 3v7h7" fill="none" stroke="${color}" stroke-width="1.5"/>` +
    (label ? `<text x="15.5" y="23" text-anchor="middle" font-size="7" font-weight="700" fill="${color}" ` +
      `font-family="-apple-system,BlinkMacSystemFont,sans-serif">${label}</text>` : '') +
    `</svg>`;

  const Icons = {
    menu: svg('<line x1="3" y1="6" x2="21" y2="6"/><line x1="3" y1="12" x2="21" y2="12"/><line x1="3" y1="18" x2="21" y2="18"/>', 22),
    search: svg('<circle cx="11" cy="11" r="7"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>', 16),
    folder: svg('<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>', 18),
    folderOpen: svg('<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v1H7.5a2 2 0 0 0-1.9 1.4L3 19z"/><path d="M3 19l2.6-7.6A2 2 0 0 1 7.5 10H21l-2.6 7.6A2 2 0 0 1 16.5 19z"/>', 18),
    caret: svg('<polyline points="9 6 15 12 9 18"/>', 14),
    plus: svg('<line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>', 18),
    folderPlus: svg('<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><line x1="12" y1="10" x2="12" y2="16"/><line x1="9" y1="13" x2="15" y2="13"/>', 18),
    upload: svg('<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/>', 18),
    download: svg('<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/>', 18),
    more: svg('<circle cx="12" cy="5" r="1.5" fill="currentColor"/><circle cx="12" cy="12" r="1.5" fill="currentColor"/><circle cx="12" cy="19" r="1.5" fill="currentColor"/>', 18),
    edit: svg('<path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z"/>', 16),
    trash: svg('<polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><path d="M10 11v6"/><path d="M14 11v6"/><path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/>', 16),
    move: svg('<polyline points="5 9 2 12 5 15"/><polyline points="9 5 12 2 15 5"/><polyline points="15 19 12 22 9 19"/><polyline points="19 9 22 12 19 15"/><line x1="2" y1="12" x2="22" y2="12"/><line x1="12" y1="2" x2="12" y2="22"/>', 16),
    back: svg('<polyline points="15 18 9 12 15 6"/>', 20),
    close: svg('<line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>', 18),
    eye: svg('<path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>', 16),
    logout: svg('<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><polyline points="16 17 21 12 16 7"/><line x1="21" y1="12" x2="9" y2="12"/>', 16),
    chevronDown: svg('<polyline points="6 9 12 15 18 9"/>', 14),
    zoomIn: svg('<circle cx="11" cy="11" r="7"/><line x1="21" y1="21" x2="16.65" y2="16.65"/><line x1="11" y1="8" x2="11" y2="14"/><line x1="8" y1="11" x2="14" y2="11"/>', 18),
    zoomOut: svg('<circle cx="11" cy="11" r="7"/><line x1="21" y1="21" x2="16.65" y2="16.65"/><line x1="8" y1="11" x2="14" y2="11"/>', 18),
    alert: svg('<circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>', 40),
    check: svg('<polyline points="20 6 9 17 4 12"/>', 16),
    shield: svg('<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>', 16),
    key: svg('<circle cx="7.5" cy="15.5" r="4.5"/><path d="M10.7 12.3 21 2"/><path d="m16 7 3 3"/><path d="m19 4 2 2"/>', 16),
    settings: svg('<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/>', 16),
    user: svg('<path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/>', 16),
    users: svg('<path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>', 16),
    building: svg('<rect x="4" y="2" width="16" height="20" rx="2"/><path d="M9 22v-4h6v4"/><path d="M8 6h.01M16 6h.01M12 6h.01M12 10h.01M12 14h.01M16 10h.01M16 14h.01M8 10h.01M8 14h.01"/>', 16),
    list: svg('<line x1="8" y1="6" x2="21" y2="6"/><line x1="8" y1="12" x2="21" y2="12"/><line x1="8" y1="18" x2="21" y2="18"/><line x1="3" y1="6" x2="3.01" y2="6"/><line x1="3" y1="12" x2="3.01" y2="12"/><line x1="3" y1="18" x2="3.01" y2="18"/>', 16),
    link: svg('<path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"/><path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"/>', 16),
    unlink: svg('<path d="m18.84 12.25 1.72-1.71h-.02a5 5 0 0 0-7.07-7.07l-1.72 1.71"/><path d="m5.17 11.75-1.71 1.71a5 5 0 0 0 7.07 7.07l1.71-1.71"/><line x1="8" y1="2" x2="8" y2="5"/><line x1="2" y1="8" x2="5" y2="8"/><line x1="16" y1="19" x2="16" y2="22"/><line x1="19" y1="16" x2="22" y2="16"/>', 16),
    /** 权限状态标志：断开继承 / 继承 + 单独授权 / 完全继承（无标志） */
    permBadge(item) {
      if (item.inheritPermissions === false) {
        return `<span class="perm-badge broken" title="已断开继承：只有自身授权生效">${Icons.unlink}</span>`;
      }
      if (item.hasOwnGrants) {
        return `<span class="perm-badge own" title="继承上级，并有单独授权">${Icons.shield}</span>`;
      }
      return '';
    },
    lock: svg('<rect x="3" y="11" width="18" height="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>', 16),
    toggle: svg('<rect x="1" y="5" width="22" height="14" rx="7"/><circle cx="16" cy="12" r="3"/>', 16),
    logo: `<svg class="icon" width="22" height="22" viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="2" width="18" height="20" rx="3" fill="#3b82f6"/><path d="M7 8h10M7 12h10M7 16h6" stroke="#fff" stroke-width="2" stroke-linecap="round"/></svg>`,
    emptyFolder: `<svg width="96" height="96" viewBox="0 0 96 96" aria-hidden="true"><path d="M14 30a6 6 0 0 1 6-6h16l6 6h34a6 6 0 0 1 6 6v34a6 6 0 0 1-6 6H20a6 6 0 0 1-6-6z" fill="#eff6ff" stroke="#bfdbfe" stroke-width="2"/><path d="M38 52h20M48 42v20" stroke="#93c5fd" stroke-width="3" stroke-linecap="round"/></svg>`,

    /** 按扩展名返回文件图标 */
    file(name) {
      const ext = Util.extOf(name);
      if (ext === 'pdf') return fileIcon('#ef4444', 'PDF');
      if (['doc', 'docx'].includes(ext)) return fileIcon('#2563eb', 'W');
      if (['xls', 'xlsx', 'csv'].includes(ext)) return fileIcon('#16a34a', ext === 'csv' ? 'CSV' : 'X');
      if (['ppt', 'pptx'].includes(ext)) return fileIcon('#ea580c', 'P');
      if (['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'svg'].includes(ext)) return fileIcon('#9333ea', 'IMG');
      if (['txt', 'md', 'json'].includes(ext)) return fileIcon('#64748b', ext === 'md' ? 'MD' : 'TXT');
      if (['zip', 'rar', '7z', 'tar', 'gz'].includes(ext)) return fileIcon('#ca8a04', 'ZIP');
      return fileIcon('#94a3b8', '');
    }
  };

  // ---------- 文件夹树 ----------
  const EXPANDED_KEY = 'docs.tree.expanded';
  const LONG_PRESS_MS = 550;

  class FolderTree {
    /**
     * @param {HTMLElement} container
     * @param {{onSelect: (node) => void, onContextMenu: (node, x, y, anchor) => void}} handlers
     */
    constructor(container, handlers) {
      this.container = container;
      this.handlers = handlers;
      this.roots = [];
      this.index = new Map();      // id → 节点
      this.parentOf = new Map();   // id → 父 id
      this.selectedId = null;
      this.expanded = new Set(this._loadExpanded());
      this._bindEvents();
    }

    /** 设置树数据并重新渲染 */
    setData(roots) {
      this.roots = roots || [];
      this.index.clear();
      this.parentOf.clear();
      const walk = (nodes, parentId) => {
        for (const n of nodes) {
          this.index.set(n.id, n);
          this.parentOf.set(n.id, parentId);
          walk(n.children || [], n.id);
        }
      };
      walk(this.roots, null);
      if (this.selectedId != null && !this.index.has(this.selectedId)) this.selectedId = null;
      this.render();
    }

    get(id) { return this.index.get(id) || null; }

    /** 从根到指定节点的路径 */
    getPath(id) {
      const path = [];
      let cur = id;
      while (cur != null && this.index.has(cur)) {
        path.unshift(this.index.get(cur));
        cur = this.parentOf.get(cur);
      }
      return path;
    }

    /** 判断 a 是否为 b 自身或其子孙 */
    isDescendantOrSelf(a, b) {
      let cur = a;
      while (cur != null) {
        if (cur === b) return true;
        cur = this.parentOf.get(cur);
      }
      return false;
    }

    /** 扁平化（用于"移动到"选择器），附带层级深度 */
    flatten() {
      const list = [];
      const walk = (nodes, depth) => {
        for (const n of nodes) {
          list.push({ node: n, depth });
          walk(n.children || [], depth + 1);
        }
      };
      walk(this.roots, 0);
      return list;
    }

    /** 选中节点，并展开其所有祖先 */
    select(id, silent) {
      if (!this.index.has(id)) return;
      this.selectedId = id;
      let p = this.parentOf.get(id);
      while (p != null) { this.expanded.add(p); p = this.parentOf.get(p); }
      this._saveExpanded();
      this.render();
      const row = this.container.querySelector(`.tree-row[data-id="${id}"]`);
      if (row) row.scrollIntoView({ block: 'nearest' });
      if (!silent && this.handlers.onSelect) this.handlers.onSelect(this.index.get(id));
    }

    expand(id) {
      this.expanded.add(id);
      this._saveExpanded();
      this.render();
    }

    toggle(id) {
      if (this.expanded.has(id)) this.expanded.delete(id); else this.expanded.add(id);
      this._saveExpanded();
      this.render();
    }

    render() {
      if (!this.roots.length) {
        this.container.innerHTML = this.emptyHtml ||
          '<div class="tree-empty">还没有文件夹<br><span>点击下方“新建文件夹”开始</span></div>';
        return;
      }
      this.container.innerHTML = '<ul class="tree-list" role="tree">' + this._renderNodes(this.roots, 0) + '</ul>';
    }

    _renderNodes(nodes, depth) {
      const esc = Util.escapeHtml;
      return nodes.map((n) => {
        const hasChildren = n.children && n.children.length > 0;
        const expanded = hasChildren && this.expanded.has(n.id);
        const selected = n.id === this.selectedId;
        return `
          <li role="treeitem" aria-expanded="${expanded}">
            <div class="tree-row${selected ? ' selected' : ''}" data-id="${n.id}" style="--depth:${depth}" title="${esc(n.name)}">
              <button class="tree-caret${hasChildren ? '' : ' invisible'}${expanded ? ' expanded' : ''}"
                      data-action="toggle" aria-label="${expanded ? '折叠' : '展开'}" tabindex="-1">${Icons.caret}</button>
              <span class="tree-icon">${expanded ? Icons.folderOpen : Icons.folder}</span>
              <span class="tree-name">${esc(n.name)}</span>
              ${Icons.permBadge(n)}
              ${n.documentCount ? `<span class="tree-count">${n.documentCount}</span>` : ''}
              <button class="tree-more" data-action="menu" aria-label="更多操作">${Icons.more}</button>
            </div>
            ${expanded ? '<ul class="tree-list" role="group">' + this._renderNodes(n.children, depth + 1) + '</ul>' : ''}
          </li>`;
      }).join('');
    }

    _bindEvents() {
      const c = this.container;

      c.addEventListener('click', (e) => {
        const row = e.target.closest('.tree-row');
        if (!row) return;
        const id = Number(row.dataset.id);
        const action = e.target.closest('[data-action]')?.dataset.action;

        if (action === 'toggle') {
          e.stopPropagation();
          this.toggle(id);
        } else if (action === 'menu') {
          e.stopPropagation();
          const btn = e.target.closest('[data-action]');
          const r = btn.getBoundingClientRect();
          this.handlers.onContextMenu?.(this.index.get(id), r.left, r.bottom, btn);
        } else {
          // 点击名称：选中；有子节点时顺带展开
          if (this.index.get(id)?.children?.length) this.expanded.add(id);
          this.select(id);
        }
      });

      // 桌面右键菜单
      c.addEventListener('contextmenu', (e) => {
        const row = e.target.closest('.tree-row');
        if (!row) return;
        e.preventDefault();
        this.handlers.onContextMenu?.(this.index.get(Number(row.dataset.id)), e.clientX, e.clientY);
      });

      // 手机长按菜单
      let timer = null, fired = false, startX = 0, startY = 0;
      const cancel = () => { clearTimeout(timer); timer = null; };
      c.addEventListener('touchstart', (e) => {
        const row = e.target.closest('.tree-row');
        if (!row || e.touches.length !== 1) return;
        fired = false;
        startX = e.touches[0].clientX;
        startY = e.touches[0].clientY;
        timer = setTimeout(() => {
          timer = null;
          fired = true;
          if (navigator.vibrate) navigator.vibrate(10);
          this.handlers.onContextMenu?.(this.index.get(Number(row.dataset.id)), startX, startY);
        }, LONG_PRESS_MS);
      }, { passive: true });
      c.addEventListener('touchmove', (e) => {
        if (!timer) return;
        const t = e.touches[0];
        if (Math.abs(t.clientX - startX) > 10 || Math.abs(t.clientY - startY) > 10) cancel();
      }, { passive: true });
      c.addEventListener('touchend', (e) => {
        cancel();
        // 长按已弹出菜单：阻止松手时触发的点击
        if (fired) { e.preventDefault(); fired = false; }
      });
      c.addEventListener('touchcancel', cancel);
    }

    _loadExpanded() {
      try { return JSON.parse(localStorage.getItem(EXPANDED_KEY) || '[]'); } catch (_) { return []; }
    }

    _saveExpanded() {
      try { localStorage.setItem(EXPANDED_KEY, JSON.stringify([...this.expanded])); } catch (_) { /* 忽略 */ }
    }
  }

  window.Icons = Icons;
  window.FolderTree = FolderTree;
})();
