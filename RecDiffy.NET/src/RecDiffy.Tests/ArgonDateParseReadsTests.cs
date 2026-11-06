namespace RecDiffy.Tests;

public class ArgonDateParseReadsTests
{
    [Fact]
    public void DateParse_rule_with_formatBy_declares_read_dependency_so_compile_rejects_read_before_write()
    {
        var exception = Assert.Throws<ProfileCompileException>(() =>
            Reconciler.Compile(ProfileDocument.Parse("""
                {
                  "formatVersion": 1,
                  "profileId": "date-formatby-read-before-write",
                  "version": "1.0.0",
                  "fields": [
                    { "name": "Counterparty", "type": "Identifier", "role": "Key" },
                    { "name": "TradeDate", "type": "Date", "role": "Key" }
                  ],
                  "normalization": {
                    "left": {
                      "parse": [
                        { "rule": "date.parse", "from": "Trade_Date", "to": "TradeDate", "formatBy": "Counterparty" }
                      ]
                    }
                  },
                  "paths": [ { "id": "only", "route": { "rule": "route.all" } } ]
                }
                """), RuleRegistry.CreateCore()));

        Assert.Contains(exception.Errors, error => error.Code == "FieldReadBeforeWrite");
    }
}
