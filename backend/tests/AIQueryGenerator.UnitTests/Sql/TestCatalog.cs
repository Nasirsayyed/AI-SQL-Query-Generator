using AIQueryGenerator.Application.Schema;
using AIQueryGenerator.Application.Sql;
using AIQueryGenerator.Domain.Enums;

namespace AIQueryGenerator.UnitTests.Sql;

/// <summary>Catalog mirroring the AIQueryDemoDB sample database, with Customers.Email restricted and dbo.Secrets denied.</summary>
internal static class TestCatalog
{
    public static AuthorizedCatalog Create()
    {
        var id = 0;
        CatalogTable T(string name, bool allowed, string[] columns, params string[] denied) => new()
        {
            TableId = ++id,
            Schema = "dbo",
            Name = name,
            ObjectType = DbObjectType.Table,
            IsAllowed = allowed,
            AllColumns = columns.ToDictionary(c => c, c => new CatalogColumn(c, "int", false, false, null), StringComparer.OrdinalIgnoreCase),
            AllowedColumns = new HashSet<string>(columns.Except(denied), StringComparer.OrdinalIgnoreCase),
        };

        return new AuthorizedCatalog("AIQueryDemoDB",
        [
            T("Customers", true, ["CustomerId", "CustomerName", "Email", "City", "Region", "CreatedDate"], "Email"),
            T("Products", true, ["ProductId", "ProductName", "Category", "UnitPrice", "ReorderLevel"]),
            T("Warehouses", true, ["WarehouseId", "WarehouseName", "City"]),
            T("Inventory", true, ["InventoryId", "ProductId", "WarehouseId", "AvailableQuantity", "ReservedQuantity", "LastUpdated"]),
            T("Orders", true, ["OrderId", "CustomerId", "OrderDate", "OrderStatus", "TotalAmount", "WarehouseId", "ShippedDate"]),
            T("OrderDetails", true, ["OrderDetailId", "OrderId", "ProductId", "Quantity", "UnitPrice", "LineTotal"]),
            T("Payments", true, ["PaymentId", "OrderId", "PaymentDate", "PaymentAmount", "PaymentStatus"]),
            T("Secrets", false, ["SecretId", "Value"]),
        ], []);
    }

    public static SqlValidationContext Context(IReadOnlyCollection<string>? parameters = null, SqlValidationOptions? options = null) => new()
    {
        Catalog = Create(),
        Options = options ?? new SqlValidationOptions(),
        ProvidedParameterNames = parameters,
    };
}
