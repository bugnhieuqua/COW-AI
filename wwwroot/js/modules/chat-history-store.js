/**
 * COW.AI - Client-Side Chat History Storage
 * Tiêu chí: Zero Hardcoded SQL - 100% Client-Side Privacy ("All chats are private")
 */
(function (global) {
  'use strict';

  function getUserId() {
    try {
      if (typeof document !== 'undefined' && document.body && document.body.dataset && document.body.dataset.userId) {
        return document.body.dataset.userId;
      }
    } catch { }
    return 'guest';
  }

  function getSessionsKey() {
    const uid = getUserId();
    return uid && uid !== 'guest' ? 'cowai_chat_sessions_u' + uid : 'cowai_chat_sessions';
  }

  function getActiveIdKey() {
    const uid = getUserId();
    return uid && uid !== 'guest' ? 'cowai_active_session_id_u' + uid : 'cowai_active_session_id';
  }

  function generateId() {
    return 'sess_' + Date.now().toString(36) + '_' + Math.random().toString(36).substring(2, 8);
  }

  function getSessions() {
    try {
      const raw = localStorage.getItem(getSessionsKey());
      if (!raw) return [];
      const parsed = JSON.parse(raw);
      if (!Array.isArray(parsed)) return [];
      // Migration: phiên đã gắn thư mục theo thiết kế cũ được coi là Dự án riêng
      parsed.forEach(function (s) {
        if (!s.kind) s.kind = (s.workspace && s.workspace.folderName) ? 'project' : 'chat';
      });
      return parsed;
    } catch (e) {
      console.warn('[ChatHistoryStore] Lỗi đọc sessions từ localStorage:', e);
      return [];
    }
  }

  function saveSessions(sessions) {
    try {
      localStorage.setItem(getSessionsKey(), JSON.stringify(sessions));
    } catch (e) {
      console.warn('[ChatHistoryStore] Lỗi lưu sessions vào localStorage:', e);
    }
  }

  function getActiveSessionId() {
    try {
      return localStorage.getItem(getActiveIdKey()) || null;
    } catch {
      return null;
    }
  }

  function setActiveSessionId(id) {
    try {
      if (id) {
        localStorage.setItem(getActiveIdKey(), id);
      } else {
        localStorage.removeItem(getActiveIdKey());
      }
    } catch (e) {
      console.warn('[ChatHistoryStore] Lỗi cập nhật active session ID:', e);
    }
  }

  function createSession(title) {
    const sessions = getSessions();
    const newSession = {
      id: generateId(),
      kind: 'chat',
      title: title || 'Đoạn chat mới',
      createdAt: Date.now(),
      updatedAt: Date.now(),
      workspace: null,
      messages: []
    };
    sessions.unshift(newSession);
    saveSessions(sessions);
    setActiveSessionId(newSession.id);
    return newSession;
  }

  function setSessionWorkspace(sessionId, workspaceObj) {
    const sessions = getSessions();
    const targetId = sessionId || getActiveSessionId();
    const idx = sessions.findIndex(s => s.id === targetId);
    if (idx === -1) return null;
    sessions[idx].workspace = workspaceObj || null;
    sessions[idx].updatedAt = Date.now();
    saveSessions(sessions);
    return sessions[idx];
  }

  function getSessionWorkspace(sessionId) {
    const targetId = sessionId || getActiveSessionId();
    if (!targetId) return null;
    const sess = getSessionById(targetId);
    return sess ? (sess.workspace || null) : null;
  }

  function getChatSessions() {
    return getSessions().filter(function (s) { return s.kind !== 'project'; });
  }

  function newProjectSessionId() {
    return 'proj_' + Date.now().toString(36) + '_' + Math.random().toString(36).substring(2, 8);
  }

  // Dự án là phiên RIÊNG (kind=project), có lịch sử riêng, không xuất hiện trong "Gần đây"
  function createProjectSession(id, title, workspaceObj) {
    const sessions = getSessions();
    const existing = sessions.find(function (s) { return s.id === id; });
    if (existing) {
      existing.kind = 'project';
      existing.workspace = workspaceObj || existing.workspace || null;
      existing.title = title || existing.title;
      existing.updatedAt = Date.now();
      saveSessions(sessions);
      setActiveSessionId(id);
      return existing;
    }
    const proj = {
      id: id,
      kind: 'project',
      title: title || 'Dự án',
      createdAt: Date.now(),
      updatedAt: Date.now(),
      workspace: workspaceObj || null,
      messages: []
    };
    sessions.unshift(proj);
    saveSessions(sessions);
    setActiveSessionId(id);
    return proj;
  }

  function getActiveSession() {
    const activeId = getActiveSessionId();
    const sessions = getSessions();
    const chats = sessions.filter(function (s) { return s.kind !== 'project'; });
    if (!activeId) {
      if (chats.length > 0) {
        setActiveSessionId(chats[0].id);
        return chats[0];
      }
      return null;
    }
    const found = sessions.find(s => s.id === activeId);
    if (!found && chats.length > 0) {
      setActiveSessionId(chats[0].id);
      return chats[0];
    }
    return found || null;
  }

  function getSessionById(id) {
    const sessions = getSessions();
    return sessions.find(s => s.id === id) || null;
  }

  function addMessage(role, content, model) {
    let session = getActiveSession();
    const sessions = getSessions();

    if (!session) {
      session = {
        id: generateId(),
        title: (role === 'user' && content) ? content.slice(0, 35).trim() + '...' : 'Đoạn chat mới',
        createdAt: Date.now(),
        updatedAt: Date.now(),
        messages: []
      };
      sessions.unshift(session);
      setActiveSessionId(session.id);
    } else {
      const idx = sessions.findIndex(s => s.id === session.id);
      if (idx !== -1) {
        session = sessions[idx];
      }
    }

    const newMsg = {
      role: role,
      content: content,
      model: model || null,
      timestamp: Date.now()
    };

    session.messages.push(newMsg);
    session.updatedAt = Date.now();

    // Tự động cập nhật tiêu đề nếu đây là câu hỏi đầu tiên của người dùng
    if (session.messages.length === 1 && role === 'user') {
      const firstLine = content.split('\n')[0].trim();
      session.title = firstLine.length > 35 ? firstLine.slice(0, 35) + '...' : firstLine;
    }

    saveSessions(sessions);
    return session;
  }

  function deleteSession(id) {
    let sessions = getSessions();
    sessions = sessions.filter(s => s.id !== id);
    saveSessions(sessions);

    if (getActiveSessionId() === id) {
      const chatsLeft = sessions.filter(function (s) { return s.kind !== 'project'; });
      if (chatsLeft.length > 0) {
        setActiveSessionId(chatsLeft[0].id);
      } else {
        setActiveSessionId(null);
      }
    }
    return sessions;
  }

  function clearAllSessions() {
    saveSessions([]);
    setActiveSessionId(null);
  }

  function exportSessionToMarkdown(id) {
    const session = id ? getSessionById(id) : getActiveSession();
    if (!session || !session.messages || session.messages.length === 0) {
      alert('Không có nội dung tin nhắn để xuất file.');
      return;
    }

    let md = `# ${session.title || 'Hội thoại COW.AI'}\n`;
    md += `*Thời gian tạo: ${new Date(session.createdAt).toLocaleString('vi-VN')}*\n\n---\n\n`;

    session.messages.forEach(msg => {
      const timeStr = new Date(msg.timestamp).toLocaleTimeString('vi-VN');
      if (msg.role === 'user') {
        md += `### 👤 Người dùng (${timeStr}):\n\n${msg.content}\n\n`;
      } else {
        const modelStr = msg.model ? ` [${msg.model}]` : '';
        md += `### 🐮 COW.AI${modelStr} (${timeStr}):\n\n${msg.content}\n\n`;
      }
      md += `---\n\n`;
    });

    const blob = new Blob([md], { type: 'text/markdown;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    const safeTitle = (session.title || 'hoi-thoai').replace(/[^a-zA-Z0-9_\-\u00C0-\u024F\u1E00-\u1EFF]/g, '_');
    a.href = url;
    a.download = `cow-ai_${safeTitle}_${Date.now()}.md`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }

  // Export module to global window
  global.CowAiHistoryStore = {
    getSessions,
    getChatSessions,
    newProjectSessionId,
    createProjectSession,
    getActiveSessionId,
    setActiveSessionId,
    createSession,
    getActiveSession,
    getSessionById,
    setSessionWorkspace,
    getSessionWorkspace,
    addMessage,
    deleteSession,
    clearAllSessions,
    exportSessionToMarkdown
  };

})(typeof window !== 'undefined' ? window : this);
