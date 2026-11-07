using System;
using System.Collections.Generic;

namespace EnterpriseSystem.Models;

public partial class PurchaseOrder
{
    public int Id { get; set; }

    public int VendorId { get; set; }

    public int BranchId { get; set; }

    public DateTime OrderDate { get; set; }

    // تأكد أيضاً إذا كانت Status أو OrderDate تحتاج قيم افتراضية في الكنترولر
    public string Status { get; set; } = null!;

    // تعديل هذين السطرين ليقبلا القيم الفارغة:
    public virtual Branch? Branch { get; set; }

    public virtual ICollection<PurchaseOrderItem> PurchaseOrderItems { get; set; } = new List<PurchaseOrderItem>();

    public virtual Vendor? Vendor { get; set; }

    public virtual ICollection<VendorInvoice> VendorInvoices { get; set; } = new List<VendorInvoice>();
}