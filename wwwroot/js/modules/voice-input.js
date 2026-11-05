/**
 * COW.AI - Voice Speech-to-Text Input Module (Web Speech API)
 * Ngôn ngữ nhận diện mặc định: Tiếng Việt (vi-VN)
 */
(function (global) {
  'use strict';

  let recognition = null;
  let isListening = false;
  let targetInput = null;
  let voiceBtn = null;
  let finalTranscript = '';

  function isSpeechSupported() {
    return 'webkitSpeechRecognition' in window || 'SpeechRecognition' in window;
  }

  function initVoiceInput(options) {
    options = options || {};
    targetInput = options.inputElement || document.getElementById('promptInput');
    voiceBtn = options.buttonElement || document.getElementById('voiceInputBtn');

    if (!voiceBtn || !targetInput) return;

    if (!isSpeechSupported()) {
      voiceBtn.title = 'Trình duyệt không hỗ trợ nhận diện giọng nói (Web Speech API)';
      voiceBtn.classList.add('disabled');
      voiceBtn.setAttribute('disabled', 'disabled');
      return;
    }

    const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    recognition = new SpeechRecognition();
    recognition.continuous = true;
    recognition.interimResults = true;
    recognition.lang = options.lang || 'vi-VN';

    recognition.onstart = function () {
      isListening = true;
      voiceBtn.classList.add('listening');
      voiceBtn.setAttribute('aria-pressed', 'true');
      voiceBtn.title = 'Đang lắng nghe... Bấm để dừng';
    };

    recognition.onresult = function (event) {
      let interimTranscript = '';
      for (let i = event.resultIndex; i < event.results.length; ++i) {
        if (event.results[i].isFinal) {
          finalTranscript += event.results[i][0].transcript;
        } else {
          interimTranscript += event.results[i][0].transcript;
        }
      }

      const currentText = finalTranscript + (interimTranscript ? ' ' + interimTranscript : '');
      if (targetInput) {
        targetInput.value = currentText.trim();
        targetInput.style.height = 'auto';
        targetInput.style.height = Math.min(targetInput.scrollHeight, 180) + 'px';
      }
    };

    recognition.onerror = function (event) {
      console.warn('[VoiceInput] Lỗi nhận diện giọng nói:', event.error);
      stopListening();
    };

    recognition.onend = function () {
      stopListening();
    };

    voiceBtn.addEventListener('click', function (e) {
      e.preventDefault();
      toggleListening();
    });
  }

  function startListening() {
    if (!recognition || isListening) return;
    try {
      finalTranscript = targetInput ? targetInput.value : '';
      if (finalTranscript && !finalTranscript.endsWith(' ')) {
        finalTranscript += ' ';
      }
      recognition.start();
    } catch (e) {
      console.warn('[VoiceInput] Không thể khởi động nhận diện giọng nói:', e);
      stopListening();
    }
  }

  function stopListening() {
    isListening = false;
    if (voiceBtn) {
      voiceBtn.classList.remove('listening');
      voiceBtn.setAttribute('aria-pressed', 'false');
      voiceBtn.title = 'Nhập bằng giọng nói (Tiếng Việt)';
    }
    if (recognition) {
      try {
        recognition.stop();
      } catch {
        // Ignore already stopped
      }
    }
  }

  function toggleListening() {
    if (isListening) {
      stopListening();
    } else {
      startListening();
    }
  }

  global.CowAiVoiceInput = {
    init: initVoiceInput,
    start: startListening,
    stop: stopListening,
    toggle: toggleListening,
    isSupported: isSpeechSupported
  };

})(typeof window !== 'undefined' ? window : this);
