using Crossport.AppManaging;

namespace Crossport;


[Serializable]
public record CrossportConfig
{
    public string Application{ get; set; }=string.Empty;

    public string Component { get; set; } = string.Empty;

    public int Character { get; set; }
}