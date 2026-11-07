using System;
using System.Collections.Generic;

namespace EnterpriseSystem.Models;

public partial class BranchInventory
{
    public int Id { get; set; }

    public int WarehouseId { get; set; }

    public int ProductId { get; set; }

    public int StockQuantity { get; set; }

    // قم بتعديل هذين السطرين فقط:
    public virtual Product? Product { get; set; }

    public virtual Warehouse? Warehouse { get; set; }
}