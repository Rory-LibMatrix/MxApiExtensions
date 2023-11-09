using System.Net.Mime;
using LibMatrix;
using LibMatrix.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.Logging.Console;
using MxApiExtensions;
using MxApiExtensions.Classes;
using MxApiExtensions.Classes.LibMatrix;
using MxApiExtensions.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers().AddJsonOptions(options => { options.JsonSerializerOptions.WriteIndented = true; });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

builder.Services.AddSingleton<MxApiExtensionsConfiguration>();

builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<AuthenticatedHomeserverProviderService>();
builder.Services.AddScoped<UserContextService>();

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

// builder.Services.AddCors(x => x.AddDefaultPolicy(y => y.AllowAnyHeader().AllowCredentials().AllowAnyOrigin().AllowAnyMethod()));
builder.Services.AddCors(options => {
    options.AddPolicy(
        "Open",
        policy => policy.AllowAnyOrigin().AllowAnyHeader());
});
// builder.Logging.AddConsole(x => x.FormatterName = "custom").AddConsoleFormatter<CustomLogFormatter, SimpleConsoleFormatterOptions>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()) {
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();
app.UseCors("Open");

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {

        var exceptionHandlerPathFeature =
            context.Features.Get<IExceptionHandlerPathFeature>();

        if (exceptionHandlerPathFeature?.Error is MatrixException mxe) {
            context.Response.StatusCode = mxe.ErrorCode switch {
                "M_NOT_FOUND" => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status500InternalServerError
            };
            context.Response.ContentType = MediaTypeNames.Application.Json;
            await context.Response.WriteAsync(mxe.GetAsJson()!);
        }
        else {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = MediaTypeNames.Application.Json;
            await context.Response.WriteAsync(new MxApiMatrixException() {
                ErrorCode = "M_UNKNOWN",
                Error = exceptionHandlerPathFeature?.Error.ToString()
            }.GetAsJson());
        }
    });
});


// app.UseAuthorization();

app.MapControllers();

app.Run();