namespace Crossport.RtcEntities;

public record CandidateRecord(string Candidate, int SdpMLineIndex, string SdpMid, long Datetime)
{
}