using Crossport.AppManaging;
using Crossport.Signalling;
using Crossport.Signalling.Prototype;
using Crossport.WebSockets;
using Serilog;
using Serilog.Events;
using Serilog.Templates;

Log.Logger = new LoggerConfiguration()
    //.MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    //.WriteTo.Console(new ExpressionTemplate)
    .CreateBootstrapLogger(); // <-- Change this line!

//
//    "formatter": {
//        "type": "Serilog.Templates.ExpressionTemplate, Serilog.Expressions",
//        "template": "[{@t:HH:mm:ss} {@l:u3} {Coalesce(SourceContext, '<none>')}] {@m}\n{@x}"
//    }
//}

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());
// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSingleton<AppManager>();

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseAuthorization();

// <snippet_UseWebSockets>
var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(1),
};

Peer.LostPeerLifetime=builder.Configuration.GetSection("Crossport:ConnectionManagement").GetValue<int>(nameof(Peer.LostPeerLifetime));
NonPeerConnection.OfferedConnectionLifetime= builder.Configuration.GetSection("Crossport:ConnectionManagement").GetValue<int>(
    nameof(NonPeerConnection.OfferedConnectionLifetime));

app.UseWebSockets(webSocketOptions);
// </snippet_UseWebSockets>

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();



app.Run();
