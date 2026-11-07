using System;
using System.Collections.Generic;

namespace EnterpriseSystem.Models;

public partial class SalesOrder
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public int BranchId { get; set; }

    public string UserId { get; set; } = null!;

    public DateTime OrderDate { get; set; }

    public string Status { get; set; } = "Pending";

    public virtual Branch Branch { get; set; } = null!;

    public virtual Customer Customer { get; set; } = null!;

    public virtual ICollection<SalesInvoice> SalesInvoices { get; set; } = new List<SalesInvoice>();

    public virtual ICollection<SalesOrderItem> SalesOrderItems { get; set; } = new List<SalesOrderItem>();

    public virtual ApplicationUser User { get; set; } = null!;
}
