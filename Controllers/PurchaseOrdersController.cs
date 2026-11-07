using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using EnterpriseSystem.Models;

namespace EnterpriseSystem.Controllers
{
    [Authorize] // السماح لأي مستخدم مسجل دخول بالدخول للكونترولر كبداية
    public class PurchaseOrdersController : Controller
    {
        private readonly EnterpriseProcurementSalesDbContext _context;

        public PurchaseOrdersController(EnterpriseProcurementSalesDbContext context)
        {
            _context = context;
        }

        // دالة مساعدة للتحقق من صلاحيات الأدمن أو المنشئ عبر الـ Roles
        private bool CanCreateOrEdit()
        {
            return User.IsInRole("Admin") || 
                   User.IsInRole("PO_Creator") || 
                   User.IsInRole("po_create") || 
                   User.IsInRole("PO Creator");
        }

        // دالة مساعدة للتحقق من صلاحيات الاعتماد عبر الـ Roles
        private bool CanApprove()
        {
            return User.IsInRole("Admin") || 
                   User.IsInRole("PO_Approver") || 
                   User.IsInRole("po_approve") || 
                   User.IsInRole("PO Approver");
        }

        // GET: PurchaseOrders
        public async Task<IActionResult> Index(string searchTerm, int page = 1, int pageSize = 50)
        {
            var query = _context.PurchaseOrders
                .Include(p => p.Branch)
                .Include(p => p.Vendor)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                string term = searchTerm.ToLower().Trim();

                // 1. أولاً نجلب كل البيانات المطابقة للأعمدة النصية والرقمية من قاعدة البيانات
                var allData = await query.OrderByDescending(p => p.Id).ToListAsync();

                // 2. نقوم بعملية التصفية في الذاكرة (In-Memory Filtering) لضمان دعم البحث بالتاريخ بكل سهولة
                var filteredData = allData.Where(p => 
                    p.Id.ToString() == term || 
                    p.Id.ToString().Contains(term) || 
                    (p.Status != null && p.Status.ToLower().Contains(term)) ||
                    (p.Branch != null && p.Branch.Name != null && p.Branch.Name.ToLower().Contains(term)) || 
                    (p.Vendor != null && p.Vendor.Name != null && p.Vendor.Name.ToLower().Contains(term)) ||
                    // البحث بأي جزء من التاريخ (سواء اليوم أو الشهر أو السنة أو التاريخ كاملاً)
                    p.OrderDate.ToString("dd/MM/yyyy").Contains(term) ||
                    p.OrderDate.ToString("yyyy-MM-dd").Contains(term) ||
                    p.OrderDate.ToString().Contains(term)
                ).ToList();

                // 3. تطبيق الـ Pagination يدوياً على القائمة المصفاة
                var totalRecords = filteredData.Count;
                int skip = (page - 1) * pageSize;
                var purchaseOrders = filteredData.Skip(skip).Take(pageSize).ToList();

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalRecords > 0 ? (int)Math.Ceiling((double)totalRecords / pageSize) : 1;
                ViewBag.SearchTerm = searchTerm;

                return View(purchaseOrders);
            }
            else
            {
                // لو مفيش بحث، يشتغل بالشكل العادي والسريع جداً من قاعدة البيانات مباشرة
                var totalRecords = await query.CountAsync();
                int skip = (page - 1) * pageSize;
                var purchaseOrders = await query
                    .OrderByDescending(p => p.Id)
                    .Skip(skip)
                    .Take(pageSize)
                    .ToListAsync();

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalRecords > 0 ? (int)Math.Ceiling((double)totalRecords / pageSize) : 1;
                ViewBag.SearchTerm = searchTerm;

                return View(purchaseOrders);
            }
        }

        // GET: PurchaseOrders/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var purchaseOrder = await _context.PurchaseOrders
                .Include(p => p.Branch)
                .Include(p => p.Vendor)
                .Include(p => p.PurchaseOrderItems)
                    .ThenInclude(pi => pi.Product)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (purchaseOrder == null)
            {
                return NotFound();
            }

            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Name");

            return View(purchaseOrder);
        }

        // GET: PurchaseOrders/Create
        public IActionResult Create()
        {
            if (!CanCreateOrEdit())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            ViewData["BranchId"] = new SelectList(_context.Branches, "Id", "Name");
            ViewData["VendorId"] = new SelectList(_context.Vendors, "Id", "Name");
            return View();
        }

        // POST: PurchaseOrders/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("VendorId,BranchId")] PurchaseOrder purchaseOrder)
        {
            if (!CanCreateOrEdit())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            purchaseOrder.OrderDate = DateTime.Now;
            purchaseOrder.Status = "Pending";

            ModelState.Remove(nameof(purchaseOrder.Status));
            ModelState.Remove(nameof(purchaseOrder.OrderDate));

            if (ModelState.IsValid)
            {
                _context.Add(purchaseOrder);
                await _context.SaveChangesAsync();
               
                return RedirectToAction(nameof(Details), new { id = purchaseOrder.Id });
            }

            ViewData["BranchId"] = new SelectList(_context.Branches, "Id", "Name", purchaseOrder.BranchId);
            ViewData["VendorId"] = new SelectList(_context.Vendors, "Id", "Name", purchaseOrder.VendorId);
            return View(purchaseOrder);
        }

        // POST: PurchaseOrders/AddExistingProductToOrder
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddExistingProductToOrder(int purchaseOrderId, int productId, int quantity, decimal unitCost)
        {
            if (!CanCreateOrEdit())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var purchaseOrder = await _context.PurchaseOrders.FindAsync(purchaseOrderId);
            if (purchaseOrder == null)
            {
                return NotFound();
            }

            var orderItem = new PurchaseOrderItem
            {
                PurchaseOrderId = purchaseOrderId,
                ProductId = productId,
                Quantity = quantity,
                UnitCost = unitCost
            };

            _context.PurchaseOrderItems.Add(orderItem);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id = purchaseOrderId });
        }

        // POST: PurchaseOrders/EditOrderItem
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditOrderItem(int id, int purchaseOrderId, int quantity, decimal unitCost)
        {
            if (!CanCreateOrEdit())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var orderItem = await _context.PurchaseOrderItems.FindAsync(id);
            if (orderItem == null)
            {
                return NotFound();
            }

            orderItem.Quantity = quantity;
            orderItem.UnitCost = unitCost;

            _context.Update(orderItem);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id = purchaseOrderId });
        }

        // POST: PurchaseOrders/DeleteOrderItem/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteOrderItem(int id)
        {
            if (!CanCreateOrEdit())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var orderItem = await _context.PurchaseOrderItems.FirstOrDefaultAsync(pi => pi.Id == id);
            if (orderItem == null)
            {
                return NotFound();
            }

            int purchaseOrderId = orderItem.PurchaseOrderId;

            _context.PurchaseOrderItems.Remove(orderItem);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id = purchaseOrderId });
        }

        // POST: PurchaseOrders/CompleteOrder (الموافقة والاعتماد)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteOrder(int id)
        {
            if (!CanApprove())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var purchaseOrder = await _context.PurchaseOrders
                .Include(p => p.Branch)
                .Include(p => p.PurchaseOrderItems)
                    .ThenInclude(pi => pi.Product)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (purchaseOrder == null)
            {
                return NotFound();
            }

            purchaseOrder.Status = "Completed";

            decimal totalAmount = purchaseOrder.PurchaseOrderItems.Sum(item => item.Quantity * item.UnitCost);

            var lastInvoice = await _context.VendorInvoices
                .OrderByDescending(i => i.Id)
                .FirstOrDefaultAsync();

            int nextId = lastInvoice != null ? lastInvoice.Id + 1 : 1;
            string generatedInvoiceNumber = $"INV-{nextId:D3}";

            var vendorInvoice = new VendorInvoice
            {
                InvoiceNumber = generatedInvoiceNumber,
                InvoiceDate = DateTime.Now,
                TotalAmount = totalAmount,
                Status = "Pending", 
                PurchaseOrderId = purchaseOrder.Id
            };

            _context.VendorInvoices.Add(vendorInvoice);

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Details), new { id = purchaseOrder.Id });
        }

        // GET: PurchaseOrders/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (!CanCreateOrEdit())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            if (id == null)
            {
                return NotFound();
            }

            var purchaseOrder = await _context.PurchaseOrders.FindAsync(id);
            if (purchaseOrder == null)
            {
                return NotFound();
            }
            ViewData["BranchId"] = new SelectList(_context.Branches, "Id", "Name", purchaseOrder.BranchId);
            ViewData["VendorId"] = new SelectList(_context.Vendors, "Id", "Name", purchaseOrder.VendorId);
            return View(purchaseOrder);
        }

        // POST: PurchaseOrders/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,VendorId,BranchId,OrderDate,Status")] PurchaseOrder purchaseOrder)
        {
            if (!CanCreateOrEdit())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            if (id != purchaseOrder.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(purchaseOrder);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!PurchaseOrderExists(purchaseOrder.Id))
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
            ViewData["BranchId"] = new SelectList(_context.Branches, "Id", "Name", purchaseOrder.BranchId);
            ViewData["VendorId"] = new SelectList(_context.Vendors, "Id", "Name", purchaseOrder.VendorId);
            return View(purchaseOrder);
        }

        // GET: PurchaseOrders/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (!CanCreateOrEdit())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            if (id == null)
            {
                return NotFound();
            }

            var purchaseOrder = await _context.PurchaseOrders
                .Include(p => p.Branch)
                .Include(p => p.Vendor)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (purchaseOrder == null)
            {
                return NotFound();
            }

            return View(purchaseOrder);
        }

        // POST: PurchaseOrders/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (!CanCreateOrEdit())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            var purchaseOrder = await _context.PurchaseOrders.FindAsync(id);
            if (purchaseOrder != null)
            {
                _context.PurchaseOrders.Remove(purchaseOrder);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool PurchaseOrderExists(int id)
        {
            return _context.PurchaseOrders.Any(e => e.Id == id);
        }
    }
}