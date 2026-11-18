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

        -- 1. APAGAR as visitas velhas
        redis.call('ZREMRANGEBYSCORE', chave, 0, agora - janelaMs)

        -- 2. CONTAR
        local total = redis.call('ZCARD', chave)

        -- 3. DECIDIR
        if total < limite then
            -- 4. ANOTAR
            redis.call('ZADD', chave, agora, idVisita)
            redis.call('PEXPIRE', chave, janelaMs)
            return { 1, limite - total - 1, 0 }
        end

        -- Cheio: quando a visita mais antiga sai da janela?
        local maisAntiga = redis.call('ZRANGE', chave, 0, 0, 'WITHSCORES')
        local horaMaisAntiga = tonumber(maisAntiga[2])
        local esperarMs = horaMaisAntiga + janelaMs - agora

        return { 0, 0, math.ceil(esperarMs / 1000) }
    ";

    public RedisSlidingWindow(IDatabase db, int limite, TimeSpan janela)
    {
        this.db = db;
        this.limite = limite;
        janelaMs = (long)janela.TotalMilliseconds;
    }

    public async Task<Resultado> PermitirAsync(string cliente)
    {
        var chave = $"janela:{cliente}";
        var agora = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var idVisita = Guid.NewGuid().ToString();

        var resposta = (RedisResult[])(await db.ScriptEvaluateAsync(
            Script,
            new RedisKey[] { chave },
            new RedisValue[] { limite, janelaMs, agora, idVisita }))!;

        return new Resultado(
            Permitido: (int)resposta[0] == 1,
            Limite: limite,
            Restantes: (int)resposta[1],
            EsperarSegundos: (int)resposta[2]);
    }
}