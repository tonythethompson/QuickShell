---
layout: default
title: Home
description: Quick Shell opens saved project folders from PowerToys Command Palette, PowerToys Run, or Raycast in the terminal you already use.
---

<div class="hero">
  <h1>Instant access to your project folders</h1>
  <p class="lead">
    Quick Shell puts your saved folders one keystroke away in
    <strong>PowerToys Command Palette</strong>, <strong>PowerToys Run</strong>, or <strong>Raycast</strong>.
    Open any project in your terminal of choice, with custom commands running automatically.
  </p>

  <div class="button-row">
    <a class="button button-primary" href="https://apps.microsoft.com/detail/9PC8S6LNRT3R" target="_blank" rel="noopener">Microsoft Store</a>
    <a class="button button-secondary" href="{{ '/install/' | relative_url }}#winget-command-line">WinGet</a>
    <a class="button button-secondary" href="{{ '/install/' | relative_url }}">Get Started</a>
    <a class="button button-secondary" href="{{ '/getting-started/' | relative_url }}">Learn More</a>
    <a class="button button-secondary" href="https://github.com/tonythethompson/QuickShell/releases">View Releases</a>
  </div>

  <pre class="install-snippet" aria-label="WinGet install commands"><code>winget install tonythethompson.QuickShell
winget install tonythethompson.QuickShellforCmdPal</code></pre>
  <p class="install-snippet-note">
    Bundled (Command Palette + PowerToys Run), or CmdPal only (Store-equivalent).
    <a href="{{ '/install/' | relative_url }}#winget-command-line">More install options</a>
  </p>

  <div class="sponsor-row">
    <span class="sponsor-label">Support development</span>
    <a class="button button-sponsor button-github-sponsor" href="https://github.com/sponsors/tonythethompson" target="_blank" rel="noopener">&#9829; GitHub Sponsors</a>
    <a class="button button-sponsor button-kofi" href="https://ko-fi.com/tonythethompson" target="_blank" rel="noopener">&#9749; Ko-fi</a>
  </div>
</div>

<p class="section-label">Features</p>

<div class="card-grid">
  <a class="card-link" href="{{ '/install/' | relative_url }}">
    <div class="card">
      <h3>Lightning-fast access</h3>
      <p>Search your saved folders from Command Palette (Win + Alt + Space) and open them instantly in any terminal.</p>
      <span class="card-arrow">Install now &rarr;</span>
    </div>
  </a>
  <a class="card-link" href="{{ '/getting-started/' | relative_url }}">
    <div class="card">
      <h3>Your preferred terminals</h3>
      <p>Works with Windows Terminal, PowerShell, WSL, Git Bash, and any terminal profile on your PC.</p>
      <span class="card-arrow">How it works &rarr;</span>
    </div>
  </a>
  <a class="card-link" href="{{ '/getting-started/' | relative_url }}#create-your-first-workspace">
    <div class="card">
      <h3>Auto-run commands</h3>
      <p>Run dev servers, build scripts, or multiple terminals from one workspace.</p>
      <span class="card-arrow">Set it up &rarr;</span>
    </div>
  </a>
  <a class="card-link" href="{{ '/getting-started/' | relative_url }}#discover-git-repos">
    <div class="card">
      <h3>Discover git repos</h3>
      <p>Scan local folders and add repositories as workspaces without typing paths.</p>
      <span class="card-arrow">Learn more &rarr;</span>
    </div>
  </a>
  <a class="card-link" href="{{ '/getting-started/' | relative_url }}#home-keywords">
    <div class="card">
      <h3>Home screen shortcuts</h3>
      <p>Create home keywords to jump directly to your most-used projects without searching.</p>
      <span class="card-arrow">Learn more &rarr;</span>
    </div>
  </a>
  <a class="card-link" href="{{ '/getting-started/' | relative_url }}#quick-shell-settings">
    <div class="card">
      <h3>Backup and transfer</h3>
      <p>Export and import workspaces to back up your setup or move it to another PC in seconds.</p>
      <span class="card-arrow">Export guide &rarr;</span>
    </div>
  </a>
  <a class="card-link" href="{{ '/privacy/' | relative_url }}">
    <div class="card">
      <h3>100% private</h3>
      <p>All shortcuts and settings stay on your PC. No cloud sync, no account, no telemetry.</p>
      <span class="card-arrow">Privacy policy &rarr;</span>
    </div>
  </a>
</div>

<p class="section-label">Command Palette</p>
<p class="section-intro">
  The primary PowerToys surface: browse, edit, and launch workspaces from Command Palette
  (<kbd>Win</kbd>+<kbd>Alt</kbd>+<kbd>Space</kbd>). Available from the Microsoft Store, WinGet, and GitHub.
</p>
<p class="screenshot-hint">Click to enlarge</p>

<div class="screenshot-grid">
  <img src="https://raw.githubusercontent.com/tonythethompson/QuickShell/master/QuickShell/Assets/Screenshot_1.png" alt="Quick Shell shortcut list with context menu open" loading="lazy">
  <img src="https://raw.githubusercontent.com/tonythethompson/QuickShell/master/QuickShell/Assets/Screenshot_2.png" alt="Quick Shell shortcut editor with command configuration" loading="lazy">
  <img src="https://raw.githubusercontent.com/tonythethompson/QuickShell/master/QuickShell/Assets/Screenshot_3.png" alt="Quick Shell settings and terminal profile configuration" loading="lazy">
</div>

<p class="section-label">PowerToys Run</p>
<p class="section-intro">
  Same workspaces from Run (<kbd>Alt</kbd>+<kbd>Space</kbd>): type <strong>qs</strong> to search and launch.
  Bundled with the WinGet / GitHub EXE installers; native settings and editor included.
  <a href="{{ '/install/' | relative_url }}">Install options &rarr;</a>
</p>
<p class="screenshot-hint">Click to enlarge</p>

<div class="screenshot-grid">
  <img src="https://raw.githubusercontent.com/tonythethompson/QuickShell/master/QuickShell/Assets/Screenshot_Run_1.png" alt="PowerToys Run search results for qs" loading="lazy">
  <img src="https://raw.githubusercontent.com/tonythethompson/QuickShell/master/QuickShell/Assets/Screenshot_Run_2.png" alt="Quick Shell PowerToys Run settings window" loading="lazy">
  <img src="https://raw.githubusercontent.com/tonythethompson/QuickShell/master/QuickShell/Assets/Screenshot_Run_3.png" alt="Create workspace in PowerToys Run, General tab" loading="lazy">
</div>

<p class="section-label">Raycast</p>
<p class="section-intro">
  Native Raycast extension for Windows and macOS: open, create, and manage workspaces from Raycast root search.
  Install from the
  <a href="https://www.raycast.com/store" target="_blank" rel="noopener">Raycast Store</a>
  (not shipped via GitHub or WinGet). Import/export JSON to bridge with CmdPal and Run.
</p>
<p class="screenshot-hint">Click to enlarge</p>

<div class="screenshot-grid">
  <img src="https://raw.githubusercontent.com/tonythethompson/QuickShell/master/QuickShell/Assets/Screenshot_Raycast_1.png" alt="Raycast Create Workspace form with directory auto-fill" loading="lazy">
  <img src="https://raw.githubusercontent.com/tonythethompson/QuickShell/master/QuickShell/Assets/Screenshot_Raycast_2.png" alt="Raycast root search showing Quick Shell commands" loading="lazy">
  <img src="https://raw.githubusercontent.com/tonythethompson/QuickShell/master/QuickShell/Assets/Screenshot_Raycast_3.png" alt="Raycast Create Workspace form with links fields" loading="lazy">
</div>

<p class="section-label">Requirements</p>

<div class="card-grid home-bottom-grid">
  <div class="card">
    <h3>Windows</h3>
    <p>Windows 10 version 2004 or later. Windows 11 recommended. Raycast also supports macOS.</p>
  </div>
  <div class="card">
    <h3>A launcher</h3>
    <p>
      <a href="https://learn.microsoft.com/windows/powertoys/install" target="_blank" rel="noopener">PowerToys</a>
      (Command Palette and/or Run) or
      <a href="https://www.raycast.com/" target="_blank" rel="noopener">Raycast</a>.
    </p>
  </div>
  <div class="card">
    <h3>Get going</h3>
    <p><a href="{{ '/install/' | relative_url }}">Install now</a> and create your first workspace in under two minutes.</p>
  </div>
  <div class="card">
    <h3>Questions?</h3>
    <p><a href="{{ '/getting-started/' | relative_url }}">Getting started</a> or email <a href="mailto:{{ site.author.email }}">{{ site.author.email }}</a>.</p>
  </div>
</div>
