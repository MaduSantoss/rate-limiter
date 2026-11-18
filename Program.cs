using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var redis = ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!);
var db = redis.GetDatabase();

var tokenBucket = new RedisTokenBucket(db, capacidade: 5, balasPorSegundo: 1);
var slidingWindow = new RedisSlidingWindow(db, limite: 5, janela: TimeSpan.FromSeconds(10));

app.UseLogger();
app.UseRateLimiter(tokenBucket, slidingWindow);

app.MapGet("/", () => "Hello World!\n");
app.MapGet("/teste", () => "funcionou\n");

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