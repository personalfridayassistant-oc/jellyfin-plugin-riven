(function () {
    'use strict';

    const supportedTypes = new Set(['Movie', 'Episode', 'Season', 'Series']);
    const logPrefix = '[Riven Plugin]';

    function log(message, value) {
        if (window.console && console.debug) {
            console.debug(logPrefix + ' ' + message, value || '');
        }
    }

    function injectStyles() {
        if (document.getElementById('rivenActionStyles')) {
            return;
        }

        const style = document.createElement('style');
        style.id = 'rivenActionStyles';
        style.textContent = `
            .rivenActionMenu { position: relative; display: inline-flex; margin-inline-end: .5em; }
            .rivenMenuButton { width: 3.2em; height: 3.2em; min-width: 3.2em; border-radius: 50%; padding: 0; font-weight: 700; }
            .rivenMenuPanel { position: fixed; z-index: 1300; width: min(18rem, calc(100vw - 1rem)); max-height: calc(100vh - 1rem); overflow: auto; padding: .35rem 0; border-radius: .25rem; background: var(--theme-primary-color, #202020); box-shadow: 0 0 .9rem rgba(0,0,0,.45); display: none; }
            .rivenActionMenu-open .rivenMenuPanel { display: block; }
            .rivenMenuItem { width: 100%; display: flex; align-items: center; gap: .8rem; padding: .8rem 1rem; border: 0; background: transparent; color: inherit; text-align: left; cursor: pointer; font: inherit; }
            .rivenMenuItem:hover, .rivenMenuItem:focus { background: rgba(255,255,255,.08); outline: 0; }
            .rivenMenuIcon { width: 1.4rem; height: 1.4rem; flex: 0 0 auto; fill: currentColor; opacity: .9; }
            .rivenStreamPickerOverlay { position: fixed; inset: 0; z-index: 1400; background: rgba(0,0,0,.65); display: flex; align-items: center; justify-content: center; padding: 1rem; }
            .rivenStreamPicker { width: min(52rem, 100%); max-height: min(42rem, 90vh); overflow: auto; border-radius: .35rem; background: var(--theme-primary-color, #202020); box-shadow: 0 0 1.5rem rgba(0,0,0,.55); }
            .rivenStreamPickerHeader { display: flex; align-items: center; justify-content: space-between; padding: 1rem 1.25rem; border-bottom: 1px solid rgba(255,255,255,.12); }
            .rivenStreamPickerTitle { font-size: 1.25rem; font-weight: 600; }
            .rivenStreamPickerBody { padding: .5rem 0; }
            .rivenStreamOption { width: 100%; display: block; border: 0; background: transparent; color: inherit; text-align: left; padding: .9rem 1.25rem; cursor: pointer; }
            .rivenStreamOption:hover, .rivenStreamOption:focus { background: rgba(255,255,255,.08); outline: 0; }
            .rivenStreamName { display: block; font-weight: 600; overflow-wrap: anywhere; }
            .rivenStreamMeta { display: block; opacity: .75; font-size: .9em; margin-top: .25rem; }
        `;
        document.head.appendChild(style);
    }

    function icon(path) {
        return '<svg class="rivenMenuIcon" viewBox="0 0 24 24" aria-hidden="true"><path d="' + path + '"></path></svg>';
    }

    const icons = {
        retry: 'M12 6V3L8 7l4 4V8c2.76 0 5 2.24 5 5 0 .85-.22 1.65-.6 2.35l1.46 1.46C18.58 15.72 19 14.41 19 13c0-3.86-3.14-7-7-7zm-5.86.19C5.42 7.28 5 8.59 5 10c0 3.86 3.14 7 7 7v3l4-4-4-4v3c-2.76 0-5-2.24-5-5 0-.85.22-1.65.6-2.35L6.14 6.19z',
        remove: 'M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM8 9h8v10H8V9zm7.5-5-1-1h-5l-1 1H5v2h14V4z',
        magnet: 'M3 9v6c0 3.31 2.69 6 6 6s6-2.69 6-6V9h-4v6c0 1.1-.9 2-2 2s-2-.9-2-2V9H3zm0-6h4v4H3V3zm8 0h4v4h-4V3z',
        refresh: 'M17.65 6.35C16.2 4.9 14.21 4 12 4c-4.42 0-7.99 3.58-7.99 8s3.57 8 7.99 8c3.73 0 6.84-2.55 7.73-6h-2.08c-.82 2.33-3.04 4-5.65 4-3.31 0-6-2.69-6-6s2.69-6 6-6c1.66 0 3.14.69 4.22 1.78L13 11h7V4l-2.35 2.35z'
    };

    function getItemId() {
        const hash = window.location.hash || '';
        const query = hash.includes('?') ? hash.slice(hash.indexOf('?')) : window.location.search;
        const params = new URLSearchParams(query);
        const id = params.get('id');
        if (id) {
            return id;
        }

        const match = hash.match(/details\/([a-f0-9-]+)/i) || window.location.pathname.match(/details\/([a-f0-9-]+)/i);
        return match ? match[1] : null;
    }

    function apiFetch(path, body) {
        if (window.ApiClient && ApiClient.ajax && ApiClient.getUrl) {
            return ApiClient.ajax({
                type: 'POST',
                url: ApiClient.getUrl(path.replace(/^\//, '')),
                contentType: 'application/json',
                data: JSON.stringify(body || {})
            }).then(checkPayload);
        }

        return fetch(path, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body || {})
        }).then(function (response) {
            return response.json().then(function (payload) {
                if (!response.ok) {
                    throw new Error(payload.Message || payload.message || response.statusText);
                }

                return checkPayload(payload);
            });
        });
    }

    function checkPayload(payload) {
        if (payload && (payload.Success === false || payload.success === false)) {
            throw new Error(payload.Message || payload.message || 'Riven action failed.');
        }

        return payload;
    }

    function notify(message) {
        if (window.Dashboard && Dashboard.alert) {
            Dashboard.alert(message);
            return;
        }

        window.alert(message);
    }

    function findActionHost() {
        return document.querySelector('.detailPagePrimaryContainer .mainDetailButtons')
            || document.querySelector('.itemDetailPage .mainDetailButtons')
            || document.querySelector('.detailButtons')
            || document.querySelector('.detailPagePrimaryContainer')
            || document.querySelector('.itemDetailPage .detailPageContent')
            || document.querySelector('.itemDetailPage')
            || document.body;
    }

    function promptRetryOptions() {
        const qualityOverride = window.prompt('Quality override (optional, for example 1080p, 2160p, remux):', '') || '';
        const profileOverride = window.prompt('Profile override (optional, profile name/id if your Riven API supports it):', '') || '';
        return {
            qualityOverride: qualityOverride.trim() || null,
            profileOverride: profileOverride.trim() || null
        };
    }

    function formatBytes(value) {
        const size = Number(value || 0);
        if (!size) {
            return 'Unknown size';
        }

        const units = ['B', 'KB', 'MB', 'GB', 'TB'];
        let current = size;
        let index = 0;
        while (current >= 1024 && index < units.length - 1) {
            current /= 1024;
            index++;
        }

        return current.toFixed(index === 0 ? 0 : 1) + ' ' + units[index];
    }

    function getFiles(payload) {
        const containers = payload.containers || payload.Containers || {};
        return containers.files || containers.Files || payload.files || payload.Files || payload.selected_files || payload.SelectedFiles || [];
    }

    function getSessionId(payload) {
        return payload.sessionId || payload.SessionId || payload.session_id;
    }

    function fileValue(file, camel, pascal, snake) {
        return file[camel] !== undefined ? file[camel] : (file[pascal] !== undefined ? file[pascal] : file[snake]);
    }

    function showStreamPicker(itemId, payload) {
        const sessionId = getSessionId(payload);
        const files = getFiles(payload);
        if (!sessionId) {
            notify('Riven did not return a manual session.');
            return;
        }

        if (!files.length) {
            apiFetch('/Riven/CompleteSession', { sessionId: sessionId }).then(function (result) {
                notify(result.Message || result.message || 'Riven manual session completed.');
            }).catch(function (error) { notify(error.message); });
            return;
        }

        const overlay = document.createElement('div');
        overlay.className = 'rivenStreamPickerOverlay';
        overlay.innerHTML = '<div class="rivenStreamPicker" role="dialog" aria-modal="true"><div class="rivenStreamPickerHeader"><span class="rivenStreamPickerTitle">Choose Riven Stream</span><button type="button" is="emby-button" class="emby-button rivenClosePicker">Close</button></div><div class="rivenStreamPickerBody"></div></div>';
        const body = overlay.querySelector('.rivenStreamPickerBody');
        overlay.querySelector('.rivenClosePicker').addEventListener('click', function () { overlay.remove(); });

        files.forEach(function (file) {
            const fileId = fileValue(file, 'fileId', 'FileId', 'file_id');
            const filename = fileValue(file, 'filename', 'Filename', 'filename') || 'Unnamed file';
            const filesize = fileValue(file, 'filesize', 'Filesize', 'filesize') || 0;
            const quality = fileValue(file, 'quality', 'Quality', 'quality');
            const resolution = fileValue(file, 'resolution', 'Resolution', 'resolution');
            const provider = fileValue(file, 'provider', 'Provider', 'provider');
            const seeders = fileValue(file, 'seeders', 'Seeders', 'seeders');
            const meta = [quality, resolution, provider, formatBytes(filesize), seeders ? seeders + ' seeders' : null].filter(Boolean).join(' - ');
            const option = document.createElement('button');
            option.type = 'button';
            option.className = 'rivenStreamOption';
            option.innerHTML = '<span class="rivenStreamName"></span><span class="rivenStreamMeta"></span>';
            option.querySelector('.rivenStreamName').textContent = filename;
            option.querySelector('.rivenStreamMeta').textContent = meta;
            option.addEventListener('click', function () {
                apiFetch('/Riven/SelectStream', {
                    itemId: itemId,
                    sessionId: sessionId,
                    fileId: String(fileId),
                    filename: filename,
                    filesize: Number(filesize || 0)
                }).then(function (result) {
                    overlay.remove();
                    notify(result.Message || result.message || 'Riven stream selected.');
                }).catch(function (error) { notify(error.message); });
            });
            body.appendChild(option);
        });

        document.body.appendChild(overlay);
    }

    function positionMenu(menu) {
        const trigger = menu.querySelector('.rivenMenuButton');
        const panel = menu.querySelector('.rivenMenuPanel');
        if (!trigger || !panel) {
            return;
        }

        panel.style.visibility = 'hidden';
        panel.style.display = 'block';
        const triggerRect = trigger.getBoundingClientRect();
        const panelRect = panel.getBoundingClientRect();
        const margin = 8;
        const left = Math.min(Math.max(triggerRect.left, margin), window.innerWidth - panelRect.width - margin);
        let top = triggerRect.bottom + margin;
        if (top + panelRect.height > window.innerHeight - margin) {
            top = Math.max(margin, triggerRect.top - panelRect.height - margin);
        }

        panel.style.left = left + 'px';
        panel.style.top = top + 'px';
        panel.style.maxHeight = Math.max(160, window.innerHeight - top - margin) + 'px';
        panel.style.visibility = '';
        panel.style.display = '';
    }

    function addMenuItem(menu, label, title, iconPath, onClick) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'rivenMenuItem';
        button.title = title;
        button.innerHTML = icon(iconPath) + '<span></span>';
        button.querySelector('span').textContent = label;
        button.addEventListener('click', function () {
            menu.classList.remove('rivenActionMenu-open');
            onClick();
        });
        menu.querySelector('.rivenMenuPanel').appendChild(button);
    }

    function addActions(item) {
        if (!item || !supportedTypes.has(item.Type)) {
            log('Skipping action render', item);
            return;
        }

        const existing = document.querySelector('.rivenActionMenu');
        if (existing && existing.getAttribute('data-riven-item-id') === item.Id) {
            return;
        }

        if (existing) {
            existing.remove();
        }

        const host = findActionHost();
        if (!host) {
            return;
        }

        injectStyles();
        const itemId = item.Id;
        const menu = document.createElement('div');
        menu.className = 'rivenActionMenu';
        menu.setAttribute('data-riven-item-id', itemId);
        menu.innerHTML = '<button type="button" is="emby-button" class="emby-button paper-icon-button-light rivenMenuButton" title="Riven actions" aria-label="Riven actions">R</button><div class="rivenMenuPanel" role="menu"></div>';
        const trigger = menu.querySelector('.rivenMenuButton');
        trigger.addEventListener('click', function (event) {
            event.stopPropagation();
            menu.classList.toggle('rivenActionMenu-open');
            if (menu.classList.contains('rivenActionMenu-open')) {
                positionMenu(menu);
            }
        });

        addMenuItem(menu, 'Retry with override', 'Retry scrape in Riven', icons.retry, function () {
            const overrides = promptRetryOptions();
            apiFetch('/Riven/Retry', Object.assign({ itemId: itemId }, overrides)).then(function (payload) {
                notify(payload.Message || payload.message || 'Riven retry started.');
            }).catch(function (error) { notify(error.message); });
        });

        if (item.Type === 'Movie' || item.Type === 'Episode') {
            addMenuItem(menu, 'Delete + retry', 'Delete from Riven and retry scrape', icons.remove, function () {
                if (!window.confirm('Delete this item in Riven and immediately retry the scrape?')) {
                    return;
                }

                const overrides = promptRetryOptions();
                apiFetch('/Riven/DeleteAndRetry', Object.assign({ itemId: itemId }, overrides)).then(function (payload) {
                    notify(payload.Message || payload.message || 'Riven delete and retry started.');
                }).catch(function (error) { notify(error.message); });
            });
        }

        if (item.Type === 'Movie' || item.Type === 'Series') {
            addMenuItem(menu, item.Type === 'Series' ? 'Add series magnet' : 'Add movie magnet', 'Submit a magnet link and choose a Riven stream', icons.magnet, function () {
                const magnet = window.prompt(item.Type === 'Series' ? 'Paste the TV series magnet link:' : 'Paste the movie magnet link:');
                if (!magnet) {
                    return;
                }

                apiFetch(item.Type === 'Series' ? '/Riven/SubmitTvMagnet' : '/Riven/SubmitMagnet', { itemId: itemId, magnet: magnet }).then(function (payload) {
                    showStreamPicker(itemId, payload);
                }).catch(function (error) { notify(error.message); });
            });
        }

        addMenuItem(menu, 'Refresh Jellyfin library', 'Trigger a Jellyfin library refresh', icons.refresh, function () {
            apiFetch('/Riven/RefreshLibrary', {}).then(function (payload) {
                notify(payload.Message || payload.message || 'Jellyfin library refresh started.');
            }).catch(function (error) { notify(error.message); });
        });

        if (host === document.body) {
            menu.style.position = 'fixed';
            menu.style.right = '1.5rem';
            menu.style.bottom = '1.5rem';
            menu.style.zIndex = '1300';
        }

        host.appendChild(menu);
        log('Rendered Riven menu for item', item);
    }

    document.addEventListener('click', function () {
        document.querySelectorAll('.rivenActionMenu-open').forEach(function (menu) {
            menu.classList.remove('rivenActionMenu-open');
        });
    });

    function getCurrentUserId() {
        if (!window.ApiClient) {
            return Promise.resolve(null);
        }

        if (ApiClient.getCurrentUserId) {
            try {
                return Promise.resolve(ApiClient.getCurrentUserId());
            } catch (error) {
                log('getCurrentUserId failed', error);
            }
        }

        if (ApiClient._serverInfo && ApiClient._serverInfo.UserId) {
            return Promise.resolve(ApiClient._serverInfo.UserId);
        }

        try {
            const stored = JSON.parse(localStorage.getItem('jellyfin_credentials') || '{}');
            const server = stored.Servers && stored.Servers[0];
            return Promise.resolve(server && server.UserId ? server.UserId : null);
        } catch (error) {
            log('Stored user lookup failed', error);
            return Promise.resolve(null);
        }
    }

    function getItem(itemId) {
        return getCurrentUserId().then(function (userId) {
            if (userId && ApiClient.getItem) {
                return ApiClient.getItem(userId, itemId);
            }

            if (ApiClient.ajax && ApiClient.getUrl) {
                return ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('Items/' + itemId) });
            }

            throw new Error('Jellyfin ApiClient is unavailable.');
        });
    }

    function refresh() {
        const itemId = getItemId();
        if (!itemId || !window.ApiClient || !ApiClient.getItem) {
            log('No item id or ApiClient yet', { itemId: itemId, hasApiClient: !!window.ApiClient });
            return;
        }

        getItem(itemId).then(addActions).catch(function (error) { log('Failed to load item', error); });
    }

    let lastHref = '';
    setInterval(function () {
        if (window.location.href !== lastHref) {
            lastHref = window.location.href;
            setTimeout(refresh, 600);
        }
    }, 800);

    document.addEventListener('viewshow', function () { setTimeout(refresh, 600); });
    document.addEventListener('pageshow', function () { setTimeout(refresh, 600); });
    document.addEventListener('visibilitychange', function () { setTimeout(refresh, 600); });
    setTimeout(refresh, 1000);
    setTimeout(refresh, 2500);
}());
