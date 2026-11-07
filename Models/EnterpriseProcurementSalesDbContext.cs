using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseSystem.Models;

public partial class EnterpriseProcurementSalesDbContext : DbContext
{
    public EnterpriseProcurementSalesDbContext()
    {
    }

    public EnterpriseProcurementSalesDbContext(DbContextOptions<EnterpriseProcurementSalesDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<ApplicationUser> ApplicationUsers { get; set; }

    public virtual DbSet<Branch> Branches { get; set; }

    public virtual DbSet<BranchInventory> BranchInventories { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<CreditNote> CreditNotes { get; set; }

    public virtual DbSet<CreditNoteItem> CreditNoteItems { get; set; }

    public virtual DbSet<Customer> Customers { get; set; }

    public virtual DbSet<InvoiceItem> InvoiceItems { get; set; }

    public virtual DbSet<Product> Products { get; set; }

    public virtual DbSet<PurchaseOrder> PurchaseOrders { get; set; }

    public virtual DbSet<PurchaseOrderItem> PurchaseOrderItems { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<SalesInvoice> SalesInvoices { get; set; }

    public virtual DbSet<SalesOrder> SalesOrders { get; set; }

    public virtual DbSet<SalesOrderItem> SalesOrderItems { get; set; }

    public virtual DbSet<UserRole> UserRoles { get; set; }

    public virtual DbSet<Vendor> Vendors { get; set; }

    public virtual DbSet<VendorInvoice> VendorInvoices { get; set; }

    public virtual DbSet<Warehouse> Warehouses { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlServer("Server=REVISION-PC\\SQLEXPRESS;Database=EnterpriseProcurementSalesDb;Trusted_Connection=True;TrustServerCertificate=True;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Applicat__3214EC071B82A908");

            entity.Property(e => e.Email).HasMaxLength(150);
            entity.Property(e => e.FullName).HasMaxLength(150);

            entity.HasOne(d => d.Branch).WithMany(p => p.ApplicationUsers)
                .HasForeignKey(d => d.BranchId)
                .HasConstraintName("FK_Users_Branches");
        });

        modelBuilder.Entity<Branch>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Branches__3214EC07AB9A5D7C");

            entity.Property(e => e.Location).HasMaxLength(250);
            entity.Property(e => e.Name).HasMaxLength(150);
        });

        modelBuilder.Entity<BranchInventory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__BranchIn__3214EC07042E19BB");

            entity.ToTable("BranchInventory");

            entity.HasIndex(e => new { e.WarehouseId, e.ProductId }, "UQ_Warehouse_Product").IsUnique();

            entity.HasOne(d => d.Product).WithMany(p => p.BranchInventories)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Inventory_Products");

            entity.HasOne(d => d.Warehouse).WithMany(p => p.BranchInventories)
                .HasForeignKey(d => d.WarehouseId)
                .HasConstraintName("FK_Inventory_Warehouses");
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Categori__3214EC07DC3D805F");

            entity.Property(e => e.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<CreditNote>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__CreditNo__3214EC071C506C5A");

            entity.Property(e => e.Reason).HasMaxLength(250);
            entity.Property(e => e.RefundAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.ReturnDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.SalesInvoice).WithMany(p => p.CreditNotes)
                .HasForeignKey(d => d.SalesInvoiceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CreditNotes_Invoice");
        });

        modelBuilder.Entity<CreditNoteItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__CreditNo__3214EC07CC94DD2B");

            entity.Property(e => e.UnitPrice).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.CreditNote).WithMany(p => p.CreditNoteItems)
                .HasForeignKey(d => d.CreditNoteId)
                .HasConstraintName("FK_CNItems_CN");

            entity.HasOne(d => d.Product).WithMany(p => p.CreditNoteItems)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CNItems_Products");
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Customer__3214EC0705AAE0B2");

            entity.Property(e => e.Name).HasMaxLength(150);
            entity.Property(e => e.Phone).HasMaxLength(50);
        });

        modelBuilder.Entity<InvoiceItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__InvoiceI__3214EC07A31C5386");

            entity.Property(e => e.UnitPrice).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.Product).WithMany(p => p.InvoiceItems)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_InvoiceItems_Products");

            entity.HasOne(d => d.SalesInvoice).WithMany(p => p.InvoiceItems)
                .HasForeignKey(d => d.SalesInvoiceId)
                .HasConstraintName("FK_InvoiceItems_Invoice");
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Products__3214EC075766C3E1");

            entity.HasIndex(e => e.Sku, "UQ__Products__CA1ECF0DC00DC445").IsUnique();

            entity.Property(e => e.Name).HasMaxLength(150);
            entity.Property(e => e.Sku)
                .HasMaxLength(50)
                .HasColumnName("SKU");
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.Category).WithMany(p => p.Products)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Products_Categories");
        });

        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Purchase__3214EC07626C0F0D");

            entity.Property(e => e.OrderDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValue("Pending");

            entity.HasOne(d => d.Branch).WithMany(p => p.PurchaseOrders)
                .HasForeignKey(d => d.BranchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PO_Branches");

            entity.HasOne(d => d.Vendor).WithMany(p => p.PurchaseOrders)
                .HasForeignKey(d => d.VendorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PO_Vendors");
        });

        modelBuilder.Entity<PurchaseOrderItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Purchase__3214EC073EB129A4");

            entity.Property(e => e.UnitCost).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.Product).WithMany(p => p.PurchaseOrderItems)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_POItems_Products");

            entity.HasOne(d => d.PurchaseOrder).WithMany(p => p.PurchaseOrderItems)
                .HasForeignKey(d => d.PurchaseOrderId)
                .HasConstraintName("FK_POItems_PO");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Roles__3214EC07D9100AFC");

            entity.Property(e => e.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<SalesInvoice>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__SalesInv__3214EC07E436EFE4");

            entity.HasIndex(e => e.InvoiceNumber, "UQ__SalesInv__D776E9811FAF62D5").IsUnique();

            entity.Property(e => e.InvoiceDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.InvoiceNumber).HasMaxLength(50);
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.SalesOrder).WithMany(p => p.SalesInvoices)
                .HasForeignKey(d => d.SalesOrderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SalesInvoices_SO");
        });

        modelBuilder.Entity<SalesOrder>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__SalesOrd__3214EC075480D12D");

            entity.Property(e => e.OrderDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValue("Pending");
            entity.Property(e => e.UserId).HasMaxLength(450);

            entity.HasOne(d => d.Branch).WithMany(p => p.SalesOrders)
                .HasForeignKey(d => d.BranchId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SO_Branches");

            entity.HasOne(d => d.Customer).WithMany(p => p.SalesOrders)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SO_Customers");

            entity.HasOne(d => d.User).WithMany(p => p.SalesOrders)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SO_Users");
        });

        modelBuilder.Entity<SalesOrderItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__SalesOrd__3214EC0780079739");

            entity.Property(e => e.UnitPrice).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.Product).WithMany(p => p.SalesOrderItems)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SOItems_Products");

            entity.HasOne(d => d.SalesOrder).WithMany(p => p.SalesOrderItems)
                .HasForeignKey(d => d.SalesOrderId)
                .HasConstraintName("FK_SOItems_SO");
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__UserRole__3214EC076CECB88B");

            entity.HasIndex(e => new { e.UserId, e.RoleId }, "UQ_User_Role").IsUnique();

            entity.HasOne(d => d.Role).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.RoleId)
                .HasConstraintName("FK_UserRoles_Roles");

            entity.HasOne(d => d.User).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_UserRoles_Users");
        });

        modelBuilder.Entity<Vendor>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Vendors__3214EC072684A674");

            entity.Property(e => e.ContactInfo).HasMaxLength(250);
            entity.Property(e => e.Name).HasMaxLength(150);
        });

        modelBuilder.Entity<VendorInvoice>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__VendorIn__3214EC0745F890F6");

            entity.Property(e => e.InvoiceNumber).HasMaxLength(50);
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValue("Unpaid");
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.PurchaseOrder).WithMany(p => p.VendorInvoices)
                .HasForeignKey(d => d.PurchaseOrderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_VendorInvoices_PO");
        });

        modelBuilder.Entity<Warehouse>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Warehous__3214EC0714CF4842");

            entity.Property(e => e.Location).HasMaxLength(250);
            entity.Property(e => e.Name).HasMaxLength(150);

            entity.HasOne(d => d.Branch).WithMany(p => p.Warehouses)
                .HasForeignKey(d => d.BranchId)
                .HasConstraintName("FK_Warehouses_Branches");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
