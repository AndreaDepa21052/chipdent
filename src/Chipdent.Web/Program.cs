using Chipdent.Web.Hubs;
using Chipdent.Web.Infrastructure.Identity;
using Chipdent.Web.Infrastructure.Mongo;
using Chipdent.Web.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection(MongoSettings.SectionName));
builder.Services.AddSingleton<MongoContext>();
builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();

builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddSingleton<INotificationPublisher, NotificationPublisher>();
builder.Services.AddSingleton<IChatPublisher, ChatPublisher>();
builder.Services.AddSingleton<Chipdent.Web.Infrastructure.Storage.IFileStorage, Chipdent.Web.Infrastructure.Storage.LocalFileStorage>();
builder.Services.AddSingleton<Chipdent.Web.Infrastructure.Notifications.IEmailSender, Chipdent.Web.Infrastructure.Notifications.LogOnlyEmailSender>();
builder.Services.AddHostedService<Chipdent.Web.Infrastructure.Notifications.DigestEmailService>();
builder.Services.AddScoped<Chipdent.Web.Infrastructure.Insights.AiInsightsEngine>();
builder.Services.AddScoped<Chipdent.Web.Infrastructure.Audit.IAuditService, Chipdent.Web.Infrastructure.Audit.AuditService>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/account/login";
        options.LogoutPath = "/account/logout";
        options.AccessDeniedPath = "/account/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name = "chipdent.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

builder.Services.AddAuthorization(Chipdent.Web.Infrastructure.Identity.Policies.Configure);
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

var staticOptions = new Microsoft.AspNetCore.Builder.StaticFileOptions
{
    ContentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider
    {
        Mappings =
        {
            [".webmanifest"] = "application/manifest+json"
        }
    },
    OnPrepareResponse = ctx =>
    {
        var path = ctx.File.Name;
        if (path.Equals("robots.txt", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("sitemap.xml", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=3600";
        }
    }
};
app.UseStaticFiles(staticOptions);

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantResolverMiddleware>();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<NotificationsHub>("/hubs/notifications");
app.MapHub<ChatHub>("/hubs/chat");

using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<MongoContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Seed");
    await MongoSeeder.SeedAsync(ctx, hasher, logger);
}

app.Run();
