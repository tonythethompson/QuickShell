# Quick Shell website (GitHub Pages)

Jekyll site published from the `/docs` folder.

**Live site:** https://tonythethompson.github.io/QuickShell/

## Enable GitHub Pages

1. Open **Settings → Pages** on [github.com/tonythethompson/QuickShell](https://github.com/tonythethompson/QuickShell)
2. **Build and deployment → Source:** **GitHub Actions** (uses `.github/workflows/pages.yml`)
3. Leave **Custom domain** empty (project site under `/QuickShell/`)
4. Enable **Enforce HTTPS**

`docs/_config.yml` sets `url` to `https://tonythethompson.github.io` and `baseurl` to `/QuickShell` so CSS and links resolve on the project Pages URL.

Use https://tonythethompson.github.io/QuickShell/privacy/ for the Microsoft Store **privacy policy** URL.

## Preview locally

Requires Ruby and Bundler:

```powershell
cd docs
bundle install
bundle exec jekyll serve --baseurl ""
```

Open http://localhost:4000/ (local preview can omit the project base path).
