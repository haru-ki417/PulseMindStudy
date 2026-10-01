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
};
