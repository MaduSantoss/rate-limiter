using StackExchange.Redis;

public class RedisTokenBucket
{
    private readonly IDatabase db;
    private readonly double capacidade;
    private readonly double balasPorSegundo;

    private const string Script = @"
        local chave = KEYS[1]
        local capacidade = tonumber(ARGV[1])
        local balasPorSegundo = tonumber(ARGV[2])
        local agora = tonumber(ARGV[3])

        -- 1. LER o potinho
        local dados = redis.call('HMGET', chave, 'fichas', 'ultimaOlhada')
        local fichas = tonumber(dados[1])
        local ultimaOlhada = tonumber(dados[2])

        -- Se não existir, começa cheio
        if fichas == nil then
            fichas = capacidade
            ultimaOlhada = agora
        end

        -- 2. RECARREGAR
        local segundos = (agora - ultimaOlhada) / 1000
        fichas = math.min(capacidade, fichas + segundos * balasPorSegundo)

        -- 3. DECIDIR
        local permitido = 0
        if fichas >= 1 then
            fichas = fichas - 1
            permitido = 1
        end

        -- 4. GRAVAR
        redis.call('HSET', chave, 'fichas', tostring(fichas), 'ultimaOlhada', tostring(agora))
        redis.call('EXPIRE', chave, 60)

        return permitido
    ";

    public RedisTokenBucket(IDatabase db, double capacidade, double balasPorSegundo)
    {
        this.db = db;
        this.capacidade = capacidade;
        this.balasPorSegundo = balasPorSegundo;
    }

    public async Task<bool> PermitirAsync(string cliente)
    {
        var chave = $"balde:{cliente}";
        var agora = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var resultado = await db.ScriptEvaluateAsync(
            Script,
            new RedisKey[] { chave },
            new RedisValue[] { capacidade, balasPorSegundo, agora });

        return (int)resultado == 1;
    }
}