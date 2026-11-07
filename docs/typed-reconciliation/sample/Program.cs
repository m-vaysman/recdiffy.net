using System.Text;

// Runs every example in docs/typed-reconciliation/README.md.
//   dotnet run --project docs/typed-reconciliation/sample -- docs/typed-reconciliation/data
Console.OutputEncoding = Encoding.UTF8;
var folder = args.Length > 0 ? args[0] : "data";
Directory.CreateDirectory(folder);
if (!File.Exists(Path.Combine(folder, "broker-confirms.csv")) || !File.Exists(Path.Combine(folder, "blotter.xlsx")))
    MockFiles.Write(folder);

Finance.Run(folder);
Logistics.Run();
Retail.Run();
Healthcare.Run();
