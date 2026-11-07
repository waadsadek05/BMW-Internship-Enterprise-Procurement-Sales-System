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
    public class ProductsController : Controller
    {
        private readonly EnterpriseProcurementSalesDbContext _context;

        public ProductsController(EnterpriseProcurementSalesDbContext context)
        {
            _context = context;
        }

        // GET: Products
        public async Task<IActionResult> Index(string searchTerm, int page = 1)
        {
            int pageSize = 50;

            // الاستعلام مع عمل Include لـ Category لجلب بياناتها بأسلوب صحيح
            var query = _context.Products
                .Include(p => p.Category)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                string term = searchTerm.ToLower().Trim();

                // 1. جلب البيانات من قاعدة البيانات أولاً بالترتيب التنازلي
                var allData = await query.OrderByDescending(p => p.Id).ToListAsync();

                // 2. الفلترة في الذاكرة لتغطي الـ Name، Sku، UnitPrice، و Category بدقة
                var filteredData = allData.Where(p => 
                    p.Id.ToString() == term || 
                    p.Id.ToString().Contains(term) || 
                    (p.Name != null && p.Name.ToLower().Contains(term)) ||
                    (p.Sku != null && p.Sku.ToLower().Contains(term)) ||
                    p.UnitPrice.ToString().Contains(term) ||
                    (p.Category != null && p.Category.Name != null && p.Category.Name.ToLower().Contains(term))
                ).ToList();

                int totalItems = filteredData.Count;
                int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
                totalPages = totalPages > 0 ? totalPages : 1;
                page = Math.Max(1, Math.Min(page, totalPages));

                var products = filteredData
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.SearchTerm = searchTerm;
                ViewBag.CurrentFilter = searchTerm; // للحفاظ على توافق أي متغير قديم في الـ View

                // إرسال الـ Categories للـ ViewBag لتعمل داخل الـ Modal في صفحة الـ Index
                ViewData["CategoryId"] = new SelectList(_context.Categories, "Id", "Name");

                return View(products);
            }
            else
            {
                // الحالة العادية (بدون بحث)
                int totalItems = await query.CountAsync();
                int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
                totalPages = totalPages > 0 ? totalPages : 1;
                page = Math.Max(1, Math.Min(page, totalPages));

                var products = await query
                    .OrderByDescending(p => p.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .AsNoTracking()
                    .ToListAsync();

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.SearchTerm = searchTerm;
                ViewBag.CurrentFilter = searchTerm;

                // إرسال الـ Categories للـ ViewBag لتعمل داخل الـ Modal في صفحة الـ Index
                ViewData["CategoryId"] = new SelectList(_context.Categories, "Id", "Name");

                return View(products);
            }
        }

        // GET: Products/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var product = await _context.Products
                .Include(p => p.Category)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (product == null)
            {
                return NotFound();
            }

            return View(product);
        }

        // POST: Products/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Name,Sku,UnitPrice,CategoryId")] Product product)
        {
            if (ModelState.IsValid)
            {
                _context.Add(product);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewData["CategoryId"] = new SelectList(_context.Categories, "Id", "Name", product.CategoryId);
            return RedirectToAction(nameof(Index));
        }

        // GET: Products/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var product = await _context.Products.FindAsync(id);
            if (product == null)
            {
                return NotFound();
            }
            ViewData["CategoryId"] = new SelectList(_context.Categories, "Id", "Name", product.CategoryId);
            return View(product);
        }

        // POST: Products/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Sku,UnitPrice,CategoryId")] Product product)
        {
            if (id != product.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(product);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ProductExists(product.Id))
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
            ViewData["CategoryId"] = new SelectList(_context.Categories, "Id", "Name", product.CategoryId);
            return View(product);
        }

        // GET: Products/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var product = await _context.Products
                .Include(p => p.Category)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (product == null)
            {
                return NotFound();
            }

            return View(product);
        }

        // POST: Products/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product != null)
            {
                _context.Products.Remove(product);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool ProductExists(int id)
        {
            return _context.Products.Any(e => e.Id == id);
        }
    }
}