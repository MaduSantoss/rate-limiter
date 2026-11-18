using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var redis = ConnectionMultiplexer.Connect("localhost:6379");
var db = redis.GetDatabase();

var limiter = new RedisTokenBucket(db, 5, 1);
var sliding = new RedisSlidingWindow(db, 5, TimeSpan.FromSeconds(10));

app.Use(async (context, next) =>
{
    Console.WriteLine($"Chegou: {context.Request.Method} {context.Request.Path}{context.Request.QueryString}");
    await next(context);
    Console.WriteLine($"Saiu: {context.Response.StatusCode}");
});

app.Use(async (context, next) =>
{
    var ip = context.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
    var nome = context.Request.Query["cliente"].ToString();
    var chave = string.IsNullOrEmpty(nome) ? ip : nome;

    var usarSliding = context.Request.Query["algo"] == "sliding";

    var resultado = usarSliding
        ? await sliding.PermitirAsync(chave)
        : await limiter.PermitirAsync(chave);

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

app.MapGet("/", () => "Hello World!\n");
app.MapGet("/teste", () => "funcionou\n");

app.MapGet("/visitas", async () =>
{
    var total = await db.StringIncrementAsync("visitas");
    return $"Visitas guardadas no Redis: {total}\n";
});

app.MapGet("/corrida", () =>
{
    var pote = new TokenBucket(100, 0);
    var entraram = 0;

    Parallel.For(0, 1000, _ =>
    {
        if (pote.Permitir())
            Interlocked.Increment(ref entraram);
    });

    return $"Balas no pote: 100 | Entraram: {entraram}\n";
});

app.Run();