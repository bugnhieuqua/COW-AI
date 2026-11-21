/**
 * COW.AI - Client-Side File & Folder Attachment Handler
 * Hỗ trợ đính kèm tệp văn bản/mã nguồn và truy cập thư mục cho AI Agent
 * (Tích hợp trình duyệt webkitdirectory + Repomix & MCP Filesystem Server)
 */
(function (global) {
  'use strict';

  let currentAttachment = null;
  const MAX_FILE_SIZE = 512 * 1024; // 512 KB cho tệp đơn lẻ
  const MAX_FOLDER_CHARS = 65000;
  const MAX_SINGLE_FOLDER_FILE_BYTES = 180 * 1024;

  const IGNORED_DIRS = new Set([
    'node_modules', '.git', '.vs', '.vscode', '.idea', 'bin', 'obj',
    'dist', 'build', '__pycache__', '.venv', 'venv', 'coverage'
  ]);

  const BINARY_EXTS = new Set([
    'png', 'jpg', 'jpeg', 'gif', 'webp', 'bmp', 'ico', 'svg',
    'mp3', 'wav', 'ogg', 'mp4', 'webm', 'avi', 'mov',
    'exe', 'dll', 'so', 'dylib', 'pdb', 'obj', 'lib', 'bin',
    'zip', 'rar', '7z', 'tar', 'gz', 'pdf', 'docx', 'pptx', 'xlsx',
    'woff', 'woff2', 'ttf', 'otf', 'eot', 'db', 'sqlite'
  ]);

  function escapeHtml(text) {
    return String(text || '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }

  function onFolderSelectedSuccess(workspace, sessionId) {
    if (!workspace) return;
    const sid = sessionId || workspace.sessionId;
    const folderName = workspace.folderName || 'Dự án';

    if (global.CowAiHistoryStore) {
      global.CowAiHistoryStore.createProjectSession(sid, 'Dự án: ' + folderName, workspace);
      global.CowAiHistoryStore.setActiveSessionId(sid);
      global.CowAiHistoryStore.setSessionWorkspace(sid, workspace);
    }
    try {
      localStorage.setItem('cowai_last_active_project_id', sid);
    } catch {}
    if (global.CowAiWorkspace) {
      if (typeof global.CowAiWorkspace.refreshProjectsList === 'function') {
        global.CowAiWorkspace.refreshProjectsList();
      }
      if (typeof global.CowAiWorkspace.openProject === 'function') {
        global.CowAiWorkspace.openProject(sid);
      } else if (typeof global.CowAiWorkspace.refreshBanner === 'function') {
        global.CowAiWorkspace.refreshBanner(sid);
      }
    }
    const popover = document.getElementById('folderAgentPopover');
    if (popover) popover.classList.add('hidden');
    const folderBtn = document.getElementById('attachFolderBtn');
    if (folderBtn) folderBtn.setAttribute('aria-expanded', 'false');

    const promptInput = document.getElementById('promptInput') || document.getElementById('userPromptInput');
    if (promptInput) {
      promptInput.placeholder = `Hỏi về ${folderName} (${workspace.fileCount || 0} tệp)...`;
      promptInput.focus();
    }
  }

  function initFileAttachment(options) {
    options = options || {};
    const attachBtn = options.attachBtn || document.getElementById('attachBtn');
    const fileInput = options.fileInput || document.getElementById('chatFileInput');
    const folderBtn = document.getElementById('attachFolderBtn');
    const folderInput = document.getElementById('chatFolderInput');
    const previewArea = options.previewArea || document.getElementById('attachmentPreviewArea');

    if (attachBtn && fileInput) {
      attachBtn.addEventListener('click', function (e) {
        e.preventDefault();
        fileInput.click();
      });

      fileInput.addEventListener('change', function () {
        if (this.files && this.files.length > 0) {
          processFile(this.files[0], previewArea);
        }
        this.value = '';
      });
    }

    initFolderPopover(folderBtn, folderInput, previewArea);
  }

  function initFolderPopover(folderBtn, folderInput, previewArea) {
    const popover = document.getElementById('folderAgentPopover');
    const btnClose = document.getElementById('btnCloseFolderPopover');
    const btnPickNativeOs = document.getElementById('btnPickNativeOsFolder');
    const btnPickBrowser = document.getElementById('btnPickBrowserFolder');
    const pathInput = document.getElementById('folderPathInput');
    const btnBrowse = document.getElementById('btnBrowseServerDirs');
    const btnReadServer = document.getElementById('btnReadServerFolder');
    const browserList = document.getElementById('folderBrowserList');
    const statusBox = document.getElementById('folderAgentStatus');
    let pendingRebindSessionId = null;
    global.CowAiDirectoryHandles = global.CowAiDirectoryHandles || {};

    function setStatus(msg, isError) {
      if (!statusBox) return;
      if (!msg) {
        statusBox.classList.add('hidden');
        statusBox.textContent = '';
        return;
      }
      statusBox.textContent = msg;
      statusBox.classList.remove('hidden', 'is-error', 'is-success');
      statusBox.classList.add(isError ? 'is-error' : 'is-success');
    }

    if (popover && popover.parentElement && popover.parentElement !== document.body) {
      document.body.appendChild(popover);
    }

    function setOpen(open) {
      if (!popover) return;
      popover.classList.toggle('hidden', !open);
      if (folderBtn) folderBtn.setAttribute('aria-expanded', open ? 'true' : 'false');
      if (open) {
        setStatus('', false);
        if (pathInput) pathInput.focus();
      }
    }

    function isUserAuthenticated() {
      return true;
    }

    function requireLoginForProject() {
      return true;
    }

    let isPickingFolder = false;

    async function triggerDirectFolderPicker(targetSessionId) {
      if (isPickingFolder) return;
      isPickingFolder = true;

      const targetSid = targetSessionId || pendingRebindSessionId || (global.CowAiHistoryStore ? global.CowAiHistoryStore.newProjectSessionId() : ('proj_' + Date.now()));

      try {
        // 1. Mở ngay lập tức bằng showDirectoryPicker của trình duyệt (mở trực tiếp Windows Explorer chuẩn 100% trong gesture)
        if (typeof window.showDirectoryPicker === 'function') {
          try {
            const dirHandle = await window.showDirectoryPicker({ mode: 'readwrite' });
            if (dirHandle) {
              setStatus(`Đang nạp "${dirHandle.name}"...`, false);
              const collected = [];
              await collectFilesFromDirectoryHandle(dirHandle, dirHandle.name, collected);
              await processBrowserFolder(collected, previewArea, targetSid, dirHandle);
              pendingRebindSessionId = null;
              setOpen(false);
              return;
            }
          } catch (err) {
            if (err && err.name === 'AbortError') {
              setStatus('', false);
              return;
            }
            console.warn('showDirectoryPicker thất bại hoặc bị hủy:', err);
            return;
          }
        }

        // 2. Chỉ fallback qua backend hoặc input khi trình duyệt KHÔNG hỗ trợ showDirectoryPicker
        if (typeof window.showDirectoryPicker !== 'function') {
          if (btnPickNativeOs) btnPickNativeOs.disabled = true;
          setStatus('Đang mở Explorer...', false);
          try {
            const res = await fetch('/api/agent/workspace/pick-native-folder', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({
                sessionId: targetSid,
                initialPath: pathInput ? pathInput.value.trim() : ''
              })
            });
            const data = await res.json();
            if (data && data.cancelled) {
              setStatus('', false);
              return;
            }
            if (res.ok && data && data.success && data.workspace) {
              onFolderSelectedSuccess(data.workspace, targetSid);
              return;
            }
            if (data && data.error) {
              setStatus(data.error, true);
            }
          } catch (err) {
            console.warn('Backend pick-native-folder không phản hồi, thử input thư mục:', err);
          } finally {
            if (btnPickNativeOs) btnPickNativeOs.disabled = false;
          }

          // Fallback cuối qua input file directory
          if (folderInput) {
            folderInput.click();
          }
        }
      } finally {
        setTimeout(function () {
          isPickingFolder = false;
        }, 400);
      }
    }

    global.CowAiTriggerDirectFolderPicker = triggerDirectFolderPicker;

    global.CowAiOpenFolderModal = function (targetSessionId) {
      pendingRebindSessionId = typeof targetSessionId === 'string' && targetSessionId.trim() ? targetSessionId.trim() : null;
      if (popover && popover.parentElement && popover.parentElement !== document.body) {
        document.body.appendChild(popover);
      }
      setOpen(true);
      if (pathInput) pathInput.focus();
    };

    global.CowAiHandleAddProject = function (e) {
      if (e) {
        e.preventDefault();
        e.stopPropagation();
      }
      triggerDirectFolderPicker();
    };

    // Bấm nút Thư mục trên thanh prompt -> MỞ EXPLORER TRỰC TIẾP
    if (folderBtn) {
      folderBtn.addEventListener('click', function (e) {
        e.preventDefault();
        e.stopPropagation();
        triggerDirectFolderPicker();
      });
    }

    if (btnClose) {
      btnClose.addEventListener('click', function () {
        setOpen(false);
      });
    }

    if (btnPickNativeOs) {
      btnPickNativeOs.addEventListener('click', function (e) {
        e.preventDefault();
        triggerDirectFolderPicker();
      });
    }

    async function collectFilesFromDirectoryHandle(dirHandle, prefix, outList) {
      for await (const [name, entry] of dirHandle.entries()) {
        if (entry.kind === 'directory') {
          if (IGNORED_DIRS.has(name)) continue;
          await collectFilesFromDirectoryHandle(entry, `${prefix}/${name}`, outList);
        } else if (entry.kind === 'file') {
          try {
            const file = await entry.getFile();
            if (file.size <= 5 * 1024 * 1024) {
              Object.defineProperty(file, 'webkitRelativePath', {
                value: `${prefix}/${name}`,
                configurable: true
              });
              outList.push(file);
            }
          } catch { }
        }
      }
    }

    if (btnPickBrowser && folderInput) {
      btnPickBrowser.addEventListener('click', async function (e) {
        e.preventDefault();
        if (typeof window.showDirectoryPicker === 'function') {
          try {
            const dirHandle = await window.showDirectoryPicker({ mode: 'readwrite' });
            if (dirHandle) {
              setStatus(`Đang đọc & ánh xạ thư mục "${dirHandle.name}"...`, false);
              const collected = [];
              await collectFilesFromDirectoryHandle(dirHandle, dirHandle.name, collected);
              await processBrowserFolder(collected, previewArea, pendingRebindSessionId, dirHandle);
              pendingRebindSessionId = null;
              setOpen(false);
              return;
            }
          } catch (err) {
            if (err && err.name === 'AbortError') return;
          }
        }
        folderInput.click();
      });
    }

    if (folderInput) {
      folderInput.addEventListener('change', async function () {
        if (this.files && this.files.length > 0) {
          setStatus('Đang đọc & ánh xạ đường dẫn gốc trên máy...', false);
          await processBrowserFolder(this.files, previewArea, pendingRebindSessionId, null);
          pendingRebindSessionId = null;
          setOpen(false);
        }
        this.value = '';
      });
    }

    async function loadServerDirectoryList(targetPath) {
      if (!browserList) return;
      browserList.classList.remove('hidden');
      browserList.innerHTML = '<div class="folder-browser-loading">Đang tải danh sách thư mục...</div>';
      try {
        const url = '/api/agent/browse-dirs' + (targetPath ? '?path=' + encodeURIComponent(targetPath) : '');
        const res = await fetch(url);
        const data = await res.json();
        if (!res.ok || !data.success) {
          browserList.innerHTML = `<div class="folder-browser-empty">${escapeHtml(data?.error || 'Không thể đọc danh sách thư mục.')}</div>`;
          return;
        }

        if (pathInput && data.currentPath !== undefined && targetPath) {
          pathInput.value = data.currentPath;
        }

        browserList.innerHTML = '';
        if (data.currentPath) {
          const upBtn = document.createElement('button');
          upBtn.type = 'button';
          upBtn.className = 'folder-browser-item folder-browser-item--up';
          upBtn.innerHTML = '<span>⬆ .. (Quay lại thư mục cha)</span>';
          upBtn.addEventListener('click', function () {
            loadServerDirectoryList(data.parentPath || '');
          });
          browserList.appendChild(upBtn);
        }

        const dirs = Array.isArray(data.directories) ? data.directories : [];
        if (dirs.length === 0) {
          const empty = document.createElement('div');
          empty.className = 'folder-browser-empty';
          empty.textContent = 'Không có thư mục con nào.';
          browserList.appendChild(empty);
          return;
        }

        dirs.forEach(function (dir) {
          const row = document.createElement('div');
          row.className = 'folder-browser-row';

          const navBtn = document.createElement('button');
          navBtn.type = 'button';
          navBtn.className = 'folder-browser-item';
          navBtn.innerHTML = `<span class="folder-browser-icon">📁</span><span class="folder-browser-name">${escapeHtml(dir.name)}</span>`;
          navBtn.title = dir.fullPath;
          navBtn.addEventListener('click', function () {
            if (pathInput) pathInput.value = dir.fullPath;
            loadServerDirectoryList(dir.fullPath);
          });

          const selectBtn = document.createElement('button');
          selectBtn.type = 'button';
          selectBtn.className = 'folder-browser-pick';
          selectBtn.textContent = 'Chọn';
          selectBtn.title = 'Nạp thư mục này cho AI Agent';
          selectBtn.addEventListener('click', function () {
            if (pathInput) pathInput.value = dir.fullPath;
            readServerFolder(dir.fullPath);
          });

          row.appendChild(navBtn);
          row.appendChild(selectBtn);
          browserList.appendChild(row);
        });
      } catch {
        browserList.innerHTML = '<div class="folder-browser-empty">Lỗi kết nối khi duyệt thư mục.</div>';
      }
    }

    function ensureActiveSession() {
      if (!global.CowAiHistoryStore) return null;
      let sess = global.CowAiHistoryStore.getActiveSession();
      if (!sess) {
        sess = global.CowAiHistoryStore.createSession('Workspace mới');
      }
      return sess;
    }

    async function readServerFolder(targetPath) {
      const cleanPath = String(targetPath || (pathInput ? pathInput.value : '') || '').trim();
      if (!cleanPath) {
        setStatus('Vui lòng nhập hoặc chọn một đường dẫn thư mục.', true);
        if (pathInput) pathInput.focus();
        return;
      }

      // Dự án là phiên RIÊNG, hoặc cập nhật lại phiên đang yêu cầu gắn lại đường dẫn thật
      const sess = { id: pendingRebindSessionId || (global.CowAiHistoryStore ? global.CowAiHistoryStore.newProjectSessionId() : ('proj_' + Date.now())) };
      pendingRebindSessionId = null;

      if (btnReadServer) btnReadServer.disabled = true;
      setStatus('Đang gắn 1-1 thư mục vào đoạn chat & lập chỉ mục RAG...', false);

      try {
        const res = await fetch('/api/agent/workspace/bind', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ sessionId: sess.id, path: cleanPath })
        });
        const data = await res.json();
        if (!res.ok || !data.success) {
          setStatus(data?.error || 'Không thể gắn thư mục.', true);
          return;
        }

        if (data.workspace) {
          onFolderSelectedSuccess(data.workspace, sess.id);
        }
      } catch {
        setStatus('Lỗi kết nối máy chủ khi mở thư mục.', true);
      } finally {
        if (btnReadServer) btnReadServer.disabled = false;
      }
    }

    if (btnBrowse) {
      btnBrowse.addEventListener('click', function () {
        loadServerDirectoryList(pathInput ? pathInput.value.trim() : '');
      });
    }

    if (btnReadServer) {
      btnReadServer.addEventListener('click', function () {
        readServerFolder(pathInput ? pathInput.value : '');
      });
    }

    if (pathInput) {
      pathInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
          e.preventDefault();
          readServerFolder(pathInput.value);
        }
      });
    }

    document.addEventListener('click', function (e) {
      if (!popover || popover.classList.contains('hidden')) return;
      const addProjBtn = document.getElementById('sidebarAddProjectBtn');
      if (!popover.contains(e.target) &&
          (!folderBtn || !folderBtn.contains(e.target)) &&
          (!addProjBtn || !addProjBtn.contains(e.target))) {
        setOpen(false);
      }
    });

    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && popover && !popover.classList.contains('hidden')) {
        setOpen(false);
      }
    });
  }

  const MARKITDOWN_SERVER_EXTS = new Set([
    'pdf', 'docx', 'pptx', 'xlsx', 'csv', 'html', 'htm', 'epub', 'ipynb', 'xml'
  ]);

  async function processFile(file, previewArea) {
    if (!file) return;

    const extension = (file.name.split('.').pop() || '').toLowerCase();

    // Chuyển đổi các định dạng tài liệu phức tạp sang Markdown thông qua ManagedCode.MarkItDown trên server
    if (MARKITDOWN_SERVER_EXTS.has(extension) || file.size > MAX_FILE_SIZE) {
      if (file.size > 100 * 1024 * 1024) {
        alert(`Tệp "${file.name}" vượt quá giới hạn 100 MB.`);
        return;
      }

      if (previewArea) {
        previewArea.classList.remove('hidden');
        previewArea.innerHTML = `<div class="attachment-chip"><span class="attachment-chip__name">Đang chuyển "${escapeHtml(file.name)}" sang Markdown (MarkItDown)...</span></div>`;
      }

      try {
        const fd = new FormData();
        fd.append('file', file);
        const res = await fetch('/Document/ExtractFile', { method: 'POST', body: fd });
        const data = await res.json();
        if (!res.ok || !data.success) {
          alert(data?.error || data?.warning || 'Không thể chuyển đổi tệp sang Markdown.');
          clearAttachment(previewArea);
          return;
        }

        currentAttachment = {
          type: 'file',
          name: file.name + ' → .md',
          size: (data.content || '').length,
          extension: 'md',
          content: data.content || ''
        };
        renderAttachmentChip(previewArea);
      } catch {
        alert('Lỗi kết nối khi chuyển đổi tệp sang Markdown.');
        clearAttachment(previewArea);
      }
      return;
    }

    const reader = new FileReader();
    reader.onload = function (e) {
      const content = e.target.result;

      currentAttachment = {
        type: 'file',
        name: file.name,
        size: file.size,
        extension: extension,
        content: content
      };

      renderAttachmentChip(previewArea);
    };

    reader.onerror = function () {
      alert('Không thể đọc nội dung tệp. Vui lòng thử lại.');
      clearAttachment(previewArea);
    };

    reader.readAsText(file);
  }

  async function processBrowserFolder(fileList, previewArea, existingSessionId, dirHandle) {
    const allFiles = Array.from(fileList || []);
    if (allFiles.length === 0 && !dirHandle) return;

    const firstRel = allFiles[0] ? (allFiles[0].webkitRelativePath || allFiles[0].name) : '';
    const rootName = dirHandle ? dirHandle.name : (firstRel.includes('/') ? firstRel.split('/')[0] : 'Thư mục');

    const filtered = [];
    for (const f of allFiles) {
      const rel = f.webkitRelativePath || f.name;
      const parts = rel.split('/');
      if (parts.some(p => IGNORED_DIRS.has(p))) continue;
      if (f.size > 5 * 1024 * 1024) continue;
      filtered.push({ file: f, rel });
    }

    if (filtered.length === 0) {
      alert('Thư mục không chứa tệp hợp lệ để tải lên.');
      return;
    }

    // Dự án là phiên RIÊNG hoặc gắn lại cho phiên đang chọn
    const sessionId = existingSessionId || (global.CowAiHistoryStore ? global.CowAiHistoryStore.newProjectSessionId() : ('proj_' + Date.now()));
    if (dirHandle) {
      global.CowAiDirectoryHandles = global.CowAiDirectoryHandles || {};
      global.CowAiDirectoryHandles[sessionId] = dirHandle;
    }

    const formData = new FormData();
    formData.append('sessionId', sessionId);
    formData.append('folderName', rootName);

    const limited = filtered.slice(0, 600);
    for (const item of limited) {
      formData.append('files', item.file, item.file.name);
      formData.append('relativePaths', item.rel);
    }

    try {
      const res = await fetch('/api/agent/workspace/upload', {
        method: 'POST',
        body: formData
      });
      const data = await res.json();
      if (!res.ok || !data.success) {
        alert(data?.error || 'Không thể tải thư mục lên Workspace Agent.');
        return;
      }

      if (data.workspace) {
        if (typeof onFolderSelectedSuccess === 'function') {
          onFolderSelectedSuccess(data.workspace, sessionId);
        } else {
          if (global.CowAiHistoryStore) {
            global.CowAiHistoryStore.createProjectSession(sessionId, 'Dự án: ' + data.workspace.folderName, data.workspace);
          }
          if (global.CowAiWorkspace) {
            if (typeof global.CowAiWorkspace.refreshProjectsList === 'function') global.CowAiWorkspace.refreshProjectsList();
            if (typeof global.CowAiWorkspace.openProject === 'function') global.CowAiWorkspace.openProject(sessionId);
          }
        }
      }
    } catch {
      alert('Lỗi kết nối khi tải thư mục lên máy chủ.');
    }
  }

  function renderAttachmentChip(previewArea) {
    previewArea = previewArea || document.getElementById('attachmentPreviewArea');
    if (!previewArea) return;

    if (!currentAttachment) {
      previewArea.innerHTML = '';
      previewArea.classList.add('hidden');
      return;
    }

    if (currentAttachment.type === 'folder') {
      previewArea.innerHTML = `
        <div class="attachment-chip attachment-chip--folder" id="attachmentChip">
          <svg class="attachment-chip__icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
          </svg>
          <span class="attachment-chip__name" title="${escapeHtml(currentAttachment.fullPath || currentAttachment.name)}">${escapeHtml(currentAttachment.name)}</span>
          <span class="attachment-chip__size">(${currentAttachment.fileCount} tệp · ${escapeHtml(currentAttachment.engine)})</span>
          <button type="button" class="attachment-chip__remove" id="removeAttachmentBtn" aria-label="Xóa thư mục đính kèm">✕</button>
        </div>
      `;
    } else {
      const sizeKb = (currentAttachment.size / 1024).toFixed(1);
      previewArea.innerHTML = `
        <div class="attachment-chip" id="attachmentChip">
          <svg class="attachment-chip__icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
            <polyline points="14 2 14 8 20 8"></polyline>
            <line x1="16" y1="13" x2="8" y2="13"></line>
            <line x1="16" y1="17" x2="8" y2="17"></line>
            <polyline points="10 9 9 9 8 9"></polyline>
          </svg>
          <span class="attachment-chip__name" title="${escapeHtml(currentAttachment.name)}">${escapeHtml(currentAttachment.name)}</span>
          <span class="attachment-chip__size">(${sizeKb} KB)</span>
          <button type="button" class="attachment-chip__remove" id="removeAttachmentBtn" aria-label="Xóa tệp đính kèm">✕</button>
        </div>
      `;
    }
    previewArea.classList.remove('hidden');

    const removeBtn = document.getElementById('removeAttachmentBtn');
    if (removeBtn) {
      removeBtn.addEventListener('click', function (e) {
        e.preventDefault();
        e.stopPropagation();
        clearAttachment(previewArea);
      });
    }
  }

  function clearAttachment(previewArea) {
    currentAttachment = null;
    previewArea = previewArea || document.getElementById('attachmentPreviewArea');
    if (previewArea) {
      previewArea.innerHTML = '';
      previewArea.classList.add('hidden');
    }
  }

  function getAttachment() {
    return currentAttachment;
  }

  function formatPromptWithAttachment(userPrompt) {
    if (!currentAttachment) return userPrompt;

    const cleanedPrompt = String(userPrompt || '').replace(/^\/(?:folder|dir|repo|thumuc)\s*/i, '').trim();

    if (currentAttachment.type === 'folder') {
      const question = cleanedPrompt || 'Hãy phân tích cấu trúc, mục đích và các thành phần chính của thư mục này.';
      return `Bạn là AI Agent kỹ thuật đang truy cập và đọc thư mục "${currentAttachment.fullPath || currentAttachment.name}" (${currentAttachment.engine}, ${currentAttachment.fileCount} tệp).\n\nYÊU CẦU:\n${question}\n\n--- NGỮ CẢNH THƯ MỤC ---\n${currentAttachment.content}\n--- HẾT NGỮ CẢNH THƯ MỤC ---`;
    }

    const ext = currentAttachment.extension || 'txt';
    const attachmentSnippet = `\`\`\`${ext}\n// [Tệp: ${currentAttachment.name}]\n${currentAttachment.content}\n\`\`\``;

    if (!cleanedPrompt) {
      return `Hãy phân tích tệp sau:\n\n${attachmentSnippet}`;
    }

    return `${cleanedPrompt}\n\n--- Dữ liệu tệp đính kèm [${currentAttachment.name}] ---\n${attachmentSnippet}`;
  }

  global.CowAiFileAttachment = {
    init: initFileAttachment,
    getAttachment: getAttachment,
    clearAttachment: clearAttachment,
    formatPromptWithAttachment: formatPromptWithAttachment
  };

})(typeof window !== 'undefined' ? window : this);
