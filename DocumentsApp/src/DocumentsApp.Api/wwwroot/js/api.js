/**
 * API 封装 + 通用工具函数
 * 所有请求均携带 Cookie（同源），未登录时自动跳转登录页
 */
(function () {
  'use strict';

  /** 业务错误：携带 HTTP 状态码与中文提示 */
  class ApiError extends Error {
    constructor(message, status) {
      super(message);
      this.status = status;
    }
  }

  /** 按状态码给出默认中文提示 */
  function defaultMessage(status) {
    switch (status) {
      case 0: return '网络连接失败，请检查网络后重试';
      case 400: return '请求参数有误';
      case 401: return '未登录或登录已过期，请重新登录';
      case 403: return '没有权限执行此操作';
      case 404: return '请求的资源不存在';
      case 409: return '操作冲突，请刷新后重试';
      case 413: return '文件过大，超出上传限制';
      case 415: return '不支持的文件类型';
      case 500: return '服务器内部错误，请稍后重试';
      case 503: return '服务暂不可用，请稍后重试';
      case 504: return '处理超时，请稍后重试';
      default: return '操作失败（错误码 ' + status + '）';
    }
  }

  /** 从响应中提取错误信息 */
  async function extractError(res) {
    let message = '';
    try {
      const ct = res.headers.get('Content-Type') || '';
      if (ct.includes('application/json')) {
        const data = await res.json();
        message = data && (data.message || data.title) || '';
      }
    } catch (_) { /* 忽略解析错误 */ }
    return new ApiError(message || defaultMessage(res.status), res.status);
  }

  /** 未登录时跳转登录页（登录页自身除外） */
  function redirectToLogin() {
    if (!/\/login\.html$/i.test(location.pathname)) {
      location.href = '/login.html';
    }
  }

  /**
   * 通用请求
   * @param {string} url
   * @param {{method?: string, body?: any, signal?: AbortSignal, raw?: boolean}} [opts]
   */
  async function request(url, opts = {}) {
    const init = {
      method: opts.method || 'GET',
      credentials: 'same-origin',
      headers: { 'Accept': 'application/json' },
      signal: opts.signal
    };
    if (opts.body !== undefined) {
      init.headers['Content-Type'] = 'application/json';
      init.body = JSON.stringify(opts.body);
    }

    let res;
    try {
      res = await fetch(url, init);
    } catch (err) {
      if (err && err.name === 'AbortError') throw err;
      throw new ApiError(defaultMessage(0), 0);
    }

    if (res.status === 401 && !opts.noRedirect) {
      redirectToLogin();
      throw new ApiError(defaultMessage(401), 401);
    }
    if (!res.ok) throw await extractError(res);
    if (opts.raw) return res;
    if (res.status === 204) return null;

    const ct = res.headers.get('Content-Type') || '';
    return ct.includes('application/json') ? res.json() : res.text();
  }

  /**
   * 上传单个文件（使用 XHR 以获得上传进度）
   * @returns {{promise: Promise<any>, abort: () => void}}
   */
  function uploadFile(file, folderId, onProgress) {
    const xhr = new XMLHttpRequest();
    const promise = new Promise((resolve, reject) => {
      const form = new FormData();
      form.append('file', file, file.name);
      form.append('folderId', String(folderId));

      xhr.open('POST', '/api/documents/upload');
      xhr.withCredentials = true;
      xhr.setRequestHeader('Accept', 'application/json');

      xhr.upload.onprogress = (e) => {
        if (e.lengthComputable && onProgress) onProgress(e.loaded / e.total);
      };
      xhr.onload = () => {
        let data = null;
        try { data = JSON.parse(xhr.responseText); } catch (_) { /* 非 JSON */ }
        if (xhr.status >= 200 && xhr.status < 300) {
          resolve(data);
        } else {
          if (xhr.status === 401) redirectToLogin();
          reject(new ApiError((data && data.message) || defaultMessage(xhr.status), xhr.status));
        }
      };
      xhr.onerror = () => reject(new ApiError(defaultMessage(0), 0));
      xhr.onabort = () => reject(new ApiError('已取消上传', -1));
      xhr.send(form);
    });
    return { promise, abort: () => xhr.abort() };
  }

  const enc = encodeURIComponent;

  /** 对外暴露的 API */
  window.Api = {
    ApiError,

    // ---------- 认证 ----------
    login: (username, password) =>
      request('/api/auth/login', { method: 'POST', body: { username, password }, noRedirect: true }),
    logout: () => request('/api/auth/logout', { method: 'POST' }),
    me: (noRedirect) => request('/api/auth/me', { noRedirect }),
    changePassword: (oldPassword, newPassword) =>
      request('/api/auth/password', { method: 'POST', body: { oldPassword, newPassword } }),

    // ---------- 权限设置（type: 'folder' | 'document'） ----------
    getPermissions: (type, id) => request('/api/permissions/' + type + '/' + id),
    savePermissions: (type, id, inheritPermissions, entries) =>
      request('/api/permissions/' + type + '/' + id, { method: 'PUT', body: { inheritPermissions, entries } }),
    checkPermission: (type, id, userId) => request('/api/permissions/' + type + '/' + id + '/check?userId=' + userId),
    getPermissionOverview: () => request('/api/permissions/overview'),
    getDirectory: () => request('/api/directory'),

    // ---------- 系统管理（仅管理员） ----------
    getUsers: () => request('/api/users'),
    createUser: (data) => request('/api/users', { method: 'POST', body: data }),
    updateUser: (id, data) => request('/api/users/' + id, { method: 'PUT', body: data }),
    resetPassword: (id, password) => request('/api/users/' + id + '/password', { method: 'PUT', body: { password } }),
    deleteUser: (id) => request('/api/users/' + id, { method: 'DELETE' }),
    getDepartments: () => request('/api/departments'),
    createDepartment: (name) => request('/api/departments', { method: 'POST', body: { name } }),
    renameDepartment: (id, name) => request('/api/departments/' + id, { method: 'PUT', body: { name } }),
    deleteDepartment: (id) => request('/api/departments/' + id, { method: 'DELETE' }),
    getAuditLogs: (page, q) =>
      request('/api/audit-logs?page=' + page + '&pageSize=50' + (q ? '&q=' + enc(q) : '')),

    // ---------- 文件夹 ----------
    getFolderTree: () => request('/api/folders'),
    getFolder: (id) => request('/api/folders/' + id),
    createFolder: (name, parentId) =>
      request('/api/folders', { method: 'POST', body: { name, parentId: parentId ?? null } }),
    renameFolder: (id, name) => request('/api/folders/' + id, { method: 'PUT', body: { name } }),
    moveFolder: (id, newParentId) =>
      request('/api/folders/' + id + '/move', { method: 'PUT', body: { newParentId: newParentId ?? null } }),
    deleteFolder: (id) => request('/api/folders/' + id, { method: 'DELETE' }),

    // ---------- 文档 ----------
    getDocuments: (folderId, signal) => request('/api/documents?folderId=' + enc(folderId), { signal }),
    searchDocuments: (keyword, signal) => request('/api/documents?q=' + enc(keyword), { signal }),
    uploadFile,
    renameDocument: (id, fileName) =>
      request('/api/documents/' + id + '/rename', { method: 'PUT', body: { fileName } }),
    deleteDocument: (id) => request('/api/documents/' + id, { method: 'DELETE' }),
    downloadUrl: (id) => '/api/documents/' + id + '/download',
    /** 预览返回原始 Response，由调用方按 Content-Type 处理 */
    fetchPreview: (id, signal) => request('/api/documents/' + id + '/preview', { raw: true, signal })
  };

  /** 权限位（与后端 PermissionActions 一致） */
  window.Perm = {
    Read: 1, Write: 2, Delete: 4, Share: 8, Manage: 16,
    /** 预设级别 */
    levels: [
      { value: 1, name: '查看者', desc: '浏览、预览、下载', docDesc: '预览、下载' },
      { value: 3, name: '上传者', desc: '另可上传文档、新建子文件夹、重命名文档', docDesc: '另可重命名' },
      { value: 7, name: '编辑者', desc: '另可删除文档', docDesc: '另可删除' },
      { value: 31, name: '管理者', desc: '另可重命名 / 移动 / 删除文件夹、设置权限', docDesc: '另可设置该文档的权限' }
    ],
    /** 位标志 → 级别名称 */
    levelName(actions) {
      if (actions & 16) return '管理者';
      if (actions & 4) return '编辑者';
      if (actions & 2) return '上传者';
      if (actions & 1) return '查看者';
      return '无权限';
    },
    /** 位标志 → 最接近的预设级别值（向下取整） */
    levelValue(actions) {
      if (actions & 16) return 31;
      if (actions & 4) return 7;
      if (actions & 2) return 3;
      return 1;
    }
  };

  // ---------- 通用工具 ----------
  window.Util = {
    /** HTML 转义 */
    escapeHtml(str) {
      return String(str ?? '').replace(/[&<>"']/g, (c) => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
      }[c]));
    },

    /** 字节数格式化 */
    formatSize(bytes) {
      if (bytes == null) return '';
      if (bytes < 1024) return bytes + ' B';
      const units = ['KB', 'MB', 'GB', 'TB'];
      let v = bytes / 1024, i = 0;
      while (v >= 1024 && i < units.length - 1) { v /= 1024; i++; }
      return (v >= 100 ? v.toFixed(0) : v.toFixed(1)) + ' ' + units[i];
    },

    /** 时间格式化：今天显示时分，今年显示月日，否则显示年月日 */
    formatDate(value) {
      if (!value) return '';
      const d = new Date(value);
      if (isNaN(d.getTime())) return '';
      const now = new Date();
      const pad = (n) => String(n).padStart(2, '0');
      const hm = pad(d.getHours()) + ':' + pad(d.getMinutes());
      if (d.toDateString() === now.toDateString()) return '今天 ' + hm;
      const y = new Date(now); y.setDate(now.getDate() - 1);
      if (d.toDateString() === y.toDateString()) return '昨天 ' + hm;
      if (d.getFullYear() === now.getFullYear()) return (d.getMonth() + 1) + '月' + d.getDate() + '日 ' + hm;
      return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate());
    },

    /** 取扩展名（小写，不含点） */
    extOf(name) {
      const i = String(name || '').lastIndexOf('.');
      return i > 0 ? name.slice(i + 1).toLowerCase() : '';
    },

    /** 防抖 */
    debounce(fn, wait) {
      let t;
      return function (...args) {
        clearTimeout(t);
        t = setTimeout(() => fn.apply(this, args), wait);
      };
    },

    /** 是否为手机布局 */
    isMobile() {
      return window.matchMedia('(max-width: 767.98px)').matches;
    }
  };
})();
