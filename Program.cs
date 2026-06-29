using Microsoft.AspNetCore.Authentication.Cookies;
using QuinielaApp.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using QuinielaApp.Data;
using QuinielaApp.Models;
using QuinielaApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o => {
        o.LoginPath         = "/Account/Login";
        o.LogoutPath        = "/Account/Logout";
        o.AccessDeniedPath  = "/Account/Login";
        o.ExpireTimeSpan    = TimeSpan.FromDays(7);
        o.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews();
builder.Services.AddSession(o => { o.IdleTimeout = TimeSpan.FromMinutes(30); });

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<TournamentService>();
builder.Services.AddScoped<PredictionService>();
builder.Services.AddScoped<SpecialPredictionService>();
builder.Services.AddScoped<MatchSyncService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<ApiFootballService>();
builder.Services.AddScoped<EmailService>();

// Background service de sincronización automática
builder.Services.AddHostedService<ScoreSyncBackgroundService>();

// Persistir claves de Data Protection en disco para que los antiforgery tokens
// sobrevivan reinicios del servicio (en prod: /var/www/quiniela/keys).
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/var/www/quiniela/keys"))
    .SetApplicationName("QuinielaApp");

var app = builder.Build();

// Migraciones + seed del admin (contraseña en texto plano)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    //db.Database.Migrate();

    if (!db.Users.Any(u => u.Role == "Admin"))
    {
        db.Users.Add(new User
        {
            FullName     = "Administrador",
            Username     = "admin",
            Email        = "admin@quiniela.com",
            PasswordHash = "Admin123!",   // texto plano — solo desarrollo
            Role         = "Admin",
            CreatedAt    = DateTime.UtcNow
        });
        db.SaveChanges();
    }
    else
    {
        // Asegurar que el admin siempre tenga la contraseña correcta
        var admin = db.Users.First(u => u.Role == "Admin");
        admin.PasswordHash = "Admin123!";
        db.SaveChanges();
    }
}

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Account/Login");

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute("admin", "Admin/{action=Index}/{id?}", new { controller = "Admin" });
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

app.Run();
