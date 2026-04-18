(() => {
    "use strict";

    const POLL_INTERVAL_MS = 2000;

    const byBind = (name) => document.querySelector(`[data-bind="${name}"]`);
    const byRole = (name) => document.querySelectorAll(`[data-role="${name}"]`);

    let rid = 0;
    let refreshTimer = null;
    let torrents = new Map();

    async function api(path, options = {}) {
        const response = await fetch(path, { credentials: "same-origin", ...options });
        return response;
    }

    async function apiForm(path, fields) {
        const body = new URLSearchParams();
        for (const [k, v] of Object.entries(fields)) body.append(k, v);
        return api(path, {
            method: "POST",
            headers: { "Content-Type": "application/x-www-form-urlencoded" },
            body,
        });
    }

    function showSignedIn(signed) {
        byRole("login").forEach((el) => (el.hidden = signed));
        byRole("signed-in").forEach((el) => (el.hidden = !signed));
    }

    async function tryLogin(username, password) {
        const r = await apiForm("/api/v2/auth/login", { username, password });
        return r.status === 200;
    }

    async function logout() {
        await api("/api/v2/auth/logout", { method: "POST" });
        stopPolling();
        showSignedIn(false);
    }

    async function checkSession() {
        // A protected endpoint is the easiest session probe.
        const r = await api("/api/v2/torrents/info");
        return r.status === 200;
    }

    function formatBytes(n) {
        if (!n || n < 1024) return `${n || 0} B`;
        const units = ["KB", "MB", "GB", "TB"];
        let i = -1;
        let v = n;
        do { v /= 1024; i++; } while (v >= 1024 && i < units.length - 1);
        return `${v.toFixed(v >= 10 ? 0 : 1)} ${units[i]}`;
    }

    function formatSpeed(n) { return `${formatBytes(n)}/s`; }

    function formatProgress(p) {
        const pct = Math.max(0, Math.min(1, p));
        return `<div class="progress"><span style="transform: scaleX(${pct});"></span></div>`;
    }

    function renderTorrents() {
        const tbody = byBind("torrents");
        const empty = byBind("empty");
        tbody.innerHTML = "";
        const rows = [...torrents.values()].sort((a, b) => (a.name || "").localeCompare(b.name || ""));
        empty.hidden = rows.length !== 0;

        for (const t of rows) {
            const tr = document.createElement("tr");
            tr.innerHTML =
                `<td title="${escapeHtml(t.name)}">${escapeHtml(t.name || t.hash)}</td>` +
                `<td class="num">${formatProgress(t.progress)}${Math.round((t.progress || 0) * 100)}%</td>` +
                `<td class="num">${formatBytes(t.size || t.total_size || 0)}</td>` +
                `<td class="num">${formatSpeed(t.dlspeed || 0)}</td>` +
                `<td class="num">${formatSpeed(t.upspeed || 0)}</td>` +
                `<td><span class="state-pill ${escapeAttr((t.state || "").toLowerCase())}">${escapeHtml(t.state || "")}</span></td>` +
                `<td class="actions">` +
                `<button class="btn btn-subtle" data-hash="${escapeAttr(t.hash)}" data-op="pause">Pause</button>` +
                `<button class="btn btn-subtle" data-hash="${escapeAttr(t.hash)}" data-op="resume">Resume</button>` +
                `<button class="btn btn-subtle" data-hash="${escapeAttr(t.hash)}" data-op="delete">Delete</button>` +
                `</td>`;
            tbody.appendChild(tr);
        }
    }

    function escapeHtml(s) {
        return (s || "").replace(/[&<>"']/g, (c) =>
            ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
    }
    function escapeAttr(s) { return escapeHtml(s); }

    async function poll() {
        const r = await api(`/api/v2/sync/maindata?rid=${rid}`);
        if (r.status === 401) {
            stopPolling();
            showSignedIn(false);
            return;
        }
        const data = await r.json();
        rid = data.rid || 0;
        if (data.full_update) torrents.clear();

        if (data.torrents) {
            for (const [hash, row] of Object.entries(data.torrents)) {
                const prev = torrents.get(hash) || {};
                torrents.set(hash, { ...prev, ...row, hash });
            }
        }
        for (const hash of data.torrents_removed || []) torrents.delete(hash);

        renderTorrents();
        updateStatusLine(data.server_state);
    }

    function updateStatusLine(ss) {
        if (!ss) return;
        byBind("status-line").textContent =
            `↓ ${formatSpeed(ss.dl_info_speed || 0)}   ↑ ${formatSpeed(ss.up_info_speed || 0)}   DHT ${ss.dht_nodes || 0}`;
    }

    function startPolling() {
        if (refreshTimer) return;
        poll();
        refreshTimer = setInterval(poll, POLL_INTERVAL_MS);
    }

    function stopPolling() {
        if (refreshTimer) { clearInterval(refreshTimer); refreshTimer = null; }
        byBind("status-line").textContent = "";
    }

    function wire() {
        document.querySelector('[data-form="login"]').addEventListener("submit", async (e) => {
            e.preventDefault();
            const form = new FormData(e.target);
            const errEl = byBind("login-error");
            errEl.hidden = true;
            const ok = await tryLogin(form.get("username"), form.get("password"));
            if (ok) {
                showSignedIn(true);
                startPolling();
            } else {
                errEl.textContent = "Incorrect username or password.";
                errEl.hidden = false;
            }
        });

        document.querySelector('[data-action="logout"]').addEventListener("click", logout);

        document.querySelector('[data-action="add"]').addEventListener("click", () => {
            document.querySelector('[data-dialog="add"]').showModal();
        });

        document.querySelector('[data-action="cancel-add"]').addEventListener("click", (e) => {
            e.preventDefault();
            document.querySelector('[data-dialog="add"]').close();
        });

        document.querySelector('[data-form="add"]').addEventListener("submit", async (e) => {
            e.preventDefault();
            const form = new FormData(e.target);
            const body = new FormData();
            body.append("urls", form.get("urls"));
            if (form.get("savepath")) body.append("savepath", form.get("savepath"));
            if (form.get("paused")) body.append("paused", "true");
            await fetch("/api/v2/torrents/add", { method: "POST", body, credentials: "same-origin" });
            document.querySelector('[data-dialog="add"]').close();
            e.target.reset();
            poll();
        });

        document.querySelector('[data-action="toggle-alt"]').addEventListener("click", async () => {
            await api("/api/v2/transfer/toggleSpeedLimitsMode", { method: "POST" });
            poll();
        });

        byBind("torrents").addEventListener("click", async (e) => {
            const btn = e.target.closest("button[data-op]");
            if (!btn) return;
            const hash = btn.dataset.hash;
            const op = btn.dataset.op;
            if (op === "pause") await apiForm("/api/v2/torrents/pause", { hashes: hash });
            else if (op === "resume") await apiForm("/api/v2/torrents/resume", { hashes: hash });
            else if (op === "delete") {
                if (confirm("Delete torrent (content kept)?")) {
                    await apiForm("/api/v2/torrents/delete", { hashes: hash, deleteFiles: "false" });
                }
            }
            poll();
        });
    }

    async function boot() {
        wire();
        if (await checkSession()) {
            showSignedIn(true);
            startPolling();
        } else {
            showSignedIn(false);
        }
    }

    boot();
})();
