(function () {
  'use strict';

  function initThemeSwitcher() {
    const themeToggleBtn = document.getElementById('themeToggleBtn');
    const themeDropdown = document.getElementById('themeDropdownMenu');
    const themeOptions = document.querySelectorAll('.theme-option');

    if (!themeToggleBtn || !themeDropdown) {
      return;
    }

    function updateThemeToggleIcon(theme, isDark) {
      const iconSvg = themeToggleBtn.querySelector('svg');
      if (!iconSvg) {
        return;
      }

      if (theme === 'system') {
        // Biểu tượng máy tính / hệ thống
        iconSvg.innerHTML = '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17h14a2 2 0 002-2V5a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />';
      } else if (isDark) {
        // Biểu tượng trăng khuyết (Dark)
        iconSvg.innerHTML = '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20.354 15.354A9 9 0 018.646 3.646 9.003 9.003 0 0012 21a9.003 9.003 0 008.354-5.646z" />';
      } else {
        // Biểu tượng mặt trời (Light)
        iconSvg.innerHTML = '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 3v1m0 16v1m9-9h-1M4 12H3m15.364 6.364l-.707-.707M6.343 6.343l-.707-.707m12.728 0l-.707.707M6.343 17.657l-.707.707M16 12a4 4 0 11-8 0 4 4 0 018 0z" />';
      }
    }

    function applyTheme(theme) {
      const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
      const isDark = (theme === 'dark') || (theme === 'system' && prefersDark);

      document.documentElement.classList.remove('dark', 'light');
      if (document.body) {
        document.body.classList.remove('dark', 'light');
      }

      if (isDark) {
        document.documentElement.classList.add('dark');
        if (document.body) document.body.classList.add('dark');
      } else {
        document.documentElement.classList.add('light');
        if (document.body) document.body.classList.add('light');
      }

      const canvas = document.getElementById('three-canvas') || document.getElementById('bg-canvas');
      if (canvas) {
        canvas.style.background = 'transparent';
      }

      localStorage.setItem('cowai_theme', theme);

      themeOptions.forEach(function (opt) {
        if (opt.getAttribute('data-theme') === theme) {
          opt.classList.add('active');
        } else {
          opt.classList.remove('active');
        }
      });

      updateThemeToggleIcon(theme, isDark);

      window.dispatchEvent(new CustomEvent('cowai-theme-change', {
        detail: { theme: theme, isDark: isDark }
      }));
    }

    // Toggle menu khi click vào nút (nếu có dropdown)
    if (themeToggleBtn && themeDropdown) {
      themeToggleBtn.addEventListener('click', function (event) {
        event.stopPropagation();
        themeDropdown.classList.toggle('active');
      });

      document.addEventListener('click', function (event) {
        if (!themeDropdown.contains(event.target) && !themeToggleBtn.contains(event.target)) {
          themeDropdown.classList.remove('active');
        }
      });

      themeOptions.forEach(function (option) {
        option.addEventListener('click', function (event) {
          event.stopPropagation();
          const selectedTheme = option.getAttribute('data-theme');
          applyTheme(selectedTheme);
          themeDropdown.classList.remove('active');
        });
      });
    }

    const savedTheme = localStorage.getItem('cowai_theme') || 'dark';
    applyTheme(savedTheme);

    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
    const handleMediaChange = function () {
      if (localStorage.getItem('cowai_theme') === 'system') {
        applyTheme('system');
      }
    };

    if (mediaQuery.addEventListener) {
      mediaQuery.addEventListener('change', handleMediaChange);
    } else if (mediaQuery.addListener) {
      mediaQuery.addListener(handleMediaChange);
    }
  }

  function initMobileNav() {
    const toggleBtn = document.getElementById('mobileNavToggleBtn');
    const mobileMenu = document.getElementById('mobileNavMenu');
    const drawerToggleBtn = document.getElementById('toggleHistoryDrawerBtn');

    if (!toggleBtn || !mobileMenu) {
      return;
    }

    const iconHamburger = toggleBtn.querySelector('.icon-menu-hamburger');
    const iconClose = toggleBtn.querySelector('.icon-menu-close');

    function setMenuOpen(open) {
      if (open) {
        mobileMenu.classList.add('active');
        mobileMenu.setAttribute('aria-hidden', 'false');
        toggleBtn.setAttribute('aria-expanded', 'true');
        toggleBtn.setAttribute('aria-label', 'Đóng menu điều hướng tính năng');
        if (iconHamburger) iconHamburger.classList.add('hidden');
        if (iconClose) iconClose.classList.remove('hidden');

        // Đóng theme dropdown nếu đang mở
        const themeDropdown = document.getElementById('themeDropdownMenu');
        if (themeDropdown) themeDropdown.classList.remove('active');
      } else {
        mobileMenu.classList.remove('active');
        mobileMenu.setAttribute('aria-hidden', 'true');
        toggleBtn.setAttribute('aria-expanded', 'false');
        toggleBtn.setAttribute('aria-label', 'Mở menu điều hướng tính năng');
        if (iconHamburger) iconHamburger.classList.remove('hidden');
        if (iconClose) iconClose.classList.add('hidden');
      }
    }

    toggleBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      const isOpen = mobileMenu.classList.contains('active');
      setMenuOpen(!isOpen);
    });

    // Đóng khi click bất kỳ link nào trong mobile menu
    mobileMenu.querySelectorAll('a').forEach(function (link) {
      link.addEventListener('click', function () {
        setMenuOpen(false);
      });
    });

    // Đóng khi click ra ngoài
    document.addEventListener('click', function (e) {
      if (!mobileMenu.contains(e.target) && !toggleBtn.contains(e.target)) {
        if (mobileMenu.classList.contains('active')) {
          setMenuOpen(false);
        }
      }
    });

    // Đóng khi bấm phím Escape
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && mobileMenu.classList.contains('active')) {
        setMenuOpen(false);
        toggleBtn.focus();
      }
    });

    // Tự động đóng mobile nav nếu người dùng mở sidebar chat
    if (drawerToggleBtn) {
      drawerToggleBtn.addEventListener('click', function () {
        setMenuOpen(false);
      });
    }

    // Đóng khi resize màn hình rộng ra desktop (> 768px)
    window.addEventListener('resize', function () {
      if (window.innerWidth > 768 && mobileMenu.classList.contains('active')) {
        setMenuOpen(false);
      }
    });
  }

  function initUserMenu() {
    const userMenuBtn = document.getElementById('userMenuBtn');
    const userDropdown = document.getElementById('userDropdownMenu');

    if (!userMenuBtn || !userDropdown) return;

    userMenuBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      const isOpen = userDropdown.classList.contains('active');
      if (isOpen) {
        userDropdown.classList.remove('active');
        userDropdown.setAttribute('aria-hidden', 'true');
        userMenuBtn.setAttribute('aria-expanded', 'false');
      } else {
        userDropdown.classList.add('active');
        userDropdown.setAttribute('aria-hidden', 'false');
        userMenuBtn.setAttribute('aria-expanded', 'true');

        // Đóng theme dropdown nếu đang mở
        const themeDropdown = document.getElementById('themeDropdownMenu');
        if (themeDropdown) themeDropdown.classList.remove('active');
      }
    });

    document.addEventListener('click', function (e) {
      if (!userDropdown.contains(e.target) && !userMenuBtn.contains(e.target)) {
        userDropdown.classList.remove('active');
        userDropdown.setAttribute('aria-hidden', 'true');
        userMenuBtn.setAttribute('aria-expanded', 'false');
      }
    });

    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && userDropdown.classList.contains('active')) {
        userDropdown.classList.remove('active');
        userDropdown.setAttribute('aria-hidden', 'true');
        userMenuBtn.setAttribute('aria-expanded', 'false');
        userMenuBtn.focus();
      }
    });
  }

  function initToast() {
    const toast = document.getElementById('appToast');
    if (toast) {
      setTimeout(function () {
        if (toast && toast.parentNode) {
          toast.style.opacity = '0';
          toast.style.transform = 'translateY(-10px)';
          toast.style.transition = 'opacity 0.3s ease, transform 0.3s ease';
          setTimeout(function () {
            if (toast.parentNode) toast.parentNode.removeChild(toast);
          }, 300);
        }
      }, 5000);
    }
  }

  function initAmbientSpotlight() {
    if (!document.body || document.body.classList.contains('studio-standalone-body')) return;
    if (!window.matchMedia('(hover: hover) and (pointer: fine)').matches) return;
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;

    let rafId = 0;
    let lastX = 0;
    let lastY = 0;
    window.addEventListener('pointermove', function (e) {
      lastX = e.clientX;
      lastY = e.clientY;
      if (rafId) return;
      rafId = requestAnimationFrame(function () {
        document.documentElement.style.setProperty('--cow-mx', lastX + 'px');
        document.documentElement.style.setProperty('--cow-my', lastY + 'px');
        rafId = 0;
      });
    }, { passive: true });
  }

  function initApp() {
    initThemeSwitcher();
    initMobileNav();
    initUserMenu();
    initToast();
    initAmbientSpotlight();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initApp);
  } else {
    initApp();
  }
})();
