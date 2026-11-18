public static class RateLimitMiddleware
{
    public static void UseRateLimiter(
        this WebApplication app,
        RedisTokenBucket tokenBucket,
        RedisSlidingWindow slidingWindow)
    {
        var modoTeste = app.Environment.IsDevelopment();

        app.Use(async (context, next) =>
        {
            var chave = context.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

            var nome = context.Request.Query["cliente"].ToString();
            if (modoTeste && !string.IsNullOrEmpty(nome))
                chave = nome;

            var usarSliding = modoTeste && context.Request.Query["algo"] == "sliding";

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
                await context.Response.WriteAsync("Muitas requisições. Tente em " + resultado.EsperarSegundos + "s.\n");
                return;
            }

            await next(context);
        });
    }
}