Copy and paste the prompt below into any other model (Claude, ChatGPT, Grok, etc.). If the model supports Python code execution, it will generate and hand you the downloadable `.xlsx` and `.json` files directly; if it does not have a sandbox, it will output the complete Python generator script for you to run locally.

```markdown
Generate a realistic institutional back-office trade reconciliation test dataset for a .NET record-diffing library (RecDiffy.NET). If you have a Python code execution environment, run a Python script using `openpyxl` to produce two downloadable files:
1. `RecDiffy_Trade_Reconciliation_Dataset.xlsx` (an Excel workbook with 3 tabs)
2. `RecDiffy_Hidden_Test_Oracle.json` (a companion ground-truth JSON file for automated unit tests)

If you cannot generate downloadable files directly, output the complete, self-contained Python script to generate both files.

### Core Dataset Architecture (300 Internal vs. 200 Counterparty Lines)
Simulate what a hedge fund or bank back office reconciles daily between its internal Order Management System (OMS) and incoming street feeds from Prime Brokers and OTC Dealers across four asset classes: **Bonds, Equities, FX Forwards, and Swaps**.

* **Tab 1: `Internal_Trades` (Exactly 300 rows, shuffled)**
  * Our clean, canonical internal book of record.
  * Columns: `Internal_Trade_ID`, `UTI_Exec_ID`, `Parent_Link_ID`, `Leg_Structure`, `Portfolio_Book`, `Trade_Date`, `Settle_Date`, `Asset_Class` (`Bond`, `Equity`, `Forward`, `Swap`), `Counterparty_Code` (e.g., `GSCO`, `JPM`, `MSCO`, `BAML`, `CITI`, `BARC`, `UBS`, `CITADEL`), `Counterparty_Legal_Name`, `Side` (`BUY`, `SELL`, `PAY_FIXED`, `REC_FIXED`), `Ticker`, `ISIN`, `CUSIP`, `SEDOL`, `Instrument_Description`, `Quantity_Notional` (always positive unsigned), `Clean_Price_Rate` (decimal), `Currency`, `Principal_Gross`, `Accrued_Interest`, `Commission_Fees`, `Net_Settlement_Amount`, `Coupon_Rate`, `Maturity_Date`.

* **Tab 2: `Counterparty_Trades` (Exactly 200 rows, shuffled)**
  * Aggregated external street feed with realistic representational noise and schema differences.
  * Columns: `Cpty_Report_ID`, `Shared_Market_Exec_Ref`, `Trans_Lifecycle_Action` (`NEW`, `CANC`, `CORR`), `Cancels_Prior_Report_ID`, `Reporting_Counterparty_Name`, `Counterparty_LEI`, `Reporting_Perspective` (`PB_CLIENT_VIEW` vs. `DEALER_BILATERAL_VIEW`), `Trade_Date`, `Value_Settle_Date`, `Product_Type`, `Dir_Ind`, `Primary_Security_ID`, `Security_ID_Source` (`ISIN`, `CUSIP`, `SEDOL`, or `TICKER`), `Security_Text`, `Reported_Qty_Notional`, `Exec_Price_Rate`, `Price_Quote_Convention`, `Trade_Ccy`, `Reported_Accrued_Int`, `Net_Cash_Amount`, `Coupon`, `Maturity`.

* **Tab 3: `Known_Exact_Matches` (120 rows + Summary Table)**
  * **CRITICAL REQUIREMENT:** This tab must list ONLY the **120 predetermined 1:1 pairs** (`Internal_Trade_ID` <-> `Cpty_Report_ID`) that are 100% economically identical after normalization.
  * The remaining **180 Internal rows and 80 Counterparty rows** must NOT be labeled in the Excel file—they must remain "blind" in Tabs 1 and 2 so the reconciliation engine has to discover them without cheating.

---

### Required Street Normalization Challenges (Inside the 120 Known Exact Matches)
Even though the 120 pairs in Tab 3 match 100% economically, the counterparty rows in Tab 2 must test normalization rules:
1. **Counterparty Naming Drift:** Internal uses `GSCO` or `JPM`, while the street sends desk aliases like `GS_NY_PRIME`, `Goldman Sachs International`, `JPMS_CLEAR`, `BOFA_MLPF&S`, `CGMI_NY`, `BARCAP_LDN`, etc.
2. **Direction & Quantity Sign Variations by Broker:**
   * Some brokers send `B` / `S` / `P` / `R` with unsigned positive quantities.
   * Some brokers leave `Dir_Ind` **completely blank** and send **negative quantities** (`-500,000`) to represent sells or pay-fixed legs.
   * Some brokers send *both* `S` and a negative quantity (`-1,200`).
   * Others send `BOT` / `SLD` / `PAY` / `REC` or lowercase `buy` / `sell`.
3. **Cross-Security Identifier Lookup:** Internal rows have `Ticker`, `ISIN`, `CUSIP`, and `SEDOL`, while the counterparty only sends a single `Primary_Security_ID` (picking one based on broker preference).
4. **Missing Shared Execution IDs:** Half of the 120 pairs share a `UTI_Exec_ID` / `Shared_Market_Exec_Ref`; the other half have different internal/external refs so the engine must match on natural business keys.
5. **US Treasury 32nds Pricing (12 pairs):** Internal records decimal clean price (e.g., `98.75` or `98.359375`), while the counterparty sends fractional 32nds string notation (`98-24` or `98-11+`) with `Price_Quote_Convention = 32NDS_UST`.
6. **Dirty Price Bond Quotes (8 pairs):** Counterparty reports Dirty Price (`Clean Price + Accrued/Notional * 100`) in `Exec_Price_Rate` while agreeing to the exact penny on `Net_Cash_Amount`.
7. **Dealer Bilateral Perspective Inversion (15 OTC pairs):** Marked `DEALER_BILATERAL_VIEW`, where an internal `BUY` or `PAY_FIXED` is reported from the dealer's perspective as `SLD` or `REC` (or opposite sign).

---

### Required Blind Discovery Scenarios (180 Internal Rows & 80 Counterparty Rows)
Embed the following unlabeled test cases across the remaining rows in Tabs 1 and 2, and record their exact ground-truth mapping in `RecDiffy_Hidden_Test_Oracle.json`:
1. **Within-Tolerance Near-Matches (18 Internal <-> 18 Counterparty):** 1:1 pairs with micro-differences in bond accrued interest ($\pm \$0.40$ to $\$1.20$), 4th-decimal FX rates, or penny fee rounding.
2. **True Economic Field Breaks (20 Internal <-> 20 Counterparty):** 1:1 pairs with a busted execution price, an 80% partial-fill quantity mismatch, a T+1 vs. T+2 settlement date lag, or a $450 commission break.
3. **N-to-1 Allocation Splits (36 Internal <-> 12 Counterparty):** 12 groups where the hedge fund books 3 internal sub-fund allocations (`MACRO_RATES`, `STAT_ARB_EQ`, `CREDIT_OPP`) that sum up to 1 single Prime Broker block execution.
4. **Multi-Leg Swap/FX Representation (16 Internal <-> 8 Counterparty):** 8 trades where the internal OMS books 2 linked rows (`LEG_1_PAY` and `LEG_2_RECEIVE` sharing a `Parent_Link_ID`), while the counterparty reports 1 combined net row.
5. **Cancel & Correct Lifecycle Chains (5 Internal <-> 15 Counterparty):** 5 trades where the counterparty sends 3 lines: an original busted `NEW` row, a reversing `CANC` row referencing `Cancels_Prior_Report_ID`, and a rebooked `CORR` row that matches the internal trade once collapsed.
6. **Unmatched Internal Orphans (85 Internal <-> 0 Counterparty):** Unconfirmed trades or DKs missing from the street file.
7. **Unmatched Counterparty Alleged Trades (0 Internal <-> 7 Counterparty):** Street trades missing from the internal OMS.

Verify that the row counts equal **exactly 300 in Tab 1, 200 in Tab 2, and 120 in Tab 3** before saving the files.

```