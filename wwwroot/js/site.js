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

    // Toggle menu khi click vào nút
    themeToggleBtn.addEventListener('click', function (event) {
      event.stopPropagation();
      themeDropdown.classList.toggle('active');
    });

    // Đóng dropdown khi click ra ngoài
    document.addEventListener('click', function (event) {
      if (!themeDropdown.contains(event.target) && !themeToggleBtn.contains(event.target)) {
        themeDropdown.classList.remove('active');
      }
    });

    // Xử lý khi click vào từng option
    themeOptions.forEach(function (option) {
      option.addEventListener('click', function (event) {
        event.stopPropagation();
        const selectedTheme = option.getAttribute('data-theme');
        applyTheme(selectedTheme);
        themeDropdown.classList.remove('active');
      });
    });

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

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initThemeSwitcher);
  } else {
    initThemeSwitcher();
  }
})();
