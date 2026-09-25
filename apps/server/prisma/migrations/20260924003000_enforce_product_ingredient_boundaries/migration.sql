-- Products are sellable catalog entries; ingredients are raw materials. Remove
-- only reference-price rows that pointed at ingredients. Historical invoice
-- lines and inventory movements are intentionally untouched.
DELETE FROM "cafe_price_revisions" revision
USING "items" item
WHERE revision."item_id" = item."id"
  AND item."kind" = 'INGREDIENT';

DELETE FROM "cafe_item_prices" price
USING "items" item
WHERE price."item_id" = item."id"
  AND item."kind" = 'INGREDIENT';

DELETE FROM "site_retail_price_revisions" revision
USING "items" item
WHERE revision."item_id" = item."id"
  AND item."kind" = 'INGREDIENT';

DELETE FROM "site_retail_prices" price
USING "items" item
WHERE price."item_id" = item."id"
  AND item."kind" = 'INGREDIENT';
