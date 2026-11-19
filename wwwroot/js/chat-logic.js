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

  const COPY_ICON_SVG = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path></svg>';
  const CHECK_ICON_SVG = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="20 6 9 17 4 12"></polyline></svg>';

  function copyTextToClipboard(text) {
    const value = String(text || '');
    if (!value) {
      return Promise.reject(new Error('Không có nội dung để sao chép.'));
    }

    if (typeof navigator !== 'undefined' && navigator.clipboard && typeof navigator.clipboard.writeText === 'function') {
      return navigator.clipboard.writeText(value).catch(function () {
        return fallbackCopyText(value);
      });
    }

    return fallbackCopyText(value);
  }

  function fallbackCopyText(text) {
    return new Promise(function (resolve, reject) {
      try {
        const textarea = document.createElement('textarea');
        textarea.value = text;
        textarea.setAttribute('readonly', '');
        textarea.style.position = 'fixed';
        textarea.style.top = '-9999px';
        textarea.style.left = '-9999px';
        textarea.style.opacity = '0';
        document.body.appendChild(textarea);
        textarea.focus();
        textarea.select();
        const ok = typeof document.execCommand === 'function' && document.execCommand('copy');
        document.body.removeChild(textarea);
        if (ok) {
          resolve();
        } else {
          reject(new Error('Copy command failed'));
        }
      } catch (err) {
        reject(err);
      }
    });
  }

  function setCopyButtonFeedback(button, labelSpan, state) {
    if (!button) return;
    if (button._copyResetTimer) {
      clearTimeout(button._copyResetTimer);
      button._copyResetTimer = null;
    }

    const defaultLabel = button.getAttribute('data-default-label') || 'Sao chép';

    if (state === 'copied') {
      button.classList.remove('copy-error');
      button.classList.add('copied');
      if (labelSpan) {
        button.innerHTML = CHECK_ICON_SVG + '<span>Đã sao chép</span>';
      } else {
        button.innerHTML = CHECK_ICON_SVG;
      }
      button.title = 'Đã sao chép';
      button.setAttribute('aria-label', 'Đã sao chép');
    } else if (state === 'error') {
      button.classList.remove('copied');
      button.classList.add('copy-error');
      if (labelSpan) {
        button.innerHTML = COPY_ICON_SVG + '<span>Lỗi sao chép</span>';
      }
      button.title = 'Không thể sao chép';
    }

    button._copyResetTimer = setTimeout(function () {
      button.classList.remove('copied', 'copy-error');
      if (labelSpan) {
        button.innerHTML = COPY_ICON_SVG + '<span>' + defaultLabel + '</span>';
      } else {
        button.innerHTML = COPY_ICON_SVG;
      }
      button.title = button.getAttribute('data-default-title') || defaultLabel;
      button.setAttribute('aria-label', button.getAttribute('data-default-title') || defaultLabel);
      button._copyResetTimer = null;
    }, 2000);
  }

  function extractCleanBubbleText(bubble) {
    if (!bubble) return '';
    if (typeof bubble.cloneNode === 'function') {
      const clone = bubble.cloneNode(true);
      if (typeof clone.querySelectorAll === 'function') {
        clone.querySelectorAll('.message-actions, .cow-code-block__header').forEach(function (el) {
          if (el.parentNode) el.parentNode.removeChild(el);
        });
      }
      return String(clone.innerText || clone.textContent || '').trim();
    }
    return String(bubble.innerText || bubble.textContent || '').trim();
  }

  function speakText(buttonElement, bubbleElement) {
    const bubble = bubbleElement ||
      (buttonElement && typeof buttonElement.closest === 'function'
        ? buttonElement.closest('.message-bubble')
        : null);

    if (!bubble) return;

    const text = extractCleanBubbleText(bubble).replace(/\s+/g, ' ').trim();
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

    const copyBtn = document.createElement('button');
    copyBtn.type = 'button';
    copyBtn.className = 'btn-copy-message';
    copyBtn.title = 'Sao chép câu trả lời';
    copyBtn.setAttribute('aria-label', 'Sao chép câu trả lời');
    copyBtn.setAttribute('data-default-title', 'Sao chép câu trả lời');
    copyBtn.setAttribute('data-default-label', 'Sao chép');
    copyBtn.innerHTML = COPY_ICON_SVG + '<span>Sao chép</span>';

    copyBtn.addEventListener('click', function (event) {
      if (event && typeof event.preventDefault === 'function') {
        event.preventDefault();
      }
      const rawContent = (bubble.dataset && bubble.dataset.rawMarkdown)
        ? bubble.dataset.rawMarkdown
        : extractCleanBubbleText(bubble);

      copyTextToClipboard(rawContent)
        .then(function () {
          setCopyButtonFeedback(copyBtn, true, 'copied');
        })
        .catch(function () {
          setCopyButtonFeedback(copyBtn, true, 'error');
        });
    });

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

    actions.appendChild(copyBtn);
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

  const STREAM_LEAK_ERROR_PATTERNS = [
    /OpenRouter:ApiKey/i,
    /OpenRouter__ApiKey/i,
    /OPENROUTER_API_KEY/i,
    /\bsk-or-v1-[a-zA-Z0-9_\-]{12,}\b/i,
    /dotnet\s+user-secrets/i,
    /Microsoft\.AspNetCore\.Diagnostics/i,
    /Unhandled\s+exception\b/i
  ];

  function isServerLeakToken(text) {
    if (!text || typeof text !== 'string') return false;
    return STREAM_LEAK_ERROR_PATTERNS.some(function (pattern) {
      return pattern.test(text);
    });
  }

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

  function enhanceCodeBlocks(container) {
    if (!container || typeof container.querySelectorAll !== 'function') return;
    const preBlocks = container.querySelectorAll('pre');
    if (!preBlocks || preBlocks.length === 0) return;

    preBlocks.forEach(function (pre) {
      if (!pre || !pre.parentNode || typeof pre.parentNode.insertBefore !== 'function') return;
      if (pre.parentNode.classList && pre.parentNode.classList.contains('cow-code-block')) return;

      const codeElem = typeof pre.querySelector === 'function' ? pre.querySelector('code') : null;
      let langLabel = 'code';
      if (codeElem && codeElem.className) {
        const match = String(codeElem.className).match(/language-([a-zA-Z0-9_+#.-]+)/i);
        if (match && match[1]) {
          langLabel = match[1].toLowerCase();
        }
      }

      const wrapper = document.createElement('div');
      wrapper.className = 'cow-code-block';

      const header = document.createElement('div');
      header.className = 'cow-code-block__header';

      const langSpan = document.createElement('span');
      langSpan.className = 'cow-code-block__lang';
      langSpan.textContent = langLabel;

      const copyBtn = document.createElement('button');
      copyBtn.type = 'button';
      copyBtn.className = 'cow-code-block__copy';
      copyBtn.title = 'Sao chép mã nguồn';
      copyBtn.setAttribute('aria-label', 'Sao chép mã nguồn ' + langLabel);
      copyBtn.setAttribute('data-default-title', 'Sao chép mã nguồn');
      copyBtn.setAttribute('data-default-label', 'Sao chép');
      copyBtn.innerHTML = COPY_ICON_SVG + '<span>Sao chép</span>';

      copyBtn.addEventListener('click', function (event) {
        if (event && typeof event.preventDefault === 'function') {
          event.preventDefault();
        }
        const codeText = codeElem ? (codeElem.textContent || '') : (pre.textContent || '');
        copyTextToClipboard(codeText)
          .then(function () {
            setCopyButtonFeedback(copyBtn, true, 'copied');
          })
          .catch(function () {
            setCopyButtonFeedback(copyBtn, true, 'error');
          });
      });

      header.appendChild(langSpan);
      header.appendChild(copyBtn);

      pre.parentNode.insertBefore(wrapper, pre);
      wrapper.appendChild(header);
      wrapper.appendChild(pre);
    });
  }

  function renderMarkdown(element, markdownText, isStreaming) {
    if (element && element.dataset) {
      element.dataset.rawMarkdown = String(markdownText || '');
    }

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

    if (!isStreaming) {
      enhanceCodeBlocks(element);
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

      const trimmedCheck = (rawPrompt || prompt || '').trim();
      const isLocalImgRequest = (selectedModel || '').toLowerCase() === 'local-image' ||
        /^\/(?:imags|localimg|localimage)(?:\s|:|$)/i.test(trimmedCheck);
      const isCodeSkillRequest = /^\/(?:code|dev|laptrinh)(?:\s|:|$)/i.test(trimmedCheck);
      const isSearchSkillRequest = (selectedModel || '').toLowerCase() === 'duckduckgo' ||
        /^\/(?:search|tim|web)(?:\s|:|$)/i.test(trimmedCheck);

      let loadingLabel = 'Đang suy nghĩ...';
      if (isLocalImgRequest) {
        loadingLabel = 'Đang vẽ ảnh với SDXL Turbo...';
      } else if (isCodeSkillRequest) {
        loadingLabel = 'Đang lập trình chuyên sâu (/code)...';
      } else if (isSearchSkillRequest) {
        loadingLabel = 'Đang tra cứu web (/search)...';
      }

      aiBubble = document.createElement('div');
      aiBubble.className = 'message-bubble chat-bubble chat-bubble-ai';
      aiBubble.innerHTML = `<div class="ai-status-loading ai-thinking-indicator"><span class="ai-thinking-dot"></span><span class="ai-thinking-dot"></span><span class="ai-thinking-dot"></span> <span class="ai-status-text">${loadingLabel}</span></div>`;

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

    // 5. Xác định model đã chọn (Edge TTS / skill /tts hoặc model AI)
    const ttsSlashMatch = String(prompt || '').trim().match(/^\/(?:tts|speak|doc)(?:\s+|:)(.+)$/is);
    const isEdgeTtsSkill = Boolean(ttsSlashMatch);
    const effectiveTtsText = ttsSlashMatch ? ttsSlashMatch[1].trim() : prompt;
    const effectiveTtsModel = (selectedModel === 'edge-male' || selectedModel === 'edge-female')
      ? selectedModel
      : 'edge-female';

    // 5.1. Edge TTS: phát trực tiếp file MP3 trả về kèm nút Play/Pause và Tải xuống MP3
    if (selectedModel === 'edge-male' || selectedModel === 'edge-female' || isEdgeTtsSkill) {
      const voiceName = effectiveTtsModel === 'edge-male' ? 'vi-VN-NamMinhNeural' : 'vi-VN-HoaiMyNeural';
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

      const audioUrl = '/Home/StreamChat?prompt=' + encodeURIComponent(effectiveTtsText) +
        '&model=' + encodeURIComponent(effectiveTtsModel);

      if (aiBubble) {
        renderAudioPlayer(aiBubble, audioUrl, effectiveTtsText, voiceName, scrollToBottom);
      }

      if (window.CowAiHistoryStore) {
        window.CowAiHistoryStore.addMessage('assistant', '[Bản ghi âm giọng đọc: "' + effectiveTtsText + '"]', effectiveTtsModel);
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
          if (typeof deltaContent === 'string' && (deltaContent.trim().startsWith('[ERROR]') || (isServerLeakToken(deltaContent) && TECHNICAL_ERROR_PATTERNS.some(function (p) { return p.test(deltaContent); })))) {
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

    const DEFAULT_PROMPT_PLACEHOLDER = 'Hỏi bất kỳ điều gì hoặc gõ / để chọn kỹ năng (/imags, /code)...';

    /* ========================================================================
       1.5. Slash Skills Autocomplete Palette (/imags, /code, /image, /search, /tts)
       ======================================================================== */
    const FALLBACK_SKILLS = [
      {
        command: '/imags',
        aliases: ['/localimg', '/localimage'],
        name: 'Tạo ảnh AI Siêu tốc',
        badge: 'SDXL Turbo',
        badgeType: 'local',
        category: 'featured',
        categoryLabel: 'Kỹ năng chính',
        icon: 'image-gpu',
        description: 'Sinh ảnh tức thì bằng SDXL Turbo chất lượng cao (2–5 giây)',
        placeholder: 'Mô tả ảnh cần vẽ (ví dụ: chú bò đeo kính râm trên bãi biển 16:9)...'
      },
      {
        command: '/code',
        aliases: ['/dev', '/laptrinh'],
        name: 'Lập trình chuyên sâu',
        badge: 'Code Pro',
        badgeType: 'code',
        category: 'featured',
        categoryLabel: 'Kỹ năng chính',
        icon: 'code',
        description: 'Sinh mã nguồn sạch, tối ưu thuật toán theo chuẩn Markdown kèm nút Sao chép',
        placeholder: 'Nhập yêu cầu lập trình (ví dụ: viết API ASP.NET Core hoặc thuật toán Dijkstra bằng Python)...'
      },
      {
        command: '/image',
        aliases: ['/img', '/draw', '/ve'],
        name: 'Tạo ảnh Cloud',
        badge: 'Pollinations',
        badgeType: 'cloud',
        category: 'builtin',
        categoryLabel: 'Kỹ năng mở rộng',
        icon: 'image-cloud',
        description: 'Tạo ảnh nghệ thuật trực tuyến qua Pollinations AI (tự dịch Việt → Anh)',
        placeholder: 'Mô tả ảnh nghệ thuật cần tạo qua Pollinations Cloud...'
      },
      {
        command: '/search',
        aliases: ['/tim', '/web'],
        name: 'Tìm kiếm Web',
        badge: 'DuckDuckGo',
        badgeType: 'cloud',
        category: 'builtin',
        categoryLabel: 'Kỹ năng mở rộng',
        icon: 'search',
        description: 'Tra cứu thông tin trực tiếp trên web qua DuckDuckGo',
        placeholder: 'Nhập từ khóa cần tra cứu trên web...'
      },
      {
        command: '/tts',
        aliases: ['/speak', '/doc'],
        name: 'Đọc giọng nói tiếng Việt',
        badge: 'Edge TTS',
        badgeType: 'audio',
        category: 'builtin',
        categoryLabel: 'Kỹ năng mở rộng',
        icon: 'audio',
        description: 'Chuyển văn bản thành giọng đọc tiếng Việt tự nhiên kèm trình phát & tải MP3',
        placeholder: 'Nhập văn bản tiếng Việt cần đọc thành giọng nói...'
      }
    ];

    const skillsPopover = document.getElementById('skillsPopover');
    const skillsTriggerBtn = document.getElementById('skillsTriggerBtn');
    let availableSkills = FALLBACK_SKILLS.slice();
    let filteredSkills = [];
    let activeSkillIndex = 0;
    let isSkillsOpen = false;

    function getSkillIconSvg(iconType) {
      switch (iconType) {
        case 'image-gpu':
          return '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="3" y="3" width="18" height="18" rx="2"></rect><circle cx="8.5" cy="8.5" r="1.5"></circle><polyline points="21 15 16 10 5 21"></polyline></svg>';
        case 'code':
          return '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="16 18 22 12 16 6"></polyline><polyline points="8 6 2 12 8 18"></polyline></svg>';
        case 'image-cloud':
          return '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M18 10h-1.26A8 8 0 1 0 9 20h9a5 5 0 0 0 0-10z"></path></svg>';
        case 'search':
          return '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="11" cy="11" r="8"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line></svg>';
        case 'audio':
          return '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5"></polygon><path d="M15.54 8.46a5 5 0 0 1 0 7.07"></path></svg>';
        default:
          return '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polygon></svg>';
      }
    }

    // Nạp danh sách Skills từ Prompts/Skills.json qua API /api/skills
    if (typeof fetch === 'function' && skillsPopover) {
      fetch('/api/skills')
        .then(function (res) { return res.ok ? res.json() : null; })
        .then(function (data) {
          if (Array.isArray(data) && data.length > 0) {
            availableSkills = data;
          }
        })
        .catch(function () {
          // Giữ FALLBACK_SKILLS khi offline hoặc trong môi trường test
        });
    }

    function closeSkillsPopover() {
      isSkillsOpen = false;
      filteredSkills = [];
      activeSkillIndex = 0;
      if (skillsPopover) {
        skillsPopover.classList.add('hidden');
        skillsPopover.innerHTML = '';
      }
      if (skillsTriggerBtn) {
        skillsTriggerBtn.setAttribute('aria-expanded', 'false');
      }
    }

    function updatePlaceholderForInput(val) {
      if (!promptInput) return;
      const trimmed = String(val || '').trimStart();
      if (!trimmed.startsWith('/')) {
        const hiddenModel = document.getElementById('hiddenSelectedModel');
        const curVal = hiddenModel ? hiddenModel.value : '';
        if (curVal !== 'edge-male' && curVal !== 'edge-female') {
          promptInput.placeholder = DEFAULT_PROMPT_PLACEHOLDER;
        }
        return;
      }

      const cmdToken = trimmed.split(/\s+/)[0].toLowerCase();
      const matched = availableSkills.find(function (s) {
        return s.command.toLowerCase() === cmdToken ||
          (Array.isArray(s.aliases) && s.aliases.some(function (a) { return a.toLowerCase() === cmdToken; }));
      });
      if (matched && matched.placeholder) {
        promptInput.placeholder = matched.placeholder;
      }
    }

    function selectSkill(skill) {
      if (!skill || !promptInput) return;
      const currentVal = String(promptInput.value || '');
      const afterCmd = currentVal.replace(/^\/\S*\s*/, '');
      promptInput.value = skill.command + ' ' + afterCmd;
      promptInput.style.height = 'auto';
      promptInput.style.height = Math.min(promptInput.scrollHeight, 180) + 'px';
      if (skill.placeholder) {
        promptInput.placeholder = skill.placeholder;
      }
      closeSkillsPopover();
      promptInput.focus();
      if (typeof promptInput.setSelectionRange === 'function') {
        const pos = promptInput.value.length;
        promptInput.setSelectionRange(pos, pos);
      }
    }

    function renderSkillsPopover(queryText) {
      if (!skillsPopover) return;
      const q = String(queryText || '').trim().toLowerCase();
      const qNoSlash = q.startsWith('/') ? q.slice(1) : q;

      filteredSkills = availableSkills.filter(function (s) {
        if (!qNoSlash) return true;
        const inCmd = s.command.toLowerCase().includes(qNoSlash);
        const inName = String(s.name || '').toLowerCase().includes(qNoSlash);
        const inDesc = String(s.description || '').toLowerCase().includes(qNoSlash);
        const inAliases = Array.isArray(s.aliases) && s.aliases.some(function (a) {
          return a.toLowerCase().includes(qNoSlash);
        });
        return inCmd || inName || inDesc || inAliases;
      });

      if (filteredSkills.length === 0) {
        closeSkillsPopover();
        return;
      }

      if (activeSkillIndex >= filteredSkills.length) {
        activeSkillIndex = 0;
      }

      isSkillsOpen = true;
      skillsPopover.classList.remove('hidden');
      if (skillsTriggerBtn) {
        skillsTriggerBtn.setAttribute('aria-expanded', 'true');
      }

      let html = '<div class="skills-popover__header">' +
        '<span class="skills-popover__title">Kỹ năng nhanh (Slash Skills)</span>' +
        '<span class="skills-popover__hint">↑↓ chọn · Enter/Tab chèn · Esc đóng</span>' +
        '</div><div class="skills-popover__list">';

      let lastCategory = null;
      filteredSkills.forEach(function (skill, idx) {
        const catLabel = skill.categoryLabel || (skill.category === 'featured' ? 'Kỹ năng chính' : 'Kỹ năng mở rộng');
        if (catLabel !== lastCategory) {
          html += '<div class="skills-popover__group">' + escapeHtml(catLabel) + '</div>';
          lastCategory = catLabel;
        }

        const isActive = idx === activeSkillIndex;
        const badgeClass = 'skills-popover__badge skills-popover__badge--' + escapeHtml(skill.badgeType || 'cloud');

        html += '<button type="button" class="skills-popover__item' + (isActive ? ' active' : '') + '" ' +
          'role="option" aria-selected="' + String(isActive) + '" data-skill-index="' + idx + '">' +
          '<span class="skills-popover__icon skills-popover__icon--' + escapeHtml(skill.badgeType || 'cloud') + '">' + getSkillIconSvg(skill.icon) + '</span>' +
          '<span class="skills-popover__body">' +
            '<span class="skills-popover__top">' +
              '<code class="skills-popover__cmd">' + escapeHtml(skill.command) + '</code>' +
              '<span class="skills-popover__name">' + escapeHtml(skill.name) + '</span>' +
              (skill.badge ? '<span class="' + badgeClass + '">' + escapeHtml(skill.badge) + '</span>' : '') +
            '</span>' +
            '<span class="skills-popover__desc">' + escapeHtml(skill.description || '') + '</span>' +
          '</span>' +
        '</button>';
      });

      html += '</div>';
      skillsPopover.innerHTML = html;

      if (typeof skillsPopover.querySelectorAll === 'function') {
        skillsPopover.querySelectorAll('.skills-popover__item').forEach(function (btn) {
          btn.addEventListener('mousedown', function (e) {
            e.preventDefault();
            const idx = parseInt(btn.getAttribute('data-skill-index'), 10);
            if (!isNaN(idx) && filteredSkills[idx]) {
              selectSkill(filteredSkills[idx]);
            }
          });
          btn.addEventListener('mouseenter', function () {
            const idx = parseInt(btn.getAttribute('data-skill-index'), 10);
            if (!isNaN(idx) && idx !== activeSkillIndex) {
              activeSkillIndex = idx;
              skillsPopover.querySelectorAll('.skills-popover__item').forEach(function (item, i) {
                const match = i === activeSkillIndex;
                item.classList.toggle('active', match);
                item.setAttribute('aria-selected', String(match));
              });
            }
          });
        });
      }
    }

    function syncSkillsPopoverFromInput() {
      if (!promptInput || !skillsPopover) return;
      const val = String(promptInput.value || '');
      updatePlaceholderForInput(val);

      // Chỉ bật popover khi bắt đầu bằng "/" và chưa gõ khoảng trắng (đang chọn tên lệnh)
      if (/^\/\S*$/.test(val)) {
        renderSkillsPopover(val);
      } else if (isSkillsOpen) {
        closeSkillsPopover();
      }
    }

    if (skillsTriggerBtn && promptInput) {
      skillsTriggerBtn.addEventListener('click', function (e) {
        e.preventDefault();
        e.stopPropagation();
        if (isSkillsOpen) {
          closeSkillsPopover();
          promptInput.focus();
        } else {
          if (!String(promptInput.value || '').startsWith('/')) {
            promptInput.value = '/' + String(promptInput.value || '').trimStart();
          }
          activeSkillIndex = 0;
          renderSkillsPopover('/');
          promptInput.focus();
        }
      });
    }

    document.addEventListener('click', function (e) {
      if (!isSkillsOpen) return;
      const target = e && e.target;
      const insidePopover = skillsPopover && typeof skillsPopover.contains === 'function' && skillsPopover.contains(target);
      const insideTrigger = skillsTriggerBtn && typeof skillsTriggerBtn.contains === 'function' && skillsTriggerBtn.contains(target);
      const insideInput = promptInput && target === promptInput;
      if (!insidePopover && !insideTrigger && !insideInput) {
        closeSkillsPopover();
      }
    });

    if (promptInput) {
      promptInput.addEventListener('input', function () {
        this.style.height = 'auto';
        this.style.height = Math.min(this.scrollHeight, 180) + 'px';
        syncSkillsPopoverFromInput();
      });

      promptInput.addEventListener('keydown', function (event) {
        if (isSkillsOpen && filteredSkills.length > 0) {
          if (event.key === 'ArrowDown') {
            event.preventDefault();
            activeSkillIndex = (activeSkillIndex + 1) % filteredSkills.length;
            renderSkillsPopover(this.value);
            return;
          }
          if (event.key === 'ArrowUp') {
            event.preventDefault();
            activeSkillIndex = (activeSkillIndex - 1 + filteredSkills.length) % filteredSkills.length;
            renderSkillsPopover(this.value);
            return;
          }
          if ((event.key === 'Enter' && !event.shiftKey) || event.key === 'Tab') {
            event.preventDefault();
            selectSkill(filteredSkills[activeSkillIndex] || filteredSkills[0]);
            return;
          }
          if (event.key === 'Escape') {
            event.preventDefault();
            closeSkillsPopover();
            return;
          }
        }

        if (event.key === 'Enter' && !event.shiftKey) {
          event.preventDefault();
          if (this.value.trim().length > 0) {
            closeSkillsPopover();
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
     2. Image Generation (Mục 5.3 - model ảnh cục bộ SDXL Turbo)
     ========================================================================== */
  function initImageGen() {
    const form = document.getElementById('imageGenForm');
    const promptInput = document.getElementById('prompt-input') || document.getElementById('imagePromptInput');
    const submitBtn = document.getElementById('imageSubmitBtn');
    const btnSpinner = document.getElementById('imageBtnSpinner');
    const btnText = document.getElementById('imageBtnText');
    const engineBadge = document.getElementById('imageEngineBadge');
    const engineBadgeText = document.getElementById('imageEngineBadgeText');
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

    const emptyStateMarkup = emptyState ? emptyState.innerHTML : '';

    // Chip trạng thái model ảnh cục bộ (SDXL Turbo qua sd-server)
    function setEngineState(state, text) {
      if (!engineBadge) return;
      engineBadge.classList.remove('is-checking', 'is-offline');
      if (state === 'checking') engineBadge.classList.add('is-checking');
      if (state === 'offline') engineBadge.classList.add('is-offline');
      if (engineBadgeText) engineBadgeText.textContent = text;
    }

    async function refreshLocalStatus() {
      setEngineState('checking', 'Đang kết nối SDXL Turbo…');
      try {
        const res = await fetch('/Image/LocalStatus', { headers: { 'Accept': 'application/json' } });
        if (!res.ok) throw new Error('status ' + res.status);
        const data = await res.json();
        if (data && data.online) {
          setEngineState('online', `${data.engine || 'SDXL Turbo'} · Sẵn sàng`);
        } else {
          setEngineState('offline', data && data.auto_start ? 'Đang khởi động…' : 'Chưa kết nối');
        }
      } catch {
        setEngineState('offline', 'Không thể kết nối máy chủ ảnh');
      }
    }

    refreshLocalStatus();

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

      if (submitBtn) submitBtn.disabled = true;
      if (btnSpinner) btnSpinner.classList.remove('hidden');
      if (btnText) btnText.textContent = 'Đang tạo...';
      if (statusTag) statusTag.textContent = 'Đang tạo ảnh nghệ thuật…';

      if (previewContainer) {
        previewContainer.className = 'image-preview-frame image-preview-frame--loading';
      }
      if (emptyState) {
        emptyState.innerHTML = emptyStateMarkup;
        emptyState.classList.add('hidden');
      }
      if (successState) successState.classList.add('hidden');
      if (resultImg) resultImg.classList.add('hidden');
      if (actionsBar) actionsBar.classList.remove('hidden');
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
          let errorMsg = 'Không tạo được ảnh. Kiểm tra máy chủ ảnh rồi thử lại.';
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
        if (!data || !data.url) {
          throw new Error('Máy chủ không trả về đường dẫn ảnh.');
        }

        await new Promise(function (resolve, reject) {
          const preloader = new Image();
          preloader.onload = function () { resolve(); };
          preloader.onerror = function () {
            reject(new Error('Không tải được ảnh vừa tạo.'));
          };
          preloader.src = data.url;
        });

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
        if (statusTag) statusTag.textContent = `Hoàn thành · ${data.width}×${data.height}`;
        if (submitBtn) submitBtn.disabled = false;
        if (btnSpinner) btnSpinner.classList.add('hidden');
        if (btnText) btnText.textContent = 'Tạo ảnh ngay';
        if (engineBadge) setEngineState('online', `${data.engine || 'SDXL Turbo'} · Sẵn sàng`);
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
        if (!resultImg.src) return;
        const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
        const link = document.createElement('a');
        link.href = resultImg.src;
        link.download = `cow-ai-${stamp}.png`;
        link.rel = 'noopener';
        document.body.appendChild(link);
        link.click();
        link.remove();
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
              if (typeof token === 'string' && (token.trim().startsWith('[ERROR]') || (isServerLeakToken(token) && TECHNICAL_ERROR_PATTERNS.some(function (p) { return p.test(token); })))) {
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
              if (typeof token === 'string' && (token.trim().startsWith('[ERROR]') || (isServerLeakToken(token) && TECHNICAL_ERROR_PATTERNS.some(function (p) { return p.test(token); })))) {
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
