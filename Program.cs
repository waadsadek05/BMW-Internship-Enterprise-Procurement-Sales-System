using Microsoft.EntityFrameworkCore;
using EnterpriseSystem.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddSession(); // تفعيل الـ Session

// 1. إضافة خدمات الـ MVC مع فرض حماية عامة (تتطلب تسجيل دخول لكل الصفحات)
builder.Services.AddControllersWithViews(options =>
{
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new AuthorizeFilter(policy));
});

// 2. إعداد خدمة الـ Cookie Authentication لتخزين جلسة المستخدم
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login"; // مسار صفحة تسجيل الدخول
        options.AccessDeniedPath = "/Account/AccessDenied"; // مسار صفحة رفض الوصول
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
    });

builder.Services.AddAuthorization();

// 3. ربط قاعدة البيانات بـ Entity Framework Core
builder.Services.AddDbContext<EnterpriseProcurementSalesDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

// ✨ 4. تفعيل الـ Session هنا (قبل المصادقة والصلاحيات لضمان قراءة البيانات صحيحة)
app.UseSession();

// 5. تفعيل المصادقة والصلاحيات
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();