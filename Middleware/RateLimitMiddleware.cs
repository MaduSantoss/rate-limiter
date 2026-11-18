public static class RateLimitMiddleware
{
    public static void UseRateLimiter(
        this WebApplication app,
        RedisTokenBucket tokenBucket,
        RedisSlidingWindow slidingWindow)
    {
        app.Use(async (context, next) =>
        {
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
            var nome = context.Request.Query["cliente"].ToString();
            var chave = string.IsNullOrEmpty(nome) ? ip : nome;

            var usarSliding = context.Request.Query["algo"] == "sliding";

            var resultado = usarSliding
                ? await slidingWindow.PermitirAsync(chave)
                : await tokenBucket.PermitirAsync(chave);

            context.Response.Headers["X-RateLimit-Limit"] = resultado.Limite.ToString();
            context.Response.Headers["X-RateLimit-Remaining"] = resultado.Restantes.ToString();

            if (!resultado.Permitido)
            {
                context.Response.Headers["Retry-After"] = resultado.EsperarSegundos.ToString();
                context.Response.StatusCode = 429;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync($"[{chave}] Muitas requisições. Tente em {resultado.EsperarSegundos}s.\n");
                return;
            }

            await next(context);
        });
    }
}