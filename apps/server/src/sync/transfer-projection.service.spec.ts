import { HttpException } from "@nestjs/common";
import { ConfigService } from "@nestjs/config";
import { DeviceProfile, StockLocation } from "@prisma/client";
import { TransferProjectionService } from "./transfer-projection.service";

const branchSiteId = "9cf1a57b-0e16-4e3b-b2f4-381ab3f5cff4";
const kitchenSiteId = "a2df98c0-20d8-4a45-80e0-b344647c59b5";
const documentId = "276e2f35-0b47-4857-85a7-003e2707f669";
const shiftId = "52307ea7-10ac-4a20-ab96-41f55b797358";
const productId = "190de9e4-7a02-4e19-8b05-12b60488c26b";
const ingredientId = "7645233b-37ce-4923-93aa-03f4dfed7b59";

function event(deduct: boolean) {
  return {
    id: documentId,
    device_sequence: 1,
    event_type: "manual_incoming.posted",
    schema_version: 1,
    occurred_at: "2026-09-27T10:00:00.000Z",
    dependencies: [],
    content_hash: "a".repeat(64),
    payload: {
      document_id: documentId,
      site_id: branchSiteId,
      shift_id: shiftId,
      source: "MANUAL_BRANCH_ENTRY",
      reason: "توريد يدوي للاختبار",
      location: "SALEABLE",
      deduct_kitchen_ingredients: deduct,
      lines: [{ item_id: productId, quantity_scaled: "5" }],
    },
  };
}

describe("TransferProjectionService manual incoming", () => {
  const service = new TransferProjectionService({
    getOrThrow: () => "test-jwt-secret",
  } as unknown as ConfigService);

  it("posts branch stock without reading recipes or touching kitchen stock when deduction is off", async () => {
    const recipeFindMany = jest.fn();
    const stockUpsert = jest.fn().mockResolvedValue(undefined);
    const tx = {
      item: { findMany: jest.fn().mockResolvedValue([{ id: productId }]) },
      recipe: { findMany: recipeFindMany },
      stockBalance: { upsert: stockUpsert },
    };

    await service.apply(
      tx as never,
      event(false),
      branchSiteId,
      DeviceProfile.BRANCH_TYPE_1,
    );

    expect(recipeFindMany).not.toHaveBeenCalled();
    expect(stockUpsert).toHaveBeenCalledTimes(1);
    expect(stockUpsert).toHaveBeenCalledWith(
      expect.objectContaining({
        where: {
          siteId_itemId_location: {
            siteId: branchSiteId,
            itemId: productId,
            location: StockLocation.SALEABLE,
          },
        },
      }),
    );
  });

  it("deducts only the selected products recipe requirements and posts branch stock in the same projection", async () => {
    let stockUpdateInput: unknown;
    let ingredientCreateInput: unknown;
    const stockUpdate = jest.fn((input: unknown) => { stockUpdateInput = input; return Promise.resolve(); });
    const stockUpsert = jest.fn().mockResolvedValue(undefined);
    const ingredientCreate = jest.fn((input: unknown) => { ingredientCreateInput = input; return Promise.resolve(); });
    const tx = {
      item: { findMany: jest.fn().mockResolvedValue([{ id: productId }]) },
      site: { findMany: jest.fn().mockResolvedValue([{ id: kitchenSiteId }]) },
      recipe: {
        findMany: jest.fn().mockResolvedValue([
          {
            productItemId: productId,
            outputScaled: 1n,
            product: { nameAr: "كيكة شوكولاتة" },
            components: [
              { ingredientItemId: ingredientId, quantityScaled: 4n },
            ],
          },
        ]),
      },
      stockBalance: {
        findUnique: jest
          .fn()
          .mockResolvedValue({
            quantityScaled: 100n,
            inventoryCostMinor: 5000n,
            item: { nameAr: "بيض" },
          }),
        update: stockUpdate,
        upsert: stockUpsert,
      },
      ingredientStockTransaction: { create: ingredientCreate },
    };

    await service.apply(
      tx as never,
      event(true),
      branchSiteId,
      DeviceProfile.BRANCH_TYPE_1,
    );

    expect(stockUpdateInput).toMatchObject({
      data: { quantityScaled: { decrement: 20n } },
    });
    expect(ingredientCreateInput).toMatchObject({
      data: {
        siteId: kitchenSiteId,
        itemId: ingredientId,
        sourceEventId: documentId,
        kind: "MANUAL_INCOMING_CONSUMPTION",
        quantityDeltaScaled: -20n,
      },
    });
    expect(stockUpsert).toHaveBeenCalledTimes(1);
  });

  it("names the selected product and leaves both projections untouched when its recipe is missing", async () => {
    const stockUpsert = jest.fn();
    const tx = {
      item: {
        findMany: jest
          .fn()
          .mockResolvedValueOnce([{ id: productId }])
          .mockResolvedValueOnce([{ nameAr: "كيكة شوكولاتة" }]),
      },
      site: { findMany: jest.fn().mockResolvedValue([{ id: kitchenSiteId }]) },
      recipe: { findMany: jest.fn().mockResolvedValue([]) },
      stockBalance: { upsert: stockUpsert },
    };

    try {
      await service.apply(
        tx as never,
        event(true),
        branchSiteId,
        DeviceProfile.BRANCH_TYPE_1,
      );
      throw new Error("Expected missing recipe validation to fail");
    } catch (caught) {
      expect(caught).toBeInstanceOf(HttpException);
      const response: unknown = (caught as HttpException).getResponse();
      expect(response).toEqual({
        code: "RECIPE_REQUIRED",
        message: "لا توجد وصفة فعالة للأصناف: كيكة شوكولاتة",
        retryable: true,
      });
    }
    expect(stockUpsert).not.toHaveBeenCalled();
  });
});
