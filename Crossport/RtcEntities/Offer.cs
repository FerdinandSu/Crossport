namespace Crossport.RtcEntities;

public record Offer(string Sdp, long Datetime, bool Polite)
{
    //public bool Polite { get; set; } = Polite;
}