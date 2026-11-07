using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EnterpriseSystem.Models;
using System.Threading.Tasks;
using System.Linq;

namespace EnterpriseSystem.Controllers
{
    public class HomeController : Controller
    {
        private readonly EnterpriseProcurementSalesDbContext _context;

        public HomeController(EnterpriseProcurementSalesDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            // لو اليوزر Admin، يفتح له صفحة الهوم الرئيسية (الأدمن)
            if (User.IsInRole("Admin"))
            {
                return View(); // بتفتح Index.cshtml الخاصة بالأدمن
            }

            // لو أي رول تاني (مثل PO_Approver أو غيره)، يروح على صفحة Welcome
            return RedirectToAction("Welcome");
        }

        public async Task<IActionResult> Approvals()
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("PO_Approver") && !User.IsInRole("Sales_Approver"))
            {
                return RedirectToAction("Welcome");
            }

            // 1. جلب الـ POs المعلقة (تظهر للأدمن ومسؤول الـ PO فقط)
            if (User.IsInRole("Admin") || User.IsInRole("PO_Approver"))
            {
                ViewBag.PendingPOs = await _context.PurchaseOrders
                    .Where(p => p.Status == "Pending")
                    .ToListAsync();

                ViewBag.PendingVendorInvoices = await _context.VendorInvoices
                    .Include(v => v.PurchaseOrder)
                    .Where(v => v.Status == "Pending")
                    .ToListAsync();
            }
            else
            {
                ViewBag.PendingPOs = new List<PurchaseOrder>();
                ViewBag.PendingVendorInvoices = new List<VendorInvoice>();
            }

            // 2. جلب الـ SOs المعلقة (تظهر للأدمن ومسؤول الـ SO فقط)
            if (User.IsInRole("Admin") || User.IsInRole("Sales_Approver"))
            {
                ViewBag.PendingSOs = await _context.SalesOrders
                    .Where(s => s.Status == "Pending" || s.Status == "Processing")
                    .ToListAsync();
            }
            else
            {
                ViewBag.PendingSOs = new List<SalesOrder>();
            }

            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult Welcome()
        {
            return View();
        }
    }
}