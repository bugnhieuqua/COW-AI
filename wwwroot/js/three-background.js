(function () {
  'use strict';

  function initThreeBackground() {
    const canvas = document.getElementById('three-canvas') || document.getElementById('bg-canvas');
    if (!canvas || typeof THREE === 'undefined') {
      return;
    }

    // 1. Khởi tạo Scene, Camera, WebGLRenderer
    const scene = new THREE.Scene();

    const camera = new THREE.PerspectiveCamera(
      75,
      window.innerWidth / window.innerHeight,
      0.1,
      1000
    );
    camera.position.z = 10;

    const renderer = new THREE.WebGLRenderer({
      canvas: canvas,
      alpha: true,
      antialias: true
    });
    renderer.setSize(window.innerWidth, window.innerHeight);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));

    // Tính toán kích thước không gian chiếu ở tọa độ Z = 0
    function calculateViewport() {
      const aspect = window.innerWidth / window.innerHeight;
      const vFov = (camera.fov * Math.PI) / 180;
      const height = 2 * Math.tan(vFov / 2) * camera.position.z;
      const width = height * aspect;
      return { width: width, height: height, aspect: aspect };
    }

    let viewport = calculateViewport();

    // 2. Tạo hiệu ứng hạt không gian 3D (Particles)
    const particlesGeometry = new THREE.BufferGeometry();
    const particlesCount = 700;
    const posArray = new Float32Array(particlesCount * 3);

    for (let i = 0; i < particlesCount * 3; i++) {
      posArray[i] = (Math.random() - 0.5) * 32;
    }

    particlesGeometry.setAttribute('position', new THREE.BufferAttribute(posArray, 3));

    const particlesMaterial = new THREE.PointsMaterial({
      size: 0.045,
      color: 0x4facfe,
      transparent: true,
      opacity: 0.6,
      blending: THREE.AdditiveBlending
    });

    const particlesMesh = new THREE.Points(particlesGeometry, particlesMaterial);
    scene.add(particlesMesh);

    // 3. Tải Logo Con Bò
    const textureLoader = new THREE.TextureLoader();
    const cowTexture = textureLoader.load('/images/logo.png');

    // Logo 1: Bò đi lang thang, lướt thong thả
    const wanderCowMaterial = new THREE.SpriteMaterial({
      map: cowTexture,
      transparent: true,
      opacity: 0.15,
      color: 0xffffff
    });
    const wanderCowSprite = new THREE.Sprite(wanderCowMaterial);
    wanderCowSprite.scale.set(3, 3, 1);
    scene.add(wanderCowSprite);

    // Logo 2: Bò bay chéo từ dưới góc trái lên trên góc phải
    const diagonalCowMaterial = new THREE.SpriteMaterial({
      map: cowTexture,
      transparent: true,
      opacity: 0.14,
      color: 0xffffff
    });
    const diagonalCowSprite = new THREE.Sprite(diagonalCowMaterial);
    diagonalCowSprite.scale.set(2.8, 2.8, 1);
    scene.add(diagonalCowSprite);

    // 4. Đồng bộ màu sắc vật liệu theo Theme (Dark / Light / System)
    function syncThemeColors(forcedIsDark) {
      let isDark;
      if (typeof forcedIsDark === 'boolean') {
        isDark = forcedIsDark;
      } else {
        isDark = document.documentElement.classList.contains('dark') ||
                 document.body.classList.contains('dark');
      }

      if (isDark) {
        // Dark Mode: Tech Blue & sáng trắng
        particlesMaterial.color.setHex(0x4facfe);
        particlesMaterial.opacity = 0.6;
        particlesMaterial.blending = THREE.AdditiveBlending;

        wanderCowMaterial.color.setHex(0xffffff);
        wanderCowMaterial.opacity = 0.15;

        diagonalCowMaterial.color.setHex(0xffffff);
        diagonalCowMaterial.opacity = 0.14;
      } else {
        // Light Mode: Trắng ra sáng, hoàn toàn không pha xanh
        particlesMaterial.color.setHex(0x94a3b8);
        particlesMaterial.opacity = 0.22;
        particlesMaterial.blending = THREE.NormalBlending;

        wanderCowMaterial.color.setHex(0x1e293b);
        wanderCowMaterial.opacity = 0.08;

        diagonalCowMaterial.color.setHex(0x1e293b);
        diagonalCowMaterial.opacity = 0.07;
      }

      particlesMaterial.needsUpdate = true;
      wanderCowMaterial.needsUpdate = true;
      diagonalCowMaterial.needsUpdate = true;
    }

    syncThemeColors();

    // Lắng nghe sự kiện đổi theme từ site.js
    window.addEventListener('cowai-theme-change', function (event) {
      if (event.detail && typeof event.detail.isDark === 'boolean') {
        syncThemeColors(event.detail.isDark);
      } else {
        syncThemeColors();
      }
    });

    // Quan sát MutationObserver để bắt mọi thay đổi class trên html / body
    const themeObserver = new MutationObserver(function () {
      syncThemeColors();
    });
    themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] });
    themeObserver.observe(document.body, { attributes: true, attributeFilter: ['class'] });

    // 5. Logic Logo 1: Bò đi lang thang, lướt từ từ
    let isPaused = false;
    let wanderTime = 0;
    const wanderSpeed = 0.65; // Vận tốc lướt êm ái

    function getWanderTarget() {
      const boundWidth = viewport.width * 0.75;
      const boundHeight = viewport.height * 0.7;
      return new THREE.Vector3(
        (Math.random() - 0.5) * boundWidth,
        (Math.random() - 0.5) * boundHeight,
        0
      );
    }

    const wanderTarget = getWanderTarget();
    const wanderBasePos = new THREE.Vector3().copy(wanderTarget);
    wanderCowSprite.position.copy(wanderTarget);

    // 6. Logic Logo 2: Đi chéo từ góc dưới bên trái đi lên góc trên bên phải
    let diagonalTime = 0;
    const diagonalSpeed = 0.85; // Vận tốc lướt chéo từ tốn
    const diagonalAngle = 0.65; // Góc bay ~ 37 độ hướng lên trên bên phải
    const cosAngle = Math.cos(diagonalAngle);
    const sinAngle = Math.sin(diagonalAngle);

    function resetDiagonalCow() {
      // Bắt đầu từ phía dưới ngoài mép trái
      const startX = -viewport.width / 2 - 2.5 - Math.random() * 2;
      const startY = -viewport.height / 2 - 2 - (Math.random() * 2);
      return new THREE.Vector2(startX, startY);
    }

    const diagonalPos = resetDiagonalCow();
    // Bò hướng mặt về bên phải theo hướng di chuyển
    diagonalCowSprite.scale.x = -Math.abs(diagonalCowSprite.scale.x);

    // 7. Tương tác chuột & cảm ứng: Giữ chuột để dừng / Nhả để đi tiếp
    window.addEventListener('mousedown', function () { isPaused = true; });
    window.addEventListener('mouseup', function () { isPaused = false; });
    window.addEventListener('touchstart', function () { isPaused = true; }, { passive: true });
    window.addEventListener('touchend', function () { isPaused = false; }, { passive: true });

    // 8. Vòng lặp Render & Animation
    const clock = new THREE.Clock();

    function animate() {
      requestAnimationFrame(animate);
      const delta = Math.min(clock.getDelta(), 0.1);

      if (!isPaused) {
        // --- Xử lý Logo 1: Bò lang thang ---
        const moveVec = new THREE.Vector3().subVectors(wanderTarget, wanderBasePos);
        const distance = moveVec.length();

        if (distance > 0.05) {
          moveVec.normalize();
          wanderBasePos.addScaledVector(moveVec, Math.min(distance, wanderSpeed * delta));

          if (wanderTarget.x > wanderBasePos.x) {
            wanderCowSprite.scale.x = -Math.abs(wanderCowSprite.scale.x); // Quay sang phải
          } else {
            wanderCowSprite.scale.x = Math.abs(wanderCowSprite.scale.x);  // Quay sang trái
          }
        } else {
          wanderTarget.copy(getWanderTarget());
        }

        wanderTime += delta * 1.5;
        wanderCowSprite.position.x = wanderBasePos.x;
        wanderCowSprite.position.y = wanderBasePos.y + Math.sin(wanderTime) * 0.08;

        // --- Xử lý Logo 2: Đi chéo từ dưới trái lên trên phải ---
        diagonalPos.x += cosAngle * diagonalSpeed * delta;
        diagonalPos.y += sinAngle * diagonalSpeed * delta;

        diagonalTime += delta * 1.6;
        const bobbingOffset = Math.sin(diagonalTime) * 0.07;

        // Vị trí có nhấp nhô nhẹ vuông góc với đường bay chéo
        diagonalCowSprite.position.x = diagonalPos.x - sinAngle * bobbingOffset;
        diagonalCowSprite.position.y = diagonalPos.y + cosAngle * bobbingOffset;

        // Khi bay ra khỏi góc trên bên phải thì reset vòng lặp về dưới bên trái
        if (diagonalPos.x > viewport.width / 2 + 3 || diagonalPos.y > viewport.height / 2 + 3) {
          const newStart = resetDiagonalCow();
          diagonalPos.x = newStart.x;
          diagonalPos.y = newStart.y;
        }
      }

      // Xoay nhẹ các hạt 3D
      particlesMesh.rotation.y += 0.0003;
      particlesMesh.rotation.x += 0.0001;

      renderer.render(scene, camera);
    }

    animate();

    // 9. Xử lý khi co giãn cửa sổ trình duyệt
    window.addEventListener('resize', function () {
      camera.aspect = window.innerWidth / window.innerHeight;
      camera.updateProjectionMatrix();
      renderer.setSize(window.innerWidth, window.innerHeight);
      renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
      viewport = calculateViewport();
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initThreeBackground);
  } else {
    initThreeBackground();
  }
})();
