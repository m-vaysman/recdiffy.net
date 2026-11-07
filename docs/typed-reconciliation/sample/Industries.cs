using RecDiffy;

// ---------- Logistics: carrier deliveries (API) vs dock receipts (warehouse database) ----------

public sealed record CarrierDelivery(string TrackingNo, string PoNumber, string Sku, int Cartons, decimal WeightKg, DateOnly DeliveredOn);

public sealed record DockReceipt(string ReceiptNo, string? Tracking, string Po, string Sku, int CartonsReceived, decimal Kg, DateTime ReceivedAt);

public static class Logistics
{
    public static void Run()
    {
        Console.WriteLine("=== Logistics: carrier deliveries vs dock receipts ===");
        CarrierDelivery[] deliveries =
        [
            new("1Z999AA10123456784", "PO-7781", "CHAIR-01", 12, 240.0m, new(2026, 11, 3)),
            new("1Z999AA10123456785", "PO-7781", "DESK-02", 8, 410.0m, new(2026, 11, 3)),
            new("1Z999AA10123456786", "PO-7782", "LAMP-03", 20, 96.0m, new(2026, 11, 4)),
            new("1Z999AA10123456787", "PO-7783", "SHELF-04", 5, 150.0m, new(2026, 11, 4)),
            new("1Z999AA10123456788", "PO-7784", "RUG-05", 3, 45.0m, new(2026, 11, 5)),
        ];
        DockReceipt[] receipts =
        [
            new("RCV-9001", "1Z999AA10123456784", "PO-7781", "CHAIR-01", 12, 240.0m, new(2026, 11, 3, 14, 10, 0)),
            new("RCV-9002", "1Z999AA10123456785", "PO-7781", "DESK-02", 8, 409.6m, new(2026, 11, 3, 14, 25, 0)),
            new("RCV-9003", "1Z999AA10123456786", "PO-7782", "LAMP-03", 18, 86.4m, new(2026, 11, 4, 9, 5, 0)),
            new("RCV-9004", "1Z999AA1012345678", "PO-7783", "SHELF-04", 5, 150.0m, new(2026, 11, 4, 11, 40, 0)),
            new("RCV-9006", null, "PO-7790", "CHAIR-01", 4, 80.0m, new(2026, 11, 5, 16, 0, 0)),
        ];

        var result = RecBuilder.Between(deliveries, receipts)
            .MatchOn(delivery => delivery.TrackingNo, receipt => receipt.Tracking)
                .And(delivery => delivery.Sku, receipt => receipt.Sku)
            .ThenMatchOn(delivery => delivery.PoNumber, receipt => receipt.Po)
                .And(delivery => delivery.Sku, receipt => receipt.Sku)
            .Compare(delivery => delivery.Cartons, receipt => receipt.CartonsReceived)
            .Compare(delivery => delivery.WeightKg, receipt => receipt.Kg, tolerance: 0.5m)
            .Run();

        Report.Print(result, delivery => $"{delivery.TrackingNo} {delivery.Sku}", receipt => receipt.ReceiptNo);
        Console.WriteLine();
    }
}

// ---------- Retail payments: POS sales vs card processor settlement ----------

public sealed record PosSale(string SaleId, string AuthCode, string CardLast4, decimal Amount, DateOnly BusinessDate);

public sealed record Settlement(string ProcessorRef, string Auth, string Last4, decimal Gross, decimal Fee, DateOnly SettledOn);

public static class Retail
{
    public static void Run()
    {
        Console.WriteLine("=== Retail: POS sales vs card settlement ===");
        PosSale[] sales =
        [
            new("S-0001", "A1B2C3", "4242", 58.40m, new(2026, 11, 5)),
            new("S-0002", "D4E5F6", "1881", 120.00m, new(2026, 11, 5)),
            new("S-0003", "G7H8I9", "0005", 19.99m, new(2026, 11, 5)),
            new("S-0004", "G7H8I9", "0005", 19.99m, new(2026, 11, 5)),
            new("S-0005", "J1K2L3", "7777", 45.00m, new(2026, 11, 5)),
        ];
        Settlement[] settlements =
        [
            new("STL-001", "A1B2C3", "4242", 58.40m, 1.69m, new(2026, 11, 6)),
            new("STL-002", "D4E5F6", "1881", 132.00m, 3.83m, new(2026, 11, 6)),
            new("STL-003", "G7H8I9", "0005", 19.99m, 0.58m, new(2026, 11, 6)),
            new("STL-004", "M4N5O6", "3005", -60.00m, 0.00m, new(2026, 11, 6)),
        ];

        var result = RecBuilder.Between(sales, settlements)
            .MatchOn(sale => sale.AuthCode, settlement => settlement.Auth)
                .And(sale => sale.CardLast4, settlement => settlement.Last4)
            .Compare(sale => sale.Amount, settlement => settlement.Gross)
            .Run();

        Report.Print(result, sale => sale.SaleId, settlement => settlement.ProcessorRef);
        Console.WriteLine();
    }
}

// ---------- Healthcare billing: submitted claim lines vs payer remittance ----------

public sealed record ClaimLine(string ClaimId, string Cpt, DateOnly ServiceDate, decimal Billed, decimal ExpectedAllowed);

public sealed record RemitLine(string PayerClaimRef, string ProcedureCode, DateOnly DateOfService, decimal Allowed, decimal Paid);

public static class Healthcare
{
    public static void Run()
    {
        Console.WriteLine("=== Healthcare: claim lines vs payer remittance ===");
        ClaimLine[] claims =
        [
            new("CLM-3001", "99213", new(2026, 10, 14), 180.00m, 92.50m),
            new("CLM-3001", "85025", new(2026, 10, 14), 45.00m, 10.80m),
            new("CLM-3002", "99214", new(2026, 10, 15), 250.00m, 130.00m),
            new("CLM-3003", "93000", new(2026, 10, 16), 95.00m, 17.20m),
            new("CLM-3004", "99203", new(2026, 10, 20), 210.00m, 110.00m),
        ];
        RemitLine[] remittance =
        [
            new("CLM-3001", "99213", new(2026, 10, 14), 92.50m, 72.50m),
            new("CLM-3001", "85025", new(2026, 10, 14), 10.80m, 10.80m),
            new("CLM-3002", "99214", new(2026, 10, 15), 112.00m, 89.60m),
            new("CLM-3003", "93000", new(2026, 10, 16), 17.00m, 17.00m),
            new("CLM-2987", "99212", new(2026, 9, 30), 61.00m, 61.00m),
        ];

        var result = RecBuilder.Between(claims, remittance)
            .MatchOn(claim => claim.ClaimId, remit => remit.PayerClaimRef)
                .And(claim => claim.Cpt, remit => remit.ProcedureCode)
            .Compare(claim => claim.ServiceDate, remit => remit.DateOfService)
            .Compare(claim => claim.ExpectedAllowed, remit => remit.Allowed, tolerance: 1.00m)
            .Run();

        Report.Print(result, claim => $"{claim.ClaimId}/{claim.Cpt}", remit => $"{remit.PayerClaimRef}/{remit.ProcedureCode}");
        Console.WriteLine();
    }
}
