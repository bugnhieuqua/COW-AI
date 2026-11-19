/**
 * COW.AI Studio Player & Multimedia Controller (v2.0)
 * Tiêu chí: Zero AI-Slop - Reactive State Machine - 60 FPS Canvas & Synchronized Playback
 * Hỗ trợ: MediaRecorder Video Export 100% Client-Side Offline, GSAP/Canvas Transforms, HyperFrames Preview
 */
(function (global) {
  'use strict';

  function initStudio() {
    const container = document.getElementById('studioContainer');
    if (!container) return;

    const projectId = container.dataset.projectId;
    const audioElement = document.getElementById('studioAudioPlayer');
    const stageImage = document.getElementById('stageImagePreview') || document.getElementById('stageImage');
    const stageSubtitle = document.getElementById('stageCaptionContainer') || document.getElementById('stageSubtitle');
    const stageSubtitleText = document.getElementById('stageCaptionSubtitle') || document.getElementById('stageSubtitleText');
    const activeSceneLabel = document.getElementById('activeSceneIndicator') || document.getElementById('playerActiveSceneLabel');
    const btnPlayPause = document.getElementById('btnPlayPause');
    const btnPrevScene = document.getElementById('btnPrevScene');
    const btnNextScene = document.getElementById('btnNextScene');
    const seeker = document.getElementById('stageScrubber') || document.getElementById('studioSeeker');
    const timeCurrent = document.getElementById('currentTimecode') || document.getElementById('timeCurrent');
    const timeTotal = document.getElementById('totalTimecode') || document.getElementById('timeTotal');
    const timelineDock = document.getElementById('studioTimelineDock') || document.querySelector('.studio-timeline-dock');
    const timelineTrack = document.getElementById('timelineTrack');

    // State
    let scenes = [];
    let currentSceneIndex = 0;
    let isPlaying = false;
    let sceneTimer = null;
    let isExporting = false;

    function readSceneCard(card, idx) {
      const order = parseInt(card.dataset.order ?? idx, 10) + (card.classList.contains('scene-item') ? 1 : 0);
      const titleEl = card.querySelector('[data-field="title"], .scene-title-text');
      const contentEl = card.querySelector('[data-field="content"], .scene-narration-input');
      const promptEl = card.querySelector('[data-field="imagePrompt"], .scene-prompt-input');
      const animEl = card.querySelector('[data-field="animationEffect"], .animationEffect');
      const filterEl = card.querySelector('[data-field="filterPreset"], .filterPreset');
      const posEl = card.querySelector('[data-field="subtitlePosition"], .subtitlePosition');
      const durEl = card.querySelector('[data-field="durationSeconds"]');
      const title = (titleEl ? ('value' in titleEl ? titleEl.value : titleEl.textContent) : '').trim() || `Cảnh ${order}`;
      return {
        id: card.dataset.sceneId,
        order,
        title,
        content: contentEl ? contentEl.value.trim() : '',
        prompt: promptEl ? promptEl.value.trim() : '',
        animation: animEl ? animEl.value : (card.dataset.anim || 'zoom-in'),
        filter: filterEl ? filterEl.value : (card.dataset.filter || 'normal'),
        subtitlePos: posEl ? posEl.value : (card.dataset.subPos || 'bottom'),
        audioUrl: card.dataset.audioUrl || null,
        imageUrl: card.dataset.imageUrl || null,
        duration: parseFloat(durEl?.value || card.dataset.duration) || 5.0,
        element: card
      };
    }

    // Collect scene cards data from DOM
    function scanScenes() {
      const sceneCards = document.querySelectorAll('.scene-item, .scene-card');
      scenes = Array.from(sceneCards, (card, idx) => readSceneCard(card, idx));
      updateTimelineWidths();

      // Fetch latest scene data from server to get accurate audioUrl and imageUrl
      if (projectId) {
        fetch(`/api/studio/projects/${projectId}`)
          .then(res => res.json())
          .then(data => {
            if (data && data.scenes && data.scenes.length > 0) {
              data.scenes.forEach((sc, i) => {
                if (scenes[i]) {
                  scenes[i].audioUrl = sc.audioUrl;
                  scenes[i].imageUrl = sc.imageUrl;
                  scenes[i].duration = sc.durationSeconds || 5.0;
                  scenes[i].animation = sc.animationEffect || 'ken-burns';
                  scenes[i].filter = sc.filterPreset || 'none';
                  scenes[i].subtitlePos = sc.subtitlePosition || 'bottom';
                }
              });
              updateTimelineWidths();
              loadScene(0, false);
            }
          })
          .catch(e => console.warn('[Studio] Không thể nạp chi tiết project:', e));
      }
    }

    function updateTimelineWidths() {
      const hasScenes = scenes.length > 0;
      const totalDur = hasScenes ? scenes.reduce((acc, s) => acc + (s.duration || 5), 0) : 0;
      if (timelineDock) timelineDock.classList.toggle('hidden', !hasScenes);
      const totalDurLabel = document.getElementById('timelineTotalDuration');
      if (totalDurLabel) totalDurLabel.textContent = `${Math.round(totalDur)}s`;
      if (timeTotal) timeTotal.textContent = formatTime(totalDur);
      if (activeSceneLabel && !hasScenes) activeSceneLabel.textContent = 'Chưa có phân cảnh';

      const ruler = document.getElementById('timelineRuler');
      if (ruler) {
        const marks = ruler.querySelectorAll('.ruler-mark');
        const fractions = [0, 0.25, 0.5, 0.75, 1];
        marks.forEach((mark, i) => {
          mark.textContent = formatTime(totalDur * (fractions[i] ?? 1));
        });
      }
    }

    function formatTime(seconds) {
      const mins = Math.floor(seconds / 60);
      const secs = Math.floor(seconds % 60);
      return `${mins < 10 ? '0' : ''}${mins}:${secs < 10 ? '0' : ''}${secs}`;
    }

    function loadScene(index, autoPlay) {
      if (index < 0 || index >= scenes.length) return;
      currentSceneIndex = index;
      const scene = scenes[index];

      document.querySelectorAll('.scene-item, .scene-card').forEach((c, idx) => {
        c.classList.toggle('active', idx === index);
      });
      document.querySelectorAll('.timeline-scene-block, .timeline-segment').forEach((seg, idx) => {
        seg.classList.toggle('active', idx === index);
      });

      if (activeSceneLabel) {
        activeSceneLabel.textContent = `Cảnh ${scene.order}: ${scene.title || ''}`;
      }

      if (stageSubtitleText) {
        stageSubtitleText.textContent = scene.content || '(Cảnh chưa có lời thoại)';
      }

      const captionTitle = document.getElementById('stageCaptionTitle') || document.getElementById('stageCyberTitle');
      if (captionTitle) {
        captionTitle.textContent = scene.title || `Cảnh ${scene.order}`;
      }

      if (stageImage) {
        if (scene.imageUrl) {
          stageImage.classList.remove('hidden', 'img-hidden');
          stageImage.style.display = 'block';
          stageImage.src = scene.imageUrl;
        } else {
          stageImage.classList.add('hidden', 'img-hidden');
          stageImage.style.display = 'none';
        }

        // Apply animation classes
        stageImage.classList.remove('ken-burns', 'zoom-in', 'pan-left', 'fade-scale');
        // Apply filter classes
        stageImage.classList.remove('filter-none', 'filter-cinematic', 'filter-warm', 'filter-cool', 'filter-noir');

        void stageImage.offsetWidth; // Force reflow

        stageImage.classList.add(scene.animation || 'ken-burns');
        stageImage.classList.add('filter-' + (scene.filter || 'none'));
      }

      // Audio loading
      if (audioElement) {
        clearTimeout(sceneTimer);
        if (scene.audioUrl) {
          audioElement.src = scene.audioUrl;
          if (autoPlay) {
            audioElement.play().then(() => {
              setPlayState(true);
            }).catch(e => console.warn('[Studio] Autoplay blocked:', e));
          }
        } else {
          audioElement.removeAttribute('src');
          if (autoPlay) {
            // Fallback timer when no audio
            sceneTimer = setTimeout(() => {
              if (isPlaying && currentSceneIndex === index) {
                nextScene();
              }
            }, (scene.duration || 5) * 1000);
          }
        }
      }
    }

    function setPlayState(playing) {
      isPlaying = playing;
      if (btnPlayPause) {
        const iconPlay = btnPlayPause.querySelector('.icon-play');
        const iconPause = btnPlayPause.querySelector('.icon-pause');
        if (iconPlay && iconPause) {
          iconPlay.classList.toggle('hidden', playing);
          iconPause.classList.toggle('hidden', !playing);
        }
      }
      const audioBars = document.getElementById('stageAudioBars');
      if (audioBars) {
        audioBars.classList.toggle('playing', playing);
      }
    }

    function togglePlay() {
      if (scenes.length === 0) return;
      if (isPlaying) {
        if (audioElement) audioElement.pause();
        clearTimeout(sceneTimer);
        setPlayState(false);
      } else {
        if (audioElement && audioElement.src) {
          audioElement.play().then(() => {
            setPlayState(true);
          }).catch(() => {
            setPlayState(true);
          });
        } else {
          setPlayState(true);
          loadScene(currentSceneIndex, true);
        }
      }
    }

    function prevScene() {
      if (currentSceneIndex > 0) {
        loadScene(currentSceneIndex - 1, isPlaying);
      }
    }

    function nextScene() {
      if (currentSceneIndex < scenes.length - 1) {
        loadScene(currentSceneIndex + 1, isPlaying);
      } else {
        // Reached end
        setPlayState(false);
        loadScene(0, false);
      }
    }

    // Audio Event listeners
    if (audioElement) {
      audioElement.addEventListener('ended', function () {
        if (isPlaying) {
          nextScene();
        }
      });

      audioElement.addEventListener('timeupdate', function () {
        if (audioElement.duration && timeCurrent) {
          timeCurrent.textContent = formatTime(audioElement.currentTime);
          if (seeker) {
            seeker.value = (audioElement.currentTime / audioElement.duration) * 100;
          }
        }
      });
    }

    // Playback control button listeners
    if (btnPlayPause) btnPlayPause.addEventListener('click', togglePlay);
    if (btnPrevScene) btnPrevScene.addEventListener('click', prevScene);
    if (btnNextScene) btnNextScene.addEventListener('click', nextScene);

    // Seeker scrub
    if (seeker && audioElement) {
      seeker.addEventListener('input', function () {
        if (audioElement.duration) {
          audioElement.currentTime = (seeker.value / 100) * audioElement.duration;
        }
      });
    }

    // Click on timeline segment to jump
    if (timelineTrack) {
      timelineTrack.addEventListener('click', function (e) {
        const seg = e.target.closest('.timeline-scene-block, .timeline-segment');
        if (!seg) return;
        const idx = seg.dataset.sceneId
          ? scenes.findIndex(s => s.id == seg.dataset.sceneId)
          : scenes.findIndex(s => s.order === parseInt(seg.dataset.order, 10));
        if (idx !== -1) loadScene(idx, isPlaying);
      });
    }

    // Click on scene card to jump
    const scenesContainer = document.getElementById('scenesList') || document.getElementById('scenesContainer');
    if (scenesContainer) {
      scenesContainer.addEventListener('click', function (e) {
        const card = e.target.closest('.scene-item, .scene-card');
        if (card && !e.target.closest('button, textarea, input, select, details, [contenteditable]')) {
          const idx = Array.from(document.querySelectorAll('.scene-item, .scene-card')).indexOf(card);
          if (idx !== -1) loadScene(idx, isPlaying);
        }
      });
    }

    function runSceneMediaAction(sceneId, btn, endpoint, busyLabel, doneLabel) {
      if (!sceneId || !btn) return;
      const origHtml = btn.innerHTML;
      btn.disabled = true;
      btn.textContent = busyLabel;

      fetch(`/api/studio/scenes/${sceneId}/${endpoint}`, { method: 'POST' })
        .then(res => res.json())
        .then(data => {
          if (data.success) {
            btn.replaceWith(createReadyBadge(doneLabel));
            scanScenes();
          } else {
            alert(`Lỗi tạo ${endpoint}: ` + (data.error || 'Thất bại'));
            btn.disabled = false;
            btn.innerHTML = origHtml;
          }
        })
        .catch(() => {
          alert('Lỗi kết nối máy chủ');
          btn.disabled = false;
          btn.innerHTML = origHtml;
        });
    }

    global.selectSceneFromTimeline = function (sceneId) {
      const idx = scenes.findIndex(s => s.id == sceneId);
      if (idx !== -1) loadScene(idx, isPlaying);
    };
    global.generateSceneAudio = (sceneId, btn) => runSceneMediaAction(sceneId, btn, 'audio', 'Đang tạo...', 'Audio sẵn sàng');
    global.generateSceneImage = (sceneId, btn) => runSceneMediaAction(sceneId, btn, 'image', 'Đang vẽ...', 'Ảnh sẵn sàng');
    global.saveSceneChanges = function (sceneId, btn) {
      const card = btn?.closest('.scene-item, .scene-card') || document.querySelector(`[data-scene-id="${sceneId}"]`);
      if (card) saveSceneData(card, sceneId, btn);
    };
    global.deleteSceneItem = function (sceneId, e) {
      if (e) e.stopPropagation();
      if (!confirm('Xóa phân cảnh này?')) return;
      fetch(`/api/studio/scenes/${sceneId}`, { method: 'DELETE' })
        .then(res => res.ok ? res.json() : Promise.reject())
        .then(() => {
          document.querySelectorAll(`[data-scene-id="${sceneId}"]`).forEach(el => el.remove());
          scanScenes();
          showStudioToast('Đã xóa phân cảnh.');
        })
        .catch(() => alert('Không thể xóa phân cảnh.'));
    };

    // Delegated legacy listeners (giữ tương thích)
    document.addEventListener('click', function (e) {
      const audioBtn = e.target.closest('.btn-gen-scene-audio');
      if (audioBtn) runSceneMediaAction(audioBtn.dataset.sceneId, audioBtn, 'audio', 'Đang tạo...', 'Audio sẵn sàng');
      const imgBtn = e.target.closest('.btn-gen-scene-image');
      if (imgBtn) runSceneMediaAction(imgBtn.dataset.sceneId, imgBtn, 'image', 'Đang vẽ...', 'Ảnh sẵn sàng');
    });

    // Save scene when property select changes
    document.addEventListener('change', function (e) {
      const field = e.target.closest('.scene-prop-select, .scene-prop-input');
      const card = field?.closest('.scene-item, .scene-card');
      if (card) saveSceneData(card, card.dataset.sceneId, card.querySelector('.btn-save-scene'), true);
    });

    function saveSceneData(card, sceneId, btn, silent = false) {
      const parsed = readSceneCard(card, 0);
      const { title, content, prompt: imagePrompt, animation: animationEffect, filter: filterPreset, subtitlePos: subtitlePosition, duration: durationSeconds } = parsed;

      if (btn && !silent) {
        btn.disabled = true;
        btn.textContent = 'Đang lưu...';
      }

      fetch(`/api/studio/scenes/${sceneId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          title,
          content,
          imagePrompt,
          durationSeconds,
          animationEffect,
          filterPreset,
          subtitlePosition
        })
      })
        .then(res => res.json())
        .then(data => {
          if (btn && !silent) {
            btn.disabled = false;
            btn.textContent = 'Đã lưu';
            setTimeout(() => { btn.textContent = 'Lưu'; }, 1500);
          }
          if (data.success) {
            const sc = scenes.find(s => s.id == sceneId);
            if (sc) Object.assign(sc, { title, content, duration: durationSeconds, animation: animationEffect, filter: filterPreset, subtitlePos: subtitlePosition });
            updateTimelineWidths();
            if (scenes[currentSceneIndex]?.id == sceneId) loadScene(currentSceneIndex, isPlaying);
            if (!silent) showStudioToast('Đã lưu thuộc tính cảnh thành công!');
          }
        })
        .catch(() => {
          if (btn && !silent) {
            btn.disabled = false;
            btn.textContent = 'Lưu';
            alert('Lỗi kết nối khi lưu cảnh.');
          }
        });
    }

    // Add Scene Button
    const btnQuickAdd = document.getElementById('btnAddScene') || document.getElementById('btnQuickAddScene');
    if (btnQuickAdd) {
      btnQuickAdd.addEventListener('click', function () {
        btnQuickAdd.disabled = true;
        btnQuickAdd.textContent = '+ Đang thêm...';

        fetch(`/api/studio/projects/${projectId}/scenes`, { method: 'POST' })
          .then(res => res.json())
          .then(data => {
            if (data.success) {
              showStudioToast('Đã thêm phân cảnh mới! Đang cập nhật...');
              setTimeout(() => window.location.reload(), 400);
            } else {
              alert('Không thể thêm cảnh mới.');
              btnQuickAdd.disabled = false;
              btnQuickAdd.textContent = '+ Cảnh mới';
            }
          })
          .catch(() => {
            alert('Lỗi kết nối máy chủ.');
            btnQuickAdd.disabled = false;
            btnQuickAdd.textContent = '+ Cảnh mới';
          });
      });
    }

    // Delete Scene Button
    document.addEventListener('click', function (e) {
      const btn = e.target.closest('.btn-delete-scene');
      if (btn) {
        const sceneId = btn.dataset.sceneId;
        if (!confirm('Bạn có chắc chắn muốn xóa phân cảnh này không?')) return;

        fetch(`/api/studio/scenes/${sceneId}`, { method: 'DELETE' })
          .then(res => res.json())
          .then(data => {
            if (data.success) {
              showStudioToast('Đã xóa phân cảnh.');
              setTimeout(() => window.location.reload(), 400);
            } else {
              alert('Lỗi khi xóa phân cảnh.');
            }
          })
          .catch(() => alert('Lỗi kết nối máy chủ.'));
      }
    });

    // Preview Composition Modal
    const btnOpenPreview = document.getElementById('btnOpenPreview');
    const previewModal = document.getElementById('compositionPreviewModal');
    const btnClosePreview = document.getElementById('btnClosePreviewModal');
    const compositionIframe = document.getElementById('compositionIframe');

    if (btnOpenPreview && previewModal && compositionIframe) {
      btnOpenPreview.addEventListener('click', function () {
        compositionIframe.src = `/studio/${projectId}/preview`;
        previewModal.classList.remove('hidden');
        previewModal.setAttribute('aria-hidden', 'false');
      });
    }

    if (btnClosePreview && previewModal) {
      btnClosePreview.addEventListener('click', function () {
        previewModal.classList.add('hidden');
        previewModal.setAttribute('aria-hidden', 'true');
        if (compositionIframe) compositionIframe.src = 'about:blank';
      });
    }

    function createReadyBadge(text) {
      const span = document.createElement('span');
      span.className = 'asset-ready-badge';
      span.textContent = text;
      return span;
    }

    // Modal Script Gen
    const modal = document.getElementById('scriptGenModal');
    const btnOpenModal = document.getElementById('btnOpenScriptModal');
    const btnCloseModal = document.getElementById('btnCloseScriptModal');
    const btnCancelModal = document.getElementById('btnCancelScriptModal');
    const btnConfirmGen = document.getElementById('btnConfirmGenerateScript');
    const topicInput = document.getElementById('scriptTopicInput');

    function openModal() {
      if (modal) {
        modal.classList.remove('hidden');
        modal.setAttribute('aria-hidden', 'false');
        if (topicInput) topicInput.focus();
      }
    }

    function closeModal() {
      if (modal) {
        modal.classList.add('hidden');
        modal.setAttribute('aria-hidden', 'true');
      }
    }

    if (btnOpenModal) btnOpenModal.addEventListener('click', openModal);
    if (btnCloseModal) btnCloseModal.addEventListener('click', closeModal);
    if (btnCancelModal) btnCancelModal.addEventListener('click', closeModal);

    // Empty state trigger button
    const btnEmptyGen = document.getElementById('btnEmptyGenerate');
    const emptyTopicInput = document.getElementById('emptyTopicInput');
    if (btnEmptyGen) {
      btnEmptyGen.addEventListener('click', function () {
        const val = emptyTopicInput ? emptyTopicInput.value.trim() : '';
        triggerScriptGeneration(val || 'Bản tin công nghệ trí tuệ nhân tạo');
      });
    }

    if (btnConfirmGen) {
      btnConfirmGen.addEventListener('click', function () {
        const val = topicInput ? topicInput.value.trim() : '';
        closeModal();
        triggerScriptGeneration(val || 'Bản tin kịch bản Studio');
      });
    }

    function triggerScriptGeneration(topic) {
      const statusBadge = document.getElementById('studioBadgeStatus');
      if (statusBadge) statusBadge.textContent = 'Đang biên kịch...';

      showStudioToast('AI đang phân tích và biên kịch các cảnh...');

      fetch(`/api/studio/projects/${projectId}/script`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ topic: topic })
      })
        .then(res => res.json())
        .then(data => {
          if (data.success) {
            showStudioToast('Biên kịch thành công! Đang tải lại Workspace...');
            setTimeout(() => window.location.reload(), 600);
          } else {
            alert('Lỗi biên kịch AI: ' + (data.error || 'Thất bại'));
            if (statusBadge) statusBadge.textContent = 'Lỗi';
          }
        })
        .catch(() => {
          alert('Lỗi kết nối máy chủ');
          if (statusBadge) statusBadge.textContent = 'Lỗi';
        });
    }

    // Auto Produce 3-in-1
    const btnAutoProduce = document.getElementById('btnAutoProduceAll');
    if (btnAutoProduce) {
      btnAutoProduce.addEventListener('click', function () {
        if (!confirm('Bắt đầu sản xuất tự động toàn bộ Giọng đọc & Hình ảnh cho các cảnh?')) return;

        btnAutoProduce.disabled = true;
        btnAutoProduce.innerHTML = '<span class="spinner-mini"></span><span>Đang sản xuất...</span>';
        showStudioToast('Đang sản xuất âm thanh và hình ảnh cho toàn bộ cảnh...');

        fetch(`/api/studio/projects/${projectId}/auto-produce`, { method: 'POST' })
          .then(res => res.json())
          .then(data => {
            if (data.success) {
              showStudioToast('Sản xuất 3-in-1 hoàn tất thành công!');
              setTimeout(() => window.location.reload(), 800);
            } else {
              alert('Lỗi sản xuất tự động: ' + (data.error || 'Thất bại'));
              btnAutoProduce.disabled = false;
              btnAutoProduce.innerHTML = '<polygon points="5 3 19 12 5 21 5 3"></polygon><span>Sản xuất 3-in-1</span>';
            }
          })
          .catch(() => {
            alert('Lỗi kết nối máy chủ');
            btnAutoProduce.disabled = false;
          });
      });
    }

    // Client-Side Video Export (MediaRecorder 60 FPS)
    const btnExportVideo = document.getElementById('btnExportVideo');
    const recordOverlay = document.getElementById('recordProgressOverlay');
    const recordText = document.getElementById('recordProgressText');
    const recordBarFill = document.getElementById('recordProgressBarFill');

    if (btnExportVideo) {
      btnExportVideo.addEventListener('click', function () {
        if (scenes.length === 0) {
          alert('Dự án chưa có phân cảnh nào để xuất video.');
          return;
        }

        if (isExporting) return;
        exportVideoClientSide();
      });
    }

    async function exportVideoClientSide() {
      isExporting = true;
      btnExportVideo.disabled = true;

      if (recordOverlay) recordOverlay.classList.remove('hidden');
      if (recordText) recordText.textContent = 'Đang khởi tạo trình dựng video 60 FPS...';
      if (recordBarFill) recordBarFill.style.width = '5%';

      // Pause preview player
      if (isPlaying) togglePlay();

      const width = 1280;
      const height = 720;
      const canvas = document.createElement('canvas');
      canvas.width = width;
      canvas.height = height;
      const ctx = canvas.getContext('2d');

      // Audio setup
      const audioCtx = new (window.AudioContext || window.webkitAudioContext)();
      const dest = audioCtx.createMediaStreamDestination();

      // Video Stream
      const videoStream = canvas.captureStream(60);
      const combinedTracks = [...videoStream.getVideoTracks(), ...dest.stream.getAudioTracks()];
      const stream = new MediaStream(combinedTracks);

      let mimeType = 'video/webm;codecs=vp9,opus';
      if (!MediaRecorder.isTypeSupported(mimeType)) {
        mimeType = 'video/webm';
      }
      if (!MediaRecorder.isTypeSupported(mimeType)) {
        mimeType = 'video/mp4';
      }

      const recorder = new MediaRecorder(stream, { mimeType: mimeType });
      const chunks = [];

      recorder.ondataavailable = e => {
        if (e.data && e.data.size > 0) chunks.push(e.data);
      };

      const recordPromise = new Promise(resolve => {
        recorder.onstop = () => resolve();
      });

      recorder.start();

      try {
        const totalScenes = scenes.length;
        for (let i = 0; i < totalScenes; i++) {
          const sc = scenes[i];
          const pct = Math.round(((i) / totalScenes) * 100);
          if (recordText) recordText.textContent = `Đang kết xuất Cảnh ${i + 1}/${totalScenes}: ${sc.title}... (${pct}%)`;
          if (recordBarFill) recordBarFill.style.width = `${pct}%`;

          // Load image
          const img = new Image();
          img.crossOrigin = 'anonymous';
          img.src = sc.imageUrl || '/images/logo.png';
          await new Promise(r => {
            img.onload = r;
            img.onerror = r;
          });

          // Play Audio track through audioCtx if present
          let audioEl = null;
          if (sc.audioUrl) {
            try {
              audioEl = new Audio(sc.audioUrl);
              audioEl.crossOrigin = 'anonymous';
              const source = audioCtx.createMediaElementSource(audioEl);
              source.connect(dest);
              source.connect(audioCtx.destination);
              await audioEl.play().catch(() => {});
            } catch (err) {
              console.warn('[Export] Audio attach fallback:', err);
            }
          }

          const sceneDurationMs = (sc.duration || 5.0) * 1000;
          const startTime = performance.now();

          // Render loop for this scene
          while (performance.now() - startTime < sceneDurationMs) {
            const elapsed = performance.now() - startTime;
            const progress = Math.min(1.0, elapsed / sceneDurationMs);

            ctx.save();
            ctx.clearRect(0, 0, width, height);

            // Filter
            switch (sc.filter) {
              case 'cinematic':
                ctx.filter = 'contrast(1.2) brightness(0.95) saturate(1.25)';
                break;
              case 'warm':
                ctx.filter = 'sepia(0.25) saturate(1.3) brightness(1.02)';
                break;
              case 'cool':
                ctx.filter = 'hue-rotate(185deg) saturate(1.1) brightness(0.95)';
                break;
              case 'noir':
                ctx.filter = 'grayscale(1) contrast(1.4) brightness(0.9)';
                break;
              default:
                ctx.filter = 'none';
                break;
            }

            // Transform based on animationEffect
            ctx.translate(width / 2, height / 2);
            let scale = 1.0;
            let transX = 0;
            let transY = 0;

            if (sc.animation === 'zoom-in') {
              scale = 1.0 + progress * 0.18;
            } else if (sc.animation === 'pan-left') {
              scale = 1.1;
              transX = 40 - progress * 80;
            } else if (sc.animation === 'fade-scale') {
              scale = 1.08 - progress * 0.08;
            } else {
              // ken-burns
              scale = 1.0 + progress * 0.12;
              transX = -20 + progress * 40;
              transY = -15 + progress * 30;
            }

            ctx.scale(scale, scale);
            ctx.translate(transX, transY);

            // Draw image (cover mode)
            const imgAspect = img.width / img.height;
            const canvasAspect = width / height;
            let drawW = width;
            let drawH = height;
            if (imgAspect > canvasAspect) {
              drawW = height * imgAspect;
            } else {
              drawH = width / imgAspect;
            }

            ctx.drawImage(img, -drawW / 2, -drawH / 2, drawW, drawH);
            ctx.restore();

            // Gradient Overlay
            const grad = ctx.createLinearGradient(0, height * 0.5, 0, height);
            grad.addColorStop(0, 'rgba(0, 0, 0, 0)');
            grad.addColorStop(1, 'rgba(0, 0, 0, 0.85)');
            ctx.fillStyle = grad;
            ctx.fillRect(0, 0, width, height);

            // Subtitles
            if (sc.content) {
              ctx.save();
              ctx.font = '600 24px -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';
              ctx.textAlign = 'center';
              ctx.textBaseline = 'middle';

              const maxTextWidth = width * 0.75;
              const words = sc.content.split(' ');
              const lines = [];
              let line = '';
              for (const w of words) {
                const testLine = line + w + ' ';
                if (ctx.measureText(testLine).width > maxTextWidth) {
                  lines.push(line.trim());
                  line = w + ' ';
                } else {
                  line = testLine;
                }
              }
              if (line) lines.push(line.trim());

              const lineHeight = 36;
              const boxHeight = lines.length * lineHeight + 24;
              let boxY = height - boxHeight - 40;
              if (sc.subtitlePos === 'top') {
                boxY = 40;
              } else if (sc.subtitlePos === 'middle') {
                boxY = (height - boxHeight) / 2;
              }

              // Subtitle Card Background
              ctx.fillStyle = 'rgba(15, 23, 42, 0.85)';
              ctx.strokeStyle = 'rgba(255, 255, 255, 0.15)';
              ctx.lineWidth = 1.5;
              const boxWidth = Math.min(width * 0.85, maxTextWidth + 48);
              const boxX = (width - boxWidth) / 2;

              roundRect(ctx, boxX, boxY, boxWidth, boxHeight, 8);
              ctx.fill();
              ctx.stroke();

              // Subtitle Text
              ctx.fillStyle = '#f8fafc';
              ctx.shadowColor = 'rgba(0, 0, 0, 0.8)';
              ctx.shadowBlur = 6;
              lines.forEach((l, idx) => {
                ctx.fillText(l, width / 2, boxY + 20 + idx * lineHeight + 12);
              });

              ctx.restore();
            }

            // Next frame
            await new Promise(r => requestAnimationFrame(r));
          }

          if (audioEl) {
            audioEl.pause();
            audioEl = null;
          }
        }

        if (recordBarFill) recordBarFill.style.width = '100%';
        if (recordText) recordText.textContent = 'Hoàn tất đóng gói video...';

        recorder.stop();
        await recordPromise;

        const blob = new Blob(chunks, { type: mimeType });
        const ext = mimeType.includes('mp4') ? 'mp4' : 'webm';
        const projectTitle = document.getElementById('studioProjectTitle')?.textContent?.trim() || 'video';
        const safeName = projectTitle.toLowerCase().replace(/[^a-z0-9_-]/gi, '_');

        const downloadUrl = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = downloadUrl;
        a.download = `${safeName}_cowai_studio.${ext}`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);

        showStudioToast('Xuất video thành công! File đã được tải về máy.');
      } catch (err) {
        console.error('[Export] Lỗi khi xuất video:', err);
        alert('Lỗi trong quá trình kết xuất video: ' + err.message);
      } finally {
        isExporting = false;
        btnExportVideo.disabled = false;
        if (recordOverlay) recordOverlay.classList.add('hidden');
        if (audioCtx) audioCtx.close();
      }
    }

    function roundRect(ctx, x, y, width, height, radius) {
      ctx.beginPath();
      ctx.moveTo(x + radius, y);
      ctx.lineTo(x + width - radius, y);
      ctx.quadraticCurveTo(x + width, y, x + width, y + radius);
      ctx.lineTo(x + width, y + height - radius);
      ctx.quadraticCurveTo(x + width, y + height, x + width - radius, y + height);
      ctx.lineTo(x + radius, y + height);
      ctx.quadraticCurveTo(x, y + height, x, y + height - radius);
      ctx.lineTo(x, y + radius);
      ctx.quadraticCurveTo(x, y, x + radius, y);
      ctx.closePath();
    }

    // New project button (opens Document Intake & Plan Wizard)
    const btnNew = document.getElementById('btnNewProject');
    if (btnNew) {
      btnNew.addEventListener('click', function () {
        const planModal = document.getElementById('studioPlanModal');
        if (planModal) {
          planModal.classList.remove('hidden');
          planModal.setAttribute('aria-hidden', 'false');
          const tabBtn = document.getElementById('tabBtnIntake');
          if (tabBtn) tabBtn.click();
        } else {
          const title = prompt('Nhập tên dự án Studio mới:');
          if (!title || !title.trim()) return;

          fetch('/api/studio/projects', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ title: title.trim() })
          })
            .then(res => res.json())
            .then(proj => {
              if (proj && proj.id) {
                window.location.href = `/studio/${proj.id}`;
              }
            })
            .catch(err => alert('Không thể tạo dự án: ' + err.message));
        }
      });
    }

    // Delete current project
    const btnDelete = document.getElementById('btnDeleteCurrentProject');
    if (btnDelete) {
      btnDelete.addEventListener('click', function () {
        if (!confirm('Bạn có chắc chắn muốn xóa dự án Studio này không?')) return;
        fetch(`/api/studio/projects/${projectId}`, { method: 'DELETE' })
          .then(res => res.json())
          .then(() => {
            window.location.href = '/studio';
          })
          .catch(err => alert('Lỗi khi xóa dự án: ' + err.message));
      });
    }

    function showStudioToast(msg) {
      const toast = document.createElement('div');
      toast.className = 'app-toast glass-panel animate-fade-in';
      toast.innerHTML = `
        <div class="app-toast__content">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#38bdf8" stroke-width="2">
            <circle cx="12" cy="12" r="10"></circle>
            <polyline points="12 6 12 12 14 14"></polyline>
          </svg>
          <span>${msg}</span>
        </div>
      `;
      document.body.appendChild(toast);
      setTimeout(() => {
        if (toast.parentNode) toast.parentNode.removeChild(toast);
      }, 4000);
    }

    // =========================================================================
    // Auto-Video Plan Modal & VieNeu Pipeline Integration
    // =========================================================================
    let uploadedAvatarUrls = [];
    let currentVideoType = 'comparison';
    let currentAspectRatio = '16:9';

    // Aspect Ratio Toggle (16:9 vs 9:16)
    const btnAspect169 = document.getElementById('btnAspect169');
    const btnAspect916 = document.getElementById('btnAspect916');
    const stageWrapper = document.getElementById('stageCanvasWrapper') || document.getElementById('stageWrapper');
    const stageCyberCanvas = document.getElementById('stageCyberCanvas');

    function setStageAspectRatio(ratio) {
      const isPortrait = ratio === '9:16';
      currentAspectRatio = isPortrait ? '9:16' : '16:9';
      if (btnAspect169) btnAspect169.classList.toggle('active', !isPortrait);
      if (btnAspect916) btnAspect916.classList.toggle('active', isPortrait);
      if (stageWrapper) {
        stageWrapper.classList.toggle('aspect-9-16', isPortrait);
        stageWrapper.classList.toggle('aspect-16-9', !isPortrait);
      }
      if (stageCyberCanvas) {
        stageCyberCanvas.width = isPortrait ? 360 : 640;
        stageCyberCanvas.height = isPortrait ? 640 : 360;
      }
    }

    if (btnAspect169 && btnAspect916) {
      btnAspect169.addEventListener('click', () => setStageAspectRatio('16:9'));
      btnAspect916.addEventListener('click', () => setStageAspectRatio('9:16'));
    }

    // Backend Video Render (Dual Mode: FFmpeg Enhanced & HyperFrames 3-Zone)
    const btnRenderBackend = document.getElementById('btnRenderBackendVideo');
    if (btnRenderBackend) {
      btnRenderBackend.addEventListener('click', function () {
        if (!projectId) return;

        if (container.dataset.authenticated !== 'true') {
          if (typeof global.openStudioLoginModal === 'function') {
            global.openStudioLoginModal();
          } else {
            window.location.href = '/Account/Login?ReturnUrl=' + encodeURIComponent(window.location.pathname);
          }
          return;
        }

        const renderMode = document.getElementById('studioRenderModeSelect')?.value || 'fast';
        btnRenderBackend.disabled = true;
        const originalHtml = btnRenderBackend.innerHTML;
        btnRenderBackend.innerHTML = renderMode === 'hyperframes'
          ? '<span>Đang render HyperFrames...</span>'
          : '<span>Đang render MP4...</span>';
        showStudioToast(`Đang xuất video ${currentAspectRatio} (${renderMode === 'hyperframes' ? 'HyperFrames 3-Zone' : 'FFmpeg Nâng cấp'})...`);

        fetch(`/api/studio/projects/${projectId}/render-backend-video`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            aspectRatio: currentAspectRatio,
            includeBgm: true,
            renderMode: renderMode
          })
        })
          .then(res => res.json())
          .then(data => {
            if (data && data.success && data.videoUrl) {
              showStudioToast(`Render video thành công (${data.duration ? data.duration.toFixed(1) : ''}s)! Đang tải về...`);
              const a = document.createElement('a');
              a.href = data.videoUrl;
              a.download = `video_proj_${projectId}.mp4`;
              document.body.appendChild(a);
              a.click();
              document.body.removeChild(a);
            } else {
              alert('Lỗi render video: ' + (data?.error || 'Không xác định'));
            }
          })
          .catch(err => {
            console.error('[RenderBackendVideo]', err);
            alert('Lỗi kết nối khi render video trên server.');
          })
          .finally(() => {
            btnRenderBackend.disabled = false;
            btnRenderBackend.innerHTML = originalHtml;
          });
      });
    }

    const btnOpenPlanModal = document.getElementById('btnOpenPlanModal');
    const planModal = document.getElementById('studioPlanModal');
    const btnClosePlanModal = document.getElementById('btnClosePlanModal');
    const btnCancelPlanModal = document.getElementById('btnCancelPlanModal');
    const tabBtnIntake = document.getElementById('tabPlanIntake') || document.getElementById('tabBtnIntake');
    const tabBtnReview = document.getElementById('tabPlanReview') || document.getElementById('tabBtnReview');
    const paneIntake = document.getElementById('paneIntake');
    const paneReview = document.getElementById('paneReview');

    const fileDropzone = document.getElementById('fileDropzone');
    const inputSourceFile = document.getElementById('sourceFileInput') || document.getElementById('inputSourceFile');
    const selectedFileInfo = document.getElementById('selectedFileInfo');
    const planFileDropzone = document.getElementById('planFileDropzone');
    const planSourceFileInput = document.getElementById('planSourceFileInput');
    const planSelectedFileInfo = document.getElementById('planSelectedFileInfo');
    const fileDropLabel = document.getElementById('fileDropLabel');

    const avatarDropzone = document.getElementById('avatarDropzone');
    const inputAvatars = document.getElementById('avatarFileInput') || document.getElementById('inputAvatars');
    const selectedAvatarInfo = document.getElementById('selectedAvatarInfo');
    const planAvatarDropzone = document.getElementById('planAvatarDropzone');
    const planAvatarFileInput = document.getElementById('planAvatarFileInput');
    const avatarPreviewStrip = document.getElementById('avatarPreviewStrip');
    const btnTypeComparison = document.getElementById('btnTypeComparison');
    const btnTypeSequential = document.getElementById('btnTypeSequential');
    const planVoiceSelect = document.getElementById('planVoiceSelect');
    const inputSourceContent = document.getElementById('inputSourceContent');
    const btnGeneratePlan = document.getElementById('btnGeneratePlan');
    const planMarkdownEditor = document.getElementById('planMarkdownEditor');
    const planReviewCards = document.getElementById('planReviewCards');
    const btnTogglePlanEditMode = document.getElementById('btnTogglePlanEditMode');
    const btnProceedPlan = document.getElementById('btnProceedPlan');

    function openPlanModal() {
      if (planModal) {
        planModal.classList.remove('hidden');
        planModal.setAttribute('aria-hidden', 'false');
        switchPlanTab('intake');
      }
    }

    function closePlanModal() {
      if (planModal) {
        planModal.classList.add('hidden');
        planModal.setAttribute('aria-hidden', 'true');
      }
    }

    if (planModal) {
      planModal.addEventListener('click', (e) => {
        if (e.target === planModal) closePlanModal();
      });
      document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && !planModal.classList.contains('hidden')) {
          closePlanModal();
        }
      });
    }

    function switchPlanTab(tab) {
      const btnGen = document.getElementById('btnGeneratePlan');
      const btnProc = document.getElementById('btnProceedPlan');
      const btnBack = document.getElementById('btnBackToIntake');
      const btnClose = document.getElementById('btnCancelPlanModal');

      if (tab === 'intake') {
        if (paneIntake) paneIntake.classList.remove('hidden');
        if (paneReview) paneReview.classList.add('hidden');
        if (tabBtnIntake) tabBtnIntake.classList.add('active');
        if (tabBtnReview) tabBtnReview.classList.remove('active');
        if (btnGen) btnGen.classList.remove('hidden');
        if (btnProc) btnProc.classList.add('hidden');
        if (btnBack) btnBack.classList.add('hidden');
        if (btnClose) btnClose.classList.remove('hidden');
      } else {
        if (paneIntake) paneIntake.classList.add('hidden');
        if (paneReview) paneReview.classList.remove('hidden');
        if (tabBtnIntake) tabBtnIntake.classList.remove('active');
        if (tabBtnReview) tabBtnReview.classList.add('active');
        if (btnGen) btnGen.classList.add('hidden');
        if (btnProc) btnProc.classList.remove('hidden');
        if (btnBack) btnBack.classList.remove('hidden');
        if (btnClose) btnClose.classList.add('hidden');
      }
    }

    if (btnOpenPlanModal) btnOpenPlanModal.addEventListener('click', openPlanModal);
    if (btnClosePlanModal) btnClosePlanModal.addEventListener('click', closePlanModal);
    if (btnCancelPlanModal) btnCancelPlanModal.addEventListener('click', closePlanModal);
    const btnBackToIntake = document.getElementById('btnBackToIntake');
    if (btnBackToIntake) btnBackToIntake.addEventListener('click', () => switchPlanTab('intake'));
    if (tabBtnIntake) tabBtnIntake.addEventListener('click', () => switchPlanTab('intake'));
    if (tabBtnReview) tabBtnReview.addEventListener('click', () => switchPlanTab('review'));

    // Video Type Toggle
    if (btnTypeComparison && btnTypeSequential) {
      btnTypeComparison.addEventListener('click', () => {
        currentVideoType = 'comparison';
        btnTypeComparison.classList.add('active');
        btnTypeSequential.classList.remove('active');
      });
      btnTypeSequential.addEventListener('click', () => {
        currentVideoType = 'sequential';
        btnTypeSequential.classList.add('active');
        btnTypeComparison.classList.remove('active');
      });
    }

    function bindFileDropzone(zoneEl, inputEl, onFiles) {
      if (!zoneEl || !inputEl) return;
      zoneEl.addEventListener('click', (e) => {
        if (e.target !== inputEl) inputEl.click();
      });
      zoneEl.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          inputEl.click();
        }
      });
      zoneEl.addEventListener('dragover', (e) => {
        e.preventDefault();
        zoneEl.classList.add('drag-active');
      });
      zoneEl.addEventListener('dragleave', () => zoneEl.classList.remove('drag-active'));
      zoneEl.addEventListener('drop', (e) => {
        e.preventDefault();
        zoneEl.classList.remove('drag-active');
        if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length > 0) {
          onFiles(e.dataTransfer.files);
        }
      });
      inputEl.addEventListener('change', () => {
        if (inputEl.files && inputEl.files.length > 0) {
          onFiles(inputEl.files);
          inputEl.value = '';
        }
      });
    }

    // Source File Upload (cả Sidebar Assets lẫn Plan Modal)
    bindFileDropzone(fileDropzone, inputSourceFile, (files) => handleSourceFileUpload(files[0]));
    bindFileDropzone(planFileDropzone, planSourceFileInput, (files) => handleSourceFileUpload(files[0]));

    function setSourceFileStatus(text, isSuccess) {
      if (fileDropLabel) fileDropLabel.textContent = text;
      [selectedFileInfo, planSelectedFileInfo].forEach(el => {
        if (!el) return;
        el.textContent = text;
        el.classList.remove('hidden');
        el.classList.toggle('is-success', !!isSuccess);
      });
    }

    async function handleSourceFileUpload(file) {
      if (!file || !projectId) return;
      if (file.size > 100 * 1024 * 1024) {
        setSourceFileStatus('Tệp vượt quá giới hạn 100 MB', false);
        showStudioToast('Tệp vượt quá giới hạn 100 MB.');
        return;
      }

      setSourceFileStatus(`Đang tải lên: ${file.name}...`, false);

      const formData = new FormData();
      formData.append('file', file);

      const controller = new AbortController();
      const timer = setTimeout(() => controller.abort(), 30000);

      try {
        const res = await fetch(`/api/studio/projects/${projectId}/upload-source`, {
          method: 'POST',
          body: formData,
          signal: controller.signal
        });
        clearTimeout(timer);

        if (res.status === 401 || res.status === 403) {
          setSourceFileStatus('Cần đăng nhập để nạp tài liệu', false);
          if (typeof window.openStudioAuthModal === 'function') window.openStudioAuthModal();
          return;
        }
        if (res.status === 413) {
          setSourceFileStatus('Tệp quá lớn (tối đa 100 MB)', false);
          showStudioToast('Tệp vượt quá giới hạn dung lượng 100 MB.');
          return;
        }

        const rawText = await res.text();
        let data = null;
        try { data = rawText ? JSON.parse(rawText) : null; } catch { data = null; }

        if (res.ok && data && data.success) {
          const label = `✓ Đã nạp: ${data.sourceFileName || file.name}`;
          setSourceFileStatus(label, true);
          if (inputSourceContent && data.sourceContent) {
            inputSourceContent.value = data.sourceContent;
            showStudioToast(`Đã trích xuất nội dung từ ${data.sourceFileName || file.name}`);
          } else {
            showStudioToast(data.warning || `Đã nạp ${data.sourceFileName || file.name} nhưng chưa tìm thấy văn bản trong tệp.`);
          }
        } else {
          const errMsg = data?.error || `Máy chủ trả về mã lỗi ${res.status}`;
          setSourceFileStatus(`Lỗi tải tệp: ${errMsg}`, false);
          showStudioToast(errMsg);
        }
      } catch (err) {
        clearTimeout(timer);
        console.error('[UploadSource]', err);
        const msg = err && err.name === 'AbortError'
          ? 'Hết thời gian tải tệp (30s) — thử tệp gọn hơn'
          : 'Lỗi kết nối khi tải tệp — thử lại';
        setSourceFileStatus(msg, false);
        showStudioToast(msg);
      }
    }

    // Avatars Upload (0 - 3 images, cả Sidebar Assets lẫn Plan Modal)
    async function handleAvatarsUpload(files) {
      if (!files || files.length === 0 || !projectId) return;
      const formData = new FormData();
      const filesToUpload = Array.from(files).slice(0, 3);
      filesToUpload.forEach(f => formData.append('avatars', f));

      try {
        const res = await fetch(`/api/studio/projects/${projectId}/upload-source`, {
          method: 'POST',
          body: formData
        });
        const data = await res.json().catch(() => null);
        if (res.status === 401 || res.status === 403) {
          if (typeof window.openStudioAuthModal === 'function') window.openStudioAuthModal();
          return;
        }
        if (res.ok && data && data.success && data.avatarUrls) {
          uploadedAvatarUrls = data.avatarUrls;
          renderAvatarPreviewStrip();
          if (selectedAvatarInfo) {
            selectedAvatarInfo.textContent = `✓ Đã chọn ${data.avatarUrls.length} ảnh giảng viên`;
            selectedAvatarInfo.classList.remove('hidden');
            selectedAvatarInfo.classList.add('is-success');
          }
          showStudioToast(`Đã cập nhật ${data.avatarUrls.length} ảnh nhân vật.`);
        } else {
          showStudioToast(data?.error || 'Không thể tải ảnh giảng viên.');
        }
      } catch (err) {
        console.error('[UploadAvatars]', err);
        showStudioToast('Lỗi kết nối khi tải ảnh giảng viên.');
      }
    }

    bindFileDropzone(avatarDropzone, inputAvatars, handleAvatarsUpload);
    bindFileDropzone(planAvatarDropzone, planAvatarFileInput, handleAvatarsUpload);

    function renderAvatarPreviewStrip() {
      if (!avatarPreviewStrip) return;
      avatarPreviewStrip.innerHTML = '';
      if (uploadedAvatarUrls.length === 0) {
        avatarPreviewStrip.innerHTML = '<span style="font-size:0.75rem; color:var(--cow-text-muted);">Chưa chọn ảnh (Chế độ Cinematic B-Roll tự sinh cảnh)</span>';
        return;
      }

      uploadedAvatarUrls.forEach((url, i) => {
        const item = document.createElement('div');
        item.className = 'avatar-preview-item';
        item.innerHTML = `
          <img src="${url}" alt="Avatar ${i + 1}" />
          <button type="button" class="btn-remove-avatar" data-index="${i}" title="Xóa ảnh">✕</button>
        `;
        avatarPreviewStrip.appendChild(item);
      });

      avatarPreviewStrip.querySelectorAll('.btn-remove-avatar').forEach(btn => {
        btn.addEventListener('click', (e) => {
          e.stopPropagation();
          const idx = parseInt(btn.dataset.index, 10);
          uploadedAvatarUrls.splice(idx, 1);
          renderAvatarPreviewStrip();
        });
      });
    }

    // Generate Plan Markdown
    if (btnGeneratePlan) {
      btnGeneratePlan.addEventListener('click', () => {
        if (!projectId) return;
        const sourceContent = inputSourceContent ? inputSourceContent.value.trim() : '';
        const voice = planVoiceSelect ? planVoiceSelect.value : 'Hải Đăng';

        btnGeneratePlan.disabled = true;
        btnGeneratePlan.innerHTML = '<span>Đang lên kế hoạch phân cảnh...</span>';

        fetch(`/api/studio/projects/${projectId}/generate-plan`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            sourceContent: sourceContent,
            videoType: currentVideoType,
            voice: voice,
            avatarUrls: uploadedAvatarUrls
          })
        })
          .then(res => res.json())
          .then(data => {
            if (data && data.success && data.planMarkdown) {
              if (planMarkdownEditor) planMarkdownEditor.value = data.planMarkdown;
              renderMarkdownCards(data.planMarkdown);
              if (btnProceedPlan) btnProceedPlan.disabled = false;
              switchPlanTab('review');
              showStudioToast('Kế hoạch phân cảnh Markdown đã sẵn sàng!');
            } else {
              alert('Lỗi tạo kế hoạch: ' + (data?.error || 'Không xác định'));
            }
          })
          .catch(err => {
            console.error('[GeneratePlan]', err);
            alert('Lỗi kết nối khi tạo kế hoạch.');
          })
          .finally(() => {
            btnGeneratePlan.disabled = false;
            btnGeneratePlan.innerHTML = `
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polyline>
              </svg>
              <span>Lên kế hoạch phân cảnh AI (.md)</span>
            `;
          });
      });
    }

    // Toggle Markdown Editor vs Visual Cards Preview
    let isEditingRawMarkdown = false;
    if (btnTogglePlanEditMode && planMarkdownEditor && planReviewCards) {
      btnTogglePlanEditMode.addEventListener('click', () => {
        isEditingRawMarkdown = !isEditingRawMarkdown;
        if (isEditingRawMarkdown) {
          planMarkdownEditor.style.display = 'block';
          planReviewCards.style.display = 'none';
          btnTogglePlanEditMode.textContent = 'Xem dạng thẻ trực quan 👁';
        } else {
          renderMarkdownCards(planMarkdownEditor.value);
          planMarkdownEditor.style.display = 'none';
          planReviewCards.style.display = 'grid';
          btnTogglePlanEditMode.textContent = 'Chỉnh sửa kịch bản ✎';
        }
      });
    }

    function renderMarkdownCards(markdown) {
      if (!planReviewCards) return;
      planReviewCards.innerHTML = '';
      if (!markdown || !markdown.trim()) return;

      const lines = markdown.split('\n');
      const scenesFound = [];
      let currentSc = null;

      lines.forEach(line => {
        const trimmed = line.trim();
        if (trimmed.startsWith('## Cảnh') || trimmed.startsWith('### Cảnh') || trimmed.startsWith('## Scene')) {
          if (currentSc) scenesFound.push(currentSc);
          currentSc = { title: trimmed.replace(/^#+\s*/, ''), duration: 5, content: '', prompt: '', layout: '' };
        } else if (currentSc) {
          if (trimmed.startsWith('- Thời lượng:')) {
            const match = trimmed.match(/(\d+(\.\d+)?)/);
            if (match) currentSc.duration = parseFloat(match[1]);
          } else if (trimmed.startsWith('- Lời thoại:')) {
            currentSc.content = trimmed.replace('- Lời thoại:', '').trim().replace(/^"|"$/g, '');
          } else if (trimmed.startsWith('- Prompt ảnh:')) {
            currentSc.prompt = trimmed.replace('- Prompt ảnh:', '').trim().replace(/^"|"$/g, '');
          } else if (trimmed.startsWith('- Bố cục:') || trimmed.startsWith('- Layout:')) {
            currentSc.layout = trimmed.replace(/^-[^:]+:/, '').trim();
          }
        }
      });
      if (currentSc) scenesFound.push(currentSc);

      if (scenesFound.length === 0) {
        planReviewCards.innerHTML = '<div style="padding: 1rem; color: var(--cow-text-muted);">Không có phân cảnh cụ thể trong kịch bản. Hãy kiểm tra nội dung Markdown.</div>';
        return;
      }

      scenesFound.forEach((sc, i) => {
        const card = document.createElement('div');
        card.className = 'plan-review-card glass-panel';
        card.innerHTML = `
          <div class="plan-card-header">
            <span class="plan-card-badge">Cảnh ${i + 1} (${sc.duration}s)</span>
            <span class="plan-card-layout font-mono">${escapeHtml(sc.layout || '3-zone layout')}</span>
          </div>
          <div class="plan-card-title">${escapeHtml(sc.title)}</div>
          <div class="plan-card-dialogue">"${escapeHtml(sc.content || '(Chưa có lời thoại)')}"</div>
          ${sc.prompt ? `<div class="plan-card-prompt">🎨 ${escapeHtml(sc.prompt)}</div>` : ''}
        `;
        planReviewCards.appendChild(card);
      });
    }

    // Proceed Plan (Chạy ngay) -> Apply & Auto Produce
    if (btnProceedPlan) {
      btnProceedPlan.addEventListener('click', () => {
        if (!projectId) return;

        if (container.dataset.isAuthenticated !== 'true') {
          if (confirm('Vui lòng đăng nhập tài khoản để áp dụng kịch bản và sản xuất video bài giảng.\nBấm OK để chuyển đến trang Đăng nhập.')) {
            window.location.href = '/Account/Login?ReturnUrl=' + encodeURIComponent(window.location.pathname);
          }
          return;
        }

        const md = planMarkdownEditor ? planMarkdownEditor.value.trim() : '';
        if (!md) {
          alert('Chưa có nội dung kế hoạch Markdown để áp dụng.');
          return;
        }

        btnProceedPlan.disabled = true;
        btnProceedPlan.innerHTML = '<span>Đang sản xuất video & âm thanh...</span>';

        fetch(`/api/studio/projects/${projectId}/apply-plan`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            planMarkdown: md,
            autoProduce: true
          })
        })
          .then(res => res.json())
          .then(data => {
            if (data && data.success) {
              showStudioToast('Đã phê duyệt và sản xuất thành công kịch bản!');
              closePlanModal();
              setTimeout(() => {
                window.location.reload();
              }, 600);
            } else {
              alert('Lỗi khi áp dụng kịch bản: ' + (data?.error || 'Không xác định'));
              btnProceedPlan.disabled = false;
            }
          })
          .catch(err => {
            console.error('[ApplyPlan]', err);
            alert('Lỗi kết nối khi sản xuất kịch bản.');
            btnProceedPlan.disabled = false;
          });
      });
    }

    // =========================================================================
    // Studio Conversational Assistant Chat (HyperFrames Bridge)
    // =========================================================================
    const studioChatForm = document.getElementById('studioChatForm');
    const studioChatInput = document.getElementById('studioChatInput');
    const studioChatMessages = document.getElementById('studioChatMessages');

    document.querySelectorAll('.chat-quick-pill').forEach(pill => {
      pill.addEventListener('click', function () {
        const promptText = this.dataset.prompt;
        if (promptText && studioChatInput) {
          studioChatInput.value = promptText;
          studioChatInput.focus();
        }
      });
    });

    if (studioChatForm && studioChatInput && studioChatMessages) {
      studioChatForm.addEventListener('submit', function (e) {
        e.preventDefault();
        const instruction = studioChatInput.value.trim();
        if (!instruction || !projectId) return;

        // Append user message
        const userMsg = document.createElement('div');
        userMsg.className = 'studio-chat-msg studio-chat-msg--user';
        userMsg.textContent = instruction;
        studioChatMessages.appendChild(userMsg);

        // Append loading assistant bubble
        const botMsg = document.createElement('div');
        botMsg.className = 'studio-chat-msg studio-chat-msg--assistant';
        botMsg.innerHTML = '<span class="studio-chat-typing">Trợ lý AI đang xử lý chỉ lệnh...</span>';
        studioChatMessages.appendChild(botMsg);
        studioChatMessages.scrollTop = studioChatMessages.scrollHeight;

        studioChatInput.value = '';
        studioChatInput.disabled = true;

        fetch(`/api/studio/projects/${projectId}/chat-edit`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ instruction: instruction })
        })
          .then(res => res.json())
          .then(data => {
            if (data && data.success) {
              botMsg.innerHTML = data.message || 'Đã cập nhật theo chỉ lệnh của bạn.';
              // Rescan scenes & refresh timeline/audio/stage
              scanScenes();
              // If preview iframe is loaded, refresh it
              if (compositionIframe && compositionIframe.src && !compositionIframe.src.includes('about:blank')) {
                compositionIframe.src = `/studio/${projectId}/preview?t=${Date.now()}`;
              }
            } else {
              botMsg.innerHTML = `<span style="color:#f87171;">Lỗi: ${data?.error || 'Không thể xử lý chỉ lệnh'}</span>`;
            }
            studioChatMessages.scrollTop = studioChatMessages.scrollHeight;
          })
          .catch(err => {
            botMsg.innerHTML = '<span style="color:#f87171;">Lỗi kết nối máy chủ khi gửi chỉ lệnh.</span>';
            console.error('[StudioChat]', err);
          })
          .finally(() => {
            studioChatInput.disabled = false;
            studioChatInput.focus();
          });
      });
    }

    function escapeHtml(text) {
      if (!text) return '';
      return text
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#039;');
    }

    // =========================================================================
    // Left Sidebar Tab Switching (Phân cảnh / Dự án & Nguồn / Trợ lý AI)
    // =========================================================================
    const sidebarTabBtns = document.querySelectorAll('.studio-sidebar-tab, .studio-tab-nav-btn');
    const sidebarTabPanes = document.querySelectorAll('.studio-tab-pane');
    if (sidebarTabBtns.length > 0) {
      sidebarTabBtns.forEach(btn => {
        btn.addEventListener('click', function () {
          const tab = (this.id.replace('tabNav', '') || this.dataset.tab || '').toLowerCase();
          sidebarTabBtns.forEach(b => {
            b.classList.toggle('active', b === this);
            b.setAttribute('aria-selected', b === this ? 'true' : 'false');
          });
          sidebarTabPanes.forEach(pane => {
            const isMatch = (pane.id.toLowerCase() === 'pane' + tab);
            pane.classList.toggle('active', isMatch);
            pane.classList.toggle('hidden', !isMatch);
          });
        });
      });
    }

    // =========================================================================
    // Global Studio Hub & Dual-View Functions
    // =========================================================================
    global.openStudioLoginModal = function () {
      const modal = document.getElementById('studioAuthModal');
      if (modal) {
        modal.classList.remove('hidden');
        modal.style.display = 'flex';
        const userInp = document.getElementById('studioLoginUser');
        if (userInp) userInp.focus();
      }
    };

    global.closeStudioLoginModal = function () {
      const modal = document.getElementById('studioAuthModal');
      if (modal) {
        modal.classList.add('hidden');
        modal.style.display = 'none';
      }
      const errBox = document.getElementById('authAlertError');
      if (errBox) {
        errBox.classList.add('hidden');
        errBox.textContent = '';
      }
    };

    // Close auth modal with button or backdrop
    const btnCloseAuth = document.getElementById('btnCloseAuthModal');
    if (btnCloseAuth) {
      btnCloseAuth.addEventListener('click', global.closeStudioLoginModal);
    }
    const authModal = document.getElementById('studioAuthModal');
    if (authModal) {
      authModal.addEventListener('click', function (e) {
        if (e.target === authModal) {
          global.closeStudioLoginModal();
        }
      });
      document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && !authModal.classList.contains('hidden')) {
          global.closeStudioLoginModal();
        }
      });
      authModal.addEventListener('keydown', function (e) {
        if (e.key === 'Tab') {
          const focusable = authModal.querySelectorAll('button:not([disabled]), input:not([disabled])');
          if (focusable.length > 0) {
            const first = focusable[0];
            const last = focusable[focusable.length - 1];
            if (e.shiftKey && document.activeElement === first) {
              e.preventDefault();
              last.focus();
            } else if (!e.shiftKey && document.activeElement === last) {
              e.preventDefault();
              first.focus();
            }
          }
        }
      });
    }

    // Auth Tabs Switch
    const tabAuthLogin = document.getElementById('tabAuthLogin');
    const tabAuthRegister = document.getElementById('tabAuthRegister');
    const formStudioLogin = document.getElementById('formStudioLogin');
    const formStudioRegister = document.getElementById('formStudioRegister');
    if (tabAuthLogin && tabAuthRegister && formStudioLogin && formStudioRegister) {
      tabAuthLogin.addEventListener('click', function () {
        tabAuthLogin.classList.add('active');
        tabAuthRegister.classList.remove('active');
        formStudioLogin.classList.remove('hidden');
        formStudioRegister.classList.add('hidden');
        document.getElementById('studioLoginUser')?.focus();
        const title = document.getElementById('studioAuthTitle');
        if (title) title.textContent = 'Đăng nhập';
      });
      tabAuthRegister.addEventListener('click', function () {
        tabAuthRegister.classList.add('active');
        tabAuthLogin.classList.remove('active');
        formStudioRegister.classList.remove('hidden');
        formStudioLogin.classList.add('hidden');
        document.getElementById('studioRegUser')?.focus();
        const title = document.getElementById('studioAuthTitle');
        if (title) title.textContent = 'Tạo tài khoản';
      });
    }

    // Toggle Password Visibility Eye
    global.togglePasswordVisibility = function (inputId, btnEl) {
      const input = document.getElementById(inputId);
      if (!input) return;
      const isPass = input.type === 'password';
      input.type = isPass ? 'text' : 'password';
      if (btnEl) {
        const eyeOpen = btnEl.querySelector('.eye-open');
        const eyeClosed = btnEl.querySelector('.eye-closed');
        if (eyeOpen && eyeClosed) {
          if (isPass) {
            eyeOpen.classList.add('hidden');
            eyeClosed.classList.remove('hidden');
          } else {
            eyeOpen.classList.remove('hidden');
            eyeClosed.classList.add('hidden');
          }
        }
      }
    };

    // Login via AJAX API
    global.handleStudioLogin = async function (e) {
      if (e) e.preventDefault();
      const user = document.getElementById('studioLoginUser')?.value?.trim();
      const pass = document.getElementById('studioLoginPass')?.value;
      const errBox = document.getElementById('authAlertError');
      const submitBtn = document.getElementById('btnSubmitStudioLogin');

      if (!user || !pass) {
        if (errBox) {
          errBox.textContent = 'Vui lòng điền đầy đủ tên đăng nhập và mật khẩu.';
          errBox.classList.remove('hidden');
        }
        return;
      }

      if (submitBtn) submitBtn.disabled = true;
      try {
        const res = await fetch('/api/account/login', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ username: user, password: pass })
        });
        const data = await res.json();
        if (res.ok && data.success) {
          if (container) container.dataset.authenticated = 'true';
          global.closeStudioLoginModal();
          // Update header
          const loginBtn = document.getElementById('btnHubOpenLogin');
          if (loginBtn) {
            loginBtn.outerHTML = `<div class="hub-account-badge"><span class="user-avatar-badge">${(data.user?.username || 'U').charAt(0).toUpperCase()}</span><span class="user-name">${data.user?.fullName || data.user?.username}</span></div>`;
          }
          // Open the 3-column create popover automatically
          const popover = document.getElementById('studioCreatePopover');
          if (popover) {
            popover.classList.remove('hidden');
          }
        } else {
          if (errBox) {
            errBox.textContent = data.error || 'Đăng nhập không thành công.';
            errBox.classList.remove('hidden');
          }
        }
      } catch (err) {
        if (errBox) {
          errBox.textContent = 'Lỗi kết nối máy chủ khi đăng nhập.';
          errBox.classList.remove('hidden');
        }
      } finally {
        if (submitBtn) submitBtn.disabled = false;
      }
    };

    // Register via AJAX API
    global.handleStudioRegister = async function (e) {
      if (e) e.preventDefault();
      const user = document.getElementById('studioRegUser')?.value?.trim();
      const email = document.getElementById('studioRegEmail')?.value?.trim();
      const pass = document.getElementById('studioRegPass')?.value;
      const passConfirm = document.getElementById('studioRegPassConfirm')?.value;
      const errBox = document.getElementById('authAlertError');
      const submitBtn = document.getElementById('btnSubmitStudioRegister');

      if (!user || !email || !pass) {
        if (errBox) {
          errBox.textContent = 'Vui lòng điền đầy đủ các thông tin đăng ký.';
          errBox.classList.remove('hidden');
        }
        return;
      }

      if (pass !== passConfirm) {
        if (errBox) {
          errBox.textContent = 'Mật khẩu và xác nhận mật khẩu không khớp.';
          errBox.classList.remove('hidden');
        }
        return;
      }

      if (submitBtn) submitBtn.disabled = true;
      try {
        const res = await fetch('/api/account/register', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ username: user, email: email, password: pass })
        });
        const data = await res.json();
        if (res.ok && data.success) {
          if (container) container.dataset.authenticated = 'true';
          global.closeStudioLoginModal();
          const popover = document.getElementById('studioCreatePopover');
          const backdrop = document.getElementById('studioCreatePopoverBackdrop');
          if (popover) popover.classList.remove('hidden');
          if (backdrop) backdrop.classList.remove('hidden');
        } else {
          if (errBox) {
            errBox.textContent = data.error || 'Đăng ký không thành công.';
            errBox.classList.remove('hidden');
          }
        }
      } catch (err) {
        if (errBox) {
          errBox.textContent = 'Lỗi kết nối máy chủ khi đăng ký.';
          errBox.classList.remove('hidden');
        }
      } finally {
        if (submitBtn) submitBtn.disabled = false;
      }
    };

    // Hub + Tạo mới button
    global.triggerCreateNew = function () {
      const isAuth = container ? container.dataset.authenticated === 'true' : false;
      if (!isAuth) {
        global.openStudioLoginModal();
        return;
      }
      const popover = document.getElementById('studioCreatePopover');
      const backdrop = document.getElementById('studioCreatePopoverBackdrop');
      if (popover) {
        const willShow = popover.classList.contains('hidden');
        if (willShow) {
          popover.classList.remove('hidden');
          if (backdrop) backdrop.classList.remove('hidden');
        } else {
          popover.classList.add('hidden');
          if (backdrop) backdrop.classList.add('hidden');
        }
      }
    };

    const btnHubCreate = document.getElementById('btnHubCreateNew');
    if (btnHubCreate) {
      btnHubCreate.addEventListener('click', function (e) {
        e.stopPropagation();
        global.triggerCreateNew();
      });
    }

    const popoverBackdrop = document.getElementById('studioCreatePopoverBackdrop');
    if (popoverBackdrop) {
      popoverBackdrop.addEventListener('click', function () {
        const popover = document.getElementById('studioCreatePopover');
        if (popover) popover.classList.add('hidden');
        popoverBackdrop.classList.add('hidden');
      });
    }

    // Dismiss popover on outside click
    document.addEventListener('click', function (e) {
      const popover = document.getElementById('studioCreatePopover');
      const backdrop = document.getElementById('studioCreatePopoverBackdrop');
      if (popover && !popover.classList.contains('hidden')) {
        const btn = document.getElementById('btnHubCreateNew');
        if (!popover.contains(e.target) && e.target !== btn && !btn?.contains(e.target)) {
          popover.classList.add('hidden');
          if (backdrop) backdrop.classList.add('hidden');
        }
      }
    });

    // View Switching
    global.switchToHubView = function () {
      const hub = document.getElementById('studioHubView');
      const editor = document.getElementById('studioEditorView');
      if (hub && editor) {
        hub.classList.add('active');
        editor.classList.remove('active');
        if (container) {
          container.classList.remove('view-mode-editor');
          container.classList.add('view-mode-hub');
        }
        window.history.pushState(null, '', '/studio');
      }
    };

    global.switchToEditorView = function (projId) {
      const hub = document.getElementById('studioHubView');
      const editor = document.getElementById('studioEditorView');
      if (hub && editor) {
        editor.classList.add('active');
        hub.classList.remove('active');
        if (container) {
          container.classList.remove('view-mode-hub');
          container.classList.add('view-mode-editor');
        }
        if (projId) {
          window.history.pushState(null, '', `/studio/editor/${projId}`);
        }
      }
    };

    global.openProjectInEditor = function (projId) {
      if (!projId) return;
      window.location.href = `/studio/editor/${projId}`;
    };

    global.handleCreateOption = async function (action, aspect) {
      const isAuth = container ? container.dataset.authenticated === 'true' : false;
      if (!isAuth) {
        global.openStudioLoginModal();
        return;
      }

      const title = `Bài giảng ${aspect} (${new Date().toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })})`;
      try {
        const res = await fetch('/api/studio/projects', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ title: title, voice: 'Hải Đăng', aspectRatio: aspect })
        });
        if (res.ok) {
          const newProj = await res.json();
          window.location.href = `/studio/editor/${newProj.id}`;
        } else if (res.status === 401) {
          global.openStudioLoginModal();
        }
      } catch (err) {
        console.error('Không thể tạo bài giảng:', err);
      }
    };

    global.triggerDocumentUpload = function (ext) {
      const isAuth = container ? container.dataset.authenticated === 'true' : false;
      if (!isAuth) {
        global.openStudioLoginModal();
        return;
      }
      const inp = document.getElementById('hubFileInputHidden');
      if (inp) {
        inp.accept = ext;
        inp.click();
      }
    };

    global.handleHubFileSelect = function (input) {
      if (input && input.files && input.files[0]) {
        const file = input.files[0];
        const badge = document.getElementById('hubFileBadge');
        if (badge) {
          badge.textContent = `📄 ${file.name}`;
          badge.classList.remove('hidden');
          badge.style.display = 'inline-block';
        }
        // Open Auto-Video plan modal and pass file
        global.openPlanModalFromHub(file);
      }
    };

    global.openPlanModalFromHub = function (file) {
      const isAuth = container ? container.dataset.authenticated === 'true' : false;
      if (!isAuth) {
        global.openStudioLoginModal();
        return;
      }
      global.switchToEditorView();
      const openBtn = document.getElementById('btnOpenPlanModal');
      if (openBtn) openBtn.click();
      if (file) {
        const fileInp = document.getElementById('sourceFileInput');
        if (fileInp) {
          const dt = new DataTransfer();
          dt.items.add(file);
          fileInp.files = dt.files;
          fileInp.dispatchEvent(new Event('change', { bubbles: true }));
        }
      }
    };

    global.openEditorWithVoice = function () {
      global.switchToEditorView();
      const tabScenes = document.getElementById('tabNavScenes');
      if (tabScenes) tabScenes.click();
    };

    global.submitHubPrompt = function () {
      const prompt = document.getElementById('hubPromptInput')?.value?.trim();
      const voice = document.getElementById('hubVoiceSelect')?.value || 'Hải Đăng';
      const isAuth = container ? container.dataset.authenticated === 'true' : false;
      if (!isAuth) {
        global.openStudioLoginModal();
        return;
      }
      if (!prompt) return;
      global.switchToEditorView();
      const openBtn = document.getElementById('btnOpenPlanModal');
      if (openBtn) openBtn.click();
      const contentInp = document.getElementById('inputSourceContent');
      if (contentInp) contentInp.value = prompt;
      const voiceSelect = document.getElementById('planVoiceSelect');
      if (voiceSelect) voiceSelect.value = voice;
    };

    global.handleHubSearch = function (q) {
      const query = (q || '').toLowerCase().trim();
      const cards = document.querySelectorAll('.hub-project-card');
      cards.forEach(card => {
        const title = card.dataset.title || '';
        card.style.display = (!query || title.includes(query)) ? '' : 'none';
      });
    };

    global.deleteProjectHub = async function (id, title) {
      if (!confirm(`Bạn có chắc chắn muốn xóa bài giảng "${title}" không?`)) return;
      try {
        const res = await fetch(`/api/studio/projects/${id}`, { method: 'DELETE' });
        if (res.ok) {
          const card = document.querySelector(`.hub-project-card[data-project-id="${id}"]`);
          if (card) card.remove();
          const countEl = document.getElementById('hubProjectsCount');
          if (countEl) {
            const current = parseInt(countEl.textContent, 10) || 1;
            countEl.textContent = Math.max(0, current - 1);
          }
          global.updateSelectedProjectsState();
        }
      } catch (err) {
        console.error('Lỗi khi xóa bài giảng:', err);
      }
    };

    // Bulk Project Selection & Deletion Logic
    global.toggleSelectAllProjects = function (masterCheck) {
      const checked = !!masterCheck?.checked;
      const checkboxes = document.querySelectorAll('.project-card-checkbox');
      checkboxes.forEach(cb => { cb.checked = checked; });
      global.updateSelectedProjectsState();
    };

    global.updateSelectedProjectsState = function () {
      const checkboxes = document.querySelectorAll('.project-card-checkbox');
      const checkedBoxes = document.querySelectorAll('.project-card-checkbox:checked');
      const count = checkedBoxes.length;
      const countEl = document.getElementById('selectedProjectsCount');
      if (countEl) countEl.textContent = count;
      const btnDel = document.getElementById('btnDeleteSelectedProjects');
      const textSpan = document.getElementById('btnDeleteSelectedText');
      if (btnDel) {
        if (count > 0) {
          btnDel.classList.remove('hidden');
          const isAll = checkboxes.length > 0 && count === checkboxes.length;
          if (textSpan) {
            textSpan.innerHTML = isAll ? `Xóa tất cả (${count})` : `Xóa đã chọn (${count})`;
          }
        } else {
          btnDel.classList.add('hidden');
        }
      }
      const masterCheck = document.getElementById('checkSelectAllProjects');
      if (masterCheck) {
        if (checkboxes.length === 0 || count === 0) {
          masterCheck.checked = false;
          masterCheck.indeterminate = false;
        } else if (count === checkboxes.length) {
          masterCheck.checked = true;
          masterCheck.indeterminate = false;
        } else {
          masterCheck.checked = false;
          masterCheck.indeterminate = true;
        }
      }
    };

    global.deleteSelectedProjects = async function () {
      const checkedBoxes = Array.from(document.querySelectorAll('.project-card-checkbox:checked'));
      const ids = checkedBoxes.map(cb => parseInt(cb.dataset.projectId, 10)).filter(id => !isNaN(id));
      if (ids.length === 0) return;
      if (!confirm(`Bạn có chắc chắn muốn xóa ${ids.length} bài giảng đã chọn không?`)) return;

      try {
        const res = await fetch('/api/studio/projects/bulk-delete', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ projectIds: ids })
        });
        if (res.ok) {
          ids.forEach(id => {
            const card = document.querySelector(`.hub-project-card[data-project-id="${id}"]`);
            if (card) card.remove();
          });
          const countEl = document.getElementById('hubProjectsCount');
          if (countEl) {
            const remaining = document.querySelectorAll('.hub-project-card').length;
            countEl.textContent = remaining;
          }
          global.updateSelectedProjectsState();
          if (document.querySelectorAll('.hub-project-card').length === 0) {
            window.location.reload();
          }
        }
      } catch (err) {
        console.error('Lỗi khi xóa bài giảng đã chọn:', err);
      }
    };

    global.deleteAllProjects = async function () {
      const allCards = Array.from(document.querySelectorAll('.hub-project-card'));
      const ids = allCards.map(c => parseInt(c.dataset.projectId, 10)).filter(id => !isNaN(id));
      if (ids.length === 0) return;
      if (!confirm('CẢNH BÁO: Bạn có chắc chắn muốn xóa TẤT CẢ bài giảng gần đây không? Hành động này không thể hoàn tác.')) return;

      try {
        const res = await fetch('/api/studio/projects/bulk-delete', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ projectIds: ids })
        });
        if (res.ok) {
          window.location.reload();
        }
      } catch (err) {
        console.error('Lỗi khi xóa tất cả bài giảng:', err);
      }
    };

    global.scrollToProjectsSection = function () {
      const sec = document.getElementById('sectionRecentProjects');
      if (sec) sec.scrollIntoView({ behavior: 'smooth' });
    };

    // Escape closes modals and popovers
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape') {
        global.closeStudioLoginModal();
        const popover = document.getElementById('studioCreatePopover');
        const backdrop = document.getElementById('studioCreatePopoverBackdrop');
        if (popover) popover.classList.add('hidden');
        if (backdrop) backdrop.classList.add('hidden');
      }
    });

    // Initialize scenes list
    scanScenes();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initStudio);
  } else {
    initStudio();
  }

})(typeof window !== 'undefined' ? window : this);
