using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Vlad3.Application.Auth;
using Vlad3.Application.Bots;
using Vlad3.Application.DependencyInjection;
using Vlad3.Application.Options;
using Vlad3.Application.Playlists;
using Vlad3.Application.Storage;
using Vlad3.Application.Users;
using Vlad3.Core.Models;
using Vlad3.WebControl.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { 
        Title = "Vlad3.WebControl", 
        Version = "v1" 
    });
    
    c.AddSecurityDefinition("bearer", new OpenApiSecurityScheme {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Введите JWT токен без префикса Bearer (он будет автодобавлен)"
    });
    
    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("bearer", document)] = []
    });
});

#region CorsPolicy

var allowedOrigins = new[]
{
    "https://vlad.mrshurukan.ru",
};


const string myAllowSpecificOrigins = "CorsPolicy";

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: myAllowSpecificOrigins,
        policy =>
        {
            policy.AllowAnyMethod()
                .AllowAnyHeader()
                .WithExposedHeaders("Content-Disposition")
                .AllowCredentials();

            policy.SetIsOriginAllowed(origin =>
            {
                var uri = new Uri(origin).Host;
#if DEBUG
                if (uri is "localhost" or "127.0.0.1")
                    return true;
#endif

                return allowedOrigins.Contains(origin);
            });
        });
});

#endregion

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Auth:Jwt"));
builder.Services.Configure<AdminSeedOptions>(builder.Configuration.GetSection("Auth:SeedAdmin"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));

builder.Services.AddSingleton<StoragePathResolver>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<IUserStore, UserStore>();
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();
builder.Services.AddSingleton<UserSeeder>();

builder.Services.AddSingleton<IBotConfigurationStore, BotConfigurationStore>();
builder.Services.AddSingleton<IPlaylistService, PlaylistService>();
builder.Services.AddSingleton<BotFactoryRegistry>();
builder.Services.AddSingleton<IBotManager, BotManager>();
builder.Services.AddHostedService<BotManagerHostedService>();
builder.Services.AddAudioBotFactories();

var jwtOptions = builder.Configuration.GetSection("Auth:Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
{
    throw new InvalidOperationException("Auth:Jwt:SigningKey is required.");
}

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", policy => policy.RequireRole(UserRoles.Admin))
    .AddPolicy("OperatorOrAdmin", policy => policy.RequireRole(UserRoles.Admin, UserRoles.Operator));

var app = builder.Build();

app.UseCors(myAllowSpecificOrigins);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<UserSeeder>();
    await seeder.SeedAsync();
}

app.Run();