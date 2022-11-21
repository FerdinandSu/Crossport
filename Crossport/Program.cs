using Crossport.WebSockets;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSingleton<SignallingHandler>();

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseAuthorization();

// <snippet_UseWebSockets>
var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
};

app.UseWebSockets(webSocketOptions);
// </snippet_UseWebSockets>

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();



app.Run();
