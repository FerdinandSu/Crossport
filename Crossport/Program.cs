using Crossport.Core;
using Crossport.Core.Connecting;
using Crossport.Core.Signalling;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .CreateBootstrapLogger(); // <-- Change this line!

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
builder.Services.AddSingleton<DiagnosticSignallingHandlerFactory>();

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseAuthorization();

// <snippet_UseWebSockets>
var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(1)
};

Peer.LostPeerLifetime = builder.Configuration.GetSection("Crossport:ConnectionManagement")
    .GetValue<int>(nameof(Peer.LostPeerLifetime));
NonPeerConnection.OfferedConnectionLifetime = builder.Configuration.GetSection("Crossport:ConnectionManagement")
    .GetValue<int>(
        nameof(NonPeerConnection.OfferedConnectionLifetime));

app.UseWebSockets(webSocketOptions);
// </snippet_UseWebSockets>

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();


app.Run();