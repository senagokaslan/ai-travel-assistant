using TravelAssistant.Api.Features.Admin;
using TravelAssistant.Api.Features.Auth;
using TravelAssistant.Api.Features.Chat;
using TravelAssistant.Api.Features.Flights;
using TravelAssistant.Api.Features.Hotels;
using TravelAssistant.Api.Features.Profile;
using TravelAssistant.Api.Features.System;
using TravelAssistant.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient<AiTravelUnderstandingService>();
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

await DatabaseMigrator.InitializeAsync(
    app.Configuration,
    app.Environment.IsDevelopment(),
    app.Logger,
    CancellationToken.None);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.MapAuthEndpoints();
app.MapChatEndpoints();
app.MapFlightEndpoints();
app.MapHotelEndpoints();
app.MapProfileEndpoints();
app.MapAdminEndpoints();
app.MapSystemEndpoints();

app.Run();
