using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EnterpriseSystem.Models;

namespace EnterpriseSystem.Controllers
{
    public class RolesController : Controller
    {
        private readonly EnterpriseProcurementSalesDbContext _context;

        public RolesController(EnterpriseProcurementSalesDbContext context)
        {
            _context = context;
        }

        // GET: Roles
        public async Task<IActionResult> Index()
        {
            var roles = await _context.Roles.ToListAsync();
            return View(roles);
        }

        // GET: Roles/Create
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Roles/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                // 1. فحص هل اسم الرول موجود مسبقاً في القاعدة أم لا؟
                bool roleExists = await _context.Roles.AnyAsync(r => r.Name.ToLower() == name.ToLower());
                
                if (roleExists)
                {
                    ModelState.AddModelError("", "This role name already exists!");
                    return View();
                }

                // 2. البحث عن أكبر رقم موجود حالياً لتوليد الـ ID التسلسلي
                int nextId = 1;
                var existingRoles = await _context.Roles.ToListAsync();
                
                if (existingRoles.Any())
                {
                    var maxId = existingRoles
                        .Where(r => int.TryParse(r.Id, out _))
                        .Max(r => int.Parse(r.Id));
                    
                    nextId = maxId + 1;
                }

                var role = new Role
                {
                    Id = nextId.ToString(),
                    Name = name
                };

                _context.Roles.Add(role);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError("", "Role name cannot be empty.");
            return View();
        }

        // POST: Roles/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            if (id != null)
            {
                var role = await _context.Roles.FindAsync(id);
                if (role != null)
                {
                    _context.Roles.Remove(role);
                    await _context.SaveChangesAsync();
                }
            }
            return RedirectToAction(nameof(Index));
        }

        // GET: Roles/Edit/5
        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            if (id == null) return NotFound();

            var role = await _context.Roles.FindAsync(id);
            if (role == null) return NotFound();

            return View(role);
        }

        // POST: Roles/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, string name)
        {
            var role = await _context.Roles.FindAsync(id);
            if (role == null) return NotFound();

            if (!string.IsNullOrWhiteSpace(name))
            {
                // فحص هل الاسم الجديد موجود عند رول تاني ولا لأ
                bool roleExists = await _context.Roles.AnyAsync(r => r.Name.ToLower() == name.ToLower() && r.Id != id);
                if (roleExists)
                {
                    ModelState.AddModelError("", "This role name already exists!");
                    return View(role);
                }

                role.Name = name;
                _context.Update(role);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError("", "Role name cannot be empty.");
            return View(role);
        }

    }
}