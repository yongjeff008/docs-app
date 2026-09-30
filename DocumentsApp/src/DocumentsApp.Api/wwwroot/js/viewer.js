/**
 * 预览组件：PDF（PDF.js 渲染）/ 图片 / 文本
 * Office 文档由后端转换为 PDF 后返回，前端统一按 PDF 处理
 */
(function () {
  'use strict';

  // PDF.js（CDN 引入，按顺序尝试，全部失败时回退到浏览器内置 PDF 阅读器）
  const PDFJS_VERSION = '3.11.174';
  const PDFJS_CDNS = [
    `https://cdn.jsdelivr.net/npm/pdfjs-dist@${PDFJS_VERSION}/build/`,
    `https://cdnjs.cloudflare.com/ajax/libs/pdf.js/${PDFJS_VERSION}/`,
    `https://unpkg.com/pdfjs-dist@${PDFJS_VERSION}/build/`
  ];

  let pdfjsPromise = null;

  function loadScript(src) {
    return new Promise((resolve, reject) => {
      const s = document.createElement('script');
      s.src = src;
      s.async = true;
      s.onload = resolve;
      s.onerror = () => { s.remove(); reject(new Error('加载失败：' + src)); };
      document.head.appendChild(s);
    });
  }

  /** 懒加载 PDF.js */
  function loadPdfJs() {
    if (window.pdfjsLib) return Promise.resolve(window.pdfjsLib);
    if (pdfjsPromise) return pdfjsPromise;
    pdfjsPromise = (async () => {
      for (const base of PDFJS_CDNS) {
        try {
          await loadScript(base + 'pdf.min.js');
          if (window.pdfjsLib) {
            window.pdfjsLib.GlobalWorkerOptions.workerSrc = base + 'pdf.worker.min.js';
            return window.pdfjsLib;
          }
        } catch (_) { /* 尝试下一个 CDN */ }
      }
      pdfjsPromise = null;
      throw new Error('PDF.js 加载失败');
    })();
    return pdfjsPromise;
  }

  const ZOOM_STEPS = [0.5, 0.75, 1, 1.25, 1.5, 2, 3];

  class Viewer {
    /** @param {HTMLElement} container 预览内容容器 */
    constructor(container) {
      this.container = container;
      this.abort = null;
      this.pdf = null;
      this.objectUrl = null;
      this.observer = null;
      this.zoom = 1;
      this._onResize = Util.debounce(() => { if (this.pdf) this._layoutPdf(); }, 200);
      window.addEventListener('resize', this._onResize);
    }

    /** 打开文档预览 */
    async open(doc) {
      this.close();
      const abort = new AbortController();
      this.abort = abort;
      this.doc = doc;
      this.zoom = 1;

      const isOffice = doc.previewKind === 'office';
      this._showLoading(isOffice ? '正在转换文档，首次预览可能需要十几秒…' : '正在加载预览…');

      try {
        const res = await Api.fetchPreview(doc.id, abort.signal);
        const ct = (res.headers.get('Content-Type') || '').toLowerCase();

        if (ct.includes('application/pdf')) {
          const data = await res.arrayBuffer();
          if (abort.signal.aborted) return;
          await this._renderPdf(data, abort);
        } else if (ct.startsWith('image/')) {
          const blob = await res.blob();
          if (abort.signal.aborted) return;
          this._renderImage(blob);
        } else if (ct.startsWith('text/')) {
          const text = await res.text();
          if (abort.signal.aborted) return;
          this._renderText(text, doc.fileName);
        } else {
          this._showError('暂不支持预览该类型的文件，请下载后查看');
        }
      } catch (err) {
        if (err && err.name === 'AbortError') return;
        if (abort.signal.aborted) return;
        this._showError(err && err.message ? err.message : '预览加载失败');
      }
    }

    /** 关闭预览并释放资源 */
    close() {
      if (this.abort) { this.abort.abort(); this.abort = null; }
      if (this.observer) { this.observer.disconnect(); this.observer = null; }
      if (this.pdf) { try { this.pdf.destroy(); } catch (_) { /* 忽略 */ } this.pdf = null; }
      if (this.objectUrl) { URL.revokeObjectURL(this.objectUrl); this.objectUrl = null; }
      this.container.innerHTML = '';
      this.container.className = 'preview-body';
    }

    // ---------- 状态显示 ----------

    _showLoading(text) {
      this.container.innerHTML =
        `<div class="preview-status"><div class="spinner"></div><p>${Util.escapeHtml(text)}</p></div>`;
    }

    _showError(message) {
      const doc = this.doc;
      this.container.innerHTML = `
        <div class="preview-status error">
          ${Icons.alert}
          <p>${Util.escapeHtml(message)}</p>
          ${doc ? `<a class="btn btn-primary" href="${Api.downloadUrl(doc.id)}" download>${Icons.download}<span>下载文件</span></a>` : ''}
        </div>`;
    }

    // ---------- 图片 ----------

    _renderImage(blob) {
      this.objectUrl = URL.createObjectURL(blob);
      this.container.classList.add('image-mode');
      const img = new Image();
      img.alt = this.doc.fileName;
      img.className = 'preview-image';
      img.onerror = () => this._showError('图片加载失败，文件可能已损坏');
      img.src = this.objectUrl;
      // 点击切换原始尺寸 / 适应窗口
      img.addEventListener('click', () => img.classList.toggle('actual-size'));
      this.container.innerHTML = '';
      this.container.appendChild(img);
    }

    // ---------- 文本 ----------

    _renderText(text, fileName) {
      this.container.classList.add('text-mode');
      let content = text;
      // JSON 自动格式化
      if (Util.extOf(fileName) === 'json') {
        try { content = JSON.stringify(JSON.parse(text), null, 2); } catch (_) { /* 保持原样 */ }
      }
      const pre = document.createElement('pre');
      pre.className = 'preview-text';
      pre.textContent = content || '（空文件）';
      this.container.innerHTML = '';
      this.container.appendChild(pre);
    }

    // ---------- PDF ----------

    async _renderPdf(data, abort) {
      let pdfjs;
      try {
        pdfjs = await loadPdfJs();
      } catch (_) {
        // CDN 不可用：回退到浏览器内置阅读器
        if (abort.signal.aborted) return;
        this._renderPdfFallback(data);
        return;
      }
      if (abort.signal.aborted) return;

      try {
        const task = pdfjs.getDocument({ data: new Uint8Array(data) });
        const pdf = await task.promise;
        if (abort.signal.aborted) { pdf.destroy(); return; }
        this.pdf = pdf;
      } catch (err) {
        if (err && err.name === 'PasswordException') {
          this._showError('该 PDF 已加密，无法在线预览，请下载后查看');
        } else {
          this._showError('PDF 解析失败，文件可能已损坏');
        }
        return;
      }

      const firstPage = await this.pdf.getPage(1);
      this.baseViewport = firstPage.getViewport({ scale: 1 });

      this.container.classList.add('pdf-mode');
      this.container.innerHTML = `
        <div class="pdf-toolbar">
          <span class="pdf-pages">共 ${this.pdf.numPages} 页</span>
          <div class="pdf-zoom">
            <button class="icon-btn" data-zoom="out" aria-label="缩小">${Icons.zoomOut}</button>
            <span class="pdf-zoom-value">100%</span>
            <button class="icon-btn" data-zoom="in" aria-label="放大">${Icons.zoomIn}</button>
          </div>
        </div>
        <div class="pdf-pages-container"></div>`;

      this.container.querySelector('.pdf-zoom').addEventListener('click', (e) => {
        const btn = e.target.closest('[data-zoom]');
        if (!btn) return;
        const idx = ZOOM_STEPS.indexOf(this.zoom);
        const next = btn.dataset.zoom === 'in' ? ZOOM_STEPS[Math.min(idx + 1, ZOOM_STEPS.length - 1)]
                                               : ZOOM_STEPS[Math.max(idx - 1, 0)];
        if (next !== this.zoom) {
          this.zoom = next;
          this.container.querySelector('.pdf-zoom-value').textContent = Math.round(next * 100) + '%';
          this._layoutPdf();
        }
      });

      this._layoutPdf();
    }

    /** 计算页面尺寸并创建占位，进入可视区域时再渲染（懒加载） */
    _layoutPdf() {
      const holder = this.container.querySelector('.pdf-pages-container');
      if (!holder || !this.pdf) return;
      if (this.observer) this.observer.disconnect();

      const available = Math.max(holder.clientWidth - 24, 200);
      // 适应宽度，桌面端最大不超过 1.5 倍原始尺寸，再乘以用户缩放
      const fitScale = Math.min(available / this.baseViewport.width, 1.5);
      this.scale = fitScale * this.zoom;
      const w = Math.floor(this.baseViewport.width * this.scale);
      const h = Math.floor(this.baseViewport.height * this.scale);

      const frag = document.createDocumentFragment();
      for (let i = 1; i <= this.pdf.numPages; i++) {
        const page = document.createElement('div');
        page.className = 'pdf-page';
        page.dataset.page = String(i);
        page.style.width = w + 'px';
        page.style.height = h + 'px';
        page.innerHTML = `<span class="pdf-page-num">${i}</span>`;
        frag.appendChild(page);
      }
      holder.innerHTML = '';
      holder.appendChild(frag);

      this.observer = new IntersectionObserver((entries) => {
        for (const entry of entries) {
          if (entry.isIntersecting) {
            this.observer.unobserve(entry.target);
            this._renderPage(entry.target);
          }
        }
      }, { root: holder, rootMargin: '600px 0px' });
      holder.querySelectorAll('.pdf-page').forEach((el) => this.observer.observe(el));
    }

    async _renderPage(el) {
      const pdf = this.pdf;
      if (!pdf) return;
      try {
        const page = await pdf.getPage(Number(el.dataset.page));
        if (pdf !== this.pdf) return;
        const viewport = page.getViewport({ scale: this.scale });
        const dpr = Math.min(window.devicePixelRatio || 1, 2);
        const canvas = document.createElement('canvas');
        canvas.width = Math.floor(viewport.width * dpr);
        canvas.height = Math.floor(viewport.height * dpr);
        canvas.style.width = Math.floor(viewport.width) + 'px';
        canvas.style.height = Math.floor(viewport.height) + 'px';
        // 页面尺寸可能与首页不同，以实际为准
        el.style.width = canvas.style.width;
        el.style.height = canvas.style.height;
        const ctx = canvas.getContext('2d');
        await page.render({
          canvasContext: ctx,
          viewport,
          transform: dpr !== 1 ? [dpr, 0, 0, dpr, 0, 0] : null
        }).promise;
        if (pdf !== this.pdf) return;
        el.appendChild(canvas);
      } catch (_) {
        el.classList.add('failed');
      }
    }

    /** 回退方案：使用浏览器内置 PDF 阅读器 */
    _renderPdfFallback(data) {
      const blob = new Blob([data], { type: 'application/pdf' });
      this.objectUrl = URL.createObjectURL(blob);
      this.container.classList.add('pdf-mode');
      this.container.innerHTML = `<iframe class="pdf-iframe" title="PDF 预览" src="${this.objectUrl}"></iframe>`;
    }
  }

  window.Viewer = Viewer;
})();
