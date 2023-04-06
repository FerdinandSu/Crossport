using Crossport.AppManaging;
using Crossport.Signalling;
using Crossport.Signalling.Prototype;
using Crossport.WebSockets;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSingleton<ISignallingHandler,CrossportSignallingHandler>();

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
