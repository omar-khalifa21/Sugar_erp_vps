CREATE TABLE "recipes" (
  "id" UUID NOT NULL,
  "product_item_id" UUID NOT NULL,
  "output_scaled" BIGINT NOT NULL,
  "version" INTEGER NOT NULL DEFAULT 1,
  "active" BOOLEAN NOT NULL DEFAULT true,
  "updated_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT "recipes_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "recipes_output_positive" CHECK ("output_scaled" > 0),
  CONSTRAINT "recipes_product_item_id_fkey" FOREIGN KEY ("product_item_id") REFERENCES "items"("id") ON DELETE RESTRICT ON UPDATE CASCADE
);
CREATE UNIQUE INDEX "recipes_product_item_id_key" ON "recipes"("product_item_id");
CREATE TABLE "recipe_components" (
  "id" UUID NOT NULL,
  "recipe_id" UUID NOT NULL,
  "ingredient_item_id" UUID NOT NULL,
  "quantity_scaled" BIGINT NOT NULL,
  CONSTRAINT "recipe_components_pkey" PRIMARY KEY ("id"),
  CONSTRAINT "recipe_components_quantity_positive" CHECK ("quantity_scaled" > 0),
  CONSTRAINT "recipe_components_recipe_id_fkey" FOREIGN KEY ("recipe_id") REFERENCES "recipes"("id") ON DELETE RESTRICT ON UPDATE CASCADE,
  CONSTRAINT "recipe_components_ingredient_item_id_fkey" FOREIGN KEY ("ingredient_item_id") REFERENCES "items"("id") ON DELETE RESTRICT ON UPDATE CASCADE
);
CREATE UNIQUE INDEX "recipe_components_recipe_id_ingredient_item_id_key" ON "recipe_components"("recipe_id", "ingredient_item_id");
CREATE INDEX "recipe_components_ingredient_item_id_idx" ON "recipe_components"("ingredient_item_id");
