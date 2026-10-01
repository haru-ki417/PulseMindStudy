namespace PulseMind.Web.Services;

/// <summary>ブラウザ側の安全対策を有効にする応答ヘッダー</summary>
public static class SecurityHeaders
{
    /// <summary>
    /// 読み込めるものを自分のサイトに限る（外部のスクリプトを差し込まれても動かないようにする）。
    /// 画面の部品で style 属性を使っているため、スタイルだけはインラインを許可する。
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; " +
        "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            // 応答を送り始める直前に付ける（途中の処理が同じヘッダーを付けても、ここで上書きされる）
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=(), serial=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                return Task.CompletedTask;
            });
            await next();
        });
}
