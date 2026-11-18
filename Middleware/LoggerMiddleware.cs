public static class LoggerMiddleware
{
    public static void UseLogger(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            Console.WriteLine($"Chegou: {context.Request.Method} {context.Request.Path}{context.Request.QueryString}");
            await next(context);
            Console.WriteLine($"Saiu: {context.Response.StatusCode}");
        });
    }
}