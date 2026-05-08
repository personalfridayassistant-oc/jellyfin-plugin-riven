(function () {
    'use strict';

    const supportedTypes = new Set(['Movie', 'Episode', 'Season', 'Series']);

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
        button.className = 'detailButton emby-button rivenActionButton';
        button.title = title;
        button.style.marginInlineEnd = '.5em';
        button.innerHTML = '<span class="material-icons" aria-hidden="true">cloud_sync</span><span class="button-text">' + label + '</span>';
        button.addEventListener('click', onClick);
        return button;
    }

    function addActions(item) {
        if (!item || !supportedTypes.has(item.Type) || document.querySelector('.rivenActionBar')) {
            return;
        }

        const host = findActionHost();
        if (!host) {
            return;
        }

        const itemId = item.Id;
        const bar = document.createElement('div');
        bar.className = 'rivenActionBar';
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
    }

    function refresh() {
        const itemId = getItemId();
        if (!itemId || !window.ApiClient || !ApiClient.getItem) {
            return;
        }

        const itemPromise = ApiClient.getCurrentUserId
            ? ApiClient.getCurrentUserId().then(function (userId) { return ApiClient.getItem(userId, itemId); })
            : ApiClient.ajax({ type: 'GET', url: ApiClient.getUrl('Items/' + itemId) });

        itemPromise.then(addActions).catch(function () { });
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
    setTimeout(refresh, 1000);
}());
