namespace Ubp.Core.Models;

/// <summary>صف من عرض bi.sales_daily (قراءة فقط).</summary>
public sealed record BiSalesDailyRow(
    DateOnly Day,
    Guid BranchId,
    int InvoiceCount,
    int ReturnCount,
    decimal NetTotal,
    decimal TaxTotal,
    decimal GrossTotal,
    decimal CogsTotal);

/// <summary>صف من عرض bi.stock_value_by_warehouse.</summary>
public sealed record BiStockValueRow(
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    decimal OnHand,
    decimal InventoryValue);
