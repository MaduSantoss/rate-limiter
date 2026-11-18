# Rate Limiter do zero em C#

Um rate limiter construído do zero em ASP.NET Core, com dois algoritmos (**Token Bucket** e **Sliding Window**), estado compartilhado no **Redis** e operações atômicas com **scripts Lua**, testado contra requisições simultâneas em mais de um servidor.

## Por que este projeto

Quase todo mundo usa uma biblioteca pronta de rate limiting e nunca vê o que acontece por baixo. A ideia aqui foi construir cada peça na mão, quebrar de propósito e entender por que cada solução existe.

## O que ele faz

- Limita quantas requisições cada cliente pode fazer por período.
- Responde **`429 Too Many Requests`** quando o limite acaba.
- Identifica o cliente pelo **IP**.
- Guarda o estado no **Redis**, então funciona com **vários servidores** ao mesmo tempo e sobrevive a reinícios.
- Usa **scripts Lua** para que cada decisão seja **atômica**, sem condição de corrida.
- Envia os headers **`X-RateLimit-Limit`**, **`X-RateLimit-Remaining`** e **`Retry-After`**.

## Os dois algoritmos

### Token Bucket

Cada cliente tem um balde com **5 fichas**. Cada requisição gasta uma ficha; sem ficha, recebe 429. O balde ganha **1 ficha por segundo**, até o limite de 5.

As fichas não são adicionadas por um timer. Quando chega uma requisição, o algoritmo calcula quanto tempo passou desde a última atualização e repõe as fichas correspondentes:

```
fichas = min(capacidade, fichas + segundos_passados × taxa)
```

Guarda apenas **2 valores por cliente** (fichas e horário da última atualização) e permite **rajadas** de até 5 requisições.

### Sliding Window

Guarda o **horário de cada requisição** num sorted set do Redis. Quando chega uma nova:

1. apaga as requisições com mais de 10 segundos;
2. conta as que sobraram;
3. se forem menos de 5, aceita e registra a nova; senão, responde 429.

Garante um **teto rígido**: em **qualquer** intervalo de 10 segundos, nunca mais de 5 requisições.

## O problema da concorrência (e como foi resolvido)

### 1. Várias threads no mesmo servidor

O ASP.NET Core atende requisições em paralelo. Se duas leem "tem 1 ficha" antes de alguma gastar, as duas passam.

Teste: balde com 100 fichas e 1.000 tentativas simultâneas (`Parallel.For`).

| Versão | Requisições aceitas |
|---|---|
| Sem `lock` | **128** ❌ |
| Com `lock` | **100** ✅ |

### 2. Vários servidores com Redis

O `lock` só protege dentro de um processo. Com o estado no Redis e duas instâncias da API, a sequência "ler → calcular → gravar" voltou a ter condição de corrida, porque eram comandos separados.

Teste: 2 servidores (portas 5176 e 5177), 100 requisições simultâneas, limite de 5.

| Versão | Requisições aceitas |
|---|---|
| Ler e gravar em comandos separados | **100** ❌ |
| Script Lua (tudo atômico no Redis) | **5** ✅ |

O Redis executa um script Lua inteiro sem intercalar outros comandos. Como todos os servidores usam o mesmo Redis, a decisão fica atômica para o sistema inteiro. O Sliding Window usa a mesma abordagem e também deixou passar exatamente **5 de 100**.

## Comparação entre os algoritmos

Teste: 1 requisição a cada 0,5 segundo, 15 vezes.

```
Token Bucket:   200 200 200 200 200 200 200 200 200 429 200 429 200 200 429  → 12 aceitas
Sliding Window: 200 200 200 200 200 429 429 429 429 429 429 429 429 429 429  →  5 aceitas
```

| | Token Bucket | Sliding Window |
|---|---|---|
| Controla | velocidade média, com folga para rajadas | teto rígido em qualquer janela |
| Memória no Redis | 2 valores por cliente | 1 registro por requisição |
| `Retry-After` ao bloquear | ~1 s | até ~10 s |
| Bom para | APIs gerais, limites altos | login, recuperação de senha, envio de SMS |

## Headers de resposta

```
HTTP/1.1 429 Too Many Requests
X-RateLimit-Limit: 5
X-RateLimit-Remaining: 0
Retry-After: 1
```

Assim o cliente sabe quanto ainda pode usar e quanto tempo esperar, em vez de tentar às cegas.

## Estrutura do projeto

```
RateLimiter/
├── Algoritmos/
│   ├── Resultado.cs             # resposta do limiter (permitido, limite, restantes, espera)
│   ├── TokenBucket.cs           # versão em memória com lock (estudo de concorrência)
│   ├── RedisTokenBucket.cs      # Token Bucket no Redis com script Lua
│   └── RedisSlidingWindow.cs    # Sliding Window no Redis com script Lua
├── Middleware/
│   ├── LoggerMiddleware.cs      # registra cada requisição
│   └── RateLimitMiddleware.cs   # aplica o limite e os headers
├── Program.cs
├── appsettings.json             # endereço do Redis
└── docker-compose.yml           # sobe o Redis
```

## Como rodar

Pré-requisitos: [.NET 10 SDK](https://dotnet.microsoft.com/download) e [Docker](https://www.docker.com/).

```bash
git clone https://github.com/MaduSantoss/rate-limiter.git
cd rate-limiter

docker compose up -d   # sobe o Redis
dotnet run             # sobe a API em http://localhost:5176
```

## Como testar

Exemplos em PowerShell.

**Ver o limite em ação:**

```powershell
1..7 | ForEach-Object { curl.exe -s -o NUL -w "Requisicao $_ : %{http_code}`n" http://localhost:5176/teste }
```

**Ver os headers:**

```powershell
curl.exe -i http://localhost:5176/teste
```

**Atalhos de teste (só em Development):**

- `?cliente=nome` simula clientes diferentes a partir da mesma máquina.
- `?algo=sliding` usa o Sliding Window em vez do Token Bucket.

Em Production esses parâmetros são ignorados e o cliente é sempre identificado pelo IP, para que ninguém burle o limite trocando de nome.

**Ataque concorrente com dois servidores:**

```powershell
# terminal 1
dotnet run --urls http://localhost:5176
# terminal 2
dotnet run --no-build --urls http://localhost:5177
# terminal 3
curl.exe -s --parallel --parallel-max 100 "http://localhost:5176/teste?cliente=ataque&n=[1-50]" "http://localhost:5177/teste?cliente=ataque&n=[1-50]" | Select-String "funcionou" | Measure-Object
```

Resultado esperado: `Count : 5`.

**Demonstração da condição de corrida em memória:**

```powershell
curl.exe -s http://localhost:5176/corrida
```

## Limitações conhecidas

- **Relógio:** o horário usado nos cálculos vem do servidor da API. Com servidores de relógios dessincronizados, o cálculo pode variar um pouco. Uma alternativa seria usar o comando `TIME` do próprio Redis dentro do script.
- **Redis fora do ar:** hoje a API falha se o Redis cair. Em produção seria preciso decidir entre bloquear tudo (mais seguro) ou liberar tudo (mais disponível).
- **IP atrás de proxy:** atrás de um load balancer, o IP visto pela API é o do proxy. Seria preciso configurar o `ForwardedHeaders` para ler o IP real do cliente.
- **Mesmo limite para todas as rotas:** o ideal seria, por exemplo, Sliding Window mais rígido no login e Token Bucket nas demais.

## Próximos passos

- [ ] Limites diferentes por rota
- [ ] Política de falha quando o Redis estiver indisponível
- [ ] Testes automatizados (unitários e de integração com Redis)
- [ ] Comparar com o rate limiter nativo do ASP.NET Core (`AddRateLimiter`)

## Tecnologias

C# · ASP.NET Core (.NET 10) · Redis · StackExchange.Redis · Lua · Docker