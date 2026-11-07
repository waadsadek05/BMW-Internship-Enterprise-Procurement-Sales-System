using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using EnterpriseSystem.Models;
using Microsoft.AspNetCore.Http; // أضفنا مكتبة الـ Session

namespace EnterpriseSystem.Controllers
{
    public class SalesOrdersController : Controller
    {
        private readonly EnterpriseProcurementSalesDbContext _context;

        public SalesOrdersController(EnterpriseProcurementSalesDbContext context)
        {
            _context = context;
        }

        // دالة مساعدة للتحقق من الصلاحيات (تدعم السيشن والـ Roles معاً)
        private bool HasPermission(string permissionType)
        {
            string currentUsername = HttpContext.Session.GetString("Username") ?? "";
            bool isSystemAdmin = currentUsername.Equals("admin", StringComparison.OrdinalIgnoreCase) || 
                                 User.IsInRole("Admin");

            if (permissionType == "Create")
            {
                return isSystemAdmin || 
                       currentUsername.Equals("sales_create", StringComparison.OrdinalIgnoreCase) || 
                       User.IsInRole("Sales_Creator") || 
                       User.IsInRole("sales_create") || 
                       User.IsInRole("Sales Creator");
            }
            else if (permissionType == "Approve")
            {
                return isSystemAdmin || 
                       currentUsername.Equals("sales_approve", StringComparison.OrdinalIgnoreCase) || 
                       User.IsInRole("Sales_Approver") || 
                       User.IsInRole("sales_approve") || 
                       User.IsInRole("Sales Approver");
            }

            return isSystemAdmin;
        }

        // GET: SalesOrders
        public async Task<IActionResult> Index(string searchTerm, int page = 1)
        {
            int pageSize = 50;

            // الاستعلام مع عمل Include للعلاقات المرتبطة
            var query = _context.SalesOrders
                .Include(s => s.Branch)
                .Include(s => s.Customer)
                .Include(s => s.User)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                string term = searchTerm.ToLower().Trim();

                // 1. جلب البيانات من قاعدة البيانات أولاً بالترتيب التنازلي
                var allData = await query.OrderByDescending(s => s.Id).ToListAsync();

                // 2. الفلترة في الذاكرة مع استخدام s.User.Username بالاسم الصحيح
                var filteredData = allData.Where(s => 
                    s.Id.ToString() == term || 
                    s.Id.ToString().Contains(term) || 
                    (s.Status != null && s.Status.ToLower().Contains(term)) ||
                    s.OrderDate.ToString("dd/MM/yyyy").Contains(term) ||
                    s.OrderDate.ToString("yyyy-MM-dd").Contains(term) ||
                    s.OrderDate.ToString().Contains(term) ||
                    (s.Branch != null && s.Branch.Name != null && s.Branch.Name.ToLower().Contains(term)) ||
                    (s.Customer != null && s.Customer.Name != null && s.Customer.Name.ToLower().Contains(term)) ||
                    (s.User != null && (
                        (s.User.Username != null && s.User.Username.ToLower().Contains(term)) ||
                        (s.User.FullName != null && s.User.FullName.ToLower().Contains(term)) ||
                        (s.User.Email != null && s.User.Email.ToLower().Contains(term))
                    ))
                ).ToList();

                int totalItems = filteredData.Count;
                int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
                totalPages = totalPages > 0 ? totalPages : 1;
                page = Math.Max(1, Math.Min(page, totalPages));

                var salesOrders = filteredData
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.SearchTerm = searchTerm;

                return View(salesOrders);
            }
            else
            {
                // الحالة العادية (بدون بحث)
                int totalItems = await query.CountAsync();
                int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
                totalPages = totalPages > 0 ? totalPages : 1;
                page = Math.Max(1, Math.Min(page, totalPages));

                var salesOrders = await query
                    .OrderByDescending(s => s.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.SearchTerm = searchTerm;

                return View(salesOrders);
            }
        }

        // GET: SalesOrders/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var salesOrder = await _context.SalesOrders
                .Include(s => s.Branch)
                .Include(s => s.Customer)
                .Include(s => s.User)
                .Include(s => s.SalesOrderItems)
                    .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (salesOrder == null)
            {
                return NotFound();
            }

            return View(salesOrder);
        }

        // POST: SalesOrders/UpdateStatus/5 (Approve/Complete or Cancel)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus([FromForm] int id, [FromForm] string status)
        {
            if (!HasPermission("Approve"))
            {
                return Unauthorized("You do not have permission to approve or complete sales orders.");
            }

            var salesOrder = await _context.SalesOrders
                .Include(s => s.SalesOrderItems)
                .ThenInclude(i => i.Product)
                .Include(s => s.Branch)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (salesOrder == null)
            {
                return NotFound();
            }

            if (salesOrder.Status == "Completed" && status == "Completed")
            {
                return RedirectToAction(nameof(Details), new { id = salesOrder.Id });
            }

            salesOrder.Status = status;

            if (status == "Completed")
{
    foreach (var item in salesOrder.SalesOrderItems)
    {
        // البحث عن سجل المخزون بدقة للمنتج والمستودع التابع للفرع
        var inventory = await _context.BranchInventories
            .Include(bi => bi.Warehouse)
            .FirstOrDefaultAsync(bi => bi.ProductId == item.ProductId && bi.Warehouse.BranchId == salesOrder.BranchId);

        if (inventory != null)
        {
            if (inventory.StockQuantity >= item.Quantity)
            {
                inventory.StockQuantity -= item.Quantity;
            }
            else
            {
                inventory.StockQuantity = 0; // أو إيقاف العملية لو المخزون لا يكفي
            }
            
            _context.BranchInventories.Update(inventory);
        }
    }

    bool invoiceExists = await _context.SalesInvoices.AnyAsync(inv => inv.SalesOrderId == salesOrder.Id);
    
    if (!invoiceExists)
    {
        decimal totalAmount = salesOrder.SalesOrderItems != null 
            ? salesOrder.SalesOrderItems.Sum(item => item.Quantity * item.UnitPrice) 
            : 0;

        string invoiceNumber = "INV-" + DateTime.Now.ToString("yyyyMMdd") + "-" + salesOrder.Id;

        var invoice = new SalesInvoice
        {
            InvoiceNumber = invoiceNumber,
            InvoiceDate = DateTime.Now,
            TotalAmount = totalAmount,
            SalesOrderId = salesOrder.Id
        };

        _context.SalesInvoices.Add(invoice);
    }
}

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Unable to save changes: " + ex.Message);
                return View("Details", salesOrder);
            }

            return RedirectToAction(nameof(Details), new { id = salesOrder.Id });
        }

        [HttpGet]
        public async Task<IActionResult> GetStockQuantity(int productId, int branchId)
        {
            var stock = await _context.BranchInventories
                .Where(bi => bi.ProductId == productId && bi.Warehouse.BranchId == branchId)
                .SumAsync(bi => (int?)bi.StockQuantity) ?? 0;

            return Json(new { stock = stock });
        }

        // دالة جديدة لجلب منتجات الفرع المختار فقط لتفادي اختيار منتجات غير موجودة
        [HttpGet]
public async Task<IActionResult> GetProductsByBranch(int branchId)
{
    var inventories = await _context.BranchInventories
        .Include(bi => bi.Product)
        .Include(bi => bi.Warehouse)
        .Where(bi => bi.Warehouse.BranchId == branchId)
        .Select(bi => new {
            id = bi.Product.Id,
            name = bi.Product.Name + " (" + bi.Warehouse.Name + ")", // دمج اسم المنتج مع المخزن لتمييزه بوضوح
            productId = bi.Product.Id,
            sellingPrice = bi.Product.UnitPrice,
            stock = bi.StockQuantity,
            warehouseName = bi.Warehouse.Name
        })
        .ToListAsync();

    return Json(inventories);
}

        // GET: SalesOrders/Create
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            if (!HasPermission("Create"))
            {
                return Unauthorized("You do not have permission to create sales orders.");
            }

            ViewData["CustomerId"] = new SelectList(_context.Customers, "Id", "Name");
            ViewData["BranchId"] = new SelectList(_context.Branches, "Id", "Name");
            ViewData["UserId"] = new SelectList(_context.ApplicationUsers, "Id", "FullName");

            // مبدئياً نعرض قائمة فارغة أو كل المنتجات لحين اختيار الفرع
            ViewBag.Products = new List<object>();
            return View();
        }

        // POST: SalesOrders/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SalesOrder salesOrder, List<SalesOrderItem> OrderItems)
        {
            if (!HasPermission("Create"))
            {
                return Unauthorized("You do not have permission to create sales orders.");
            }

            if (salesOrder.OrderDate == default)
            {
                salesOrder.OrderDate = DateTime.Now;
            }
            
            if (string.IsNullOrWhiteSpace(salesOrder.Status))
            {
                salesOrder.Status = "Pending";
            }

            ModelState.Clear();

            if (salesOrder.CustomerId == 0 || salesOrder.BranchId == 0)
            {
                ModelState.AddModelError("", "Please select a valid Customer and Branch.");
            }

            if (OrderItems == null || OrderItems.Count == 0 || OrderItems.All(i => i.ProductId == 0))
            {
                ModelState.AddModelError("", "Please add at least one valid product.");
            }

            // التحقق من توافر المنتجات في مخزون الفرع المختار قبل إتمام الحفظ
            if (OrderItems != null && salesOrder.BranchId > 0)
            {
                foreach (var item in OrderItems.Where(i => i.ProductId > 0))
                {
                    var availableStock = await _context.BranchInventories
                        .Where(bi => bi.ProductId == item.ProductId && bi.Warehouse.BranchId == salesOrder.BranchId)
                        .SumAsync(bi => (int?)bi.StockQuantity) ?? 0;

                    if (availableStock < item.Quantity)
                    {
                        var prod = await _context.Products.FindAsync(item.ProductId);
                        string prodName = prod != null ? prod.Name : "Product ID " + item.ProductId;
                        ModelState.AddModelError("", $"The product '{prodName}' is not available in sufficient quantity in the selected branch (Available: {availableStock}).");
                    }
                }
            }

            if (ModelState.ErrorCount == 0)
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    _context.Add(salesOrder);
                    await _context.SaveChangesAsync();

                    var validItems = OrderItems.Where(i => i.ProductId > 0 && i.Quantity > 0).ToList();
                    foreach (var item in validItems)
                    {
                        item.SalesOrderId = salesOrder.Id;
                        _context.SalesOrderItems.Add(item);
                    }
                    await _context.SaveChangesAsync();

                    decimal totalAmount = validItems.Sum(item => item.Quantity * item.UnitPrice);
                    string invoiceNumber = "INV-" + DateTime.Now.ToString("yyyyMMdd") + "-" + salesOrder.Id;

                    var invoice = new SalesInvoice
                    {
                        InvoiceNumber = invoiceNumber,
                        InvoiceDate = DateTime.Now,
                        TotalAmount = totalAmount,
                        SalesOrderId = salesOrder.Id
                    };

                    _context.SalesInvoices.Add(invoice);
                    await _context.SaveChangesAsync();

                    foreach (var item in validItems)
                    {
                        var invoiceItem = new InvoiceItem
                        {
                            SalesInvoiceId = invoice.Id,
                            ProductId = item.ProductId,
                            Quantity = item.Quantity,
                            UnitPrice = item.UnitPrice
                        };
                        _context.InvoiceItems.Add(invoiceItem);
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    ModelState.AddModelError("", "Database Error: " + ex.Message);
                }
            }

            PopulateDropdowns(salesOrder);
            ViewBag.Products = _context.Products.Select(p => new { p.Id, p.Name, SellingPrice = p.UnitPrice }).ToList();
            return View(salesOrder);
        }

        // GET: SalesOrders/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (!HasPermission("Create"))
            {
                return Unauthorized("You do not have permission to edit sales orders.");
            }

            if (id == null)
            {
                return NotFound();
            }

            var salesOrder = await _context.SalesOrders.FindAsync(id);
            if (salesOrder == null)
            {
                return NotFound();
            }

            PopulateDropdowns(salesOrder);
            return View(salesOrder);
        }

        // POST: SalesOrders/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,CustomerId,BranchId,UserId,OrderDate,Status")] SalesOrder salesOrder)
        {
            if (!HasPermission("Create"))
            {
                return Unauthorized("You do not have permission to edit sales orders.");
            }

            if (id != salesOrder.Id)
            {
                return NotFound();
            }

            ModelState.Remove("Branch");
            ModelState.Remove("Customer");
            ModelState.Remove("User");

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(salesOrder);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!SalesOrderExists(salesOrder.Id))
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

            PopulateDropdowns(salesOrder);
            return View(salesOrder);
        }

        // GET: SalesOrders/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (!HasPermission("Create"))
            {
                return Unauthorized("You do not have permission to delete sales orders.");
            }

            if (id == null)
            {
                return NotFound();
            }

            var salesOrder = await _context.SalesOrders
                .Include(s => s.Branch)
                .Include(s => s.Customer)
                .Include(s => s.User)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (salesOrder == null)
            {
                return NotFound();
            }

            return View(salesOrder);
        }

        // POST: SalesOrders/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (!HasPermission("Create"))
            {
                return Unauthorized("You do not have permission to delete sales orders.");
            }

            var salesOrder = await _context.SalesOrders
                .Include(s => s.Branch)
                .Include(s => s.Customer)
                .Include(s => s.User)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (salesOrder == null)
            {
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.SalesOrders.Remove(salesOrder);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            catch (Exception)
            {
                ModelState.AddModelError("", "Cannot delete this Sales Order because it is linked to an existing Sales Invoice. Please delete the invoice first.");
                return View(salesOrder);
            }
        }

        private bool SalesOrderExists(int id)
        {
            return _context.SalesOrders.Any(e => e.Id == id);
        }

        private void PopulateDropdowns(SalesOrder salesOrder = null)
        {
            var branches = _context.Branches != null ? _context.Branches.ToList() : new List<Branch>();
            var customers = _context.Customers != null ? _context.Customers.ToList() : new List<Customer>();
            var users = _context.ApplicationUsers != null ? _context.ApplicationUsers.ToList() : new List<ApplicationUser>();

            ViewData["BranchId"] = new SelectList(branches, "Id", "Name", salesOrder?.BranchId);
            ViewData["CustomerId"] = new SelectList(customers, "Id", "Name", salesOrder?.CustomerId);
            ViewData["UserId"] = new SelectList(users, "Id", "FullName", salesOrder?.UserId);
        }
    }
}