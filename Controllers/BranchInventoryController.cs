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
    public class BranchInventoryController : Controller
    {
        private readonly EnterpriseProcurementSalesDbContext _context;

        public BranchInventoryController(EnterpriseProcurementSalesDbContext context)
        {
            _context = context;
        }

        // GET: BranchInventory
        public async Task<IActionResult> Index(string searchTerm, int page = 1)
        {
            int pageSize = 50;

            // الاستعلام مع عمل Include لـ Product و Warehouse لجلب بياناتهم بأسلوب صحيح
            var query = _context.BranchInventories
                .Include(b => b.Product)
                .Include(b => b.Warehouse)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                string term = searchTerm.ToLower().Trim();

                // 1. جلب البيانات من قاعدة البيانات أولاً بالترتيب التنازلي
                var allData = await query.OrderByDescending(b => b.Id).ToListAsync();

                // 2. الفلترة في الذاكرة لتغطي الـ Id، StockQuantity، وأسماء الـ Product والـ Warehouse بدقة
                var filteredData = allData.Where(b => 
                    b.Id.ToString() == term || 
                    b.Id.ToString().Contains(term) || 
                    b.StockQuantity.ToString().Contains(term) ||
                    (b.Product != null && b.Product.Name != null && b.Product.Name.ToLower().Contains(term)) ||
                    (b.Warehouse != null && b.Warehouse.Name != null && b.Warehouse.Name.ToLower().Contains(term))
                ).ToList();

                int totalItems = filteredData.Count;
                int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
                totalPages = totalPages > 0 ? totalPages : 1;
                page = Math.Max(1, Math.Min(page, totalPages));

                var branchInventories = filteredData
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.SearchTerm = searchTerm;

                return View(branchInventories);
            }
            else
            {
                // الحالة العادية (بدون بحث)
                int totalItems = await query.CountAsync();
                int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
                totalPages = totalPages > 0 ? totalPages : 1;
                page = Math.Max(1, Math.Min(page, totalPages));

                var branchInventories = await query
                    .OrderByDescending(b => b.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.SearchTerm = searchTerm;

                return View(branchInventories);
            }
        }

        // GET: BranchInventory/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var branchInventory = await _context.BranchInventories
                .Include(b => b.Product)
                .Include(b => b.Warehouse)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (branchInventory == null)
            {
                return NotFound();
            }

            return View(branchInventory);
        }

        // GET: BranchInventories/Create
        public async Task<IActionResult> Create()
        {
            ViewData["WarehouseId"] = new SelectList(await _context.Warehouses.ToListAsync(), "Id", "Name");
            ViewData["ProductId"] = new SelectList(await _context.Products.ToListAsync(), "Id", "Name");
            return View();
        }

        // POST: BranchInventories/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,WarehouseId,ProductId,StockQuantity")] BranchInventory branchInventory)
        {
            if (ModelState.IsValid)
            {
                _context.Add(branchInventory);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewData["WarehouseId"] = new SelectList(_context.Warehouses, "Id", "Name", branchInventory.WarehouseId);
            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Name", branchInventory.ProductId);
            return View(branchInventory);
        }

        // GET: BranchInventory/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var branchInventory = await _context.BranchInventories.FindAsync(id);
            if (branchInventory == null)
            {
                return NotFound();
            }
            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Id", branchInventory.ProductId);
            ViewData["WarehouseId"] = new SelectList(_context.Warehouses, "Id", "Id", branchInventory.WarehouseId);
            return View(branchInventory);
        }

        // POST: BranchInventory/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,WarehouseId,ProductId,StockQuantity")] BranchInventory branchInventory)
        {
            if (id != branchInventory.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(branchInventory);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!BranchInventoryExists(branchInventory.Id))
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
            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Id", branchInventory.ProductId);
            ViewData["WarehouseId"] = new SelectList(_context.Warehouses, "Id", "Id", branchInventory.WarehouseId);
            return View(branchInventory);
        }

        // GET: BranchInventory/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var branchInventory = await _context.BranchInventories
                .Include(b => b.Product)
                .Include(b => b.Warehouse)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (branchInventory == null)
            {
                return NotFound();
            }

            return View(branchInventory);
        }

        // POST: BranchInventory/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var branchInventory = await _context.BranchInventories.FindAsync(id);
            if (branchInventory != null)
            {
                _context.BranchInventories.Remove(branchInventory);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool BranchInventoryExists(int id)
        {
            return _context.BranchInventories.Any(e => e.Id == id);
        }
    }
}
