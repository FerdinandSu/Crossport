namespace Crossport.WebSockets;

public record Offer(string Sdp,long Datetime,bool Polite)
{
    public bool Polite { get; set; } = Polite;
}