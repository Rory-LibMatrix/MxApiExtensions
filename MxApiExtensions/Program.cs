using LibMatrix.Services;
using Microsoft.AspNetCore.Http.Timeouts;
using MxApiExtensions;
using MxApiExtensions.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers().AddJsonOptions(options => {
    options.JsonSerializerOptions.WriteIndented = true;
});
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

builder.Services.AddSingleton<MxApiExtensionsConfiguration>();

builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<AuthenticatedHomeserverProviderService>();

builder.Services.AddSingleton<TieredStorageService>(x => {
    var config = x.GetRequiredService<MxApiExtensionsConfiguration>();
    return new TieredStorageService(
        cacheStorageProvider: new FileStorageProvider("/run"),
        dataStorageProvider: new FileStorageProvider("/run")
    );
});
builder.Services.AddRoryLibMatrixServices();

builder.Services.AddRequestTimeouts(x => {
    x.DefaultPolicy = new RequestTimeoutPolicy {
        Timeout = TimeSpan.FromMinutes(10),
        WriteTimeoutResponse = async context => {
            context.Response.StatusCode = 504;
            context.Response.ContentType = "application/json";
            await context.Response.StartAsync();
            await context.Response.WriteAsJsonAsync(new MxApiMatrixException {
                ErrorCode = "M_TIMEOUT",
                Error = "Request timed out"
            }.GetAsJson());
            await context.Response.CompleteAsync();
        }
    };
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()) {
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
