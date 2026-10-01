// 画面から呼ぶ小さな補助（Blazor の JS 連携で使う）
window.pulseMind = {
    // クリップボードにコピーする。成功したら true
    copy: async (text) => {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            return false;
        }
    },

    // 集中モード: スペースキーで開始・終了、Esc で閉じる（文字の入力中は反応しない）
    focusKeys: (dotnet) => {
        const handler = (e) => {
            if (e.target.closest && e.target.closest('input, textarea, select, button, a')) {
                if (e.key !== 'Escape') return;
            }
            if (e.code === 'Space') {
                e.preventDefault();
                dotnet.invokeMethodAsync('OnSpace');
            } else if (e.key === 'Escape') {
                dotnet.invokeMethodAsync('OnEscape');
            }
        };
        window.pulseMind._focusHandler = handler;
        document.addEventListener('keydown', handler);
    },
    focusKeysOff: () => {
        if (window.pulseMind._focusHandler) document.removeEventListener('keydown', window.pulseMind._focusHandler);
        window.pulseMind._focusHandler = null;
    },

    // 勉強中に画面が暗くならないようにする（対応していないブラウザでは何もしない）
    wakeLock: async (on) => {
        const self = window.pulseMind;
        self._wakeWanted = on;
        try {
            if (on && 'wakeLock' in navigator) {
                self._wake = await navigator.wakeLock.request('screen');
                if (!self._wakeListener) {
                    // 別のタブから戻ってきたときに取り直す
                    self._wakeListener = async () => {
                        if (self._wakeWanted && document.visibilityState === 'visible') {
                            try { self._wake = await navigator.wakeLock.request('screen'); } catch { }
                        }
                    };
                    document.addEventListener('visibilitychange', self._wakeListener);
                }
            } else if (self._wake) {
                await self._wake.release();
                self._wake = null;
            }
            return true;
        } catch {
            return false;
        }
    },

    toggleFullscreen: async () => {
        try {
            if (document.fullscreenElement) await document.exitFullscreen();
            else await document.documentElement.requestFullscreen();
        } catch { }
    },

    // ブラウザのタイムゾーン（初回設定で使う）
    timeZone: () => {
        try { return Intl.DateTimeFormat().resolvedOptions().timeZone || ''; } catch { return ''; }
    },

    // 振り返りカード（SVG）を PNG 画像にして、共有またはダウンロードする
    saveSvgAsPng: async (elementId, fileName, width, height) => {
        const svg = document.getElementById(elementId);
        if (!svg) return 'error';
        const xml = new XMLSerializer().serializeToString(svg);
        const url = URL.createObjectURL(new Blob([xml], { type: 'image/svg+xml;charset=utf-8' }));
        try {
            const image = new Image();
            await new Promise((resolve, reject) => { image.onload = resolve; image.onerror = reject; image.src = url; });
            const canvas = document.createElement('canvas');
            canvas.width = width;
            canvas.height = height;
            canvas.getContext('2d').drawImage(image, 0, 0, width, height);
            const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
            const file = new File([blob], fileName, { type: 'image/png' });
            if (navigator.canShare && navigator.canShare({ files: [file] }) && /iPhone|iPad|Android/i.test(navigator.userAgent)) {
                try { await navigator.share({ files: [file] }); return 'shared'; } catch { return 'cancelled'; }
            }
            const link = document.createElement('a');
            link.href = URL.createObjectURL(blob);
            link.download = fileName;
            document.body.appendChild(link);
            link.click();
            link.remove();
            setTimeout(() => URL.revokeObjectURL(link.href), 10000);
            return 'downloaded';
        } catch {
            return 'error';
        } finally {
            URL.revokeObjectURL(url);
        }
    },
};
