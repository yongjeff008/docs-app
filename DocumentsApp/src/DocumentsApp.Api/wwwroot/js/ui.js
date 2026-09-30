/**
 * 通用界面组件：Toast 提示 / 弹窗 / 上下文菜单
 * 主界面（app.js）与系统管理页（admin.js）共用
 */
(function () {
  'use strict';

  const $ = (sel, root = document) => root.querySelector(sel);
  const esc = (s) => Util.escapeHtml(s);

  // ======================================================
  // Toast 提示（页面右上角）
  // ======================================================
  const Toast = {
    show(message, type = 'success', duration = 2800) {
      const root = $('#toastContainer');
      const el = document.createElement('div');
      el.className = 'toast toast-' + type;
      el.setAttribute('role', type === 'error' ? 'alert' : 'status');
      el.innerHTML = `<span class="toast-icon">${type === 'error' ? '!' : type === 'info' ? 'i' : '✓'}</span>` +
                     `<span class="toast-text">${esc(message)}</span>`;
      root.appendChild(el);
      requestAnimationFrame(() => el.classList.add('show'));
      const remove = () => {
        el.classList.remove('show');
        setTimeout(() => el.remove(), 250);
      };
      setTimeout(remove, type === 'error' ? Math.max(duration, 4000) : duration);
      el.addEventListener('click', remove);
    },
    success(m) { this.show(m, 'success'); },
    error(m) { this.show(m, 'error'); },
    info(m) { this.show(m, 'info'); }
  };

  // ======================================================
  // 弹窗：输入 / 确认 / 表单 / 选择文件夹 / 自定义内容
  // ======================================================
  const Modal = {
    /**
     * 打开通用弹窗，返回 Promise
     * onOk(body) 可返回 Promise；返回 undefined 表示校验未通过，弹窗保持打开
     */
    _open(buildBody, { title, okText = '确定', cancelText = '取消', danger = false, wide = false, onOk }) {
      return new Promise((resolve) => {
        const root = $('#modalRoot');
        const overlay = document.createElement('div');
        overlay.className = 'modal-overlay';
        overlay.innerHTML = `
          <div class="modal${wide ? ' modal-wide' : ''}" role="dialog" aria-modal="true" aria-label="${esc(title)}">
            <div class="modal-header">
              <h3>${esc(title)}</h3>
              <button class="icon-btn" data-role="cancel" aria-label="关闭">${Icons.close}</button>
            </div>
            <div class="modal-body"></div>
            <div class="modal-footer">
              <button class="btn" data-role="cancel">${esc(cancelText)}</button>
              <button class="btn ${danger ? 'btn-danger' : 'btn-primary'}" data-role="ok">${esc(okText)}</button>
            </div>
          </div>`;
        const body = overlay.querySelector('.modal-body');
        const okBtn = overlay.querySelector('[data-role="ok"]');
        const focusEl = buildBody(body);
        root.appendChild(overlay);
        requestAnimationFrame(() => overlay.classList.add('show'));

        let done = false;
        let busy = false;
        const finish = (value) => {
          if (done) return;
          done = true;
          document.removeEventListener('keydown', onKey, true);
          overlay.classList.remove('show');
          setTimeout(() => overlay.remove(), 200);
          resolve(value);
        };
        const ok = async () => {
          if (busy) return;
          busy = true;
          okBtn.disabled = true;
          try {
            const value = onOk ? await onOk(body) : true;
            if (value !== undefined) finish(value); // undefined = 校验未通过
          } finally {
            busy = false;
            okBtn.disabled = false;
          }
        };
        const onKey = (e) => {
          if (e.key === 'Escape') { e.stopPropagation(); finish(null); }
          else if (e.key === 'Enter' && !['TEXTAREA', 'BUTTON', 'SELECT'].includes(e.target.tagName)) {
            e.preventDefault(); ok();
          }
        };
        document.addEventListener('keydown', onKey, true);

        overlay.addEventListener('mousedown', (e) => { if (e.target === overlay) finish(null); });
        overlay.querySelectorAll('[data-role="cancel"]').forEach((b) => b.addEventListener('click', () => finish(null)));
        okBtn.addEventListener('click', ok);

        setTimeout(() => (focusEl || okBtn).focus(), 50);
      });
    },

    /** 在弹窗内显示错误信息 */
    showError(body, message) {
      const err = body.querySelector('.field-error');
      if (!err) { Toast.error(message); return; }
      err.textContent = message;
      err.hidden = false;
    },

    /** 输入框弹窗，返回输入值或 null */
    prompt({ title, label, value = '', placeholder = '', okText = '确定', selectBaseName = false }) {
      return this._open((body) => {
        body.innerHTML = `
          <label class="field">
            <span>${esc(label)}</span>
            <input type="text" class="input" maxlength="200" placeholder="${esc(placeholder)}" value="${esc(value)}">
          </label>
          <p class="field-error" hidden></p>`;
        const input = body.querySelector('input');
        setTimeout(() => {
          // 重命名文件时只选中不含扩展名的部分
          const dot = value.lastIndexOf('.');
          if (selectBaseName && dot > 0) input.setSelectionRange(0, dot); else input.select();
        }, 60);
        return input;
      }, {
        title, okText,
        onOk: (body) => {
          const v = body.querySelector('input').value.trim();
          if (!v) {
            this.showError(body, '名称不能为空');
            return undefined;
          }
          return v;
        }
      });
    },

    /** 确认弹窗，返回 true / null */
    confirm({ title, message, okText = '确定', danger = false }) {
      return this._open((body) => {
        body.innerHTML = `<p class="modal-message">${esc(message)}</p>`;
        return null;
      }, { title, okText, danger });
    },

    /**
     * 表单弹窗
     * fields: [{ name, label, type: 'text'|'password'|'select'|'checkbox', value, options: [{value, label}],
     *            placeholder, hint, required, autocomplete, disabled }]
     * submit(values)：可选，异步提交；抛出错误时弹窗保持打开并显示错误；返回值作为结果
     * 返回 submit 的结果（无 submit 时返回 values），取消返回 null
     */
    form({ title, fields, okText = '保存', submit, validate }) {
      return this._open((body) => {
        body.innerHTML = fields.map((f) => {
          const id = 'f_' + f.name;
          const common = `id="${id}" name="${esc(f.name)}" ${f.disabled ? 'disabled' : ''}`;
          let control;
          if (f.type === 'select') {
            control = `<select class="input" ${common}>` + f.options.map((o) =>
              `<option value="${esc(o.value)}"${String(o.value) === String(f.value ?? '') ? ' selected' : ''}>${esc(o.label)}</option>`
            ).join('') + `</select>`;
          } else if (f.type === 'checkbox') {
            return `
              <label class="field field-check">
                <input type="checkbox" ${common}${f.value ? ' checked' : ''}>
                <span>${esc(f.label)}</span>
              </label>`;
          } else {
            control = `<input class="input" type="${f.type || 'text'}" ${common} value="${esc(f.value ?? '')}"
                         placeholder="${esc(f.placeholder || '')}" autocomplete="${esc(f.autocomplete || 'off')}"
                         maxlength="${f.maxlength || 128}" autocapitalize="off" spellcheck="false">`;
          }
          return `
            <label class="field" for="${id}">
              <span>${esc(f.label)}${f.required ? ' <i class="required">*</i>' : ''}</span>
              ${control}
              ${f.hint ? `<small class="field-hint">${esc(f.hint)}</small>` : ''}
            </label>`;
        }).join('') + '<p class="field-error" hidden></p>';
        return body.querySelector('input:not([disabled]), select:not([disabled])');
      }, {
        title, okText,
        onOk: async (body) => {
          const values = {};
          for (const f of fields) {
            const el = body.querySelector(`[name="${CSS.escape(f.name)}"]`);
            values[f.name] = f.type === 'checkbox' ? el.checked : (f.type === 'password' ? el.value : el.value.trim());
            if (f.required && !values[f.name]) {
              this.showError(body, '请填写' + f.label);
              el.focus();
              return undefined;
            }
          }
          const error = validate && validate(values);
          if (error) { this.showError(body, error); return undefined; }
          if (!submit) return values;
          try {
            const result = await submit(values);
            return result === undefined ? true : result;
          } catch (err) {
            this.showError(body, err.message || '操作失败');
            return undefined;
          }
        }
      });
    },

    /**
     * 自定义内容弹窗
     * build(body) 渲染内容并返回首个聚焦元素；submit(body) 异步提交，抛错时保持打开
     */
    custom({ title, okText = '保存', wide = false, build, submit }) {
      return this._open(build, {
        title, okText, wide,
        onOk: async (body) => {
          try {
            const result = await submit(body);
            return result === undefined ? true : result;
          } catch (err) {
            this.showError(body, err.message || '操作失败');
            return undefined;
          }
        }
      });
    },

    /**
     * 选择目标文件夹（用于移动），返回 { id } 或 null；id 为 null 表示根级
     * canSelect(node)：该文件夹能否作为目标；allowRoot：能否选择根目录
     */
    pickFolder({ title, tree, excludeId, currentParentId, canSelect = () => true, allowRoot = true }) {
      let selected;
      return this._open((body) => {
        const items = tree.flatten().filter(({ node }) => !tree.isDescendantOrSelf(node.id, excludeId));
        body.innerHTML = `
          <div class="folder-picker" role="listbox">
            ${allowRoot ? `<button class="picker-item" data-id="" style="--depth:0">${Icons.folder}<span>根目录</span></button>` : ''}
            ${items.map(({ node, depth }) => `
              <button class="picker-item" data-id="${node.id}" style="--depth:${depth + (allowRoot ? 1 : 0)}"
                      ${canSelect(node) ? '' : 'disabled title="没有该文件夹的写入权限"'}>
                ${Icons.folder}<span>${esc(node.name)}</span>
              </button>`).join('')}
          </div>
          <p class="field-error" hidden></p>`;
        const picker = body.querySelector('.folder-picker');
        picker.addEventListener('click', (e) => {
          const item = e.target.closest('.picker-item');
          if (!item || item.disabled) return;
          picker.querySelectorAll('.picker-item').forEach((b) => b.classList.remove('active'));
          item.classList.add('active');
          selected = item.dataset.id === '' ? null : Number(item.dataset.id);
          body.querySelector('.field-error').hidden = true;
        });
        // 标记当前位置
        const cur = picker.querySelector(`.picker-item[data-id="${currentParentId ?? ''}"]`);
        if (cur) cur.insertAdjacentHTML('beforeend', '<em>当前位置</em>');
        return null;
      }, {
        title, okText: '移动到此处',
        onOk: (body) => {
          if (selected === undefined) {
            this.showError(body, '请选择目标文件夹');
            return undefined;
          }
          return { id: selected };
        }
      });
    }
  };

  // ======================================================
  // 上下文菜单（桌面为浮动菜单，手机为底部操作面板）
  // ======================================================
  const Menu = {
    el: null,

    /**
     * @param {{label: string, icon?: string, danger?: boolean, action: () => void}[]} items
     */
    show(items, x, y, title) {
      this.hide();
      const mobile = Util.isMobile();
      const wrap = document.createElement('div');
      wrap.className = 'menu-layer' + (mobile ? ' sheet' : '');
      wrap.innerHTML = `
        <div class="menu" role="menu">
          ${mobile && title ? `<div class="menu-title">${esc(title)}</div>` : ''}
          ${items.map((it, i) => `
            <button class="menu-item${it.danger ? ' danger' : ''}" role="menuitem" data-index="${i}">
              ${it.icon || ''}<span>${esc(it.label)}</span>
            </button>`).join('')}
          ${mobile ? '<button class="menu-item menu-cancel" data-index="-1"><span>取消</span></button>' : ''}
        </div>`;
      document.body.appendChild(wrap);
      this.el = wrap;

      const menu = wrap.querySelector('.menu');
      if (!mobile) {
        // 保证菜单不超出视口
        const r = menu.getBoundingClientRect();
        const left = Math.min(x, window.innerWidth - r.width - 8);
        const top = y + r.height > window.innerHeight - 8 ? Math.max(8, y - r.height) : y;
        menu.style.left = Math.max(8, left) + 'px';
        menu.style.top = top + 'px';
      }
      requestAnimationFrame(() => wrap.classList.add('show'));

      wrap.addEventListener('click', (e) => {
        const btn = e.target.closest('.menu-item');
        if (btn) {
          const idx = Number(btn.dataset.index);
          this.hide();
          if (idx >= 0) items[idx].action();
        } else if (e.target === wrap) {
          this.hide();
        }
      });
      wrap.addEventListener('contextmenu', (e) => { e.preventDefault(); this.hide(); });
    },

    hide() {
      if (this.el) { this.el.remove(); this.el = null; }
    }
  };

  /** 修改自己的密码（主界面与管理页共用） */
  async function changeOwnPassword() {
    const ok = await Modal.form({
      title: '修改密码',
      okText: '确认修改',
      fields: [
        { name: 'oldPassword', label: '原密码', type: 'password', required: true, autocomplete: 'current-password' },
        { name: 'newPassword', label: '新密码', type: 'password', required: true, autocomplete: 'new-password', hint: '至少 6 位' },
        { name: 'confirm', label: '确认新密码', type: 'password', required: true, autocomplete: 'new-password' }
      ],
      validate: (v) => {
        if (v.newPassword.length < 6) return '新密码至少需要 6 位';
        if (v.newPassword !== v.confirm) return '两次输入的新密码不一致';
        return null;
      },
      submit: (v) => Api.changePassword(v.oldPassword, v.newPassword)
    });
    if (ok) Toast.success('密码已修改，其他设备上的登录已失效');
  }

  window.Toast = Toast;
  window.Modal = Modal;
  window.Menu = Menu;
  window.UI = { changeOwnPassword };
})();
