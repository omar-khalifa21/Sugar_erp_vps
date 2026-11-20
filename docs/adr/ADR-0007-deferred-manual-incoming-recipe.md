# ADR-0007 — Send Branch 1 orders while a receipt awaits its recipe

The owner approved posting a Branch 1 manual receipt before its missing recipe is entered. This supersedes the missing-recipe consequence in ADR-0003 for deduction-enabled manual receipts.

The VPS commits the immutable sync event, branch product balance, and a pending Kitchen deduction in one transaction. The pending record identifies the original receipt and the Kitchen responsible for its ingredients. Later device events, including Kitchen orders, keep their original sequence and IDs. Admin shows the receipt's ingredient deduction as pending.

The API retries pending deductions after restart and periodically while running. It uses the receipt quantities and all approved active recipes available at settlement. Stock and rounding checks apply to the entire receipt. A serializable transaction locks the pending row, posts the ingredient movements against the original event, and removes the pending row. A failed settlement remains pending. Existing immediate deductions continue when the receipt already has complete recipes.
