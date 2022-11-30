namespace Crossport.Signalling;

public interface ISignallingHandler
{
    void Track(WebRtcPeer session);
    Task UnTrack(WebRtcPeer session);
    void Add(WebRtcPeer session);
    Task Remove(WebRtcPeer session);

    public class OfferAnswerStruct
    {
        public string ConnectionId { get; set; } = "";
        public string Sdp { get; set; } = "";
    }

    public class CandidateStruct
    {
        public string ConnectionId { get; set; } = "";
        //public string Sdp { get; set; } = "";
        public string Candidate { get; set; } = "";
        public int SdpMLineIndex { get; set; }
        public int SdpMid { get; set; }
    }
}