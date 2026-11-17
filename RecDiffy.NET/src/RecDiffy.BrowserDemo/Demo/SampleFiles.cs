using System.Text;

namespace RecDiffy.BrowserDemo.Demo;

/// <summary>A broker's trade confirms and a fund's blotter, built into the page so the sample needs no download.</summary>
public static class SampleFiles
{
    private const string Confirms = """
        ConfirmId,TradeRef,Isin,Side,Quantity,Price,NetAmount
        C-1001,T-5001,US0378331005,B,1000,189.25,189250.00
        C-1002,T-5002,US5949181045,S,500,412.10,206050.00
        C-1003,T-5003,US88160R1014,B,250,245.80,61450.00
        C-1004,T-5004,US02079K3059,B,300,171.55,51465.00
        C-1005,T-5005,US0231351067,S,120,201.40,24168.00
        C-1006,T-5006,US30303M1027,B,400,590.00,236000.00
        C-1007,T-5007,US46625H1005,S,1500,214.30,321450.00
        C-1008,T-5008,US67066G1040,B,800,131.40,105120.00
        C-1009,T-5010,US1912161007,B,2000,62.40,124800.00
        C-1010,T-5011,US7427181091,S,900,165.20,148680.00
        """;

    private const string Blotter = """
        TradeId,Isin,Side,Qty,Price,NetAmount
        T-5001,US0378331005,B,1000,189.25,189250.00
        T-5002,US5949181045,S,500,412.10,206049.99
        T-5003,US88160R1014,B,200,245.80,49160.00
        T-5004,US02079K3059,B,300,171.55,51465.00
        T-5005,US0231351067,S,120,201.40,24168.00
        T-5006,US30303M1027,B,400,590.10,236040.00
        T-5007,US46625H1005,S,1500,214.30,321450.00
        T-5008,US67066G1040,B,800,131.40,105120.00
        T-5009,US92826C8394,S,650,280.00,182000.00
        T-5011,US7427181091,S,900,165.20,148680.00
        """;

    public static LoadedFile Left() => LoadedFile.Read("broker-confirms.csv", Encoding.UTF8.GetBytes(Confirms));

    public static LoadedFile Right() => LoadedFile.Read("fund-blotter.csv", Encoding.UTF8.GetBytes(Blotter));

    public static RecSetup Setup { get; } = new(
        [new ColumnPair("TradeRef", "TradeId")],
        [
            new ColumnPair("Isin", "Isin"),
            new ColumnPair("Side", "Side"),
            new ColumnPair("Quantity", "Qty", Number: true),
            new ColumnPair("Price", "Price", Number: true, Tolerance: 0.0001m),
            new ColumnPair("NetAmount", "NetAmount", Number: true, Tolerance: 0.01m),
        ]);
}
