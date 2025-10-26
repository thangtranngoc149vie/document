using System.Text.Json;
using DocumentApi.Data;
using DocumentApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var authority = builder.Configuration["Authentication:Authority"];
        var audience = builder.Configuration["Authentication:Audience"];

        if (!string.IsNullOrWhiteSpace(authority))
        {
            options.Authority = authority;
            options.RequireHttpsMetadata = builder.Configuration.GetValue<bool>("Authentication:RequireHttpsMetadata", false);
        }

        if (!string.IsNullOrWhiteSpace(audience))
        {
            options.TokenValidationParameters ??= new TokenValidationParameters();
            options.TokenValidationParameters.ValidateAudience = true;
            options.TokenValidationParameters.ValidAudience = audience;
        }
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("proj.document.read", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
        {
            var permissionClaims = context.User.FindAll("permissions");
            foreach (var claim in permissionClaims)
            {
                var values = claim.Value.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (values.Any(v => string.Equals(v, "proj:document:read", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        });
    });
});

builder.Services.AddScoped(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("Default");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Connection string 'Default' is not configured.");
    }

    return new NpgsqlConnection(connectionString);
});

builder.Services.AddScoped<IDocumentTypesRepository, DocumentTypesRepository>();
builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
builder.Services.AddScoped<IDocumentTypesService, DocumentTypesService>();
builder.Services.AddSingleton<IProjectAccessEvaluator, ProjectAccessEvaluator>();

var app = builder.Build();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
