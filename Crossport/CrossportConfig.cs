namespace Crossport;

[Flags]
public enum CrossportCharacter
{
    None = 0,
    MediaConsumer = 1 << 0,
    MediaProducer = 1 << 1,
}
[Serializable]
public record CrossportConfig
{
    public string Application{ get; set; }=string.Empty;

    public string Component { get; set; } = string.Empty;

    public CrossportCharacter Character { get; set; }
    public bool AllowAnonymous { get; set; }
}