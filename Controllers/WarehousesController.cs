using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using EnterpriseSystem.Models;

namespace EnterpriseSystem.Controllers
{
    public class WarehousesController : Controller
    {
        private readonly EnterpriseProcurementSalesDbContext _context;

        public WarehousesController(EnterpriseProcurementSalesDbContext context)
        {
            _context = context;
        }

        // GET: Warehouses
        public async Task<IActionResult> Index(string searchTerm, int page = 1)
        {
            int pageSize = 50;

            // الاستعلام مع عمل Include لـ Branch لجلب بياناته بأسلوب صحيح
            var query = _context.Warehouses
                .Include(w => w.Branch)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                string term = searchTerm.ToLower().Trim();

                // 1. جلب البيانات من قاعدة البيانات أولاً بالترتيب التنازلي
                var allData = await query.OrderByDescending(w => w.Id).ToListAsync();

                // 2. الفلترة في الذاكرة لتغطي الـ Id، Name، Location، والـ Branch بدقة
                var filteredData = allData.Where(w => 
                    w.Id.ToString() == term || 
                    w.Id.ToString().Contains(term) || 
                    (w.Name != null && w.Name.ToLower().Contains(term)) ||
                    (w.Location != null && w.Location.ToLower().Contains(term)) ||
                    (w.Branch != null && w.Branch.Name != null && w.Branch.Name.ToLower().Contains(term))
                ).ToList();

                int totalItems = filteredData.Count;
                int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
                totalPages = totalPages > 0 ? totalPages : 1;
                page = Math.Max(1, Math.Min(page, totalPages));

                var warehouses = filteredData
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.SearchTerm = searchTerm;

                return View(warehouses);
            }
            else
            {
                // الحالة العادية (بدون بحث)
                int totalItems = await query.CountAsync();
                int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
                totalPages = totalPages > 0 ? totalPages : 1;
                page = Math.Max(1, Math.Min(page, totalPages));

                var warehouses = await query
                    .OrderByDescending(w => w.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.SearchTerm = searchTerm;

                return View(warehouses);
            }
        }

        // GET: Warehouses/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var warehouse = await _context.Warehouses
                .Include(w => w.Branch)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (warehouse == null)
            {
                return NotFound();
            }

            return View(warehouse);
        }

        // GET: Warehouses/Create
        // GET: Warehouses/Create
        public async Task<IActionResult> Create()
        {
            Console.WriteLine(">>> GET: Create action called."); // اطبع هذه لتتأكد من فتح الصفحة
            ViewBag.Branches = await _context.Branches.ToListAsync(); 
            return View();
        }

        // POST: Warehouses/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,BranchId,Name,Location")] Warehouse warehouse)
        {
            Console.WriteLine(">>> POST: Create action was triggered!"); // اطبع هذه لنعرف هل الزر أرسل طلب أم لا

            if (ModelState.IsValid)
            {
                Console.WriteLine(">>> ModelState is VALID. Saving to database...");
                _context.Add(warehouse);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            else
            {
                Console.WriteLine(">>> ModelState is INVALID! Errors:");
                foreach (var error in ModelState.Values.SelectMany(v => v.Errors))
                {
                    Console.WriteLine($" - {error.ErrorMessage}");
                }
            }

            ViewBag.Branches = await _context.Branches.ToListAsync();
            return View(warehouse);
        }

        // GET: Warehouses/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var warehouse = await _context.Warehouses.FindAsync(id);
            if (warehouse == null)
            {
                return NotFound();
            }
            ViewData["BranchId"] = new SelectList(_context.Branches, "Id", "Id", warehouse.BranchId);
            return View(warehouse);
        }

        // POST: Warehouses/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,BranchId,Name,Location")] Warehouse warehouse)
        {
            if (id != warehouse.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(warehouse);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!WarehouseExists(warehouse.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            ViewData["BranchId"] = new SelectList(_context.Branches, "Id", "Id", warehouse.BranchId);
            return View(warehouse);
        }

        // GET: Warehouses/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var warehouse = await _context.Warehouses
                .Include(w => w.Branch)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (warehouse == null)
            {
                return NotFound();
            }

            return View(warehouse);
        }

        // POST: Warehouses/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var warehouse = await _context.Warehouses.FindAsync(id);
            if (warehouse != null)
            {
                _context.Warehouses.Remove(warehouse);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool WarehouseExists(int id)
        {
            return _context.Warehouses.Any(e => e.Id == id);
        }
    }
}
