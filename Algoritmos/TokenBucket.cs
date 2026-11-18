public class TokenBucket
{
    private readonly double capacidade;       
    private readonly double balasPorSegundo;  
    private double fichas;                    
    private DateTime ultimaOlhada;            
    private readonly object trava = new object();

    public TokenBucket(double capacidade, double balasPorSegundo)
    {
        this.capacidade = capacidade;
        this.balasPorSegundo = balasPorSegundo;
        fichas = capacidade;
        ultimaOlhada = DateTime.UtcNow;
    }

    public bool Permitir()
    {
        lock (trava)              
        {
            Recarregar();

            if (fichas >= 1)
            {
                fichas -= 1;
                return true;
            }

            return false;
        }
    }

    private void Recarregar()
    {
        var agora = DateTime.UtcNow;
        var segundos = (agora - ultimaOlhada).TotalSeconds;  

        fichas = Math.Min(capacidade, fichas + segundos * balasPorSegundo); 
        ultimaOlhada = agora;                               
    }

    public double Fichas => fichas;
}