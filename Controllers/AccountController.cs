using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EnterpriseSystem.Models;

namespace EnterpriseSystem.Controllers
{
    public class AccountController : Controller
    {
        private readonly EnterpriseProcurementSalesDbContext _context;

        public AccountController(EnterpriseProcurementSalesDbContext context)
        {
            _context = context;
        }

        // GET: /Account/Login
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        // POST: /Account/Login
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ModelState.AddModelError("", "Please enter both username and password.");
                return View();
            }

            // البحث عن المستخدم باستخدام الـ Username
            var user = await _context.ApplicationUsers
                .FirstOrDefaultAsync(u => u.Username == username);

            if (user == null || user.Password != password)
            {
                ModelState.AddModelError("", "Invalid username or password.");
                return View();
            }

            // جلب الأدوار (Roles) الخاصة بالمستخدم
            var roleIds = await _context.UserRoles
                .Where(ur => ur.UserId == user.Id)
                .Select(ur => ur.RoleId)
                .ToListAsync();

            var roles = await _context.Roles
                .Where(r => roleIds.Contains(r.Id))
                .Select(r => r.Name)
                .ToListAsync();
                

            // بناء بيانات الهوية (Claims)
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.FullName ?? user.Username ?? "User"),
                new Claim(ClaimTypes.Email, user.Email ?? "")
            };

            foreach (var role in roles)
            {
                if (!string.IsNullOrEmpty(role))
                {
                    claims.Add(new Claim(ClaimTypes.Role, role));
                }
            }

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

            // حفظ الـ Username في الـ Session للاستخدام السريع في الصلاحيات
            HttpContext.Session.SetString("Username", user.Username);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        // POST: /Account/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }

        // GET: /Account/Profile
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return RedirectToAction("Login");

            var user = await _context.ApplicationUsers.FindAsync(userId);
            if (user == null) return NotFound();

            return View(user);
        }

        // POST: /Account/Profile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(string id, string fullName, string email, string username, string currentPassword, string newPassword)
        {
            var user = await _context.ApplicationUsers.FindAsync(id);
            if (user == null) return NotFound();

            // فحص هل الـ Username الجديد مستخدم من قبل شخص آخر
            bool usernameExists = await _context.ApplicationUsers.AnyAsync(u => u.Username == username && u.Id != id);
            if (usernameExists)
            {
                ModelState.AddModelError("", "This username is already taken by another user.");
                return View(user);
            }

            // تحديث البيانات الأساسية
            user.FullName = fullName;
            user.Email = email;
            user.Username = username;

            // إذا أراد المستخدم تغيير كلمة المرور
            if (!string.IsNullOrEmpty(newPassword))
            {
                if (user.Password != currentPassword)
                {
                    ModelState.AddModelError("", "Current password is incorrect.");
                    return View(user);
                }
                user.Password = newPassword;
            }

            _context.Update(user);
            await _context.SaveChangesAsync();

            // تحديث الـ Session للـ Username لو تغير
            HttpContext.Session.SetString("Username", user.Username);

            TempData["SuccessMessage"] = "Profile updated successfully!";
            return RedirectToAction(nameof(Profile));
        }

        // GET: /Account/AccessDenied
        [HttpGet]
        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}