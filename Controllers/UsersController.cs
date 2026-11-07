using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using EnterpriseSystem.Models;

namespace EnterpriseSystem.Controllers
{
    [Authorize(Roles = "Admin")]
    public class UsersController : Controller
    {
        private readonly EnterpriseProcurementSalesDbContext _context;

        public UsersController(EnterpriseProcurementSalesDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(int page = 1)
        {
            int pageSize = 50;

            var query = _context.ApplicationUsers
                .Include(u => u.Branch)
                .OrderByDescending(u => u.Id);

            int totalItems = await query.CountAsync();
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            // التأكد إن رقم الصفحة في النطاق الصحيح
            page = Math.Max(1, Math.Min(page, totalPages == 0 ? 1 : totalPages));

            var users = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            
            var userRolesDict = new Dictionary<string, List<string>>();

            foreach (var user in users)
            {
                var roleIds = await _context.UserRoles
                    .Where(ur => ur.UserId == user.Id)
                    .Select(ur => ur.RoleId)
                    .ToListAsync();

                var roleNames = await _context.Roles
                    .Where(r => roleIds.Contains(r.Id))
                    .Select(r => r.Name)
                    .ToListAsync();

                userRolesDict[user.Id] = roleNames;
            }

            ViewBag.UserRoles = userRolesDict;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;

            return View(users);
        }

        public async Task<IActionResult> Create()
        {
            var roles = await _context.Roles.ToListAsync();
            ViewBag.Roles = new SelectList(roles, "Id", "Name");

            var branches = await _context.Branches.ToListAsync();
            ViewBag.Branches = new SelectList(branches, "Id", "Name");

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ApplicationUser user, string? selectedRoleId)
        {
            int nextId = 1;
            var existingUsers = await _context.ApplicationUsers.ToListAsync();

            if (existingUsers.Any())
            {
                var maxId = existingUsers
                    .Where(u => int.TryParse(u.Id, out _))
                    .Max(u => int.Parse(u.Id));

                nextId = maxId + 1;
            }

            user.Id = nextId.ToString();
            user.IsActive = true; 

            ModelState.Remove("Id");
            ModelState.Remove("Branch");
            ModelState.Remove("SalesOrders");
            ModelState.Remove("selectedRoleId");

            bool usernameExists = await _context.ApplicationUsers
                .AnyAsync(u => u.Username.ToLower() == user.Username.ToLower());

            bool emailExists = await _context.ApplicationUsers
                .AnyAsync(u => u.Email.ToLower() == user.Email.ToLower());

            if (usernameExists || emailExists)
            {
                if (usernameExists) ModelState.AddModelError("Username", "This username is already taken.");
                if (emailExists) ModelState.AddModelError("Email", "This email address is already registered.");

                var rolesList = await _context.Roles.ToListAsync();
                ViewBag.Roles = new SelectList(rolesList, "Id", "Name", selectedRoleId);

                var branchesList = await _context.Branches.ToListAsync();
                ViewBag.Branches = new SelectList(branchesList, "Id", "Name", user.BranchId);

                return View(user);
            }

            if (ModelState.IsValid)
            {
                _context.ApplicationUsers.Add(user);
                await _context.SaveChangesAsync();

                if (!string.IsNullOrEmpty(selectedRoleId))
                {
                    _context.UserRoles.Add(new UserRole
                    {
                        UserId = user.Id,
                        RoleId = selectedRoleId
                    });
                    await _context.SaveChangesAsync();
                }

                return RedirectToAction(nameof(Index));
            }

            var roles = await _context.Roles.ToListAsync();
            ViewBag.Roles = new SelectList(roles, "Id", "Name", selectedRoleId);

            var branches = await _context.Branches.ToListAsync();
            ViewBag.Branches = new SelectList(branches, "Id", "Name", user.BranchId);

            return View(user);
        }

        // ==================== EDIT ACTIONS ====================
        
        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            if (id == null) return NotFound();

            var user = await _context.ApplicationUsers.FindAsync(id);
            if (user == null) return NotFound();

            var roles = await _context.Roles.ToListAsync();
            var userRole = await _context.UserRoles.FirstOrDefaultAsync(ur => ur.UserId == user.Id);
            string? currentRoleId = userRole?.RoleId;

            ViewBag.Roles = new SelectList(roles, "Id", "Name", currentRoleId);

            var branches = await _context.Branches.ToListAsync();
            ViewBag.Branches = new SelectList(branches, "Id", "Name", user.BranchId);

            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, ApplicationUser user, string? selectedRoleId)
        {
            if (id != user.Id) return NotFound();

            ModelState.Remove("Branch");
            ModelState.Remove("SalesOrders");
            ModelState.Remove("selectedRoleId");

            bool usernameExists = await _context.ApplicationUsers
                .AnyAsync(u => u.Username.ToLower() == user.Username.ToLower() && u.Id != id);

            bool emailExists = await _context.ApplicationUsers
                .AnyAsync(u => u.Email.ToLower() == user.Email.ToLower() && u.Id != id);

            if (usernameExists || emailExists)
            {
                if (usernameExists) ModelState.AddModelError("Username", "This username is already taken.");
                if (emailExists) ModelState.AddModelError("Email", "This email address is already registered.");

                var rolesList = await _context.Roles.ToListAsync();
                ViewBag.Roles = new SelectList(rolesList, "Id", "Name", selectedRoleId);
                var branchesList = await _context.Branches.ToListAsync();
                ViewBag.Branches = new SelectList(branchesList, "Id", "Name", user.BranchId);

                return View(user);
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var existingUser = await _context.ApplicationUsers.FindAsync(id);
                    if (existingUser == null) return NotFound();

                    existingUser.FullName = user.FullName;
                    existingUser.Username = user.Username;
                    existingUser.Email = user.Email;
                    existingUser.Password = user.Password;
                    existingUser.BranchId = user.BranchId;

                    _context.Update(existingUser);

                    var existingUserRole = await _context.UserRoles.FirstOrDefaultAsync(ur => ur.UserId == id);
                    if (!string.IsNullOrEmpty(selectedRoleId))
                    {
                        if (existingUserRole != null)
                        {
                            existingUserRole.RoleId = selectedRoleId;
                            _context.UserRoles.Update(existingUserRole);
                        }
                        else
                        {
                            _context.UserRoles.Add(new UserRole { UserId = id, RoleId = selectedRoleId });
                        }
                    }
                    else
                    {
                        if (existingUserRole != null)
                        {
                            _context.UserRoles.Remove(existingUserRole);
                        }
                    }

                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.ApplicationUsers.Any(e => e.Id == id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }

            var roles = await _context.Roles.ToListAsync();
            ViewBag.Roles = new SelectList(roles, "Id", "Name", selectedRoleId);
            var branches = await _context.Branches.ToListAsync();
            ViewBag.Branches = new SelectList(branches, "Id", "Name", user.BranchId);

            return View(user);
        }

        [HttpPost]
        public async Task<IActionResult> ToggleStatus(string id)
        {
            var user = await _context.ApplicationUsers.FindAsync(id);
            if (user != null)
            {
                user.IsActive = !user.IsActive;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            var user = await _context.ApplicationUsers.FindAsync(id);
            if (user != null)
            {
                _context.ApplicationUsers.Remove(user);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }
    }
}