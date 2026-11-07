using System;
using System.Collections.Generic;

namespace EnterpriseSystem.Models;

public partial class Warehouse
{
    public int Id { get; set; }

    public int BranchId { get; set; }

    public string Name { get; set; } = null!;

    public string? Location { get; set; }

    // اجعلها قابلة للقيم الفارغة هكذا:
    public virtual Branch? Branch { get; set; }

    public virtual ICollection<BranchInventory> BranchInventories { get; set; } = new List<BranchInventory>();
}