(function () {
  "use strict";

  var data = window.SKILLS_SITE_DATA;
  if (!data) return;

  var GITHUB_BLOB_BASE = "https://github.com/TimyVillarmia/skills/blob/main/";
  var GITHUB_TREE_BASE = "https://github.com/TimyVillarmia/skills/tree/main/";

  // 1. Popover polyfill (only when native popover is missing)
  if (!Object.hasOwn(HTMLElement.prototype, "popover")) {
    var polyScript = document.createElement("script");
    polyScript.type = "module";
    polyScript.src = "https://unpkg.com/@oddbird/popover-polyfill@latest/dist/popover.min.js";
    document.head.appendChild(polyScript);
  }

  // 2. Sticky header scroll fallback when scroll-driven animations are unsupported
  var siteHeader = document.getElementById("site-header");
  if (siteHeader && !CSS.supports("(animation-timeline: scroll()) and (animation-range: 0% 100%)")) {
    var syncHeaderScroll = function () {
      siteHeader.classList.toggle("is-scrolled", window.scrollY > 12);
    };
    window.addEventListener("scroll", syncHeaderScroll, { passive: true });
    syncHeaderScroll();
  }

  // 3. Adaptive Popover Navbar (ResizeObserver measurement)
  var headerInner = document.getElementById("header-inner");
  var siteMenu = document.getElementById("site-menu");

  if (siteHeader && headerInner && siteMenu) {
    var lastWidth = 0;

    var updateNavLayout = function () {
      var wasOpen = false;
      try {
        wasOpen = siteMenu.matches(":popover-open, .\\:popover-open");
      } catch (e) {
        wasOpen = siteMenu.classList.contains(":popover-open");
      }

      siteHeader.classList.add("is-measuring", "is-wide");
      siteMenu.removeAttribute("popover");
      var fitsInline = headerInner.scrollWidth <= headerInner.clientWidth;

      if (fitsInline) {
        siteHeader.classList.add("is-wide");
      } else {
        siteHeader.classList.remove("is-wide");
        siteMenu.setAttribute("popover", "auto");
        if (wasOpen && typeof siteMenu.showPopover === "function") {
          try {
            siteMenu.showPopover();
          } catch (e) {}
        }
      }

      requestAnimationFrame(function () {
        siteHeader.classList.remove("is-measuring");
      });
    };

    if ("ResizeObserver" in window) {
      var navObserver = new ResizeObserver(function (entries) {
        var width = entries[0] ? entries[0].contentRect.width : headerInner.clientWidth;
        var isWide = siteHeader.classList.contains("is-wide");
        if (isWide || width > lastWidth) {
          updateNavLayout();
        }
        lastWidth = width;
      });
      navObserver.observe(siteHeader);
      navObserver.observe(siteMenu);
    }

    siteMenu.addEventListener("click", function (e) {
      if (e.target.closest("a") && !siteHeader.classList.contains("is-wide") && typeof siteMenu.hidePopover === "function") {
        try {
          siteMenu.hidePopover();
        } catch (err) {}
      }
    });

    updateNavLayout();
  }

  // 4. Two-State Color-Scheme Toggle (<meta name="color-scheme">)
  var colorSchemeMeta = document.querySelector('meta[name="color-scheme"]');
  var themeToggle = document.getElementById("theme-toggle");
  var darkMedia = window.matchMedia("(prefers-color-scheme: dark)");

  function getStoredScheme() {
    try {
      var val = localStorage.getItem("color-scheme");
      return val === "light" || val === "dark" ? val : null;
    } catch (e) {
      return null;
    }
  }

  function getSystemScheme() {
    return darkMedia.matches ? "dark" : "light";
  }

  function syncMetaFromStorage() {
    if (!colorSchemeMeta) return;
    var stored = getStoredScheme();
    colorSchemeMeta.content = stored ?? "light dark";
  }

  if (themeToggle && colorSchemeMeta) {
    themeToggle.addEventListener("click", function () {
      var rendered = getStoredScheme() ?? getSystemScheme();
      var target = rendered === "dark" ? "light" : "dark";
      var system = getSystemScheme();

      if (target !== system) {
        try {
          localStorage.setItem("color-scheme", target);
        } catch (e) {}
        colorSchemeMeta.content = target;
      } else {
        try {
          localStorage.removeItem("color-scheme");
        } catch (e) {}
        colorSchemeMeta.content = "light dark";
      }
    });

    window.addEventListener("storage", function (e) {
      if (e.key === "color-scheme") {
        syncMetaFromStorage();
      }
    });
  }

  // 5. Tab helpers: selection, hidden="until-found", beforematch, focusgroup fallback, scroll fade hints
  var hasNativeFocusgroup = Object.hasOwn(HTMLElement.prototype, "focusGroup");

  function setupTabGroup(tablistEl, panelsContainer, onSelected) {
    if (!tablistEl || !panelsContainer) return;

    function getTabs() {
      return Array.prototype.slice.call(tablistEl.querySelectorAll('[role="tab"]'));
    }

    function getPanels() {
      return Array.prototype.slice.call(panelsContainer.querySelectorAll('[role="tabpanel"]'));
    }

    function selectTab(targetTab, shouldScrollIntoView) {
      var tabs = getTabs();
      var panels = getPanels();
      var targetId = targetTab.id;

      tabs.forEach(function (tab) {
        var selected = tab === targetTab;
        tab.setAttribute("aria-selected", String(selected));
        if (selected) {
          tab.setAttribute("focusgroupstart", "");
          if (!hasNativeFocusgroup) tab.tabIndex = 0;
        } else {
          tab.removeAttribute("focusgroupstart");
          if (!hasNativeFocusgroup) tab.tabIndex = -1;
        }
      });

      panels.forEach(function (panel) {
        var isMatch = panel.getAttribute("aria-labelledby") === targetId;
        if (isMatch) {
          panel.hidden = false;
          panel.tabIndex = 0;
        } else {
          panel.hidden = "until-found";
          panel.removeAttribute("tabindex");
        }
      });

      if (shouldScrollIntoView && typeof targetTab.scrollIntoView === "function") {
        try {
          targetTab.scrollIntoView({ block: "nearest", inline: "nearest", container: "nearest" });
        } catch (e) {
          targetTab.scrollIntoView({ block: "nearest", inline: "nearest" });
        }
      }

      if (onSelected) onSelected(targetTab);
    }

    tablistEl.addEventListener("click", function (e) {
      var tab = e.target.closest('[role="tab"]');
      if (!tab) return;
      selectTab(tab, true);
    });

    // Roving tabindex + selection-follows-focus fallback when native focusgroup is unsupported
    tablistEl.addEventListener("keydown", function (e) {
      var tabs = getTabs();
      var idx = tabs.indexOf(document.activeElement);
      if (idx === -1) return;

      var nextIdx = null;
      if (e.key === "ArrowRight") nextIdx = (idx + 1) % tabs.length;
      else if (e.key === "ArrowLeft") nextIdx = (idx - 1 + tabs.length) % tabs.length;
      else if (e.key === "Home") nextIdx = 0;
      else if (e.key === "End") nextIdx = tabs.length - 1;

      if (nextIdx !== null) {
        e.preventDefault();
        tabs[nextIdx].focus();
        selectTab(tabs[nextIdx], true);
      }
    });

    // Reveal matching tab when browser Find-in-Page opens a hidden="until-found" panel
    panelsContainer.addEventListener("beforematch", function (e) {
      var panel = e.target.closest('[role="tabpanel"]');
      if (!panel) return;
      var tabId = panel.getAttribute("aria-labelledby");
      var tab = tabId ? document.getElementById(tabId) : null;
      if (tab) {
        selectTab(tab, true);
      }
    });

    // Initialize tabIndex state when native focusgroup is absent
    if (!hasNativeFocusgroup) {
      var tabs = getTabs();
      tabs.forEach(function (tab, i) {
        tab.tabIndex = tab.getAttribute("aria-selected") === "true" || i === 0 ? 0 : -1;
      });
    }

    return { selectTab: selectTab };
  }

  // Scroll fade hints fallback when scroll-driven animations are unsupported
  if (!CSS.supports("animation-timeline", "scroll()")) {
    document.querySelectorAll(".tablist").forEach(function (scroller) {
      var first = scroller.firstElementChild;
      var last = scroller.lastElementChild;
      if (!first || !last || !("IntersectionObserver" in window)) return;
      var observer = new IntersectionObserver(function (entries) {
        entries.forEach(function (entry) {
          var isFirst = entry.target === first;
          scroller.classList.toggle(isFirst ? "more-start" : "more-end", entry.intersectionRatio < 1);
        });
      }, { root: scroller, threshold: 1, rootMargin: "1px" });
      observer.observe(first);
      observer.observe(last);
    });
  }

  // 6. Install Workbench Tabs + Copy
  var installTabs = document.getElementById("install-tabs");
  var installPanels = document.getElementById("install-panels");
  var installCopy = document.getElementById("install-copy");
  var copyStatus = document.getElementById("copy-status");
  var activeInstallKey = "agy";

  setupTabGroup(installTabs, installPanels, function (tab) {
    activeInstallKey = tab.getAttribute("data-install") || "agy";
  });

  function copyText(text) {
    if (navigator.clipboard && window.isSecureContext) {
      return navigator.clipboard.writeText(text);
    }
    return new Promise(function (resolve, reject) {
      var ta = document.createElement("textarea");
      ta.value = text;
      ta.setAttribute("readonly", "");
      ta.style.position = "fixed";
      ta.style.opacity = "0";
      document.body.appendChild(ta);
      ta.select();
      try {
        document.execCommand("copy");
        resolve();
      } catch (err) {
        reject(err);
      } finally {
        document.body.removeChild(ta);
      }
    });
  }

  var copyResetTimer = null;
  if (installCopy) {
    installCopy.addEventListener("click", function () {
      var entry = data.installCommands[activeInstallKey];
      if (!entry) return;
      copyText(entry.code).then(function () {
        var label = installCopy.querySelector(".copy-label");
        if (label) label.textContent = "Copied";
        installCopy.classList.add("is-copied");
        if (copyStatus) copyStatus.textContent = "Install command copied to clipboard.";
        clearTimeout(copyResetTimer);
        copyResetTimer = setTimeout(function () {
          if (label) label.textContent = "Copy";
          installCopy.classList.remove("is-copied");
        }, 1800);
      });
    });
  }

  // 7. Lightweight Syntax Highlighter
  var CSHARP_KEYWORDS = new Set([
    "public", "private", "internal", "protected", "sealed", "static", "partial",
    "class", "record", "struct", "interface", "enum", "namespace", "using",
    "async", "await", "return", "new", "var", "if", "else", "while", "foreach",
    "in", "is", "not", "and", "or", "true", "false", "null", "this", "override",
    "readonly", "required", "init", "get", "set", "field", "void", "long", "int",
    "string", "bool", "decimal", "double", "try", "catch", "finally", "throw"
  ]);

  function escapeHtml(str) {
    return str
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function highlightCSharpLine(line) {
    var trimmed = line.trimStart();
    if (trimmed.startsWith("//")) {
      return '<span class="tok-cmt">' + escapeHtml(line) + "</span>";
    }
    if (trimmed.startsWith("[") && trimmed.endsWith("]")) {
      return '<span class="tok-attr">' + escapeHtml(line) + "</span>";
    }

    var tokenRe = /(\/\/.*$)|(\$?"(?:\\.|[^"\\])*")|(\b[A-Za-z_][A-Za-z0-9_]*\b)|(\b\d+(?:_\d+)*(?:\.\d+)?\b)/g;
    var out = "";
    var lastIndex = 0;
    var match;

    while ((match = tokenRe.exec(line)) !== null) {
      if (match.index > lastIndex) {
        out += escapeHtml(line.slice(lastIndex, match.index));
      }
      var comment = match[1];
      var str = match[2];
      var ident = match[3];
      var num = match[4];

      if (comment) {
        out += '<span class="tok-cmt">' + escapeHtml(comment) + "</span>";
      } else if (str) {
        out += '<span class="tok-str">' + escapeHtml(str) + "</span>";
      } else if (ident) {
        if (CSHARP_KEYWORDS.has(ident)) {
          out += '<span class="tok-kw">' + escapeHtml(ident) + "</span>";
        } else if (/^[A-Z][A-Za-z0-9_]*$/.test(ident)) {
          out += '<span class="tok-type">' + escapeHtml(ident) + "</span>";
        } else {
          out += escapeHtml(ident);
        }
      } else if (num) {
        out += '<span class="tok-num">' + escapeHtml(num) + "</span>";
      }
      lastIndex = tokenRe.lastIndex;
    }

    if (lastIndex < line.length) {
      out += escapeHtml(line.slice(lastIndex));
    }
    return out;
  }

  function highlightXmlLine(line) {
    var trimmed = line.trimStart();
    if (trimmed.startsWith("<!--") || trimmed.startsWith("//")) {
      return '<span class="tok-cmt">' + escapeHtml(line) + "</span>";
    }
    var tokenRe = /(<\/?[A-Za-z0-9_.-]+)|(\b[A-Za-z0-9_.-]+)=("[^"]*")/g;
    var out = "";
    var lastIndex = 0;
    var match;

    while ((match = tokenRe.exec(line)) !== null) {
      if (match.index > lastIndex) {
        out += escapeHtml(line.slice(lastIndex, match.index));
      }
      if (match[1]) {
        out += '<span class="tok-kw">' + escapeHtml(match[1]) + "</span>";
      } else if (match[2] && match[3]) {
        out += '<span class="tok-type">' + escapeHtml(match[2]) + '</span>=<span class="tok-str">' + escapeHtml(match[3]) + "</span>";
      }
      lastIndex = tokenRe.lastIndex;
    }

    if (lastIndex < line.length) {
      out += escapeHtml(line.slice(lastIndex));
    }
    return out;
  }

  function buildCodeHtml(code, lang, highlightLines, highlightClass) {
    var lines = code.split("\n");
    var highlightedSet = new Set(highlightLines || []);
    return lines.map(function (line, idx) {
      var lineNo = idx + 1;
      var cls = "code-line" + (highlightedSet.has(lineNo) ? " " + highlightClass : "");
      var trimmed = line.trimStart();
      var useXml = lang === "xml" || (lang === "mixed" && (trimmed.startsWith("<") || trimmed.startsWith("<!--")));
      var rendered = useXml ? highlightXmlLine(line) : highlightCSharpLine(line);
      return '<span class="' + cls + '"><span class="line-no">' + lineNo + '</span><span class="line-text">' + (rendered || " ") + "</span></span>";
    }).join("");
  }

  function buildDiffPane(title, file, sideData, paneClass, lineHighlightClass, highlightLines) {
    var pane = document.createElement("div");
    pane.className = "diff-pane " + paneClass;

    var header = document.createElement("div");
    header.className = "pane-header";

    var titleSpan = document.createElement("span");
    titleSpan.className = "pane-title";
    titleSpan.textContent = title;

    var fileSpan = document.createElement("span");
    fileSpan.className = "pane-file";
    fileSpan.textContent = file;

    header.appendChild(titleSpan);
    header.appendChild(fileSpan);

    var scrollWrap = document.createElement("div");
    scrollWrap.className = "code-scroll";
    scrollWrap.tabIndex = 0;
    scrollWrap.setAttribute("aria-label", title + " code");

    var pre = document.createElement("pre");
    pre.className = "code-block";
    pre.innerHTML = buildCodeHtml(sideData.code, sideData.lang, highlightLines, lineHighlightClass);
    scrollWrap.appendChild(pre);

    var callouts = document.createElement("ul");
    callouts.className = "callout-list";
    sideData.callouts.forEach(function (text) {
      var li = document.createElement("li");
      li.textContent = text;
      callouts.appendChild(li);
    });

    pane.appendChild(header);
    pane.appendChild(scrollWrap);
    pane.appendChild(callouts);
    return pane;
  }

  // 8. Benchmark Scenario Tabs & Pre-rendered Panels (hidden="until-found")
  var scenarioTabs = document.getElementById("scenario-tabs");
  var scenarioPanels = document.getElementById("scenario-panels");

  if (scenarioTabs && scenarioPanels && data.scenarios.length) {
    data.scenarios.forEach(function (s, idx) {
      var tabId = "scenario-tab-" + s.id;
      var btn = document.createElement("button");
      btn.type = "button";
      btn.id = tabId;
      btn.setAttribute("role", "tab");
      btn.setAttribute("aria-selected", String(idx === 0));
      if (idx === 0) btn.setAttribute("focusgroupstart", "");
      btn.textContent = s.label;
      scenarioTabs.appendChild(btn);

      var panel = document.createElement("div");
      panel.setAttribute("role", "tabpanel");
      panel.setAttribute("aria-labelledby", tabId);
      if (idx === 0) {
        panel.tabIndex = 0;
      } else {
        panel.hidden = "until-found";
      }

      var inner = document.createElement("div");
      inner.className = "tabpanel-inner";

      var meta = document.createElement("div");
      meta.className = "scenario-meta";

      var h3 = document.createElement("h3");
      h3.className = "scenario-heading";
      h3.textContent = s.title;

      var summary = document.createElement("p");
      summary.className = "scenario-summary";
      summary.textContent = s.summary;

      meta.appendChild(h3);
      meta.appendChild(summary);

      inner.appendChild(meta);

      if (s.prompt) {
        var promptStrip = document.createElement("div");
        promptStrip.className = "prompt-strip";

        var promptLabel = document.createElement("span");
        promptLabel.className = "prompt-label";
        promptLabel.textContent = "Shared prompt";

        var promptText = document.createElement("p");
        promptText.className = "prompt-text";
        promptText.textContent = s.prompt;

        promptStrip.appendChild(promptLabel);
        promptStrip.appendChild(promptText);
        inner.appendChild(promptStrip);
      }

      var grid = document.createElement("div");
      grid.className = "diff-grid";
      grid.appendChild(buildDiffPane("Without Skill (Baseline)", s.baseline.file, s.baseline, "diff-baseline", "is-warn", s.baseline.warnLines));
      grid.appendChild(buildDiffPane("With dotnet Skill", s.skill.file, s.skill, "diff-skill", "is-pass", s.skill.passLines));

      inner.appendChild(grid);
      panel.appendChild(inner);
      scenarioPanels.appendChild(panel);
    });

    var scenarioTabGroup = setupTabGroup(scenarioTabs, scenarioPanels);

    document.querySelectorAll("[data-jump-scenario]").forEach(function (link) {
      link.addEventListener("click", function () {
        var id = link.getAttribute("data-jump-scenario");
        var tab = id ? document.getElementById("scenario-tab-" + id) : null;
        if (tab && scenarioTabGroup) {
          scenarioTabGroup.selectTab(tab, true);
        }
      });
    });
  }

  // 9. Scorecard Table (<th scope="row"> for sticky row headers)
  var scorecardBody = document.getElementById("scorecard-body");
  if (scorecardBody && data.scorecard) {
    data.scorecard.forEach(function (row) {
      var tr = document.createElement("tr");

      var thRule = document.createElement("th");
      thRule.setAttribute("scope", "row");
      thRule.textContent = row.rule;

      var tdBase = document.createElement("td");
      tdBase.className = "status-cell " + (row.baseline ? "status-pass" : "status-fail");
      tdBase.textContent = row.baseline ? "Pass" : "Miss";

      var tdSkill = document.createElement("td");
      tdSkill.className = "status-cell " + (row.skill ? "status-pass" : "status-fail");
      tdSkill.textContent = row.skill ? "Pass" : "Miss";

      var tdEvidence = document.createElement("td");
      tdEvidence.textContent = row.evidence;

      tr.appendChild(thRule);
      tr.appendChild(tdBase);
      tr.appendChild(tdSkill);
      tr.appendChild(tdEvidence);
      scorecardBody.appendChild(tr);
    });
  }

  // 10. Catalog Filter & Search (<th scope="row">)
  var catalogFilters = document.getElementById("catalog-filters");
  var catalogSearch = document.getElementById("catalog-search");
  var catalogBody = document.getElementById("catalog-body");
  var catalogEmpty = document.getElementById("catalog-empty");
  var activeSkillFilter = "all";

  function renderCatalog() {
    if (!catalogBody || !data.catalog) return;
    var query = (catalogSearch ? catalogSearch.value : "").trim().toLowerCase();

    var filtered = data.catalog.filter(function (item) {
      var matchesSkill = activeSkillFilter === "all" || item.skill === activeSkillFilter;
      if (!matchesSkill) return false;
      if (!query) return true;
      return (
        item.skill.toLowerCase().includes(query) ||
        item.name.toLowerCase().includes(query) ||
        item.scope.toLowerCase().includes(query)
      );
    });

    catalogBody.innerHTML = "";
    if (catalogEmpty) {
      catalogEmpty.hidden = filtered.length > 0;
    }

    filtered.forEach(function (item) {
      var tr = document.createElement("tr");

      var thSkill = document.createElement("th");
      thSkill.setAttribute("scope", "row");
      var codeSkill = document.createElement("code");
      codeSkill.textContent = item.skill;
      thSkill.appendChild(codeSkill);

      var tdName = document.createElement("td");
      var codeName = document.createElement("code");
      codeName.textContent = item.name;
      tdName.appendChild(codeName);

      var tdScope = document.createElement("td");
      tdScope.textContent = item.scope;

      var tdLink = document.createElement("td");
      var a = document.createElement("a");
      var isDir = item.name.endsWith("/");
      a.href = (isDir ? GITHUB_TREE_BASE : GITHUB_BLOB_BASE) + item.path;
      a.target = "_blank";
      a.rel = "noopener noreferrer";
      a.className = "catalog-link";
      a.textContent = "View source";
      tdLink.appendChild(a);

      tr.appendChild(thSkill);
      tr.appendChild(tdName);
      tr.appendChild(tdScope);
      tr.appendChild(tdLink);
      catalogBody.appendChild(tr);
    });
  }

  if (catalogFilters) {
    catalogFilters.addEventListener("click", function (e) {
      var btn = e.target.closest('[role="tab"]');
      if (!btn) return;
      activeSkillFilter = btn.getAttribute("data-filter") || "all";
      catalogFilters.querySelectorAll('[role="tab"]').forEach(function (b) {
        var selected = b === btn;
        b.setAttribute("aria-selected", String(selected));
        if (selected) b.setAttribute("focusgroupstart", "");
        else b.removeAttribute("focusgroupstart");
      });
      renderCatalog();
    });

    catalogFilters.addEventListener("keydown", function (e) {
      var tabs = Array.prototype.slice.call(catalogFilters.querySelectorAll('[role="tab"]'));
      var idx = tabs.indexOf(document.activeElement);
      if (idx === -1) return;
      var nextIdx = null;
      if (e.key === "ArrowRight") nextIdx = (idx + 1) % tabs.length;
      else if (e.key === "ArrowLeft") nextIdx = (idx - 1 + tabs.length) % tabs.length;
      else if (e.key === "Home") nextIdx = 0;
      else if (e.key === "End") nextIdx = tabs.length - 1;
      if (nextIdx !== null) {
        e.preventDefault();
        tabs[nextIdx].focus();
        tabs[nextIdx].click();
      }
    });
  }

  if (catalogSearch) {
    catalogSearch.addEventListener("input", renderCatalog);
  }

  renderCatalog();
})();
