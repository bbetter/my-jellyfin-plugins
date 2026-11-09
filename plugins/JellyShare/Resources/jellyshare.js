/* JellyShare — injects a Share button into Jellyfin item detail pages */
(function JellyShare() {
    'use strict';

    // ── Auth ──────────────────────────────────────────────────────────────────

    function getAuthToken() {
        if (window.ApiClient && typeof window.ApiClient.accessToken === 'function') {
            return window.ApiClient.accessToken() || '';
        }
        try {
            const raw = localStorage.getItem('jellyfin_credentials');
            if (raw) {
                const creds = JSON.parse(raw);
                const token = (creds.Servers || creds.servers || [])[0]?.AccessToken;
                if (token) return token;
            }
        } catch (_) {}
        return '';
    }

    // ── Base URL ──────────────────────────────────────────────────────────────
    // Prefer ApiClient.getUrl so a server mounted under a sub-path still works.
    // Fall back to absolute paths when ApiClient is unavailable.

    function hasApiClientUrl() {
        return !!(window.ApiClient && typeof window.ApiClient.getUrl === 'function');
    }

    // ── Item ID ───────────────────────────────────────────────────────────────
    // Jellyfin 10.11 React Router puts params in location.search for BrowserRouter
    // or in location.hash for HashRouter. Old Emby framework used #!/path?id=.
    // UUIDs may appear with or without hyphens.

    function normalizeId(raw) {
        if (!raw) return null;
        const clean = raw.replace(/-/g, '');
        return /^[0-9a-fA-F]{32}$/.test(clean) ? clean : null;
    }

    function getItemId() {
        // 1. location.search  (React BrowserRouter or plain query string)
        let m = new URLSearchParams(window.location.search).get('id');
        const id1 = normalizeId(m);
        if (id1) return id1;

        // 2. location.hash  (React HashRouter: #/details?id=... or #!/path?id=...)
        const hash = window.location.hash;
        if (hash.includes('?')) {
            const hashSearch = hash.slice(hash.indexOf('?'));
            m = new URLSearchParams(hashSearch).get('id');
            const id2 = normalizeId(m);
            if (id2) return id2;
        }

        // 3. Regex fallback on full href (catches any remaining format)
        const hrefMatch = window.location.href.match(/[?&#/]id[=:]([0-9a-fA-F-]{32,36})/);
        return normalizeId(hrefMatch?.[1]);
    }

    // ── API call ──────────────────────────────────────────────────────────────

    async function getItemDurationMinutes(itemId) {
        try {
            const token = getAuthToken();
            const headers = token ? { 'Authorization': `MediaBrowser Token="${token}"` } : {};
            const url = hasApiClientUrl()
                ? window.ApiClient.getUrl('Items/' + itemId, { Fields: 'RunTimeTicks' })
                : `/Items/${itemId}?Fields=RunTimeTicks`;
            const res = await fetch(url, { headers });
            if (!res.ok) return null;
            const data = await res.json();
            const ticks = data.RunTimeTicks ?? data.runTimeTicks;
            if (!ticks) return null;
            return Math.ceil(ticks / 10_000_000 / 60); // ticks → seconds → minutes
        } catch (_) { return null; }
    }

    async function requestShareLink(itemId, minutes) {
        const token = getAuthToken();
        const headers = { 'Content-Type': 'application/json' };
        if (token) headers['Authorization'] = `MediaBrowser Token="${token}"`;

        const url = hasApiClientUrl()
            ? window.ApiClient.getUrl('share/create')
            : '/share/create';

        const res = await fetch(url, {
            method: 'POST',
            headers,
            body: JSON.stringify({ itemId, expirationMinutes: minutes })
        });

        if (!res.ok) {
            const text = await res.text().catch(() => res.statusText);
            throw new Error(`Server returned ${res.status}: ${text}`);
        }

        const data = await res.json();

        // Normalise: server may return camelCase or PascalCase depending on config
        return {
            shareUrl: data.shareUrl ?? data.ShareUrl,
            expiresAt: data.expiresAt ?? data.ExpiresAt,
            token:     data.token    ?? data.Token,
        };
    }

    // ── Toast (inline replacement for alert) ─────────────────────────────────

    function showToast(message) {
        const toast = document.createElement('div');
        toast.className = 'js-toast';
        toast.setAttribute('role', 'alert');
        toast.style.cssText =
            'position:fixed;bottom:24px;left:50%;transform:translateX(-50%);z-index:10001;' +
            'background:#1c1c1c;color:#fff;padding:12px 36px 12px 16px;border-radius:6px;' +
            'box-shadow:0 8px 32px rgba(0,0,0,.5);font-family:sans-serif;font-size:14px;' +
            'max-width:min(480px,90vw);box-sizing:border-box';

        const text = document.createElement('span');
        text.textContent = message;
        toast.appendChild(text);

        const close = document.createElement('button');
        close.type = 'button';
        close.setAttribute('aria-label', 'Dismiss');
        close.textContent = '×';
        close.style.cssText =
            'position:absolute;top:4px;right:6px;background:none;border:none;color:#aaa;' +
            'font-size:18px;line-height:1;cursor:pointer;padding:4px';
        toast.appendChild(close);

        const dismiss = () => toast.remove();
        close.onclick = dismiss;
        document.body.appendChild(toast);
        setTimeout(dismiss, 5000);
    }

    // ── Expiry dialog ─────────────────────────────────────────────────────────

    function askExpiry(defaultMinutes = 60) {
        return new Promise(resolve => {
            document.getElementById('js-expiry-dialog')?.remove();

            const overlay = document.createElement('div');
            overlay.id = 'js-expiry-dialog';
            overlay.style.cssText =
                'position:fixed;inset:0;background:rgba(0,0,0,.7);z-index:10000;' +
                'display:flex;align-items:center;justify-content:center;font-family:sans-serif';

            overlay.innerHTML = `
<div style="background:#1c1c1c;border-radius:8px;padding:24px;width:min(340px,90vw);color:#fff;box-shadow:0 8px 32px rgba(0,0,0,.5)">
  <h3 style="margin:0 0 16px;font-size:17px">Create Share Link</h3>
  <label style="display:block;font-size:13px;color:#aaa;margin-bottom:6px">Link expires after (minutes)</label>
  <input id="js-expiry-input" type="number" min="1" max="10080" value="${defaultMinutes}"
    style="width:100%;box-sizing:border-box;padding:8px 10px;background:#2a2a2a;border:1px solid #555;
           border-radius:4px;color:#fff;font-size:14px;margin-bottom:16px">
  <div style="display:flex;gap:8px">
    <button id="js-expiry-cancel"
      style="flex:1;padding:9px;background:#333;border:none;border-radius:4px;color:#fff;cursor:pointer;font-size:13px">
      Cancel
    </button>
    <button id="js-expiry-ok"
      style="flex:1;padding:9px;background:#00a4dc;border:none;border-radius:4px;color:#fff;cursor:pointer;font-size:13px;font-weight:600">
      Create
    </button>
  </div>
</div>`;

            document.body.appendChild(overlay);

            const input = overlay.querySelector('#js-expiry-input');
            input.focus();
            input.select();

            const submit = () => {
                const val = parseInt(input.value, 10);
                overlay.remove();
                resolve(isNaN(val) || val < 1 ? null : val);
            };

            overlay.querySelector('#js-expiry-ok').onclick = submit;
            overlay.querySelector('#js-expiry-cancel').onclick = () => { overlay.remove(); resolve(null); };
            overlay.onclick = e => { if (e.target === overlay) { overlay.remove(); resolve(null); } };
            input.addEventListener('keydown', e => { if (e.key === 'Enter') submit(); });
        });
    }

    // ── Result modal ──────────────────────────────────────────────────────────

    function showResultModal(shareUrl, expiresAt) {
        document.getElementById('js-share-result')?.remove();

        if (!shareUrl) {
            showToast('JellyShare: received empty share URL from server.');
            return;
        }

        const expiryStr = expiresAt ? new Date(expiresAt).toLocaleString() : 'unknown';

        const overlay = document.createElement('div');
        overlay.id = 'js-share-result';
        overlay.style.cssText =
            'position:fixed;inset:0;background:rgba(0,0,0,.7);z-index:10000;' +
            'display:flex;align-items:center;justify-content:center;font-family:sans-serif';

        // Static markup only; dynamic values are set via DOM properties below.
        overlay.innerHTML = `
<div style="background:#1c1c1c;border-radius:8px;padding:24px;width:min(480px,90vw);color:#fff;box-shadow:0 8px 32px rgba(0,0,0,.5)">
  <h3 style="margin:0 0 6px;font-size:17px">Share Link Ready</h3>
  <p style="margin:0 0 16px;color:#888;font-size:12px">Expires&nbsp;<span id="js-expiry-text"></span></p>
  <div style="display:flex;gap:8px;margin-bottom:12px">
    <button id="js-copy-btn"
      style="padding:8px 14px;background:#00a4dc;border:none;border-radius:4px;
             color:#fff;cursor:pointer;white-space:nowrap;font-size:13px;font-weight:600">
      Copy
    </button>
  </div>
  <button id="js-close-btn"
    style="width:100%;padding:9px;background:#333;border:none;border-radius:4px;
           color:#fff;cursor:pointer;font-size:13px">
    Close
  </button>
</div>`;

        const copyBtn = overlay.querySelector('#js-copy-btn');

        // Expiry is plain text: set via textContent, never interpolated.
        overlay.querySelector('#js-expiry-text').textContent = expiryStr;

        // URL input is created with the DOM API and its value set as a property.
        const urlInput = document.createElement('input');
        urlInput.id = 'js-share-url-input';
        urlInput.readOnly = true;
        urlInput.value = shareUrl;
        urlInput.style.cssText =
            'flex:1;min-width:0;padding:8px 10px;background:#2a2a2a;border:1px solid #555;' +
            'border-radius:4px;color:#fff;font-size:13px';
        copyBtn.parentNode.insertBefore(urlInput, copyBtn);

        document.body.appendChild(overlay);

        urlInput.focus();
        urlInput.select();

        copyBtn.onclick = async () => {
            try {
                await navigator.clipboard.writeText(shareUrl);
            } catch (_) {
                urlInput.select();
                document.execCommand('copy');
            }
            const btn = overlay.querySelector('#js-copy-btn');
            if (btn) { btn.textContent = '✓ Copied'; setTimeout(() => { if (btn) btn.textContent = 'Copy'; }, 2000); }
        };

        overlay.querySelector('#js-close-btn').onclick = () => overlay.remove();
        overlay.onclick = e => { if (e.target === overlay) overlay.remove(); };
    }

    // ── Shared share action ───────────────────────────────────────────────────

    // btn (optional): the button that triggered the share; disabled while the
    // duration is being fetched so repeated clicks don't stack requests.
    async function doShare(itemId, btn) {
        let durationMinutes = null;
        if (btn) btn.disabled = true;
        try {
            durationMinutes = await getItemDurationMinutes(itemId);
        } finally {
            if (btn) btn.disabled = false;
        }
        const defaultMinutes = durationMinutes ? Math.max(1, durationMinutes * 4) : 60;
        const minutes = await askExpiry(defaultMinutes);
        if (!minutes) return;
        try {
            const data = await requestShareLink(itemId, minutes);
            showResultModal(data.shareUrl, data.expiresAt);
        } catch (err) {
            showToast('JellyShare error: ' + err.message);
        }
    }

    // ── Detail page Share button (movies, episodes — top action row) ──────────

    function injectDetailButton() {
        const itemId = getItemId();
        if (!itemId) return;

        const selectors = ['.mainDetailButtons', '.detailButtons', '.itemDetailButtons'];
        for (const sel of selectors) {
            document.querySelectorAll(sel).forEach(container => {
                if (container.querySelector(`[data-jellyshare-btn="${itemId}"]`)) return;
                container.querySelectorAll('[data-jellyshare-btn]').forEach(b => b.remove());

                const btn = document.createElement('button');
                btn.type = 'button';
                btn.setAttribute('is', 'emby-button');
                btn.setAttribute('data-jellyshare-btn', itemId);
                btn.className = 'button-flat detailButton';
                btn.title = 'Share';
                btn.innerHTML = `
<div class="detailButton-content">
  <span class="material-icons detailButton-icon share" aria-hidden="true"></span>
</div>`;
                btn.onclick = () => doShare(itemId, btn);
                container.appendChild(btn);
            });
        }
    }

    // ── Context menu ("⋮") injection ─────────────────────────────────────────
    // Strategy:
    //   1. Capture clicks anywhere → walk up DOM to find nearest [data-id] → store pending ID
    //   2. Watch for .actionSheetScroller appearing in DOM → inject Share item

    let _pendingId = null;
    let _pendingTimer = null;
    let _pendingSetAt = 0;
    const PENDING_TTL_MS = 5000;

    // Each set restarts the expiry timer, so a stale click ID never lingers.
    function setPendingId(id) {
        _pendingId = id;
        _pendingSetAt = Date.now();
        clearTimeout(_pendingTimer);
        _pendingTimer = setTimeout(clearPendingId, PENDING_TTL_MS);
    }

    function clearPendingId() {
        _pendingId = null;
        _pendingSetAt = 0;
        clearTimeout(_pendingTimer);
        _pendingTimer = null;
    }

    function onListItemButtonClick(e) {
        // Walk up from click target to find any element with data-id / data-itemid
        let el = e.target;
        while (el && el !== document.body) {
            const raw = el.getAttribute('data-id') || el.getAttribute('data-itemid');
            const id = normalizeId(raw);
            if (id) { setPendingId(id); return; }
            el = el.parentElement;
        }
    }

    function injectShareIntoSheet(scroller) {
        // Only use the pending ID if it was set recently enough to be this sheet's item
        const fresh = _pendingId && (Date.now() - _pendingSetAt) <= PENDING_TTL_MS;
        const itemId = fresh ? _pendingId : null;
        clearPendingId();
        if (!itemId) return;

        if (scroller.querySelector('[data-jellyshare-menu]')) return;

        const sheet = scroller.closest('.actionSheet') ?? scroller.parentElement;

        const btn = document.createElement('button');
        btn.setAttribute('is', 'emby-button');
        btn.type = 'button';
        btn.className = 'listItem listItem-button actionSheetMenuItem';
        btn.setAttribute('data-jellyshare-menu', '1');
        btn.innerHTML = `
<span class="actionsheetMenuItemIcon listItemIcon listItemIcon-transparent material-icons share" aria-hidden="true"></span>
<div class="listItemBody actionsheetListItemBody">
  <div class="listItemBodyText actionSheetItemText">Share</div>
</div>`;

        btn.addEventListener('click', async e => {
            e.stopPropagation();
            sheet?.querySelector('.btnCloseActionSheet')?.click();
            await new Promise(r => setTimeout(r, 150));
            doShare(itemId);
        });

        scroller.appendChild(btn);
    }

    function tryInject() {
        injectDetailButton();
    }

    // Coalesce bursts of DOM mutations into at most one tryInject per frame.
    let _injectScheduled = false;
    function scheduleInject() {
        if (_injectScheduled) return;
        _injectScheduled = true;
        requestAnimationFrame(() => {
            _injectScheduled = false;
            tryInject();
        });
    }

    // ── Bootstrap ─────────────────────────────────────────────────────────────

    // React Router (BrowserRouter) fires popstate; old Emby fires hashchange
    window.addEventListener('hashchange', () => setTimeout(tryInject, 600));
    window.addEventListener('popstate',   () => setTimeout(tryInject, 600));

    new MutationObserver(scheduleInject)
        .observe(document.documentElement, { childList: true, subtree: true });

    // Capture-phase click listener: record which track row triggered the "⋮" menu
    document.addEventListener('click', onListItemButtonClick, true);

    // Watch for .actionSheetScroller appearing anywhere in the DOM
    new MutationObserver(mutations => {
        // No pending item means injectShareIntoSheet would do nothing; skip the DOM scan.
        if (!_pendingId) return;
        for (const m of mutations) {
            for (const node of m.addedNodes) {
                if (node.nodeType !== 1) continue; // text/comment nodes can't hold a scroller
                if (node.classList?.contains('actionSheetScroller')) {
                    injectShareIntoSheet(node);
                } else {
                    const scroller = node.querySelector?.('.actionSheetScroller');
                    if (scroller) injectShareIntoSheet(scroller);
                }
            }
        }
    }).observe(document.body, { childList: true, subtree: true });

    setTimeout(tryInject, 800);
})();
