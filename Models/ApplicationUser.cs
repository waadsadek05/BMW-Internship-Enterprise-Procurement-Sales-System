using System;
using System.Collections.Generic;

namespace EnterpriseSystem.Models;

public partial class ApplicationUser
{
    public string Id { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public string Email { get; set; } = null!;

    // تأكدي من كتابة هذين السطرين وحفظ الملف بـ Ctrl + S
    public string? Username { get; set; }
    public string? Password { get; set; }

    public bool IsActive { get; set; } = true;

    public int? BranchId { get; set; }

    public virtual Branch? Branch { get; set; }

    public virtual ICollection<SalesOrder> SalesOrders { get; set; } = new List<SalesOrder>();

    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}