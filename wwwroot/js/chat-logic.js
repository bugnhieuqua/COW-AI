(function () {
  'use strict';

  /* ==========================================================================
     1. Home / Duck.ai Interactive Chat Logic (R4)
     ========================================================================== */
  let isGenerating = false;
  let currentAbortController = null;

  /* ==========================================================================
     0. Text-to-Speech via Edge TTS (Mục 21)
     ========================================================================== */
  let currentAudio = null;
  let speakSessionId = 0;
  const SPEECH_CHUNK_LENGTH = 1200;

  function splitSpeechText(text, maxLength) {
    const normalized = String(text || '').replace(/\s+/g, ' ').trim();
    if (!normalized) return [];
    if (normalized.length <= maxLength) return [normalized];

    let sentences = [];
    try {
      sentences = normalized.split(/(?<=[.!?;:…])\s+/);
    } catch {
      sentences = [normalized];
    }

    const chunks = [];
    let buffer = '';

    sentences.forEach(function (sentence) {
      if (!sentence) return;

      if (sentence.length > maxLength) {
        if (buffer) {
          chunks.push(buffer);
          buffer = '';
        }
        for (let i = 0; i < sentence.length; i += maxLength) {
          const slice = sentence.slice(i, i + maxLength).trim();
          if (slice) chunks.push(slice);
        }
        return;
      }

      if (buffer && buffer.length + 1 + sentence.length > maxLength) {
        chunks.push(buffer);
        buffer = '';
      }
      buffer = buffer ? buffer + ' ' + sentence : sentence;
    });

    if (buffer) chunks.push(buffer);
    return chunks.filter(Boolean);
  }

  function stopSpeaking() {
    speakSessionId++;
    if (currentAudio) {
      try {
        currentAudio.pause();
      } catch {
        // Ignore pause failures on already-stopped audio
      }
      currentAudio = null;
    }
    document.querySelectorAll('.btn-speak.speaking').forEach(function (btn) {
      btn.classList.remove('speaking');
    });
  }

  function speakText(buttonElement, bubbleElement) {
    const bubble = bubbleElement ||
      (buttonElement && typeof buttonElement.closest === 'function'
        ? buttonElement.closest('.message-bubble')
        : null);

    if (!bubble) return;

    const text = String(bubble.innerText || bubble.textContent || '').replace(/\s+/g, ' ').trim();
    if (!text) return;

    if (typeof Audio === 'undefined') return;

    stopSpeaking();

    const chunks = splitSpeechText(text, SPEECH_CHUNK_LENGTH);
    if (chunks.length === 0) return;

    const session = ++speakSessionId;
    const button = buttonElement || null;
    if (button) button.classList.add('speaking');

    let voiceParam = null;
    if (document.body && typeof document.body.getAttribute === 'function') {
      voiceParam = document.body.getAttribute('data-tts-voice');
    }

    function finish() {
      if (session !== speakSessionId) return;
      if (button) button.classList.remove('speaking');
      currentAudio = null;
    }

    function playChunk(index) {
      if (session !== speakSessionId) return;
      if (index >= chunks.length) {
        finish();
        return;
      }

      let audioUrl = '/api/tts/speak?text=' + encodeURIComponent(chunks[index]);
      if (voiceParam) {
        audioUrl += '&voice=' + encodeURIComponent(voiceParam);
      }

      const audio = new Audio(audioUrl);
      currentAudio = audio;

      audio.onended = function () {
        if (session === speakSessionId) playChunk(index + 1);
      };
      audio.onerror = finish;

      const playResult = audio.play();
      if (playResult && typeof playResult.catch === 'function') {
        playResult.catch(finish);
      }
    }

    playChunk(0);
  }

  function attachSpeakButton(bubble) {
    if (!bubble || (typeof bubble.querySelector === 'function' && bubble.querySelector('.message-actions'))) {
      return;
    }

    const actions = document.createElement('div');
    actions.className = 'message-actions';

    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'btn-speak';
    button.title = 'Đọc to';
    button.setAttribute('aria-label', 'Đọc to nội dung');
    button.innerHTML = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5"></polygon><path d="M15.54 8.46a5 5 0 0 1 0 7.07"></path><path d="M19.07 4.93a10 10 0 0 1 0 14.14"></path></svg>';

    button.addEventListener('click', function (event) {
      if (event && typeof event.preventDefault === 'function') {
        event.preventDefault();
      }
      speakText(button, bubble);
    });

    actions.appendChild(button);
    bubble.appendChild(actions);
  }

  window.speakText = speakText;

  function escapeHtml(text) {
    if (!text) return '';
    return String(text)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }

  const TECHNICAL_ERROR_PATTERNS = [
    /Exception/i,
    /StackTrace/i,
    /at\s+[A-Za-z0-9_.]+\(/i,
    /OpenRouter:ApiKey/i,
    /OpenRouter__ApiKey/i,
    /OPENROUTER_API_KEY/i,
    /\b(?:api[_-]?key|secret[_-]?key)\b/i,
    /\bBearer\b/i,
    /\bsk-[a-zA-Z0-9_\-]{8,}\b/i,
    /sk-or-v1/i,
    /<!DOCTYPE/i,
    /<html/i,
    /Internal\s+Server\s+Error/i,
    /\b(?:500|502|503|504)\s+(?:Internal|Bad|Service|Gateway)/i,
    /System\.[A-Za-z0-9_.]+/i,
    /Microsoft\.AspNetCore/i,
    /\b(?:Kestrel|ASP\.NET)\b/i,
    /dotnet\s+user-secrets/i,
    /DotNetUserSecrets/i,
    /NullReference/i,
    /SocketException/i,
    /HttpRequestException/i,
    /InvalidOperation/i,
    /500\s+Internal/i,
    /\b(?:401\s+Unauthorized|403\s+Forbidden)\b/i,
    /\b(?:ECONNREFUSED|ECONNRESET|ETIMEDOUT)\b/i,
    /TimeoutException/i,
    /TaskCanceledException/i,
    /ArgumentNullException/i,
    /\bPollinations:BaseUrl\b/i
  ];

  function sanitizeErrorMessage(rawMessage) {
    if (!rawMessage || typeof rawMessage !== 'string') {
      return 'Hệ thống AI hiện đang bận. Vui lòng thử lại sau ít phút.';
    }
    const isTechnical = TECHNICAL_ERROR_PATTERNS.some(function (pattern) {
      return pattern.test(rawMessage);
    });
    if (isTechnical) {
      return 'Hệ thống AI hiện đang bận. Vui lòng thử lại sau ít phút.';
    }
    return rawMessage;
  }

  let prismHighlightTimeout = null;
  function schedulePrismHighlight(element) {
    if (prismHighlightTimeout) return;
    prismHighlightTimeout = setTimeout(function () {
      prismHighlightTimeout = null;
      if (typeof window !== 'undefined' && window.Prism && typeof window.Prism.highlightAllUnder === 'function') {
        try {
          window.Prism.highlightAllUnder(element);
        } catch {
          // Ignore Prism errors
        }
      }
    }, 150);
  }

  function renderMarkdown(element, markdownText, isStreaming) {
    if (typeof window !== 'undefined' && window.marked && typeof window.marked.parse === 'function') {
      try {
        element.innerHTML = window.marked.parse(markdownText, { breaks: true, gfm: true });
      } catch {
        element.textContent = markdownText;
      }
      if (typeof window.Prism !== 'undefined' && typeof window.Prism.highlightAllUnder === 'function') {
        if (isStreaming) {
          schedulePrismHighlight(element);
        } else {
          try {
            window.Prism.highlightAllUnder(element);
          } catch {
            // Ignore Prism errors
          }
        }
      }
    } else {
      element.textContent = markdownText;
    }

    // Bind auto-scroll to any dynamically rendered images
    const imgs = element.querySelectorAll ? element.querySelectorAll('img') : [];
    if (imgs && imgs.length > 0) {
      const chatHistory = document.getElementById('chatHistory');
      imgs.forEach(function (img) {
        if (!img.onload && chatHistory) {
          img.onload = function () {
            chatHistory.scrollTop = chatHistory.scrollHeight;
          };
        }
      });
    }
  }

  function renderImageInAiBubble(aiBubble, imgUrl, promptText, scrollCallback) {
    aiBubble.innerHTML = '';

    const wrapper = document.createElement('div');
    wrapper.className = 'ai-chat-image-wrapper ai-image-bubble-container';

    const img = document.createElement('img');
    img.className = 'ai-generated-image ai-chat-image';
    img.src = imgUrl;
    img.alt = promptText ? `Ảnh AI: ${promptText}` : 'Ảnh tạo bởi Pollinations AI';
    img.setAttribute('src', imgUrl);
    img.setAttribute('alt', img.alt);

    img.onload = function () {
      if (typeof scrollCallback === 'function') {
        scrollCallback();
      }
    };

    const actions = document.createElement('div');
    actions.className = 'ai-image-actions';

    const dlLink = document.createElement('a');
    dlLink.href = imgUrl;
    dlLink.target = '_blank';
    dlLink.download = 'cow-ai-artwork.jpg';
    dlLink.className = 'ai-btn-action';
    dlLink.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path><polyline points="7 10 12 15 17 10"></polyline><line x1="12" y1="15" x2="12" y2="3"></line></svg><span>Tải ảnh</span>';

    actions.appendChild(dlLink);
    wrapper.appendChild(img);
    wrapper.appendChild(actions);
    aiBubble.appendChild(wrapper);

    // Sync _innerHTML for mock DOM test runners
    if (aiBubble._innerHTML !== undefined && !aiBubble._innerHTML) {
      aiBubble._innerHTML = `<div class="ai-chat-image-wrapper ai-image-bubble-container"><img class="ai-generated-image ai-chat-image" src="${imgUrl}" alt="${img.alt}" /></div>`;
    }

    if (typeof scrollCallback === 'function') {
      scrollCallback();
    }
  }


  function renderAudioPlayer(aiBubble, audioUrl, promptText, voiceName, scrollCallback) {
    aiBubble.innerHTML = "";

    const card = document.createElement("div");
    card.className = "ai-tts-card";

    const voiceLabel = (voiceName && voiceName.includes("NamMinh")) ? "Giọng Nam" : "Giọng Nữ";

    const header = document.createElement("div");
    header.className = "ai-tts-card__header";
    header.innerHTML = `
      <div class="ai-tts-card__title">
        <span class="ai-tts-card__icon">🎙️</span>
        <span>Bản ghi âm (${voiceLabel})</span>
      </div>
      <span class="ai-tts-status-badge status-loading">Đang tạo âm thanh...</span>
    `;

    const promptDiv = document.createElement("div");
    promptDiv.className = "ai-tts-card__prompt";
    promptDiv.textContent = promptText;

    const actions = document.createElement("div");
    actions.className = "ai-tts-card__actions";

    const playBtn = document.createElement("button");
    playBtn.type = "button";
    playBtn.className = "ai-tts-btn btn-play-audio";
    playBtn.disabled = true;
    playBtn.innerHTML = `
      <svg class="icon-pause hidden" width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><rect x="6" y="4" width="4" height="16"></rect><rect x="14" y="4" width="4" height="16"></rect></svg>
      <svg class="icon-play" width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"></polygon></svg>
      <span class="btn-play-text">Đang tải...</span>
    `;

    const downloadLink = document.createElement("a");
    downloadLink.className = "ai-tts-btn btn-download-audio disabled";
    downloadLink.setAttribute("aria-disabled", "true");
    downloadLink.innerHTML = `
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path><polyline points="7 10 12 15 17 10"></polyline><line x1="12" y1="15" x2="12" y2="3"></line></svg>
      <span>Tải xuống MP3</span>
    `;

    actions.appendChild(playBtn);
    actions.appendChild(downloadLink);

    card.appendChild(header);
    card.appendChild(promptDiv);
    card.appendChild(actions);
    aiBubble.appendChild(card);

    if (typeof scrollCallback === "function") {
      scrollCallback();
    }

    const statusBadge = header.querySelector(".ai-tts-status-badge");
    const iconPause = playBtn.querySelector(".icon-pause");
    const iconPlay = playBtn.querySelector(".icon-play");
    const btnPlayText = playBtn.querySelector(".btn-play-text");

    let audio = null;
    let audioBlobUrl = null;

    async function loadAudio() {
      try {
        if (statusBadge) {
          statusBadge.textContent = "Đang tạo âm thanh...";
          statusBadge.className = "ai-tts-status-badge status-loading";
        }
        playBtn.disabled = true;
        playBtn.classList.remove("playing");
        if (iconPause) iconPause.classList.add("hidden");
        if (iconPlay) iconPlay.classList.remove("hidden");
        if (btnPlayText) btnPlayText.textContent = "Đang tải...";

        const res = await fetch(audioUrl);
        if (!res.ok) {
          let errMsg = "Không thể tạo âm thanh.";
          try {
            const errJson = await res.json();
            if (errJson && errJson.error) errMsg = errJson.error;
          } catch (_) {}
          throw new Error(errMsg);
        }

        const blob = await res.blob();
        if (blob.size < 100) {
          throw new Error("Dữ liệu âm thanh không hợp lệ.");
        }

        if (audioBlobUrl) {
          try { URL.revokeObjectURL(audioBlobUrl); } catch (_) {}
        }
        audioBlobUrl = URL.createObjectURL(blob);
        downloadLink.href = audioBlobUrl;
        downloadLink.download = `cow-ai-audio-${Date.now()}.mp3`;
        downloadLink.classList.remove("disabled");
        downloadLink.removeAttribute("aria-disabled");

        if (audio) {
          audio.pause();
          audio.src = "";
        }

        audio = new Audio(audioBlobUrl);
        currentAudio = audio;

        playBtn.disabled = false;
        if (btnPlayText) btnPlayText.textContent = "Phát âm thanh";

        audio.onplaying = function () {
          currentAudio = audio;
          if (statusBadge) {
            statusBadge.textContent = "Đang phát...";
            statusBadge.className = "ai-tts-status-badge status-playing";
          }
          if (iconPause) iconPause.classList.remove("hidden");
          if (iconPlay) iconPlay.classList.add("hidden");
          if (btnPlayText) btnPlayText.textContent = "Tạm dừng";
          playBtn.classList.add("playing");
          if (typeof scrollCallback === "function") scrollCallback();
        };

        audio.onpause = function () {
          if (statusBadge) {
            statusBadge.textContent = "Tạm dừng";
            statusBadge.className = "ai-tts-status-badge";
          }
          if (iconPause) iconPause.classList.add("hidden");
          if (iconPlay) iconPlay.classList.remove("hidden");
          if (btnPlayText) btnPlayText.textContent = "Tiếp tục";
          playBtn.classList.remove("playing");
        };

        audio.onended = function () {
          currentAudio = null;
          if (statusBadge) {
            statusBadge.textContent = "Đã phát xong";
            statusBadge.className = "ai-tts-status-badge status-done";
          }
          if (iconPause) iconPause.classList.add("hidden");
          if (iconPlay) iconPlay.classList.remove("hidden");
          if (btnPlayText) btnPlayText.textContent = "Nghe lại";
          playBtn.classList.remove("playing");
          if (typeof scrollCallback === "function") scrollCallback();
        };

        audio.onerror = function (err) {
          console.warn("[AudioPlayer] Lỗi phát âm thanh:", err);
          if (statusBadge) {
            statusBadge.textContent = "Lỗi phát âm thanh";
            statusBadge.className = "ai-tts-status-badge status-error";
          }
          if (iconPause) iconPause.classList.add("hidden");
          if (iconPlay) iconPlay.classList.remove("hidden");
          if (btnPlayText) btnPlayText.textContent = "Thử lại";
          playBtn.classList.remove("playing");
        };

        const playPromise = audio.play();
        if (playPromise && typeof playPromise.catch === "function") {
          playPromise.catch(function (autoplayErr) {
            console.log("[AudioPlayer] Tự động phát chờ thao tác người dùng:", autoplayErr);
            if (statusBadge) {
              statusBadge.textContent = "Bấm để nghe";
              statusBadge.className = "ai-tts-status-badge";
            }
            if (iconPause) iconPause.classList.add("hidden");
            if (iconPlay) iconPlay.classList.remove("hidden");
            if (btnPlayText) btnPlayText.textContent = "Phát âm thanh";
            playBtn.classList.remove("playing");
          });
        }
      } catch (err) {
        console.error("[AudioPlayer] Lỗi tạo âm thanh:", err);
        if (statusBadge) {
          statusBadge.textContent = "Lỗi tạo âm thanh";
          statusBadge.className = "ai-tts-status-badge status-error";
        }
        playBtn.disabled = false;
        if (iconPause) iconPause.classList.add("hidden");
        if (iconPlay) iconPlay.classList.remove("hidden");
        if (btnPlayText) btnPlayText.textContent = "Thử lại";
        playBtn.classList.remove("playing");
      }
    }

    playBtn.addEventListener("click", function (e) {
      e.preventDefault();
      if (btnPlayText && btnPlayText.textContent === "Thử lại") {
        loadAudio();
        return;
      }
      if (!audio) return;
      if (!audio.paused) {
        audio.pause();
      } else {
        stopSpeaking();
        currentAudio = audio;
        audio.play().catch(function (err) {
          console.warn("[AudioPlayer] Lỗi phát lại:", err);
        });
      }
    });

    loadAudio();
  }

  async function startChat(customPrompt) {
    const promptInput = document.getElementById('promptInput') || document.getElementById('userPromptInput');
    const submitBtn = document.getElementById('submitBtn');
    const chatHistory = document.getElementById('chatHistory');
    const viewport = document.getElementById('chatAppViewport') || document.querySelector('.chat-app-viewport');
    const heroHeader = document.getElementById('chatHeroHeader');
    const suggestionsGroup = document.getElementById('chatSuggestionsGroup');
    const hiddenSelectedModel = document.getElementById('hiddenSelectedModel') || document.getElementById('modelSelector');
    const selectedModel = hiddenSelectedModel ? (hiddenSelectedModel.value || 'gemini') : 'gemini';

    let rawPrompt = (customPrompt !== undefined && customPrompt !== null && typeof customPrompt === 'string'
      ? customPrompt
      : (promptInput ? promptInput.value : '')).trim();

    // Handle File Attachment if present
    let prompt = rawPrompt;
    if (window.CowAiFileAttachment && window.CowAiFileAttachment.getAttachment()) {
      prompt = window.CowAiFileAttachment.formatPromptWithAttachment(rawPrompt);
      window.CowAiFileAttachment.clearAttachment();
    }

    // Stop active Voice recognition
    if (window.CowAiVoiceInput) {
      window.CowAiVoiceInput.stop();
    }

    // Ignore empty or whitespace-only prompts or if streaming is already active
    if (!prompt || isGenerating) return;

    isGenerating = true;

    // Dừng audio TTS đang phát khi bắt đầu lượt chat mới
    stopSpeaking();

    // 1. Smooth 2-state transition on first send
    if (viewport) viewport.classList.add('bottom-mode');
    if (document.body) document.body.classList.add('chat-active');
    if (chatHistory) chatHistory.classList.add('active');
    if (heroHeader) {
      heroHeader.classList.add('collapsed');
      heroHeader.classList.add('hidden');
    }
    if (suggestionsGroup) {
      suggestionsGroup.classList.add('collapsed');
      suggestionsGroup.classList.add('hidden');
    }

    // 2. Clear input, reset height, and disable input + button
    if (promptInput) {
      promptInput.value = '';
      promptInput.style.height = 'auto';
      promptInput.disabled = true;
    }
    if (submitBtn) {
      submitBtn.disabled = true;
    }

    function scrollToBottom() {
      if (chatHistory) {
        chatHistory.scrollTop = chatHistory.scrollHeight;
      }
    }

    // 3. Append User Message Bubble to #chatHistory (safe textContent prevents XSS)
    if (chatHistory) {
      const userMsgRow = document.createElement('div');
      userMsgRow.className = 'message user chat-message chat-message--user';

      const userBubble = document.createElement('div');
      userBubble.className = 'message-bubble chat-bubble chat-bubble-user';
      userBubble.textContent = rawPrompt || prompt;

      userMsgRow.appendChild(userBubble);
      chatHistory.appendChild(userMsgRow);
      scrollToBottom();

      if (window.CowAiHistoryStore) {
        window.CowAiHistoryStore.addMessage('user', prompt, selectedModel);
        if (typeof window.renderSessionList === 'function') {
          window.renderSessionList();
        }
      }
    }

    // 4. Append AI Message Bubble to #chatHistory with typing indicator
    let aiBubble = null;
    if (chatHistory) {
      const aiMsgRow = document.createElement('div');
      aiMsgRow.className = 'message ai chat-message chat-message--ai';

      aiBubble = document.createElement('div');
      aiBubble.className = 'message-bubble chat-bubble chat-bubble-ai';
      aiBubble.innerHTML = '<div class="ai-status-loading ai-thinking-indicator"><span class="ai-thinking-dot"></span><span class="ai-thinking-dot"></span><span class="ai-thinking-dot"></span> <span class="ai-status-text">Đang suy nghĩ...</span></div>';

      aiMsgRow.appendChild(aiBubble);
      chatHistory.appendChild(aiMsgRow);
      scrollToBottom();
    } else {
      // Legacy fallback container if chatHistory is absent
      const chatBox = document.getElementById('chatBox');
      const responseContainer = document.getElementById('aiResponseContainer');
      const chatBoxCard = document.getElementById('chatBoxCard');
      if (responseContainer) responseContainer.classList.remove('hidden');
      if (chatBoxCard) chatBoxCard.classList.remove('hidden');
      if (chatBox) {
        chatBox.innerHTML = '<span class="ai-status-loading">Đang suy nghĩ...</span>';
        aiBubble = chatBox;
      }
    }

    // 5. Xác định model đã chọn (Edge TTS hoặc model AI)

    // 5.1. Edge TTS: phát trực tiếp file MP3 trả về kèm nút Play/Pause và Tải xuống MP3
    if (selectedModel === 'edge-male' || selectedModel === 'edge-female') {
      const voiceName = selectedModel === 'edge-male' ? 'vi-VN-NamMinhNeural' : 'vi-VN-HoaiMyNeural';
      if (document.body) {
        document.body.setAttribute('data-tts-voice', voiceName);
      }

      if (typeof Audio === 'undefined') {
        if (aiBubble) {
          aiBubble.innerHTML = '<div class="ai-alert-error">⚠️ Trình duyệt không hỗ trợ phát âm thanh.</div>';
        }
        if (promptInput) {
          promptInput.disabled = false;
          promptInput.focus();
        }
        if (submitBtn) {
          submitBtn.disabled = false;
        }
        isGenerating = false;
        return;
      }

      const audioUrl = '/Home/StreamChat?prompt=' + encodeURIComponent(prompt) +
        '&model=' + encodeURIComponent(selectedModel);

      if (aiBubble) {
        renderAudioPlayer(aiBubble, audioUrl, prompt, voiceName, scrollToBottom);
      }

      if (window.CowAiHistoryStore) {
        window.CowAiHistoryStore.addMessage('assistant', '[Bản ghi âm giọng đọc: "' + prompt + '"]', selectedModel);
        if (typeof window.renderSessionList === 'function') {
          window.renderSessionList();
        }
      }

      if (promptInput) {
        promptInput.disabled = false;
        promptInput.focus();
      }
      if (submitBtn) {
        submitBtn.disabled = false;
      }
      isGenerating = false;
      return;
    }

    // 6. POST /Home/StreamChat with AbortController
    currentAbortController = new AbortController();

    let accumulatedMarkdown = '';
    let isFirstToken = true;

    function handleSSELine(line) {
      line = line.trim();
      if (!line || !line.startsWith('data:')) return false;

      const payload = line.startsWith('data: ') ? line.slice(6).trim() : line.slice(5).trim();
      if (payload === '[DONE]') {
        return true;
      }

      try {
        const json = JSON.parse(payload);

        if (json.error) {
          const errMsg = typeof json.error === 'string'
            ? json.error
            : (json.error.message || JSON.stringify(json.error));
          const safeMsg = sanitizeErrorMessage(errMsg);
          if (aiBubble) {
            aiBubble.innerHTML = `<div class="ai-alert-error">⚠️ ${escapeHtml(safeMsg)}</div>`;
            scrollToBottom();
          }
          return true;
        }

        if (json.is_image_url || json.image_url) {
          const imgUrl = json.is_image_url || json.image_url;
          if (aiBubble) {
            renderImageInAiBubble(aiBubble, imgUrl, json.prompt || prompt, scrollToBottom);
          }
          return true;
        }

        const deltaContent = json.choices?.[0]?.delta?.content ?? json.choices?.[0]?.text;
        if (deltaContent !== undefined && deltaContent !== null) {
          if (typeof deltaContent === 'string' && (deltaContent.trim().startsWith('[ERROR]') || TECHNICAL_ERROR_PATTERNS.some(function (p) { return p.test(deltaContent); }))) {
            const errMsg = deltaContent.trim().replace(/^\[ERROR\]\s*/, '');
            const safeMsg = sanitizeErrorMessage(errMsg);
            if (aiBubble) {
              aiBubble.innerHTML = `<div class="ai-alert-error">⚠️ ${escapeHtml(safeMsg)}</div>`;
              scrollToBottom();
            }
            return true;
          }
          if (isFirstToken) {
            if (aiBubble) aiBubble.innerHTML = '';
            isFirstToken = false;
          }
          accumulatedMarkdown += deltaContent;
          if (aiBubble) {
            renderMarkdown(aiBubble, accumulatedMarkdown, true);
            scrollToBottom();
          }
        }
      } catch {
        if (payload.startsWith('[ERROR]')) {
          const errMsg = payload.replace(/^\[ERROR\]\s*/, '');
          const safeMsg = sanitizeErrorMessage(errMsg);
          if (aiBubble) {
            aiBubble.innerHTML = `<div class="ai-alert-error">⚠️ ${escapeHtml(safeMsg)}</div>`;
            scrollToBottom();
          }
          return true;
        } else if (payload) {
          if (isFirstToken) {
            if (aiBubble) aiBubble.innerHTML = '';
            isFirstToken = false;
          }
          accumulatedMarkdown += payload;
          if (aiBubble) {
            renderMarkdown(aiBubble, accumulatedMarkdown);
            scrollToBottom();
          }
        }
      }
      return false;
    }

    try {
      const hiddenTemp = document.getElementById('hiddenTemperature');
      const hiddenTokens = document.getElementById('hiddenMaxTokens');
      let bodyData = `prompt=${encodeURIComponent(prompt)}&model=${encodeURIComponent(selectedModel || '')}`;
      if (hiddenTemp && hiddenTemp.value) bodyData += `&temperature=${encodeURIComponent(hiddenTemp.value)}`;
      if (hiddenTokens && hiddenTokens.value) bodyData += `&maxTokens=${encodeURIComponent(hiddenTokens.value)}`;

      const response = await fetch('/Home/StreamChat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: bodyData,
        signal: currentAbortController.signal
      });

      if (!response.ok) {
        let msg = 'Hệ thống AI hiện đang bận. Vui lòng thử lại sau ít phút.';
        try {
          const errorText = await response.text();
          let parsedMsg = null;
          try {
            const errJson = JSON.parse(errorText);
            if (errJson && errJson.error) {
              parsedMsg = typeof errJson.error === 'string' ? errJson.error : (errJson.error.message || null);
            }
          } catch {
            if (errorText && errorText.trim().length < 200 && !errorText.includes('<')) {
              parsedMsg = errorText.trim();
            }
          }
          if (parsedMsg) {
            msg = sanitizeErrorMessage(parsedMsg);
          }
        } catch {
          // Fallback to friendly message
        }
        throw new Error(msg);
      }

      const reader = response.body.getReader();
      const decoder = new TextDecoder('utf-8');
      let buffer = '';

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split('\n');
        buffer = lines.pop(); // keep incomplete tail line in buffer

        let streamFinished = false;
        for (const line of lines) {
          if (handleSSELine(line)) {
            streamFinished = true;
            break;
          }
        }

        if (streamFinished) {
          break;
        }
      }

      if (buffer && buffer.trim()) {
        handleSSELine(buffer.trim());
      }
    } catch (err) {
      if (err.name === 'AbortError') {
        return;
      }
      if (aiBubble) {
        const safeMsg = sanitizeErrorMessage(err.message || 'Lỗi kết nối máy chủ AI.');
        aiBubble.innerHTML = `<div class="ai-alert-error">⚠️ ${escapeHtml(safeMsg)}</div>`;
        scrollToBottom();
      }
    } finally {
      if (promptInput) {
        promptInput.disabled = false;
        promptInput.focus();
      }
      if (submitBtn) {
        submitBtn.disabled = false;
      }
      if (aiBubble && accumulatedMarkdown.trim() && !String(aiBubble.innerHTML || '').includes('ai-alert-error')) {
        renderMarkdown(aiBubble, accumulatedMarkdown, false);
        attachSpeakButton(aiBubble);
        if (window.CowAiHistoryStore) {
          window.CowAiHistoryStore.addMessage('assistant', accumulatedMarkdown, selectedModel);
          if (typeof window.renderSessionList === 'function') {
            window.renderSessionList();
          }
        }
      }
      isGenerating = false;
      currentAbortController = null;
    }
  }

  async function generateImage(customPrompt) {
    const promptInput = document.getElementById('promptInput') || document.getElementById('userPromptInput');
    const prompt = (customPrompt || (promptInput ? promptInput.value : '')).trim();
    const imageResult = document.getElementById('imageResult');
    const responseContainer = document.getElementById('aiResponseContainer');
    const chatBoxCard = document.getElementById('chatBoxCard');
    const imageBoxCard = document.getElementById('imageBoxCard');
    const submitBtn = document.getElementById('submitBtn');
    const downloadBtn = document.getElementById('downloadImageResultBtn');

    if (!prompt) return;

    if (responseContainer) {
      responseContainer.classList.remove('hidden');
      responseContainer.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
    if (imageBoxCard) imageBoxCard.classList.remove('hidden');
    if (chatBoxCard) chatBoxCard.classList.add('hidden');

    if (imageResult) {
      imageResult.classList.add('hidden');
    }
    const wrapper = document.querySelector('.ai-image-wrapper');
    if (wrapper) {
      let loader = wrapper.querySelector('.ai-image-loading');
      if (!loader) {
        loader = document.createElement('div');
        loader.className = 'ai-image-loading text-center';
        loader.innerHTML = '<div class="ai-spinner"></div><p style="margin-top:0.5rem;font-size:0.875rem;color:var(--cow-text-muted);">Đang kết xuất hình ảnh từ Pollinations AI...</p>';
        wrapper.appendChild(loader);
      }
      loader.classList.remove('hidden');
    }

    if (submitBtn) submitBtn.disabled = true;

    try {
      const response = await fetch('/Home/GenerateImage', {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: `prompt=${encodeURIComponent(prompt)}`
      });

      if (!response.ok) {
        let errorMsg = 'Hệ thống tạo ảnh hiện đang bận. Vui lòng thử lại sau ít phút.';
        try {
          const errData = await response.json();
          if (errData && errData.error) {
            errorMsg = typeof errData.error === 'string' ? errData.error : (errData.error.message || errorMsg);
          }
        } catch {
        }
        throw new Error(errorMsg);
      }

      const data = await response.json();
      if (data && data.error) {
        throw new Error(typeof data.error === 'string' ? data.error : (data.error.message || 'Lỗi khi tạo ảnh.'));
      }
      if (data && data.url && imageResult) {
        imageResult.onload = function () {
          const loader = wrapper?.querySelector('.ai-image-loading');
          if (loader) loader.classList.add('hidden');
          imageResult.classList.remove('hidden');
          if (downloadBtn) downloadBtn.href = data.url;
        };
        imageResult.onerror = function () {
          throw new Error('Không thể tải ảnh kết quả.');
        };
        imageResult.src = data.url;
      }
    } catch (err) {
      const loader = wrapper?.querySelector('.ai-image-loading');
      if (loader) {
        const safeMsg = sanitizeErrorMessage(err.message || 'Lỗi khi tạo ảnh.');
        loader.innerHTML = `<span class="ai-alert-error">⚠️ ${escapeHtml(safeMsg)}</span>`;
      }
    } finally {
      if (submitBtn) submitBtn.disabled = false;
    }
  }

  // Export globally for Mục 11.6
  window.startChat = startChat;
  window.generateImage = generateImage;

  function initPromptBar() {
    const promptInput = document.getElementById('promptInput') || document.getElementById('userPromptInput');
    const promptForm = document.getElementById('chatPromptForm');
    const submitBtn = document.getElementById('submitBtn');
    const exampleButtons = document.querySelectorAll('.example-prompt-btn');
    const copyChatBtn = document.getElementById('copyChatBtn');
    const clearChatBtn = document.getElementById('clearChatBtn');
    const clearImageBtn = document.getElementById('clearImageBtn');

    if (promptInput) {
      promptInput.addEventListener('input', function () {
        this.style.height = 'auto';
        this.style.height = Math.min(this.scrollHeight, 180) + 'px';
      });

      promptInput.addEventListener('keydown', function (event) {
        if (event.key === 'Enter' && !event.shiftKey) {
          event.preventDefault();
          if (this.value.trim().length > 0) {
            startChat();
          }
        }
      });
    }

    if (promptForm) {
      promptForm.addEventListener('submit', function (e) {
        e.preventDefault();
        startChat();
      });
    }

    if (submitBtn) {
      submitBtn.addEventListener('click', function (e) {
        e.preventDefault();
        startChat();
      });
    }

    if (exampleButtons.length > 0 && promptInput) {
      exampleButtons.forEach(function (button) {
        button.addEventListener('click', function () {
          const promptText = button.getAttribute('data-prompt') || (button.dataset ? button.dataset.prompt : null);
          if (promptText) {
            promptInput.value = promptText;
            promptInput.style.height = 'auto';
            promptInput.style.height = Math.min(promptInput.scrollHeight, 180) + 'px';
            promptInput.focus();

            if (promptText.startsWith('🎨') || promptText.startsWith('Vẽ ') || promptText.includes('Tạo ảnh')) {
              if (typeof window.selectModelByValue === 'function') {
                window.selectModelByValue('pollinations');
              } else {
                const hiddenModel = document.getElementById('hiddenSelectedModel');
                const currentModelLabel = document.getElementById('currentModelLabel');
                if (hiddenModel) hiddenModel.value = 'pollinations';
                if (currentModelLabel) currentModelLabel.textContent = 'Pollinations AI';
              }
            }

            startChat();
          }
        });
      });
    }

    if (copyChatBtn) {
      copyChatBtn.addEventListener('click', function () {
        const chatBox = document.getElementById('chatBox');
        if (chatBox) {
          navigator.clipboard.writeText(chatBox.innerText).then(() => {
            const span = copyChatBtn.querySelector('span');
            if (span) {
              const old = span.textContent;
              span.textContent = 'Đã sao chép! ✓';
              setTimeout(() => { span.textContent = old; }, 2000);
            }
          });
        }
      });
    }

    if (clearChatBtn) {
      clearChatBtn.addEventListener('click', function () {
        const card = document.getElementById('chatBoxCard');
        if (card) card.classList.add('hidden');
      });
    }

    if (clearImageBtn) {
      clearImageBtn.addEventListener('click', function () {
        const card = document.getElementById('imageBoxCard');
        if (card) card.classList.add('hidden');
      });
    }
  }

  /* ==========================================================================
     1.1 AI Model Selector Dropdown (Tham chiếu Sakana.ai - Mục 10 Model.md)
     ========================================================================== */
  function initModelSelector() {
    const trigger = document.getElementById('modelTrigger');
    const menu = document.getElementById('modelMenu');
    const currentModelLabel = document.getElementById('currentModelLabel');
    if (!trigger || !menu || !currentModelLabel) {
      return;
    }

    const options = Array.from(menu.querySelectorAll('.model-option'));
    const tempBtn = document.getElementById('tempSettingBtn');
    const currentTempVal = document.getElementById('currentTempValue');
    const tokensBtn = document.getElementById('tokensSettingBtn');
    const currentTokensVal = document.getElementById('currentTokensValue');
    const hiddenInput = document.getElementById('hiddenSelectedModel');

    const temps = ['0.7', '0.2', '1.0'];
    let tempIndex = 0;
    const tokens = ['2048', '4096', '1024'];
    let tokenIndex = 0;

    function updateMenuPosition() {
      const viewport = document.getElementById('chatAppViewport') || document.querySelector('.chat-app-viewport');
      const isBottomMode = viewport ? viewport.classList.contains('bottom-mode') : false;
      if (isBottomMode) {
        menu.classList.add('pop-up');
        menu.classList.remove('pop-down');
        return;
      }

      const rect = trigger.getBoundingClientRect();
      const spaceBelow = window.innerHeight - rect.bottom;
      if (spaceBelow < 280 && rect.top > spaceBelow) {
        menu.classList.add('pop-up');
        menu.classList.remove('pop-down');
      } else {
        menu.classList.add('pop-down');
        menu.classList.remove('pop-up');
      }
    }

    trigger.addEventListener('click', function (e) {
      e.stopPropagation();
      const isExpanded = trigger.getAttribute('aria-expanded') === 'true';
      if (!isExpanded) {
        updateMenuPosition();
      }
      trigger.setAttribute('aria-expanded', String(!isExpanded));
      menu.classList.toggle('active');
    });

    document.addEventListener('click', function (e) {
      if (!menu.contains(e.target) && !trigger.contains(e.target)) {
        trigger.setAttribute('aria-expanded', 'false');
        menu.classList.remove('active');
      }
    });

    document.addEventListener('focusin', function (e) {
      if (!menu.contains(e.target) && !trigger.contains(e.target) && menu.classList.contains('active')) {
        trigger.setAttribute('aria-expanded', 'false');
        menu.classList.remove('active');
      }
    });

    function selectModelByValue(val) {
      if (!val) return;
      options.forEach(function (opt) {
        const isMatch = (opt.getAttribute('data-value') || '').toLowerCase() === val.toLowerCase();
        opt.classList.toggle('selected', isMatch);
        opt.setAttribute('aria-selected', String(isMatch));
        if (isMatch) {
          const nameElem = opt.querySelector('.model-option__name');
          if (nameElem && currentModelLabel) {
            currentModelLabel.textContent = nameElem.textContent.trim();
          }
        }
      });
      if (hiddenInput) {
        hiddenInput.value = val;
      }

      const promptInput = document.getElementById('promptInput');
      const isEdgeTts = val === 'edge-male' || val === 'edge-female';
      if (promptInput) {
        promptInput.placeholder = isEdgeTts
          ? 'Nhập văn bản để chuyển sang giọng nói...'
          : 'Hỏi bất kỳ điều gì hoặc nhập lệnh cho COW.AI...';
      }

      const ttsBanner = document.getElementById('ttsModeNotice');
      if (ttsBanner) {
        if (isEdgeTts) {
          ttsBanner.classList.remove('hidden');
        } else {
          ttsBanner.classList.add('hidden');
        }
      }

      if (isEdgeTts && document.body) {
        document.body.setAttribute(
          'data-tts-voice',
          val === 'edge-male' ? 'vi-VN-NamMinhNeural' : 'vi-VN-HoaiMyNeural'
        );
      }
    }

    const btnExitTts = document.getElementById('btnExitTtsMode');
    if (btnExitTts) {
      btnExitTts.addEventListener('click', function (e) {
        e.preventDefault();
        selectModelByValue('gemini');
      });
    }

    window.selectModelByValue = selectModelByValue;

    options.forEach(function (option) {
      option.addEventListener('click', function (e) {
        e.stopPropagation();
        const val = option.getAttribute('data-value') || 'gemini';
        selectModelByValue(val);
        trigger.setAttribute('aria-expanded', 'false');
        menu.classList.remove('active');
        trigger.focus();
      });
    });

    if (tempBtn && currentTempVal) {
      tempBtn.addEventListener('click', function (e) {
        e.stopPropagation();
        tempIndex = (tempIndex + 1) % temps.length;
        currentTempVal.innerHTML = temps[tempIndex] + ' <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 18l6-6-6-6"/></svg>';
        const hiddenTemp = document.getElementById('hiddenTemperature');
        if (hiddenTemp) hiddenTemp.value = temps[tempIndex];
      });
    }

    if (tokensBtn && currentTokensVal) {
      tokensBtn.addEventListener('click', function (e) {
        e.stopPropagation();
        tokenIndex = (tokenIndex + 1) % tokens.length;
        currentTokensVal.innerHTML = tokens[tokenIndex] + ' <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 18l6-6-6-6"/></svg>';
        const hiddenTokens = document.getElementById('hiddenMaxTokens');
        if (hiddenTokens) hiddenTokens.value = tokens[tokenIndex];
      });
    }

    trigger.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowDown' || e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        updateMenuPosition();
        trigger.setAttribute('aria-expanded', 'true');
        menu.classList.add('active');
        const selected = menu.querySelector('.model-option.selected') || options[0];
        if (selected) selected.focus();
      } else if (e.key === 'Escape') {
        trigger.setAttribute('aria-expanded', 'false');
        menu.classList.remove('active');
      }
    });

    menu.addEventListener('keydown', function (e) {
      const activeOption = document.activeElement;
      const currentIndex = options.indexOf(activeOption);

      if (e.key === 'Escape') {
        e.preventDefault();
        trigger.setAttribute('aria-expanded', 'false');
        menu.classList.remove('active');
        trigger.focus();
      } else if (e.key === 'ArrowDown') {
        e.preventDefault();
        const nextIndex = (currentIndex + 1) % options.length;
        options[nextIndex].focus();
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        const prevIndex = (currentIndex - 1 + options.length) % options.length;
        options[prevIndex].focus();
      } else if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        if (currentIndex !== -1) {
          options[currentIndex].click();
        }
      }
    });
  }

  /* ==========================================================================
     2. Image Generation (Mục 5.3 & Kết nối Backend Pollinations)
     ========================================================================== */
  function initImageGen() {
    const form = document.getElementById('imageGenForm');
    const promptInput = document.getElementById('prompt-input') || document.getElementById('imagePromptInput');
    const submitBtn = document.getElementById('imageSubmitBtn');
    const btnSpinner = document.getElementById('imageBtnSpinner');
    const btnText = document.getElementById('imageBtnText');
    const quotaBadge = document.getElementById('imageQuotaBadge');
    const statusTag = document.getElementById('imageStatusTag');
    const previewContainer = document.getElementById('imagePreviewContainer');
    const emptyState = document.getElementById('imageEmptyState');
    const loadingState = document.getElementById('imageLoadingState');
    const successState = document.getElementById('imageSuccessState');
    const resultImg = document.getElementById('imageResultImg');
    const actionsBar = document.getElementById('imageActionsBar');
    const downloadBtn = document.getElementById('downloadImageBtn');
    const regenerateBtn = document.getElementById('regenerateImageBtn');
    const ratioChips = document.querySelectorAll('#ratioChips .ai-chip');
    const styleChips = document.querySelectorAll('#styleChips .ai-chip');

    if (!form || !promptInput) {
      return;
    }

    let remainingQuota = 5;

    function setupChips(chips) {
      chips.forEach(function (chip) {
        chip.addEventListener('click', function () {
          chips.forEach(function (c) { c.classList.remove('active'); });
          chip.classList.add('active');
        });
      });
    }

    setupChips(ratioChips);
    setupChips(styleChips);

    async function generateImageDirect() {
      const promptValue = promptInput.value.trim();
      if (!promptValue) {
        promptInput.focus();
        return;
      }

      if (remainingQuota <= 0) {
        if (quotaBadge) {
          quotaBadge.textContent = '⚠️ Hết lượt hôm nay';
        }
        return;
      }

      remainingQuota--;
      if (quotaBadge) {
        quotaBadge.textContent = '⚡ Còn ' + remainingQuota + '/5 lượt hôm nay';
      }

      if (submitBtn) submitBtn.disabled = true;
      if (btnSpinner) btnSpinner.classList.remove('hidden');
      if (btnText) btnText.textContent = 'Đang tạo...';
      if (statusTag) statusTag.textContent = 'Đang xử lý';

      if (previewContainer) {
        previewContainer.className = 'image-preview-frame image-preview-frame--loading';
      }
      if (emptyState) emptyState.classList.add('hidden');
      if (successState) successState.classList.add('hidden');
      if (resultImg) resultImg.classList.add('hidden');
      if (actionsBar) actionsBar.classList.add('hidden');
      if (loadingState) loadingState.classList.remove('hidden');

      try {
        const activeStyle = document.querySelector('#styleChips .ai-chip.active')?.getAttribute('data-style') || '';
        const activeRatio = document.querySelector('#ratioChips .ai-chip.active')?.getAttribute('data-ratio') || '1:1';
        let combinedPrompt = promptValue;
        if (activeStyle && activeStyle !== 'photorealistic') {
          combinedPrompt += `, ${activeStyle} style`;
        }
        if (activeRatio && activeRatio !== '1:1') {
          combinedPrompt += ` --ar ${activeRatio}`;
        }

        const response = await fetch('/Image/GenerateImage', {
          method: 'POST',
          headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
          body: `prompt=${encodeURIComponent(combinedPrompt)}`
        });

        if (!response.ok) {
          let errorMsg = 'Hệ thống tạo ảnh hiện đang bận. Vui lòng thử lại sau ít phút.';
          try {
            const errData = await response.json();
            if (errData && errData.error) {
              errorMsg = typeof errData.error === 'string' ? errData.error : (errData.error.message || errorMsg);
            }
          } catch {
          }
          throw new Error(errorMsg);
        }

        const data = await response.json();
        if (data && data.error) {
          throw new Error(typeof data.error === 'string' ? data.error : (data.error.message || 'Lỗi khi tạo ảnh.'));
        }
        if (data && data.url) {
          const preloader = new Image();
          preloader.onload = function () {
            if (loadingState) loadingState.classList.add('hidden');
            if (successState) successState.classList.remove('hidden');
            if (resultImg) {
              resultImg.src = data.url;
              resultImg.classList.remove('hidden');
            }
            if (previewContainer) {
              previewContainer.className = 'image-preview-frame image-preview-frame--ready';
            }
            if (actionsBar) actionsBar.classList.remove('hidden');
            if (statusTag) statusTag.textContent = 'Hoàn thành';
            if (submitBtn) submitBtn.disabled = false;
            if (btnSpinner) btnSpinner.classList.add('hidden');
            if (btnText) btnText.textContent = 'Tạo ảnh ngay';
          };
          preloader.onerror = function () {
            throw new Error('Lỗi khi tải ảnh tạo bởi Pollinations.');
          };
          preloader.src = data.url;
        }
      } catch (err) {
        if (loadingState) loadingState.classList.add('hidden');
        if (successState) successState.classList.add('hidden');
        if (emptyState) {
          const safeMsg = sanitizeErrorMessage(err.message || 'Lỗi khi tạo ảnh.');
          emptyState.innerHTML = `<span class="ai-alert-error">⚠️ ${escapeHtml(safeMsg)}</span>`;
          emptyState.classList.remove('hidden');
        }
        if (statusTag) statusTag.textContent = 'Thất bại';
        if (submitBtn) submitBtn.disabled = false;
        if (btnSpinner) btnSpinner.classList.add('hidden');
        if (btnText) btnText.textContent = 'Thử lại';
      }
    }

    form.addEventListener('submit', function (event) {
      event.preventDefault();
      generateImageDirect();
    });

    if (regenerateBtn) {
      regenerateBtn.addEventListener('click', function () {
        generateImageDirect();
      });
    }

    if (downloadBtn && resultImg) {
      downloadBtn.addEventListener('click', function () {
        window.open(resultImg.src, '_blank');
      });
    }
  }

  /* ==========================================================================
     3. Document Summary (Mục 5.4a & Kết nối Backend OpenRouter)
     ========================================================================== */
  function initDocSummary() {
    const tabTextBtn = document.getElementById('tabTextBtn');
    const tabFileBtn = document.getElementById('tabFileBtn');
    const textContainer = document.getElementById('textInputContainer');
    const fileContainer = document.getElementById('fileInputContainer');
    const summaryInput = document.getElementById('summaryInput');
    const charCount = document.getElementById('charCount');
    const dropzone = document.getElementById('summaryDropzone');
    const fileInput = document.getElementById('summaryFileInput');
    const fileInfo = document.getElementById('fileSelectedInfo');
    const fileNameDisplay = document.getElementById('fileNameDisplay');
    const removeFileBtn = document.getElementById('removeFileBtn');
    const form = document.getElementById('summaryForm');
    const submitBtn = document.getElementById('summarySubmitBtn');
    const btnSpinner = document.getElementById('summaryBtnSpinner');
    const btnText = document.getElementById('summaryBtnText');
    const statusBadge = document.getElementById('summaryStatusBadge');
    const emptyState = document.getElementById('summaryEmptyState');
    const resultContent = document.getElementById('summaryResultContent');
    const actionsBar = document.getElementById('summaryActionsBar');
    const copyBtn = document.getElementById('copySummaryBtn');
    const exportBtn = document.getElementById('exportTxtBtn');
    const lengthChips = document.querySelectorAll('#lengthChips .ai-chip');

    if (!form) {
      return;
    }

    if (tabTextBtn && tabFileBtn) {
      tabTextBtn.addEventListener('click', function () {
        tabTextBtn.classList.add('active');
        tabFileBtn.classList.remove('active');
        if (textContainer) textContainer.classList.remove('hidden');
        if (fileContainer) fileContainer.classList.add('hidden');
      });

      tabFileBtn.addEventListener('click', function () {
        tabFileBtn.classList.add('active');
        tabTextBtn.classList.remove('active');
        if (fileContainer) fileContainer.classList.remove('hidden');
        if (textContainer) textContainer.classList.add('hidden');
      });
    }

    if (summaryInput && charCount) {
      summaryInput.addEventListener('input', function () {
        charCount.textContent = this.value.length + ' ký tự';
      });
    }

    if (dropzone && fileInput) {
      dropzone.addEventListener('click', function () {
        fileInput.click();
      });

      dropzone.addEventListener('dragover', function (e) {
        e.preventDefault();
        dropzone.classList.add('dragover');
      });

      dropzone.addEventListener('dragleave', function () {
        dropzone.classList.remove('dragover');
      });

      dropzone.addEventListener('drop', function (e) {
        e.preventDefault();
        dropzone.classList.remove('dragover');
        if (e.dataTransfer.files.length > 0) {
          handleFileSelected(e.dataTransfer.files[0]);
        }
      });

      fileInput.addEventListener('change', function () {
        if (this.files.length > 0) {
          handleFileSelected(this.files[0]);
        }
      });
    }

    function handleFileSelected(file) {
      if (fileNameDisplay && fileInfo && dropzone) {
        fileNameDisplay.textContent = file.name + ' (' + (file.size / 1024).toFixed(1) + ' KB)';
        fileInfo.classList.remove('hidden');
        dropzone.classList.add('hidden');

        // Đọc nội dung nếu là file text / md
        if (file.type.includes('text') || file.name.endsWith('.md') || file.name.endsWith('.txt')) {
          const reader = new FileReader();
          reader.onload = function (e) {
            if (summaryInput) {
              summaryInput.value = e.target.result;
              if (charCount) charCount.textContent = summaryInput.value.length + ' ký tự';
            }
          };
          reader.readAsText(file);
        }
      }
    }

    if (removeFileBtn) {
      removeFileBtn.addEventListener('click', function () {
        if (fileInput) fileInput.value = '';
        if (fileInfo) fileInfo.classList.add('hidden');
        if (dropzone) dropzone.classList.remove('hidden');
      });
    }

    lengthChips.forEach(function (chip) {
      chip.addEventListener('click', function () {
        lengthChips.forEach(function (c) { c.classList.remove('active'); });
        chip.classList.add('active');
      });
    });

    form.addEventListener('submit', async function (event) {
      event.preventDefault();
      const content = (summaryInput ? summaryInput.value : '').trim();
      if (!content) {
        if (summaryInput) summaryInput.focus();
        return;
      }

      if (submitBtn) submitBtn.disabled = true;
      if (btnSpinner) btnSpinner.classList.remove('hidden');
      if (btnText) btnText.textContent = 'Đang phân tích...';
      if (statusBadge) statusBadge.textContent = 'Đang trích xuất';

      if (emptyState) emptyState.classList.add('hidden');
      if (resultContent) {
        resultContent.innerHTML = '<span class="ai-status-loading">AI đang đọc hiểu và trích xuất luận điểm cốt lõi...</span>';
        resultContent.classList.remove('hidden');
      }

      const activeLength = document.querySelector('#lengthChips .ai-chip.active')?.getAttribute('data-length') || 'bullets';

      try {
        const response = await fetch('/Document/StreamSummary', {
          method: 'POST',
          headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
          body: `content=${encodeURIComponent(content)}&length=${encodeURIComponent(activeLength)}`
        });

        if (!response.ok) {
          let errorMsg = 'Hệ thống AI hiện đang bận. Vui lòng thử lại sau ít phút.';
          try {
            const errText = await response.text();
            try {
              const errJson = JSON.parse(errText);
              if (errJson && errJson.error) {
                errorMsg = typeof errJson.error === 'string' ? errJson.error : (errJson.error.message || errorMsg);
              }
            } catch {
              if (errText && errText.trim().length < 200 && !errText.includes('<')) {
                errorMsg = errText.trim();
              }
            }
          } catch {
          }
          throw new Error(errorMsg);
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder('utf-8');
        let fullText = '';
        let isFirst = true;
        let buffer = '';
        let hasError = false;

        function handleSummaryLine(line) {
          const trimmed = line.trim();
          if (!trimmed || !trimmed.startsWith('data:')) return false;

          const data = trimmed.startsWith('data: ') ? trimmed.slice(6).trim() : trimmed.slice(5).trim();
          if (data === '[DONE]') return true;

          try {
            const json = JSON.parse(data);
            if (json.error) {
              hasError = true;
              const safeMsg = sanitizeErrorMessage(json.error.message || json.error);
              resultContent.innerHTML = `<div class="ai-alert-error">⚠️ ${escapeHtml(safeMsg)}</div>`;
              if (statusBadge) statusBadge.textContent = 'Thất bại';
              if (actionsBar) actionsBar.classList.add('hidden');
              return true;
            }
            const token = json.choices?.[0]?.delta?.content ?? json.choices?.[0]?.text;
            if (token !== undefined && token !== null) {
              if (typeof token === 'string' && (token.trim().startsWith('[ERROR]') || TECHNICAL_ERROR_PATTERNS.some(function (p) { return p.test(token); }))) {
                hasError = true;
                const safeMsg = sanitizeErrorMessage(token.trim().replace(/^\[ERROR\]\s*/, ''));
                resultContent.innerHTML = `<div class="ai-alert-error">⚠️ ${escapeHtml(safeMsg)}</div>`;
                if (statusBadge) statusBadge.textContent = 'Thất bại';
                if (actionsBar) actionsBar.classList.add('hidden');
                return true;
              }
              if (isFirst) {
                resultContent.innerHTML = '';
                isFirst = false;
              }
              fullText += token;
              if (window.marked) {
                resultContent.innerHTML = window.marked.parse(fullText);
              } else {
                resultContent.textContent = fullText;
              }
            }
          } catch {
            if (data.startsWith('[ERROR]')) {
              hasError = true;
              const safeMsg = sanitizeErrorMessage(data.replace(/^\[ERROR\]\s*/, ''));
              resultContent.innerHTML = `<div class="ai-alert-error">⚠️ ${escapeHtml(safeMsg)}</div>`;
              if (statusBadge) statusBadge.textContent = 'Thất bại';
              if (actionsBar) actionsBar.classList.add('hidden');
              return true;
            } else if (data && !data.startsWith('{')) {
              if (isFirst) {
                resultContent.innerHTML = '';
                isFirst = false;
              }
              fullText += data;
              resultContent.textContent = fullText;
            }
          }
          return false;
        }

        while (true) {
          const { done, value } = await reader.read();
          if (done) break;

          buffer += decoder.decode(value, { stream: true });
          const lines = buffer.split('\n');
          buffer = lines.pop();

          let finished = false;
          for (const line of lines) {
            if (handleSummaryLine(line)) {
              finished = true;
              break;
            }
          }
          if (finished) break;
        }

        if (buffer && buffer.trim()) {
          handleSummaryLine(buffer.trim());
        }

        if (!hasError && fullText) {
          if (actionsBar) actionsBar.classList.remove('hidden');
          if (statusBadge) statusBadge.textContent = 'Hoàn thành';
        }
      } catch (err) {
        if (resultContent) {
          const safeMsg = sanitizeErrorMessage(err.message || 'Lỗi khi kết nối tóm tắt tài liệu.');
          resultContent.innerHTML = `<div class="ai-alert-error">⚠️ ${escapeHtml(safeMsg)}</div>`;
        }
        if (statusBadge) statusBadge.textContent = 'Thất bại';
        if (actionsBar) actionsBar.classList.add('hidden');
      } finally {
        if (submitBtn) submitBtn.disabled = false;
        if (btnSpinner) btnSpinner.classList.add('hidden');
        if (btnText) btnText.textContent = 'Bắt đầu tóm tắt';
      }
    });

    if (copyBtn && resultContent) {
      copyBtn.addEventListener('click', function () {
        const textToCopy = resultContent.innerText;
        navigator.clipboard.writeText(textToCopy).then(function () {
          const originalText = copyBtn.querySelector('span').textContent;
          copyBtn.querySelector('span').textContent = 'Đã sao chép! ✓';
          setTimeout(function () {
            copyBtn.querySelector('span').textContent = originalText;
          }, 2000);
        });
      });
    }

    if (exportBtn && resultContent) {
      exportBtn.addEventListener('click', function () {
        const blob = new Blob([resultContent.innerText], { type: 'text/plain;charset=utf-8' });
        const downloadUrl = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = downloadUrl;
        a.download = 'COW_AI_TomTat.txt';
        a.click();
        URL.revokeObjectURL(downloadUrl);
      });
    }
  }

  /* ==========================================================================
     4. Code Generation (Mục 5.4b & Kết nối Backend DeepSeek)
     ========================================================================== */
  function initCodeGen() {
    const form = document.getElementById('codeGenForm');
    const promptInput = document.getElementById('codePromptInput');
    const langSelect = document.getElementById('codeLanguageSelect');
    const langBadge = document.getElementById('codeLangBadge');
    const fileNameDisplay = document.getElementById('codeFileNameDisplay');
    const submitBtn = document.getElementById('codeSubmitBtn');
    const btnSpinner = document.getElementById('codeBtnSpinner');
    const btnText = document.getElementById('codeBtnText');
    const emptyState = document.getElementById('codeEmptyState');
    const codeBlockWrapper = document.getElementById('codeBlockWrapper');
    const codeOutputBlock = document.getElementById('codeOutputBlock');
    const explanationBox = document.getElementById('codeExplanationBox');
    const explanationText = document.getElementById('codeExplanationText');
    const copyCodeBtn = document.getElementById('copyCodeBtn');
    const standardChips = document.querySelectorAll('#codeStandardChips .ai-chip');

    if (!form) {
      return;
    }

    standardChips.forEach(function (chip) {
      chip.addEventListener('click', function () {
        chip.classList.toggle('active');
      });
    });

    const fileExtensionMap = {
      python: { ext: 'py', file: 'solution.py', badge: 'Python' },
      csharp: { ext: 'cs', file: 'Solution.cs', badge: 'C# 10' },
      javascript: { ext: 'js', file: 'solution.js', badge: 'JavaScript' },
      typescript: { ext: 'ts', file: 'solution.ts', badge: 'TypeScript' },
      sql: { ext: 'sql', file: 'query.sql', badge: 'SQL Query' }
    };

    if (langSelect) {
      langSelect.addEventListener('change', function () {
        const selected = fileExtensionMap[this.value] || fileExtensionMap.python;
        if (fileNameDisplay) fileNameDisplay.textContent = selected.file;
        if (langBadge) langBadge.textContent = selected.badge;
      });
    }

    form.addEventListener('submit', async function (event) {
      event.preventDefault();
      const promptValue = promptInput.value.trim();
      if (!promptValue) {
        promptInput.focus();
        return;
      }

      const selectedLangKey = langSelect ? langSelect.value : 'python';
      const standards = Array.from(document.querySelectorAll('#codeStandardChips .ai-chip.active'))
        .map(c => c.textContent.trim())
        .join(', ');

      if (submitBtn) submitBtn.disabled = true;
      if (btnSpinner) btnSpinner.classList.remove('hidden');
      if (btnText) btnText.textContent = 'Đang sinh mã nguồn...';

      if (emptyState) emptyState.classList.add('hidden');
      if (codeBlockWrapper) codeBlockWrapper.classList.remove('hidden');
      if (codeOutputBlock) {
        codeOutputBlock.className = 'language-' + (selectedLangKey === 'csharp' ? 'csharp' : selectedLangKey);
        codeOutputBlock.textContent = '// AI đang khởi tạo cấu trúc và phân tích thuật toán...';
      }

      try {
        const response = await fetch('/Document/StreamCodeGen', {
          method: 'POST',
          headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
          body: `prompt=${encodeURIComponent(promptValue)}&language=${encodeURIComponent(selectedLangKey)}&standards=${encodeURIComponent(standards)}`
        });

        if (!response.ok) {
          let errorMsg = 'Hệ thống AI hiện đang bận. Vui lòng thử lại sau ít phút.';
          try {
            const errText = await response.text();
            try {
              const errJson = JSON.parse(errText);
              if (errJson && errJson.error) {
                errorMsg = typeof errJson.error === 'string' ? errJson.error : (errJson.error.message || errorMsg);
              }
            } catch {
              if (errText && errText.trim().length < 200 && !errText.includes('<')) {
                errorMsg = errText.trim();
              }
            }
          } catch {
          }
          throw new Error(errorMsg);
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder('utf-8');
        let fullText = '';
        let isFirst = true;
        let buffer = '';
        let hasError = false;

        function handleCodeGenLine(line) {
          const trimmed = line.trim();
          if (!trimmed || !trimmed.startsWith('data:')) return false;

          const data = trimmed.startsWith('data: ') ? trimmed.slice(6).trim() : trimmed.slice(5).trim();
          if (data === '[DONE]') return true;

          try {
            const json = JSON.parse(data);
            if (json.error) {
              hasError = true;
              const safeMsg = sanitizeErrorMessage(json.error.message || json.error);
              codeOutputBlock.textContent = `// Lỗi: ${safeMsg}`;
              if (explanationBox) explanationBox.classList.add('hidden');
              return true;
            }
            const token = json.choices?.[0]?.delta?.content ?? json.choices?.[0]?.text;
            if (token !== undefined && token !== null) {
              if (typeof token === 'string' && (token.trim().startsWith('[ERROR]') || TECHNICAL_ERROR_PATTERNS.some(function (p) { return p.test(token); }))) {
                hasError = true;
                const safeMsg = sanitizeErrorMessage(token.trim().replace(/^\[ERROR\]\s*/, ''));
                codeOutputBlock.textContent = `// Lỗi: ${safeMsg}`;
                if (explanationBox) explanationBox.classList.add('hidden');
                return true;
              }
              if (isFirst) {
                codeOutputBlock.textContent = '';
                isFirst = false;
              }
              fullText += token;
              codeOutputBlock.textContent = fullText;
              if (window.Prism) {
                window.Prism.highlightElement(codeOutputBlock);
              }
            }
          } catch {
            if (data.startsWith('[ERROR]')) {
              hasError = true;
              const safeMsg = sanitizeErrorMessage(data.replace(/^\[ERROR\]\s*/, ''));
              codeOutputBlock.textContent = `// Lỗi: ${safeMsg}`;
              if (explanationBox) explanationBox.classList.add('hidden');
              return true;
            } else if (data && !data.startsWith('{')) {
              if (isFirst) {
                codeOutputBlock.textContent = '';
                isFirst = false;
              }
              fullText += data;
              codeOutputBlock.textContent = fullText;
            }
          }
          return false;
        }

        while (true) {
          const { done, value } = await reader.read();
          if (done) break;

          buffer += decoder.decode(value, { stream: true });
          const lines = buffer.split('\n');
          buffer = lines.pop();

          let finished = false;
          for (const line of lines) {
            if (handleCodeGenLine(line)) {
              finished = true;
              break;
            }
          }
          if (finished) break;
        }

        if (buffer && buffer.trim()) {
          handleCodeGenLine(buffer.trim());
        }

        if (!hasError && fullText) {
          if (explanationBox && explanationText) {
            explanationText.textContent = 'Mã nguồn đã được sinh và tối ưu hóa bởi mô hình DeepSeek R1 chuyên sâu tư duy logic và thuật toán.';
            explanationBox.classList.remove('hidden');
          }
        }
      } catch (err) {
        if (codeOutputBlock) {
          const safeMsg = sanitizeErrorMessage(err.message || 'Không thể sinh code.');
          codeOutputBlock.textContent = `// Lỗi kết nối: ${safeMsg}`;
        }
        if (explanationBox) {
          explanationBox.classList.add('hidden');
        }
      } finally {
        if (submitBtn) submitBtn.disabled = false;
        if (btnSpinner) btnSpinner.classList.add('hidden');
        if (btnText) btnText.textContent = 'Sinh mã nguồn ngay';
      }
    });

    if (copyCodeBtn && codeOutputBlock) {
      copyCodeBtn.addEventListener('click', function () {
        const text = codeOutputBlock.textContent;
        navigator.clipboard.writeText(text).then(function () {
          copyCodeBtn.classList.add('active');
          setTimeout(function () {
            copyCodeBtn.classList.remove('active');
          }, 1500);
        });
      });
    }
  }


  /* ==========================================================================
     1.2 Chat History Drawer & Session Restoration
     ========================================================================== */
  function resetChatUI() {
    const chatHistory = document.getElementById('chatHistory');
    const viewport = document.getElementById('chatAppViewport') || document.querySelector('.chat-app-viewport');
    const heroHeader = document.getElementById('chatHeroHeader');
    const suggestionsGroup = document.getElementById('chatSuggestionsGroup');

    if (chatHistory) {
      chatHistory.innerHTML = '';
      chatHistory.classList.remove('active');
    }
    if (viewport) {
      viewport.classList.remove('bottom-mode');
    }
    if (document.body) {
      document.body.classList.remove('chat-active');
    }
    if (heroHeader) {
      heroHeader.classList.remove('collapsed');
      heroHeader.classList.remove('hidden');
    }
    if (suggestionsGroup) {
      suggestionsGroup.classList.remove('collapsed');
      suggestionsGroup.classList.remove('hidden');
    }
  }

  function loadSessionIntoChat(sessionId) {
    if (!window.CowAiHistoryStore) return;
    const session = window.CowAiHistoryStore.getSessionById(sessionId);
    if (!session) return;

    window.CowAiHistoryStore.setActiveSessionId(sessionId);
    const chatHistory = document.getElementById('chatHistory');
    const viewport = document.getElementById('chatAppViewport') || document.querySelector('.chat-app-viewport');
    const heroHeader = document.getElementById('chatHeroHeader');
    const suggestionsGroup = document.getElementById('chatSuggestionsGroup');

    if (!chatHistory) return;

    chatHistory.innerHTML = '';

    if (session.messages && session.messages.length > 0) {
      if (viewport) viewport.classList.add('bottom-mode');
      if (document.body) document.body.classList.add('chat-active');
      chatHistory.classList.add('active');
      if (heroHeader) {
        heroHeader.classList.add('collapsed');
        heroHeader.classList.add('hidden');
      }
      if (suggestionsGroup) {
        suggestionsGroup.classList.add('collapsed');
        suggestionsGroup.classList.add('hidden');
      }

      session.messages.forEach(function (msg) {
        if (msg.role === 'user') {
          const userMsgRow = document.createElement('div');
          userMsgRow.className = 'message user chat-message chat-message--user';
          const userBubble = document.createElement('div');
          userBubble.className = 'message-bubble chat-bubble chat-bubble-user';
          userBubble.textContent = msg.content;
          userMsgRow.appendChild(userBubble);
          chatHistory.appendChild(userMsgRow);
        } else {
          const aiMsgRow = document.createElement('div');
          aiMsgRow.className = 'message ai chat-message chat-message--ai';
          const aiBubble = document.createElement('div');
          aiBubble.className = 'message-bubble chat-bubble chat-bubble-ai';
          renderMarkdown(aiBubble, msg.content, false);
          attachSpeakButton(aiBubble);
          aiMsgRow.appendChild(aiBubble);
          chatHistory.appendChild(aiMsgRow);
        }
      });
      chatHistory.scrollTop = chatHistory.scrollHeight;
    } else {
      resetChatUI();
    }
    renderSessionList();
  }

  function renderSessionList() {
    const container = document.getElementById('sessionListContainer');
    if (!container || !window.CowAiHistoryStore) return;

    const sessions = window.CowAiHistoryStore.getSessions();
    const activeId = window.CowAiHistoryStore.getActiveSessionId();

    if (!sessions || sessions.length === 0) {
      container.innerHTML = '<div class="drawer-empty-msg">Chưa có lịch sử trò chuyện nào.</div>';
      return;
    }

    container.innerHTML = '';
    sessions.forEach(function (sess) {
      const item = document.createElement('div');
      item.className = 'drawer-session-item' + (sess.id === activeId ? ' active' : '');
      item.setAttribute('data-id', sess.id);

      const textSpan = document.createElement('span');
      textSpan.className = 'drawer-session-item__text';
      textSpan.textContent = sess.title || 'Đoạn chat';
      textSpan.title = sess.title || 'Đoạn chat';

      const delBtn = document.createElement('button');
      delBtn.className = 'drawer-session-item__delete';
      delBtn.title = 'Xóa đoạn chat này';
      delBtn.setAttribute('aria-label', 'Xóa đoạn chat');
      delBtn.innerHTML = '✕';
      delBtn.addEventListener('click', function (e) {
        e.stopPropagation();
        window.CowAiHistoryStore.deleteSession(sess.id);
        if (sess.id === activeId) {
          resetChatUI();
        }
        renderSessionList();
      });

      item.appendChild(textSpan);
      item.appendChild(delBtn);

      item.addEventListener('click', function () {
        loadSessionIntoChat(sess.id);
        const drawer = document.getElementById('chatHistoryDrawer');
        const backdrop = document.getElementById('drawerBackdrop');
        if (drawer) drawer.classList.remove('active');
        if (backdrop) backdrop.classList.remove('active');
      });

      container.appendChild(item);
    });
  }
  window.renderSessionList = renderSessionList;

  function initHistoryDrawer() {
    const toggleBtn = document.getElementById('toggleHistoryDrawerBtn');
    const drawer = document.getElementById('chatHistoryDrawer');
    const backdrop = document.getElementById('drawerBackdrop');
    const closeBtn = document.getElementById('closeDrawerBtn');
    const btnNewChat = document.getElementById('btnNewChat');
    const btnExportChat = document.getElementById('btnExportChat');
    const btnClearAll = document.getElementById('btnClearAllChats');

    function openDrawer() {
      if (drawer) {
        drawer.classList.add('active');
        drawer.setAttribute('aria-hidden', 'false');
      }
      if (backdrop) {
        backdrop.classList.add('active');
        backdrop.setAttribute('aria-hidden', 'false');
      }
      renderSessionList();
    }

    function closeDrawer() {
      if (drawer) {
        drawer.classList.remove('active');
        drawer.setAttribute('aria-hidden', 'true');
      }
      if (backdrop) {
        backdrop.classList.remove('active');
        backdrop.setAttribute('aria-hidden', 'true');
      }
    }

    if (toggleBtn) toggleBtn.addEventListener('click', openDrawer);
    if (closeBtn) closeBtn.addEventListener('click', closeDrawer);
    if (backdrop) backdrop.addEventListener('click', closeDrawer);

    if (btnNewChat) {
      btnNewChat.addEventListener('click', function () {
        if (window.CowAiHistoryStore) {
          window.CowAiHistoryStore.createSession();
        }
        resetChatUI();
        closeDrawer();
      });
    }

    if (btnExportChat) {
      btnExportChat.addEventListener('click', function () {
        if (window.CowAiHistoryStore) {
          window.CowAiHistoryStore.exportSessionToMarkdown();
        }
      });
    }

    if (btnClearAll) {
      btnClearAll.addEventListener('click', function () {
        if (confirm('Bạn có chắc chắn muốn xóa toàn bộ lịch sử trò chuyện trên máy này?')) {
          if (window.CowAiHistoryStore) {
            window.CowAiHistoryStore.clearAllSessions();
          }
          resetChatUI();
          renderSessionList();
        }
      });
    }
  }

  /* ==========================================================================
     Global Initialization
     ========================================================================== */
  function initializeAllComponents() {
    initPromptBar();
    initModelSelector();
    initImageGen();
    initDocSummary();
    initCodeGen();
    initHistoryDrawer();
    if (window.CowAiVoiceInput && typeof window.CowAiVoiceInput.init === 'function') {
      window.CowAiVoiceInput.init();
    }
    if (window.CowAiFileAttachment && typeof window.CowAiFileAttachment.init === 'function') {
      window.CowAiFileAttachment.init();
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initializeAllComponents);
  } else {
    initializeAllComponents();
  }
})();
