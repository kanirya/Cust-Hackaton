# Test persona: Imran Sheikh (EXT777) — high-risk demo

CNIC to search after import: **42101-***77** (or name "Imran Sheikh").

All 7 files use the same `personId` = EXT777, which links every record to one
identity/CNIC. Import them in order via Gov Data Sandbox → Dataset Feed Console.

## How to import each file
For EACH file below:
1. Set the **Dataset** dropdown to the matching type (the type is in the file name).
2. Keep **Format = CSV**.
3. Click **Choose File** and pick the CSV.
4. Click **Feed dataset**.

On the LAST file (7-travel.csv), also tick **Run risk pipeline** before
Feed dataset so the case is scored.

| Order | File              | Dataset dropdown |
|-------|-------------------|------------------|
| 1     | 1-identity.csv    | identity         |
| 2     | 2-tax.csv         | tax              |
| 3     | 3-vehicle.csv     | vehicle          |
| 4     | 4-property.csv    | property         |
| 5     | 5-utility.csv     | utility          |
| 6     | 6-business.csv    | business         |
| 7     | 7-travel.csv      | travel           |

## Then
Type `42101-***77` in the top search bar and press Enter → the CNIC
investigation aggregates the vehicle, property, utility, business, travel, and
tax records and runs the Claude analysis.

Why it flags high-risk: declared income is PKR 0 (Non-Filer) while linked
assets include a ~PKR 65M vehicle, ~PKR 90M property, ~PKR 450k/mo utility
bills, a company directorship, and ~PKR 6M travel spend.
