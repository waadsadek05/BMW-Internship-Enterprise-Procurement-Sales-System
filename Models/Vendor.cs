using System;
using System.Collections.Generic;

namespace EnterpriseSystem.Models;

public partial class Vendor
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string? ContactInfo { get; set; }

    public virtual ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
}
