namespace Crossport.Core.Entities;

[Serializable]
public record CrossportConfig
{
    public string Application { get; set; } = string.Empty;

    public string Component { get; set; } = string.Empty;

    public int Capacity { get; set; }
}