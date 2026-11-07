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
    public class SalesInvoicesController : Controller
    {
        private readonly EnterpriseProcurementSalesDbContext _context;

        public SalesInvoicesController(EnterpriseProcurementSalesDbContext context)
        {
            _context = context;
        }

        // GET: SalesInvoices
        public async Task<IActionResult> Index(string searchTerm, int page = 1)
        {
            int pageSize = 50;

            // الاستعلام مع عمل Include لـ SalesOrder لجلب بياناته بأسلوب صحيح
            var query = _context.SalesInvoices
                .Include(s => s.SalesOrder)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                string term = searchTerm.ToLower().Trim();

                // 1. جلب البيانات من قاعدة البيانات أولاً بالترتيب التنازلي
                var allData = await query.OrderByDescending(s => s.Id).ToListAsync();

                // 2. الفلترة في الذاكرة لتغطي جميع الأعمدة والتواريخ وعلاقة SalesOrder بدقة
                var filteredData = allData.Where(s => 
                    s.Id.ToString() == term || 
                    s.Id.ToString().Contains(term) || 
                    (s.InvoiceNumber != null && s.InvoiceNumber.ToLower().Contains(term)) ||
                    s.TotalAmount.ToString().Contains(term) ||
                    s.InvoiceDate.ToString("dd/MM/yyyy").Contains(term) ||
                    s.InvoiceDate.ToString("yyyy-MM-dd").Contains(term) ||
                    s.InvoiceDate.ToString().Contains(term) ||
                    (s.SalesOrder != null && s.SalesOrder.Id.ToString().Contains(term))
                ).ToList();

                int totalItems = filteredData.Count;
                int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
                totalPages = totalPages > 0 ? totalPages : 1;
                page = Math.Max(1, Math.Min(page, totalPages));

                var salesInvoices = filteredData
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.SearchTerm = searchTerm;

                return View(salesInvoices);
            }
            else
            {
                // الحالة العادية (بدون بحث)
                int totalItems = await query.CountAsync();
                int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
                totalPages = totalPages > 0 ? totalPages : 1;
                page = Math.Max(1, Math.Min(page, totalPages));

                var salesInvoices = await query
                    .OrderByDescending(s => s.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.SearchTerm = searchTerm;

                return View(salesInvoices);
            }
        }

        // GET: SalesInvoices/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            // جلب الفاتورة مع الأوردر، الفرع، العميل، المستخدم، وعناصر المنتجات بالتفصيل
            var salesInvoice = await _context.SalesInvoices
                .Include(s => s.SalesOrder)
                    .ThenInclude(o => o.Branch)
                .Include(s => s.SalesOrder)
                    .ThenInclude(o => o.Customer)
                .Include(s => s.SalesOrder)
                    .ThenInclude(o => o.User)
                .Include(s => s.SalesOrder)
                    .ThenInclude(o => o.SalesOrderItems)
                        .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (salesInvoice == null)
            {
                return NotFound();
            }

            return View(salesInvoice);
        }

        // GET: SalesInvoices/Create
        public IActionResult Create()
        {
            ViewData["SalesOrderId"] = new SelectList(_context.SalesOrders, "Id", "Id");
            return View();
        }

        // POST: SalesInvoices/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,SalesOrderId,TotalAmount")] SalesInvoice salesInvoice)
        {
            int nextId = _context.SalesInvoices.Count() + 1;
            salesInvoice.InvoiceNumber = $"INV-{DateTime.Now.Year}-{nextId:D3}";

            ModelState.Remove("InvoiceNumber");
            ModelState.Remove("SalesOrder");
            ModelState.Remove("SalesInvoiceItems");

            if (ModelState.IsValid)
            {
                _context.Add(salesInvoice);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            ViewData["SalesOrderId"] = new SelectList(_context.SalesOrders, "Id", "Id", salesInvoice.SalesOrderId);
            return View(salesInvoice);
        }

        // GET: SalesInvoices/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var salesInvoice = await _context.SalesInvoices.FindAsync(id);
            if (salesInvoice == null)
            {
                return NotFound();
            }
            ViewData["SalesOrderId"] = new SelectList(_context.SalesOrders, "Id", "Id", salesInvoice.SalesOrderId);
            return View(salesInvoice);
        }

        // POST: SalesInvoices/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,SalesOrderId,InvoiceNumber,InvoiceDate,TotalAmount")] SalesInvoice salesInvoice)
        {
            if (id != salesInvoice.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(salesInvoice);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!SalesInvoiceExists(salesInvoice.Id))
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
            ViewData["SalesOrderId"] = new SelectList(_context.SalesOrders, "Id", "Id", salesInvoice.SalesOrderId);
            return View(salesInvoice);
        }

        // GET: SalesInvoices/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var salesInvoice = await _context.SalesInvoices
                .Include(s => s.SalesOrder)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (salesInvoice == null)
            {
                return NotFound();
            }

            return View(salesInvoice);
        }

        // POST: SalesInvoices/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var salesInvoice = await _context.SalesInvoices.FindAsync(id);
            if (salesInvoice != null)
            {
                _context.SalesInvoices.Remove(salesInvoice);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool SalesInvoiceExists(int id)
        {
            return _context.SalesInvoices.Any(e => e.Id == id);
        }
    }
}