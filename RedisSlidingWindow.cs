using StackExchange.Redis;

public class RedisSlidingWindow
{
    private readonly IDatabase db;
    private readonly int limite;     
    private readonly long janelaMs;

    private const string Script = @"
        local chave = KEYS[1]
        local limite = tonumber(ARGV[1])
        local janelaMs = tonumber(ARGV[2])
        local agora = tonumber(ARGV[3])
        local idVisita = ARGV[4]

        -- 1. APAGAR as visitas velhas (de antes do começo da janela)
        redis.call('ZREMRANGEBYSCORE', chave, 0, agora - janelaMs)

        -- 2. CONTAR as que sobraram
        local total = redis.call('ZCARD', chave)

        -- 3. DECIDIR
        if total < limite then
            -- 4. ANOTAR a nova visita
            redis.call('ZADD', chave, agora, idVisita)
            redis.call('PEXPIRE', chave, janelaMs)
            return 1
        end

        return 0
    ";

    public RedisSlidingWindow(IDatabase db, int limite, TimeSpan janela)
    {
        this.db = db;
        this.limite = limite;
        janelaMs = (long)janela.TotalMilliseconds;
    }

    public async Task<bool> PermitirAsync(string cliente)
    {
        var chave = $"janela:{cliente}";
        var agora = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var idVisita = Guid.NewGuid().ToString();

        var resultado = await db.ScriptEvaluateAsync(
            Script,
            new RedisKey[] { chave },
            new RedisValue[] { limite, janelaMs, agora, idVisita });

        return (int)resultado == 1;
    }
}