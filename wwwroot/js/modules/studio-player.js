/**
 * COW.AI Studio Player & Multimedia Controller (v2.0)
 * Tiêu chí: Zero AI-Slop - Reactive State Machine - 60 FPS Canvas & Synchronized Playback
 * Hỗ trợ: MediaRecorder Video Export 100% Client-Side Offline, GSAP/Canvas Transforms, HyperFrames Preview
 */
(function (global) {
  'use strict';

  global.openProjectInEditor = function (projId) {
    if (!projId) return;
    window.location.href = `/studio/editor/${projId}`;
  };

  global.switchToHubView = function () {
    window.location.href = '/studio';
  };

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
    const btnToggleBgm = document.getElementById('btnTogglePlayerBgm');

    // Studio Multimedia Audio Engine (48kHz BGM Ducking & SFX)
    const clickAudio = new Audio('/assets/studio/audio/click.wav');
    clickAudio.volume = 0.25;

    const whooshAudio = new Audio('/assets/studio/audio/whoosh_48k.wav');
    whooshAudio.volume = 0.20;

    const hitAudio = new Audio('/assets/studio/audio/hit_48k.wav');
    hitAudio.volume = 0.16;

    const bgmAudio = new Audio('/assets/studio/audio/bgm.mp3');
    bgmAudio.loop = true;
    bgmAudio.volume = 0.18;
    let isBgmActive = true;

    function playClick() {
      try {
        clickAudio.currentTime = 0;
        clickAudio.play().catch(() => {});
      } catch {}
    }

    function playWhoosh() {
      if (!isBgmActive) return;
      try {
        whooshAudio.currentTime = 0;
        whooshAudio.play().catch(() => {});
      } catch {}
    }

    function playHit() {
      if (!isBgmActive) return;
      try {
        hitAudio.currentTime = 0;
        hitAudio.play().catch(() => {});
      } catch {}
    }

    function syncBgmDucking(hasVoice) {
      if (!isBgmActive) {
        bgmAudio.pause();
        return;
      }
      if (isPlaying) {
        bgmAudio.volume = hasVoice ? 0.07 : 0.22;
        if (bgmAudio.paused) {
          bgmAudio.play().catch(() => {});
        }
      } else {
        bgmAudio.pause();
      }
    }

    // State
    let scenes = [];
    let currentSceneIndex = 0;
    let isPlaying = false;
    let sceneTimer = null;
    let playbackTicker = null;
    let sceneElapsedSec = 0;
    let sceneStartedAtMs = 0;
    let isExporting = false;
    let stageImageErrorUrl = null;

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
              // Giữ nguyên phân cảnh đang được chọn, tuyệt đối không tự ý ép về Cảnh 1 (index 0)
              const currentActiveCard = document.querySelector('.scene-item.active, .scene-card.active');
              const activeIdx = currentActiveCard ? Array.from(sceneCards).indexOf(currentActiveCard) : -1;
              const keepIdx = (activeIdx >= 0)
                ? activeIdx
                : (currentSceneIndex >= 0 && currentSceneIndex < scenes.length ? currentSceneIndex : 0);
              loadScene(keepIdx, isPlaying);
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

    function getProjectTotalDuration() {
      return scenes.length > 0 ? scenes.reduce((acc, s) => acc + (Number(s.duration) || 5), 0) : 0;
    }

    function getSceneStartOffset(index) {
      let sum = 0;
      for (let i = 0; i < index && i < scenes.length; i++) {
        sum += Number(scenes[i].duration) || 5;
      }
      return sum;
    }

    function updatePlaybackClockUI(localSec) {
      const totalDur = getProjectTotalDuration();
      const offset = getSceneStartOffset(currentSceneIndex);
      const currentSceneDur = Number(scenes[currentSceneIndex]?.duration) || 5;
      const clampedLocal = Math.max(0, Math.min(currentSceneDur, Number(localSec) || 0));
      const globalSec = Math.min(totalDur, offset + clampedLocal);
      const pct = totalDur > 0 ? Math.max(0, Math.min(100, (globalSec / totalDur) * 100)) : 0;

      if (timeCurrent) timeCurrent.textContent = formatTime(globalSec);
      if (timeTotal) timeTotal.textContent = formatTime(totalDur);
      if (seeker) seeker.value = String(pct);

      const playhead = document.getElementById('timelinePlayhead');
      if (playhead) {
        playhead.style.left = `${pct}%`;
      }
    }

    function drawCyberCanvasSlide(scene) {
      const canvas = document.getElementById('stageCyberCanvas');
      if (!canvas) return;
      const ctx = canvas.getContext('2d');
      if (!ctx) return;

      const w = canvas.width || 640;
      const h = canvas.height || 360;
      ctx.clearRect(0, 0, w, h);

      // Tonal surface background (strict monochrome & subtle depth, zero AI-slop glow)
      const bg = ctx.createLinearGradient(0, 0, 0, h);
      bg.addColorStop(0, '#141418');
      bg.addColorStop(1, '#09090b');
      ctx.fillStyle = bg;
      ctx.fillRect(0, 0, w, h);

      // Subtle architectural grid
      ctx.strokeStyle = 'rgba(255, 255, 255, 0.04)';
      ctx.lineWidth = 1;
      for (let x = 0; x < w; x += 48) {
        ctx.beginPath();
        ctx.moveTo(x, 0);
        ctx.lineTo(x, h);
        ctx.stroke();
      }
      for (let y = 0; y < h; y += 48) {
        ctx.beginPath();
        ctx.moveTo(0, y);
        ctx.lineTo(w, y);
        ctx.stroke();
      }

      if (!scene) return;
      ctx.save();
      ctx.fillStyle = 'rgba(255, 255, 255, 0.72)';
      ctx.font = '600 13px "JetBrains Mono", Consolas, monospace';
      ctx.textAlign = 'center';
      ctx.fillText(`SCENE ${scene.order || 1} · ${(scene.duration || 5).toFixed(1)}s`, w / 2, h * 0.34);

      ctx.fillStyle = '#f4f4f5';
      ctx.font = '700 22px "Inter", -apple-system, BlinkMacSystemFont, sans-serif';
      const titleText = (scene.title || `Cảnh ${scene.order || 1}`).slice(0, 48);
      ctx.fillText(titleText, w / 2, h * 0.44);
      ctx.restore();
    }

    function stopPlaybackTicker() {
      if (playbackTicker) {
        clearInterval(playbackTicker);
        playbackTicker = null;
      }
    }

    function startPlaybackTicker(index, scene) {
      stopPlaybackTicker();
      sceneStartedAtMs = performance.now() - sceneElapsedSec * 1000;
      const targetDur = Math.max(0.5, Number(scene?.duration) || 5);

      playbackTicker = setInterval(() => {
        if (!isPlaying || currentSceneIndex !== index) {
          stopPlaybackTicker();
          return;
        }

        if (audioElement && scene?.audioUrl && audioElement.duration && !Number.isNaN(audioElement.duration)) {
          sceneElapsedSec = audioElement.currentTime;
          updatePlaybackClockUI(sceneElapsedSec);
          return;
        }

        const elapsed = (performance.now() - sceneStartedAtMs) / 1000;
        sceneElapsedSec = elapsed;
        updatePlaybackClockUI(elapsed);

        if (elapsed >= targetDur) {
          stopPlaybackTicker();
          nextScene();
        }
      }, 50);
    }

    function loadScene(index, autoPlay) {
      if (index < 0 || index >= scenes.length) return;
      currentSceneIndex = index;
      const scene = scenes[index];
      sceneElapsedSec = 0;
      stopPlaybackTicker();
      clearTimeout(sceneTimer);

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

      if (stageSubtitle) {
        stageSubtitle.classList.remove('pos-top', 'pos-center', 'pos-bottom');
        const subPos = scene.subtitlePos === 'top' ? 'pos-top' : (scene.subtitlePos === 'center' || scene.subtitlePos === 'middle' ? 'pos-center' : 'pos-bottom');
        stageSubtitle.classList.add(subPos);
      }

      drawCyberCanvasSlide(scene);

      if (stageImage) {
        stageImage.classList.remove('ken-burns', 'zoom-in', 'zoom-out', 'pan-left', 'pan-right', 'fade', 'fade-scale');
        stageImage.classList.remove('filter-none', 'filter-normal', 'filter-cinematic', 'filter-warm', 'filter-cool', 'filter-noir', 'filter-cyberpunk');

        const canShowImage = Boolean(scene.imageUrl) && stageImageErrorUrl !== scene.imageUrl;
        if (canShowImage) {
          stageImage.onerror = () => {
            stageImageErrorUrl = scene.imageUrl;
            stageImage.classList.add('hidden', 'img-hidden');
            stageImage.style.display = 'none';
            drawCyberCanvasSlide(scene);
          };
          stageImage.onload = () => {
            stageImage.classList.remove('hidden', 'img-hidden');
            stageImage.style.display = 'block';
            if (global.gsap) {
              global.gsap.killTweensOf(stageImage);
              // Chỉ kích hoạt hiệu ứng Ken-Burns zoom/pan khi đang phát video
              if (autoPlay || isPlaying) {
                global.gsap.fromTo(stageImage,
                  { scale: 1.04, x: -8 },
                  { scale: 1.14, x: 8, duration: Math.max(3, scene.duration || 5), ease: "none" }
                );
              } else {
                stageImage.style.transform = 'none';
              }
            }
          };
          stageImage.src = scene.imageUrl;
          stageImage.classList.remove('hidden', 'img-hidden');
          stageImage.style.display = 'block';
          void stageImage.offsetWidth;
          // Chỉ thêm lớp animation khi video đang ở trạng thái PLAY
          if (autoPlay || isPlaying) {
            stageImage.classList.add(scene.animation || 'ken-burns');
          } else {
            stageImage.style.transform = 'none';
          }
          stageImage.classList.add('filter-' + (scene.filter || 'normal'));
        } else {
          stageImage.removeAttribute('src');
          stageImage.classList.add('hidden', 'img-hidden');
          stageImage.style.display = 'none';
        }
      }

      // GSAP Motion Engine: Chỉ tạo chuyển động nảy khi video đang PLAY
      if (global.gsap) {
        if (stageSubtitle) global.gsap.killTweensOf(stageSubtitle);
        if (captionTitle) global.gsap.killTweensOf(captionTitle);

        if (autoPlay || isPlaying) {
          if (stageSubtitle) {
            global.gsap.fromTo(stageSubtitle,
              { y: 30, scale: 0.95, opacity: 0 },
              { y: 0, scale: 1, opacity: 1, duration: 0.5, ease: "back.out(1.4)" }
            );
          }
          if (captionTitle) {
            global.gsap.fromTo(captionTitle,
              { x: -35, opacity: 0 },
              { x: 0, opacity: 1, duration: 0.4, ease: "power3.out" }
            );
          }
        } else {
          if (stageSubtitle) {
            stageSubtitle.style.transform = 'none';
            stageSubtitle.style.opacity = '1';
          }
          if (captionTitle) {
            captionTitle.style.transform = 'none';
            captionTitle.style.opacity = '1';
          }
        }
      }

      if (autoPlay) {
        playWhoosh();
        setTimeout(() => {
          if (isPlaying) playHit();
        }, 150);
      }

      updatePlaybackClockUI(0);

      // Audio loading & playback: Khi không play, chỉ dừng và không kích hoạt phát
      if (audioElement) {
        if (scene.audioUrl) {
          if (audioElement.src !== scene.audioUrl && !audioElement.src.endsWith(scene.audioUrl)) {
            audioElement.src = scene.audioUrl;
          }
          audioElement.currentTime = 0;
          if (autoPlay) {
            setPlayState(true);
            audioElement.play().then(() => {
              syncBgmDucking(true);
              startPlaybackTicker(index, scene);
            }).catch(e => {
              console.warn('[Studio] Audio play fallback timer:', e);
              syncBgmDucking(false);
              startPlaybackTicker(index, scene);
            });
          } else {
            audioElement.pause();
            setPlayState(false);
          }
        } else {
          audioElement.removeAttribute('src');
          audioElement.pause();
          setPlayState(false);
          if (autoPlay) {
            setPlayState(true);
            syncBgmDucking(false);
            startPlaybackTicker(index, scene);
          }
        }
      } else if (autoPlay) {
        setPlayState(true);
        syncBgmDucking(false);
        startPlaybackTicker(index, scene);
      } else {
        setPlayState(false);
      }
    }

    function setPlayState(playing) {
      isPlaying = playing;
      syncBgmDucking(Boolean(audioElement && !audioElement.paused && audioElement.currentTime > 0));
      if (btnPlayPause) {
        const iconPlay = btnPlayPause.querySelector('#iconPlay, .icon-play');
        const iconPause = btnPlayPause.querySelector('#iconPause, .icon-pause');
        if (iconPlay && iconPause) {
          iconPlay.classList.toggle('hidden', playing);
          iconPause.classList.toggle('hidden', !playing);
        }
      }
      const audioBars = document.getElementById('stageAudioBars');
      if (audioBars) {
        audioBars.classList.toggle('hidden', !playing);
        audioBars.classList.toggle('playing', playing);
      }
    }

    function togglePlay() {
      playClick();
      if (scenes.length === 0) {
        showStudioToast('Dự án chưa có phân cảnh nào để phát.');
        return;
      }

      if (isPlaying) {
        if (audioElement) audioElement.pause();
        clearTimeout(sceneTimer);
        stopPlaybackTicker();
        setPlayState(false);
        syncBgmDucking(false);
        return;
      }

      const activeScene = scenes[currentSceneIndex];
      setPlayState(true);
      if (audioElement && activeScene?.audioUrl && audioElement.src) {
        audioElement.play().then(() => {
          syncBgmDucking(true);
          startPlaybackTicker(currentSceneIndex, activeScene);
        }).catch(() => {
          syncBgmDucking(false);
          startPlaybackTicker(currentSceneIndex, activeScene);
        });
      } else if (activeScene?.audioUrl || sceneElapsedSec > 0) {
        syncBgmDucking(false);
        startPlaybackTicker(currentSceneIndex, activeScene);
      } else {
        loadScene(currentSceneIndex, true);
      }
    }

    function prevScene() {
      playClick();
      if (currentSceneIndex > 0) {
        loadScene(currentSceneIndex - 1, isPlaying);
      }
    }

    function nextScene() {
      playClick();
      if (currentSceneIndex < scenes.length - 1) {
        loadScene(currentSceneIndex + 1, isPlaying);
      } else {
        stopPlaybackTicker();
        setPlayState(false);
        syncBgmDucking(false);
        loadScene(0, false);
      }
    }

    // Audio Event listeners
    if (audioElement) {
      audioElement.addEventListener('play', () => syncBgmDucking(true));
      audioElement.addEventListener('pause', () => syncBgmDucking(false));
      audioElement.addEventListener('ended', function () {
        stopPlaybackTicker();
        syncBgmDucking(false);
        if (isPlaying) {
          nextScene();
        }
      });

      audioElement.addEventListener('timeupdate', function () {
        if (audioElement.duration && !Number.isNaN(audioElement.duration)) {
          sceneElapsedSec = audioElement.currentTime;
          updatePlaybackClockUI(sceneElapsedSec);
        }
      });
    }

    // Playback control button listeners
    if (btnPlayPause) btnPlayPause.addEventListener('click', togglePlay);
    if (btnPrevScene) btnPrevScene.addEventListener('click', prevScene);
    if (btnNextScene) btnNextScene.addEventListener('click', nextScene);

    if (btnToggleBgm) {
      btnToggleBgm.addEventListener('click', function () {
        playClick();
        isBgmActive = !isBgmActive;
        btnToggleBgm.classList.toggle('active', isBgmActive);
        btnToggleBgm.title = isBgmActive ? "Nhạc nền & SFX: BẬT" : "Nhạc nền & SFX: TẮT";
        syncBgmDucking(Boolean(audioElement && !audioElement.paused));
      });
    }

    // Fullscreen toggle cho sân khấu video (tách biệt native Fullscreen API và CSS fallback để thu nhỏ dứt điểm)
    const btnFullscreenStage = document.getElementById('btnFullscreenStage');
    const stageScreenEl = document.querySelector('.studio-stage-screen');
    let isFallbackFullscreen = false;

    function syncFullscreenButtonState() {
      const isNativeFs = Boolean(stageScreenEl && document.fullscreenElement === stageScreenEl);
      if (!isNativeFs && document.fullscreenElement == null && isFallbackFullscreen === false) {
        // Đảm bảo class fullscreen bị gỡ hoàn toàn sau khi thoát native fullscreen hoặc Esc
        if (stageScreenEl) stageScreenEl.classList.remove('is-fullscreen-stage');
      }
      const isFs = isNativeFs || isFallbackFullscreen;
      if (stageScreenEl) stageScreenEl.classList.toggle('is-fullscreen-stage', isFs);
      if (btnFullscreenStage) {
        const enterIcon = btnFullscreenStage.querySelector('.icon-fullscreen-enter');
        const exitIcon = btnFullscreenStage.querySelector('.icon-fullscreen-exit');
        if (enterIcon) enterIcon.classList.toggle('hidden', isFs);
        if (exitIcon) exitIcon.classList.toggle('hidden', !isFs);
        btnFullscreenStage.title = isFs ? 'Thu nhỏ màn hình (Esc)' : 'Phóng to toàn màn hình';
        btnFullscreenStage.setAttribute('aria-pressed', isFs ? 'true' : 'false');
      }
    }

    async function toggleStageFullscreen() {
      if (!stageScreenEl) return;
      const isNativeFs = Boolean(document.fullscreenElement === stageScreenEl);
      if (isNativeFs) {
        isFallbackFullscreen = false;
        try {
          await document.exitFullscreen();
        } catch { }
      } else if (isFallbackFullscreen) {
        isFallbackFullscreen = false;
      } else if (stageScreenEl.requestFullscreen) {
        try {
          await stageScreenEl.requestFullscreen();
        } catch {
          isFallbackFullscreen = true;
        }
      } else {
        isFallbackFullscreen = true;
      }
      syncFullscreenButtonState();
    }

    if (btnFullscreenStage) {
      btnFullscreenStage.addEventListener('click', toggleStageFullscreen);
    }
    const stageMediaEl = document.getElementById('stageCanvasWrapper');
    if (stageMediaEl) {
      stageMediaEl.addEventListener('dblclick', toggleStageFullscreen);
    }
    document.addEventListener('fullscreenchange', function () {
      if (document.fullscreenElement !== stageScreenEl) {
        isFallbackFullscreen = false;
      }
      syncFullscreenButtonState();
    });
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && (isFallbackFullscreen || stageScreenEl?.classList.contains('is-fullscreen-stage'))) {
        if (document.fullscreenElement !== stageScreenEl) {
          isFallbackFullscreen = false;
          syncFullscreenButtonState();
        }
      }
    });

    // Global Seeker scrub across full project timeline
    if (seeker) {
      seeker.addEventListener('input', function () {
        const totalDur = getProjectTotalDuration();
        if (totalDur <= 0 || scenes.length === 0) return;
        const targetGlobal = (Number(seeker.value) / 100) * totalDur;
        let acc = 0;
        let targetIdx = 0;
        let localOffset = 0;
        for (let i = 0; i < scenes.length; i++) {
          const d = Number(scenes[i].duration) || 5;
          if (targetGlobal <= acc + d || i === scenes.length - 1) {
            targetIdx = i;
            localOffset = Math.max(0, targetGlobal - acc);
            break;
          }
          acc += d;
        }
        if (targetIdx !== currentSceneIndex) {
          loadScene(targetIdx, isPlaying);
        }
        sceneElapsedSec = localOffset;
        sceneStartedAtMs = performance.now() - localOffset * 1000;
        if (audioElement && audioElement.duration && !Number.isNaN(audioElement.duration)) {
          audioElement.currentTime = Math.min(audioElement.duration, localOffset);
        }
        updatePlaybackClockUI(localOffset);
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

    const vieneuBadge = document.getElementById('vieneuStatusBadge');
    async function refreshVieNeuStatus(warmup = false) {
      try {
        const res = await fetch(`/api/studio/vieneu-status?warmup=${warmup ? 'true' : 'false'}`);
        if (!res.ok) return null;
        const st = await res.json();
        if (vieneuBadge && st) {
          if (st.isLoading || !st.isReady) {
            vieneuBadge.textContent = st.message || '⏳ Đang đợi Server nạp model âm thanh VieNeu-TTS 48kHz...';
            vieneuBadge.classList.remove('hidden', 'is-ready', 'is-error');
            vieneuBadge.classList.add(st.state === 'error' ? 'is-error' : 'is-loading');
          } else {
            vieneuBadge.textContent = '🎙 VieNeu-TTS 48kHz sẵn sàng';
            vieneuBadge.classList.remove('hidden', 'is-loading', 'is-error');
            vieneuBadge.classList.add('is-ready');
          }
        }
        return st;
      } catch {
        return null;
      }
    }

    async function runSceneMediaAction(sceneId, btn, endpoint, busyLabel, doneLabel) {
      if (!sceneId || !btn) return;
      const origHtml = btn.innerHTML;
      btn.disabled = true;
      btn.textContent = busyLabel;

      if (endpoint === 'audio') {
        const st = await refreshVieNeuStatus(true);
        if (st && !st.isReady) {
          btn.textContent = 'Đợi VieNeu load...';
          showStudioToast(st.message || '⏳ Server âm thanh VieNeu-TTS đang nạp model 48kHz, vui lòng đợi...');
        }
      }

      fetch(`/api/studio/scenes/${sceneId}/${endpoint}`, { method: 'POST' })
        .then(res => res.json())
        .then(data => {
          if (data.success) {
            btn.replaceWith(createReadyBadge(doneLabel));
            if (endpoint === 'audio') refreshVieNeuStatus(false);
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
    global.uploadSceneCustomImage = async function (sceneId, input) {
      if (!input || !input.files || input.files.length === 0) return;
      const file = input.files[0];
      const validTypes = ['image/jpeg', 'image/png', 'image/webp', 'image/gif', 'image/bmp', 'image/x-ms-bmp'];
      if (!validTypes.includes(file.type) && !/\.(jpg|jpeg|png|webp|gif|bmp|jfif)$/i.test(file.name)) {
        showStudioToast('Vui lòng chọn tệp hình ảnh hợp lệ (.png, .jpg, .jpeg, .webp, .gif, .bmp, .jfif)');
        input.value = '';
        return;
      }
      if (file.size > 50 * 1024 * 1024) {
        showStudioToast('Kích thước ảnh tối đa là 50MB.');
        input.value = '';
        return;
      }
      const label = input.closest('.btn-choose-image');
      const originalSpanText = label ? label.querySelector('span')?.textContent : '';
      if (label) {
        const span = label.querySelector('span');
        if (span) span.textContent = 'Đang tải...';
      }
      const fd = new FormData();
      fd.append('file', file);
      showStudioToast(`Đang nén và tải ảnh "${file.name}" cho Phân cảnh...`);
      try {
        const res = await fetch(`/api/studio/scenes/${sceneId}/upload-image`, {
          method: 'POST',
          body: fd
        });
        let data = null;
        try {
          data = await res.json();
        } catch (parseErr) {
          console.warn('[UploadSceneImage] Non-JSON response:', res.status, res.statusText);
        }
        if (res.ok && data && data.success && data.imageUrl) {
          showStudioToast('Đã cập nhật ảnh phân cảnh thành công!');
          try {
            const list = document.getElementById('scenesList');
            const allCards = list ? Array.from(list.querySelectorAll('.scene-item')) : Array.from(document.querySelectorAll('.scene-item, .scene-card'));
            const card = list ? list.querySelector(`.scene-item[data-scene-id="${sceneId}"]`) : document.querySelector(`[data-scene-id="${sceneId}"]`);
            if (card) {
              card.dataset.imageUrl = data.imageUrl;
              const imgEl = card.querySelector('.scene-thumbnail img');
              if (imgEl) {
                imgEl.src = data.imageUrl;
                imgEl.classList.remove('hidden');
              }
              const idx = allCards.indexOf(card);
              if (idx !== -1) {
                if (scenes[idx]) {
                  scenes[idx].imageUrl = data.imageUrl;
                }
                currentSceneIndex = idx;
                // Giữ nguyên phân cảnh này và hiển thị ngay trên Stage ở chế độ tĩnh (không play)
                loadScene(idx, false);
              }
            }
          } catch (uiErr) {
            console.warn('[UploadSceneImage] Lỗi cập nhật giao diện sau khi tải ảnh:', uiErr);
          }
        } else {
          const errMsg = data?.error || (res.status === 401 ? 'Phiên đăng nhập đã hết hạn, vui lòng tải lại trang.' : `Lỗi máy chủ (${res.status})`);
          showStudioToast('Lỗi tải ảnh: ' + errMsg);
        }
      } catch (err) {
        console.error('[UploadSceneImage]', err);
        showStudioToast('Lỗi tải ảnh lên: ' + (err?.message || 'Không thể kết nối máy chủ'));
      } finally {
        if (label && originalSpanText) {
          const span = label.querySelector('span');
          if (span) span.textContent = originalSpanText;
        }
        input.value = '';
      }
    };
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

    // Reorder & Duplicate Scene helpers
    global.moveSceneItem = async function (sceneId, direction, ev) {
      if (ev) ev.stopPropagation();
      try {
        const res = await fetch(`/api/studio/scenes/${sceneId}/move`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ direction })
        });
        if (res.status === 401 || res.status === 403) {
          if (typeof global.openStudioLoginModal === 'function') global.openStudioLoginModal();
          return;
        }
        const data = await res.json();
        if (res.ok && data.success) {
          showStudioToast(direction === 'up' ? 'Đã đưa cảnh lên trên.' : 'Đã đưa cảnh xuống dưới.');
          setTimeout(() => window.location.reload(), 250);
        } else {
          showStudioToast(data?.error || 'Không thể đổi thứ tự phân cảnh.');
        }
      } catch {
        showStudioToast('Lỗi kết nối khi sắp xếp phân cảnh.');
      }
    };

    global.duplicateSceneItem = async function (sceneId, ev) {
      if (ev) ev.stopPropagation();
      try {
        const res = await fetch(`/api/studio/scenes/${sceneId}/duplicate`, { method: 'POST' });
        if (res.status === 401 || res.status === 403) {
          if (typeof global.openStudioLoginModal === 'function') global.openStudioLoginModal();
          return;
        }
        const data = await res.json();
        if (res.ok && data.success) {
          showStudioToast('Đã nhân bản phân cảnh.');
          setTimeout(() => window.location.reload(), 250);
        } else {
          showStudioToast(data?.error || 'Không thể nhân bản phân cảnh.');
        }
      } catch {
        showStudioToast('Lỗi kết nối khi nhân bản phân cảnh.');
      }
    };

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
    try {
      const rawAvatars = container?.dataset.avatars;
      if (rawAvatars) {
        const parsed = JSON.parse(rawAvatars);
        if (Array.isArray(parsed)) uploadedAvatarUrls = parsed.filter(Boolean);
      }
    } catch { uploadedAvatarUrls = []; }
    let currentVideoType = 'comparison';
    let currentSequentialSubMode = 'auto';
    let currentDurationPreset = 'auto';
    const urlAspect = new URLSearchParams(window.location.search).get('aspect');
    let currentAspectRatio = urlAspect || (container && container.dataset.aspectRatio) || '16:9';

    // Aspect Ratio Toggle (16:9 Ngang, 9:16 Dọc, 1:1 Vuông)
    const btnAspect169 = document.getElementById('btnAspect169');
    const btnAspect916 = document.getElementById('btnAspect916');
    const btnAspect11 = document.getElementById('btnAspect11');
    const stageWrapper = document.getElementById('stageCanvasWrapper') || document.getElementById('stageWrapper');
    const stageCyberCanvas = document.getElementById('stageCyberCanvas');

    function setStageAspectRatio(ratio, persist = true) {
      const normalized = ratio === '9:16' ? '9:16' : (ratio === '1:1' ? '1:1' : '16:9');
      currentAspectRatio = normalized;
      if (container) container.dataset.aspectRatio = normalized;

      if (btnAspect169) btnAspect169.classList.toggle('active', normalized === '16:9');
      if (btnAspect916) btnAspect916.classList.toggle('active', normalized === '9:16');
      if (btnAspect11) btnAspect11.classList.toggle('active', normalized === '1:1');

      if (stageWrapper) {
        stageWrapper.classList.toggle('aspect-16-9', normalized === '16:9');
        stageWrapper.classList.toggle('aspect-9-16', normalized === '9:16');
        stageWrapper.classList.toggle('aspect-1-1', normalized === '1:1');
      }
      if (stageCyberCanvas) {
        stageCyberCanvas.width = normalized === '9:16' ? 360 : (normalized === '1:1' ? 480 : 640);
        stageCyberCanvas.height = normalized === '9:16' ? 640 : (normalized === '1:1' ? 480 : 360);
      }

      if (persist && projectId && container && container.dataset.authenticated === 'true') {
        fetch(`/api/studio/projects/${projectId}/aspect-ratio`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ aspectRatio: normalized })
        }).catch(() => {});
      }
    }

    setStageAspectRatio(currentAspectRatio, false);

    if (btnAspect169) btnAspect169.addEventListener('click', () => setStageAspectRatio('16:9', true));
    if (btnAspect916) btnAspect916.addEventListener('click', () => setStageAspectRatio('9:16', true));
    if (btnAspect11) btnAspect11.addEventListener('click', () => setStageAspectRatio('1:1', true));

    // Backend Video Render (Dual Mode: FFmpeg Enhanced & HyperFrames 3-Zone)
    const btnRenderBackend = document.getElementById('btnRenderBackendVideo');
    if (btnRenderBackend) {
      btnRenderBackend.addEventListener('click', function () {
        if (!projectId) return;

        const renderMode = document.getElementById('studioRenderModeSelect')?.value || 'fast';
        const progressBox = document.getElementById('studioRenderProgress');
        const progressFill = document.getElementById('studioRenderProgressFill');
        const progressLabel = document.getElementById('studioRenderProgressLabel');
        const progressPct = document.getElementById('studioRenderProgressPct');

        function updateRenderProgressUI(pct, detail) {
          if (!progressBox) return;
          progressBox.classList.remove('hidden');
          const safePct = Math.max(0, Math.min(100, Number(pct) || 0));
          progressBox.setAttribute('aria-valuenow', String(safePct));
          if (progressFill) progressFill.style.width = `${safePct}%`;
          if (progressPct) progressPct.textContent = `${safePct}%`;
          if (progressLabel && detail) progressLabel.textContent = detail;
        }

        btnRenderBackend.disabled = true;
        const originalHtml = btnRenderBackend.innerHTML;
        btnRenderBackend.innerHTML = renderMode === 'hyperframes'
          ? '<span>Đang render HyperFrames...</span>'
          : '<span>Đang render MP4...</span>';
        updateRenderProgressUI(5, 'Khởi động tiến trình xuất video...');
        showStudioToast(`Đang xuất video ${currentAspectRatio} (${renderMode === 'hyperframes' ? 'HyperFrames 3-Zone' : 'FFmpeg Nâng cấp'})...`);

        const pollTimer = setInterval(() => {
          fetch(`/api/studio/projects/${projectId}/render-progress`)
            .then(r => (r.ok ? r.json() : null))
            .then(snap => {
              if (snap && typeof snap.percent === 'number') {
                updateRenderProgressUI(snap.percent, snap.detail);
              }
            })
            .catch(() => {});
        }, 550);

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
              updateRenderProgressUI(100, 'Hoàn tất! Đang tải file MP4...');
              showStudioToast(`Render video thành công (${data.duration ? data.duration.toFixed(1) : ''}s)! Đang tải về...`);
              const a = document.createElement('a');
              a.href = data.videoUrl;
              a.download = `video_proj_${projectId}.mp4`;
              document.body.appendChild(a);
              a.click();
              document.body.removeChild(a);
            } else {
              updateRenderProgressUI(0, data?.error || 'Lỗi render video');
              alert('Lỗi render video: ' + (data?.error || 'Không xác định'));
            }
          })
          .catch(err => {
            console.error('[RenderBackendVideo]', err);
            updateRenderProgressUI(0, 'Lỗi kết nối máy chủ');
            alert('Lỗi kết nối khi render video trên server.');
          })
          .finally(() => {
            clearInterval(pollTimer);
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
    const sequentialSubModeGroup = document.getElementById('sequentialSubModeGroup');
    const durationPresetGroup = document.getElementById('durationPresetGroup');
    const sourceMdStatsBadge = document.getElementById('sourceMdStatsBadge');
    const btnDownloadSourceMd = document.getElementById('btnDownloadSourceMd');
    const btnDownloadScriptMd = document.getElementById('btnDownloadScriptMd');
    const planMetaSummaryBar = document.getElementById('planMetaSummaryBar');
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

    function updateSourceDurationBadge(serverEstimate) {
      if (!sourceMdStatsBadge) return;
      if (serverEstimate && serverEstimate.summaryBadgeText) {
        sourceMdStatsBadge.textContent = serverEstimate.summaryBadgeText;
        return;
      }
      const text = (inputSourceContent ? inputSourceContent.value : '').trim();
      const words = text ? (text.match(/[\p{L}\p{N}_]+/gu) || []).length : 0;
      const headings = text ? (text.match(/^\s*#{1,4}\s+.+$/gm) || []).length : 0;
      let targetSec = 36;
      let beats = currentVideoType === 'comparison' ? 12 : 7;
      if (currentDurationPreset === 'short_35s') {
        targetSec = 36;
        beats = currentVideoType === 'comparison' ? 12 : 7;
      } else if (currentDurationPreset === 'medium_2_3m') {
        targetSec = Math.min(180, Math.max(110, Math.round((words / 2.42) * 0.75) || 135));
        beats = currentVideoType === 'comparison' ? 12 : 10;
      } else if (currentDurationPreset === 'deep_4_6m') {
        targetSec = Math.min(360, Math.max(240, Math.round((words / 2.42) * 0.85) || 300));
        beats = 15;
      } else {
        if (currentVideoType === 'comparison' && words <= 450) {
          targetSec = 36;
          beats = 12;
        } else if (words <= 300) {
          targetSec = 36;
          beats = currentVideoType === 'comparison' ? 12 : 7;
        } else if (words <= 1100) {
          targetSec = Math.min(180, Math.max(90, Math.round(((words * 0.55) / 2.42) / 5) * 5));
          beats = currentVideoType === 'comparison' ? 12 : Math.min(12, Math.max(8, Math.round(targetSec / 14)));
        } else {
          targetSec = Math.min(360, Math.max(210, Math.round(((words * 0.42) / 2.42) / 10) * 10));
          beats = Math.min(16, Math.max(12, Math.round(targetSec / 18)));
        }
      }
      const minsLabel = targetSec < 60 ? `~${targetSec} giây` : `~${(targetSec / 60).toFixed(1)} phút (${targetSec}s)`;
      const skillLabel = currentVideoType === 'comparison'
        ? 'Skill So sánh 3-Zone'
        : (currentSequentialSubMode === 'kinetic' || (currentSequentialSubMode === 'auto' && currentAspectRatio === '16:9' && words > 320)
            ? 'Skill Tuần tự 16:9 Kinetic'
            : 'Skill Tuần tự 1-Khung + MC');
      sourceMdStatsBadge.textContent = `${words} từ · ${Math.max(1, headings)} đề mục · Thời lượng tự căn chỉnh: ${minsLabel} (${beats} phân đoạn · ${skillLabel})`;
    }

    function setVideoTypeSelection(type) {
      currentVideoType = type === 'sequential' ? 'sequential' : 'comparison';
      if (btnTypeComparison && btnTypeSequential) {
        btnTypeComparison.classList.toggle('active', currentVideoType === 'comparison');
        btnTypeSequential.classList.toggle('active', currentVideoType === 'sequential');
      }
      if (sequentialSubModeGroup) {
        sequentialSubModeGroup.classList.toggle('hidden', currentVideoType !== 'sequential');
      }
      updateSourceDurationBadge();
    }

    // Video Type Toggle
    if (btnTypeComparison && btnTypeSequential) {
      if (btnTypeSequential.classList.contains('active')) {
        currentVideoType = 'sequential';
      }
      btnTypeComparison.addEventListener('click', () => setVideoTypeSelection('comparison'));
      btnTypeSequential.addEventListener('click', () => setVideoTypeSelection('sequential'));
    }

    if (sequentialSubModeGroup) {
      sequentialSubModeGroup.querySelectorAll('.btn-submode-pill').forEach(btn => {
        btn.addEventListener('click', () => {
          currentSequentialSubMode = btn.dataset.submode || 'auto';
          sequentialSubModeGroup.querySelectorAll('.btn-submode-pill').forEach(b => b.classList.toggle('active', b === btn));
          updateSourceDurationBadge();
        });
      });
    }

    if (durationPresetGroup) {
      durationPresetGroup.querySelectorAll('.btn-submode-pill').forEach(btn => {
        btn.addEventListener('click', () => {
          currentDurationPreset = btn.dataset.duration || 'auto';
          durationPresetGroup.querySelectorAll('.btn-submode-pill').forEach(b => b.classList.toggle('active', b === btn));
          updateSourceDurationBadge();
        });
      });
    }

    if (inputSourceContent) {
      inputSourceContent.addEventListener('input', () => updateSourceDurationBadge());
    }
    updateSourceDurationBadge();

    function triggerMarkdownBlobDownload(content, filenameSuffix) {
      const text = (content || '').trim();
      if (!text) {
        showStudioToast('Chưa có nội dung Markdown để tải xuống.');
        return;
      }
      const blob = new Blob([text], { type: 'text/markdown;charset=utf-8' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `cowai-project-${projectId || 'studio'}-${filenameSuffix}.md`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    }

    if (btnDownloadSourceMd) {
      btnDownloadSourceMd.addEventListener('click', () => {
        const text = inputSourceContent ? inputSourceContent.value : '';
        triggerMarkdownBlobDownload(text, 'source');
      });
    }

    if (btnDownloadScriptMd) {
      btnDownloadScriptMd.addEventListener('click', () => {
        const text = planMarkdownEditor ? planMarkdownEditor.value : '';
        triggerMarkdownBlobDownload(text, 'video-script');
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
          if (typeof global.openStudioLoginModal === 'function') global.openStudioLoginModal();
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
          if (data.projectTitle) {
            const titleEl = document.querySelector('.studio-header__title');
            if (titleEl) titleEl.textContent = data.projectTitle;
          }
          if (data.projectStatus) {
            const statusEl = document.querySelector('.studio-header__status');
            if (statusEl) statusEl.textContent = data.projectStatus;
          }
          if (inputSourceContent && (data.sourceMarkdown || data.sourceContent)) {
            inputSourceContent.value = data.sourceMarkdown || data.sourceContent;
            if (data.recommendedVideoType) {
              setVideoTypeSelection(data.recommendedVideoType);
            }
            updateSourceDurationBadge(data.durationEstimate);
            showStudioToast(`Đã chuẩn hóa ${data.sourceFileName || file.name} sang Markdown (.md)`);
          } else {
            showStudioToast(data.warning || `Đã lưu ${data.sourceFileName || file.name} vào CSDL nhưng chưa tìm thấy văn bản trong tệp.`);
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

    // Avatars Upload (hỗ trợ chọn & cộng dồn nhiều ảnh chân dung, tối đa 6 ảnh)
    const sidebarAvatarPreviewStrip = document.getElementById('sidebarAvatarPreviewStrip');
    const stageAvatarOverlay = document.getElementById('stageAvatarOverlay');

    function renderStageAvatarOverlay() {
      if (!stageAvatarOverlay) return;
      if (!uploadedAvatarUrls || uploadedAvatarUrls.length === 0) {
        stageAvatarOverlay.classList.add('hidden');
        stageAvatarOverlay.innerHTML = '';
        return;
      }
      stageAvatarOverlay.classList.remove('hidden');
      stageAvatarOverlay.innerHTML = uploadedAvatarUrls.map((url, idx) =>
        `<img src="${url}" alt="Chân dung ${idx + 1}" class="stage-avatar-thumb" />`
      ).join('');
    }

    async function persistAvatarListToServer() {
      if (!projectId || container?.dataset.authenticated !== 'true') return;
      try {
        const res = await fetch(`/api/studio/projects/${projectId}/avatars`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ avatarUrls: uploadedAvatarUrls })
        });
        if (res.ok) {
          const d = await res.json().catch(() => null);
          if (d && Array.isArray(d.avatarUrls)) {
            uploadedAvatarUrls = d.avatarUrls;
          }
        }
      } catch { }
    }

    async function handleAvatarsUpload(files) {
      if (!files || files.length === 0 || !projectId) return;
      const remainingSlots = Math.max(0, 6 - uploadedAvatarUrls.length);
      if (remainingSlots <= 0) {
        showStudioToast('Đã đạt tối đa 6 ảnh chân dung. Hãy xóa bớt ảnh cũ trước khi thêm ảnh mới.');
        return;
      }

      const formData = new FormData();
      const filesToUpload = Array.from(files).slice(0, remainingSlots);
      filesToUpload.forEach(f => formData.append('avatars', f));

      try {
        const res = await fetch(`/api/studio/projects/${projectId}/upload-source`, {
          method: 'POST',
          body: formData
        });
        const data = await res.json().catch(() => null);
        if (res.status === 401 || res.status === 403) {
          if (typeof global.openStudioLoginModal === 'function') global.openStudioLoginModal();
          return;
        }
        if (res.ok && data && data.success && data.avatarUrls) {
          uploadedAvatarUrls = data.avatarUrls;
          renderAvatarPreviewStrip();
          showStudioToast(`Đã lưu tổng cộng ${uploadedAvatarUrls.length} ảnh chân dung.`);
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
      const strips = [avatarPreviewStrip, sidebarAvatarPreviewStrip].filter(Boolean);
      strips.forEach(stripEl => {
        stripEl.innerHTML = '';
        if (uploadedAvatarUrls.length === 0) {
          stripEl.innerHTML = '<span style="font-size:0.75rem; color:var(--cow-text-muted);">Chưa chọn ảnh (Chế độ Cinematic B-Roll tự sinh cảnh)</span>';
          return;
        }

        uploadedAvatarUrls.forEach((url, i) => {
          const item = document.createElement('div');
          item.className = 'avatar-preview-item';
          item.innerHTML = `
            <img src="${url}" alt="Avatar ${i + 1}" />
            <button type="button" class="btn-remove-avatar" data-index="${i}" title="Xóa ảnh">✕</button>
          `;
          stripEl.appendChild(item);
        });

        stripEl.querySelectorAll('.btn-remove-avatar').forEach(btn => {
          btn.addEventListener('click', async (e) => {
            e.stopPropagation();
            const idx = parseInt(btn.dataset.index, 10);
            uploadedAvatarUrls.splice(idx, 1);
            await persistAvatarListToServer();
            renderAvatarPreviewStrip();
          });
        });
      });

      if (selectedAvatarInfo) {
        if (uploadedAvatarUrls.length > 0) {
          selectedAvatarInfo.textContent = `✓ Đã chọn ${uploadedAvatarUrls.length}/6 ảnh chân dung`;
          selectedAvatarInfo.classList.remove('hidden');
          selectedAvatarInfo.classList.add('is-success');
        } else {
          selectedAvatarInfo.textContent = '';
          selectedAvatarInfo.classList.add('hidden');
        }
      }

      renderStageAvatarOverlay();
    }

    renderAvatarPreviewStrip();

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
            sequentialSubMode: currentSequentialSubMode,
            durationPreset: currentDurationPreset,
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

      // Trích xuất YAML Frontmatter nếu có để hiển thị thanh thông số tổng quan
      const yamlMatch = markdown.match(/^\s*---\r?\n([\s\S]*?)\r?\n---/);
      if (planMetaSummaryBar) {
        if (yamlMatch) {
          const yamlText = yamlMatch[1];
          const getYamlVal = (key) => {
            const m = yamlText.match(new RegExp(`^\\s*${key}:\\s*"?([^"\\r\\n]+)"?`, 'm'));
            return m ? m[1].trim() : '';
          };
          const skill = getYamlVal('skill') || 'compare-3zone';
          const mins = getYamlVal('target_minutes') || '0.6';
          const secs = getYamlVal('target_duration_seconds') || '36';
          const beats = getYamlVal('recommended_beats') || '';
          const aspect = getYamlVal('aspect_ratio') || currentAspectRatio;
          planMetaSummaryBar.innerHTML = `
            <span class="meta-pill meta-pill--primary">Skill: ${escapeHtml(skill)}</span>
            <span class="meta-pill">Khung hình: ${escapeHtml(aspect)}</span>
            <span class="meta-pill">Thời lượng: ~${escapeHtml(mins)} phút (${escapeHtml(secs)}s)</span>
            ${beats ? `<span class="meta-pill">${escapeHtml(beats)} phân đoạn</span>` : ''}
          `;
        } else {
          planMetaSummaryBar.textContent = 'Bản kịch bản video Markdown (.md) chi tiết:';
        }
      }

      const bodyWithoutYaml = markdown.replace(/^\s*---[\s\S]*?---\s*/, '');
      const lines = bodyWithoutYaml.split('\n');
      const scenesFound = [];
      let currentSc = null;

      lines.forEach(line => {
        const trimmed = line.trim();
        if (
          trimmed.startsWith('### [') ||
          trimmed.startsWith('## [') ||
          trimmed.startsWith('### Phần') ||
          trimmed.startsWith('### Cảnh') ||
          trimmed.startsWith('## Cảnh') ||
          trimmed.startsWith('## Scene')
        ) {
          if (currentSc) scenesFound.push(currentSc);
          const rawHead = trimmed.replace(/^#+\s*/, '').replace(/^(?:Cảnh|Scene)\s*\d+\s*[:\-–]?\s*/i, '');
          let badge = '';
          let title = rawHead;
          const bMatch = rawHead.match(/^\[([^\]]+)\]\s*[:\-–]?\s*(.*)$/);
          if (bMatch) {
            badge = bMatch[1].trim();
            title = bMatch[2].trim() || badge;
          }
          currentSc = { title, badge, role: '', duration: 4.5, content: '', ttsText: '', prompt: '', layout: '', effect: '' };
        } else if (currentSc) {
          const clean = trimmed.replace(/\*\*/g, '');
          if (clean.startsWith('- Thời lượng:')) {
            const match = clean.match(/(\d+(\.\d+)?)/);
            if (match) currentSc.duration = parseFloat(match[1]);
          } else if (clean.startsWith('- Lời hiển thị') || clean.startsWith('- Lời thoại:')) {
            currentSc.content = clean.replace(/^-[^:]+:\s*/i, '').trim().replace(/^"|"$/g, '');
          } else if (clean.startsWith('- Lời đọc TTS')) {
            currentSc.ttsText = clean.replace(/^-[^:]+:\s*/i, '').trim().replace(/^"|"$/g, '');
          } else if (clean.startsWith('- Huy hiệu chuyên mục:')) {
            currentSc.badge = clean.replace(/^-[^:]+:\s*/i, '').trim();
          } else if (clean.startsWith('- Vai trò nhịp:')) {
            currentSc.role = clean.replace(/^-[^:]+:\s*/i, '').trim();
          } else if (clean.startsWith('- Hiệu ứng:')) {
            currentSc.effect = clean.replace(/^-[^:]+:\s*/i, '').trim();
          } else if (clean.startsWith('- Prompt hình ảnh:') || clean.startsWith('- Prompt ảnh:')) {
            currentSc.prompt = clean.replace(/^-\s*Prompt(?:\s+hình)?\s+ảnh:\s*/i, '').trim().replace(/^"|"$/g, '');
          } else if (clean.startsWith('- Bố cục:') || clean.startsWith('- Layout:') || clean.startsWith('- Loại bố cục:') || clean.startsWith('- Loại cảnh:')) {
            currentSc.layout = clean.replace(/^-[^:]+:/, '').trim();
          }
        }
      });
      if (currentSc) scenesFound.push(currentSc);

      if (scenesFound.length === 0) {
        planReviewCards.innerHTML = '<div style="padding: 1rem; color: var(--cow-text-muted);">Không tìm thấy phân đoạn kịch bản. Hãy kiểm tra nội dung Markdown.</div>';
        return;
      }

      scenesFound.forEach((sc, i) => {
        const card = document.createElement('div');
        card.className = 'plan-review-card glass-panel';
        const badgeLabel = sc.badge || `Phần ${i + 1}`;
        const formattedCaption = escapeHtml(sc.content || '(Chưa có lời thoại)')
          .replace(/\[kw\](.+?)\[\/kw\]/gi, '<strong class="plan-kw-tag">$1</strong>');
        card.innerHTML = `
          <div class="plan-card-header">
            <span class="plan-card-badge">${escapeHtml(badgeLabel)} · ${sc.duration}s</span>
            <span class="plan-card-layout font-mono">${escapeHtml(sc.layout || '3-zone')}${sc.effect ? ` · ${escapeHtml(sc.effect)}` : ''}</span>
          </div>
          <div class="plan-card-title">${escapeHtml(sc.title)}</div>
          <div class="plan-card-dialogue">"${formattedCaption}"</div>
          ${sc.ttsText ? `<div class="plan-card-tts">🔊 TTS: ${escapeHtml(sc.ttsText)}</div>` : ''}
          ${sc.prompt ? `<div class="plan-card-prompt">🎨 ${escapeHtml(sc.prompt)}</div>` : ''}
        `;
        planReviewCards.appendChild(card);
      });
    }

    // Proceed Plan (Chạy ngay) -> Apply & Auto Produce
    if (btnProceedPlan) {
      btnProceedPlan.addEventListener('click', () => {
        if (!projectId) return;

        if (container.dataset.authenticated !== 'true') {
          if (typeof global.openStudioLoginModal === 'function') {
            global.openStudioLoginModal();
          } else {
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
                window.location.href = `/studio/editor/${projectId}?view=editor&tab=scenes`;
              }, 400);
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
      window.location.href = `/studio/editor/${projId}?view=editor&tab=scenes`;
    };

    global.openLatestOrNewEditor = function () {
      const isAuth = container ? container.dataset.authenticated === 'true' : false;
      if (!isAuth) {
        global.openStudioLoginModal();
        return;
      }
      const firstCard = document.querySelector('.hub-project-card');
      if (firstCard && firstCard.dataset.projectId) {
        window.location.href = `/studio/editor/${firstCard.dataset.projectId}?view=editor&tab=scenes`;
        return;
      }
      global.openPlanModalFromHub();
    };

    global.openPlanModalWithAspect = function (aspect) {
      const isAuth = container ? container.dataset.authenticated === 'true' : false;
      if (!isAuth) {
        global.openStudioLoginModal();
        return;
      }
      const popover = document.getElementById('studioCreatePopover');
      if (popover) popover.classList.add('hidden');
      const backdrop = document.getElementById('studioCreatePopoverBackdrop');
      if (backdrop) backdrop.classList.add('hidden');

      global.openPlanModalFromHub();
      if (aspect) {
        setTimeout(() => {
          const btnAspect = document.getElementById(aspect === '9:16' ? 'btnAspect916' : (aspect === '1:1' ? 'btnAspect11' : 'btnAspect169'));
          if (btnAspect) btnAspect.click();
        }, 150);
      }
    };

    global.handleCreateOption = function (action, aspect) {
      global.openPlanModalWithAspect(aspect || '16:9');
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

    // Khôi phục kế hoạch Markdown đã lưu trong CSDL (nếu có) & tự mở Plan Modal nếu URL có ?openPlan=1
    if (planMarkdownEditor && planMarkdownEditor.value.trim().length > 0) {
      renderMarkdownCards(planMarkdownEditor.value);
      if (btnProceedPlan) btnProceedPlan.disabled = false;
    }
    const initUrlParams = new URLSearchParams(window.location.search);
    if (initUrlParams.get('openPlan') === '1') {
      openPlanModal();
    }
    if (initUrlParams.get('view') === 'editor' || initUrlParams.get('tab') === 'scenes' || window.location.pathname.includes('/studio/editor')) {
      if (typeof global.switchToEditorView === 'function') {
        global.switchToEditorView();
      }
      if (initUrlParams.get('tab') === 'scenes' || !initUrlParams.has('tab')) {
        const tabScenes = document.getElementById('tabNavScenes');
        if (tabScenes) tabScenes.click();
      }
    }

    global.openPlanModalFromHub = async function (file) {
      const isAuth = container ? container.dataset.authenticated === 'true' : false;
      if (!isAuth) {
        global.openStudioLoginModal();
        return;
      }
      if (file) {
        // Tạo ngay bản ghi dự án mới trong CSDL và lưu tệp nguồn để xuất hiện trong lịch sử tức thì
        try {
          const baseName = file.name.replace(/\.[^.]+$/, '') || 'Bài giảng mới';
          const voice = document.getElementById('hubVoiceSelect')?.value || 'Hải Đăng';
          const createRes = await fetch('/api/studio/projects', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
              title: baseName,
              description: `Bài giảng tạo từ tệp ${file.name}`,
              voice
            })
          });
          if (createRes.ok) {
            const newProj = await createRes.json();
            const fd = new FormData();
            fd.append('file', file);
            await fetch(`/api/studio/projects/${newProj.id}/upload-source`, {
              method: 'POST',
              body: fd
            });
            window.location.href = `/studio/editor/${newProj.id}?openPlan=1`;
            return;
          }
        } catch (err) {
          console.error('[HubUploadCreate]', err);
        }
      }
      global.switchToEditorView();
      const openBtn = document.getElementById('btnOpenPlanModal');
      if (openBtn) openBtn.click();
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

    // Initialize scenes list & kiểm tra trạng thái VieNeu-TTS
    scanScenes();
    if (container.dataset.activeView === 'editor') {
      refreshVieNeuStatus(false);
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initStudio);
  } else {
    initStudio();
  }

})(typeof window !== 'undefined' ? window : this);
