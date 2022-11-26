using Microsoft.AspNetCore.Mvc;

namespace Crossport.WebSockets;
using Microsoft.AspNetCore.Mvc.Routing;

public sealed class HttpConnectAttribute : HttpMethodAttribute
{
    private static readonly IEnumerable<string> SupportedMethods = new[] { "CONNECT" };

    public HttpConnectAttribute()
        : base(SupportedMethods)
    {
    }

    public HttpConnectAttribute(string template)
        : base(SupportedMethods, template)
    {
    }
}