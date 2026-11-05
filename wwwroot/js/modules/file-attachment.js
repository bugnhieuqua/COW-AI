/**
 * COW.AI - Client-Side File Attachment Handler
 * Đọc nội dung tệp văn bản/mã nguồn hoàn toàn trên trình duyệt (Zero upload server)
 */
(function (global) {
  'use strict';

  let currentAttachment = null;
  const MAX_FILE_SIZE = 512 * 1024; // 512 KB

  function initFileAttachment(options) {
    options = options || {};
    const attachBtn = options.attachBtn || document.getElementById('attachBtn');
    const fileInput = options.fileInput || document.getElementById('chatFileInput');
    const previewArea = options.previewArea || document.getElementById('attachmentPreviewArea');

    if (!attachBtn || !fileInput) return;

    attachBtn.addEventListener('click', function (e) {
      e.preventDefault();
      fileInput.click();
    });

    fileInput.addEventListener('change', function () {
      if (this.files && this.files.length > 0) {
        processFile(this.files[0], previewArea);
      }
      this.value = ''; // Reset input to allow re-uploading same file
    });
  }

  function processFile(file, previewArea) {
    if (!file) return;

    if (file.size > MAX_FILE_SIZE) {
      alert(`Tệp "${file.name}" vượt quá kích thước cho phép (${Math.round(MAX_FILE_SIZE / 1024)} KB). Vui lòng chọn tệp nhỏ hơn.`);
      return;
    }

    const reader = new FileReader();
    reader.onload = function (e) {
      const content = e.target.result;
      const extension = file.name.split('.').pop().toLowerCase();

      currentAttachment = {
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

  function renderAttachmentChip(previewArea) {
    previewArea = previewArea || document.getElementById('attachmentPreviewArea');
    if (!previewArea) return;

    if (!currentAttachment) {
      previewArea.innerHTML = '';
      previewArea.classList.add('hidden');
      return;
    }

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
        <span class="attachment-chip__name" title="${currentAttachment.name}">${currentAttachment.name}</span>
        <span class="attachment-chip__size">(${sizeKb} KB)</span>
        <button type="button" class="attachment-chip__remove" id="removeAttachmentBtn" aria-label="Xóa tệp đính kèm">✕</button>
      </div>
    `;
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

    const ext = currentAttachment.extension || 'txt';
    const attachmentSnippet = `\`\`\`${ext}\n// [Tệp: ${currentAttachment.name}]\n${currentAttachment.content}\n\`\`\``;

    if (!userPrompt || !userPrompt.trim()) {
      return `Hãy phân tích tệp sau:\n\n${attachmentSnippet}`;
    }

    return `${userPrompt}\n\n--- Dữ liệu tệp đính kèm [${currentAttachment.name}] ---\n${attachmentSnippet}`;
  }

  global.CowAiFileAttachment = {
    init: initFileAttachment,
    getAttachment: getAttachment,
    clearAttachment: clearAttachment,
    formatPromptWithAttachment: formatPromptWithAttachment
  };

})(typeof window !== 'undefined' ? window : this);
