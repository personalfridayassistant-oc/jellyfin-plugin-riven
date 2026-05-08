(function () {
    'use strict';

    const supportedTypes = new Set(['Movie', 'Episode', 'Season', 'Series']);
    const logPrefix = '[Riven Plugin]';

    function log(message, value) {
        if (window.console && console.debug) {
            console.debug(logPrefix + ' ' + message, value || '');
        }
    }

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
                data: JSON.stringify(body)
            }).then(function (payload) {
                if (payload.Success === false || payload.success === false) {
                    throw new Error(payload.Message || payload.message || 'Riven action failed.');
                }

                return payload;
            });
        }

        return fetch(path, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        }).then(function (response) {
            return response.json().then(function (payload) {
                if (!response.ok || payload.Success === false || payload.success === false) {
                    throw new Error(payload.Message || payload.message || response.statusText);
                }

                return payload;
            });
        });
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

    function makeButton(label, title, onClick) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'raised emby-button rivenActionButton';
        button.title = title;
        button.style.marginInlineEnd = '.5em';
        button.style.marginBottom = '.5em';
        button.style.display = 'inline-flex';
        button.style.alignItems = 'center';
        button.style.gap = '.35em';
        button.style.padding = '.55em .9em';
        button.innerHTML = '<span aria-hidden="true">R</span><span>' + label + '</span>';
        button.addEventListener('click', onClick);
        return button;
    }

    function addActions(item) {
        if (!item || !supportedTypes.has(item.Type) || document.querySelector('.rivenActionBar')) {
            log('Skipping action render', item);
            return;
        }

        const host = findActionHost();
        if (!host) {
            return;
        }

        const itemId = item.Id;
        const bar = document.createElement('div');
        bar.className = 'rivenActionBar';
        bar.setAttribute('data-riven-item-id', itemId);
        bar.style.display = 'inline-flex';
        bar.style.gap = '.35em';
        bar.style.alignItems = 'center';
        bar.style.marginTop = '.5em';
        bar.style.flexWrap = 'wrap';
        bar.style.width = '100%';

        bar.appendChild(makeButton('Retry Riven', 'Retry scrape in Riven', function () {
            apiFetch('/Riven/Retry', { itemId: itemId }).then(function (payload) {
                notify(payload.Message || 'Riven retry started.');
            }).catch(function (error) { notify(error.message); });
        }));

        if (item.Type === 'Movie' || item.Type === 'Episode') {
            bar.appendChild(makeButton('Delete + Retry', 'Delete from Riven and retry scrape', function () {
                if (!window.confirm('Delete this item in Riven and immediately retry the scrape?')) {
                    return;
                }

                apiFetch('/Riven/DeleteAndRetry', { itemId: itemId }).then(function (payload) {
                    notify(payload.Message || 'Riven delete and retry started.');
                }).catch(function (error) { notify(error.message); });
            }));
        }

        if (item.Type === 'Movie') {
            bar.appendChild(makeButton('Submit Magnet', 'Submit a magnet link to Riven', function () {
                const magnet = window.prompt('Paste the magnet link to attach to this movie:');
                if (!magnet) {
                    return;
                }

                apiFetch('/Riven/SubmitMagnet', { itemId: itemId, magnet: magnet }).then(function (payload) {
                    notify(payload.Message || 'Riven manual magnet session completed.');
                }).catch(function (error) { notify(error.message); });
            }));
        }

        if (host === document.body) {
            bar.style.position = 'fixed';
            bar.style.right = '1.5rem';
            bar.style.bottom = '1.5rem';
            bar.style.zIndex = '1300';
            bar.style.width = 'auto';
        }

        host.appendChild(bar);
        log('Rendered actions for item', item);
    }

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
