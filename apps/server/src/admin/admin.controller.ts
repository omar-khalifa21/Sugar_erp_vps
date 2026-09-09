import { Body, Controller, Delete, Get, Param, ParseUUIDPipe, Patch, Post, Put, Req } from '@nestjs/common';
import type { Request } from 'express';
import { Roles } from '../common/roles.decorator';
import { AdminService } from './admin.service';
import { CreateStockAdjustmentDto } from './create-stock-adjustment.dto';
import { DecideConflictDto } from './decide-conflict.dto';
import { CreateCafeCustomerDto } from './create-cafe-customer.dto';
import { SetCafePriceDto } from './set-cafe-price.dto';
import { UpdateCafeCustomerDto } from './update-cafe-customer.dto';

interface AdminRequest extends Request {
  user: { id: string; roles: string[] };
}

@Roles('ADMIN')
@Controller('admin/sites/:siteId')
export class AdminController {
  constructor(private readonly admin: AdminService) {}

  @Get('overview')
  overview(@Param('siteId', ParseUUIDPipe) siteId: string) {
    return this.admin.siteOverview(siteId);
  }

  @Get('stock-adjustments')
  adjustments(@Param('siteId', ParseUUIDPipe) siteId: string) {
    return this.admin.listAdjustments(siteId);
  }

  @Post('stock-adjustments')
  requestAdjustment(
    @Param('siteId', ParseUUIDPipe) siteId: string,
    @Req() request: AdminRequest,
    @Body() input: CreateStockAdjustmentDto,
  ) {
    return this.admin.requestStockAdjustment(siteId, request.user.id, input);
  }
}

@Roles('ADMIN')
@Controller('admin')
export class AdminBusinessController {
  constructor(private readonly admin: AdminService) {}

  @Get('cafe/overview')
  cafeOverview() {
    return this.admin.cafeOverview();
  }

  @Post('cafe/customers')
  createCafeCustomer(@Body() input: CreateCafeCustomerDto) {
    return this.admin.createCafeCustomer(input);
  }

  @Patch('cafe/customers/:id')
  updateCafeCustomer(
    @Param('id', ParseUUIDPipe) id: string,
    @Body() input: UpdateCafeCustomerDto,
  ) {
    return this.admin.updateCafeCustomer(id, input);
  }

  @Delete('cafe/customers/:id')
  archiveCafeCustomer(@Param('id', ParseUUIDPipe) id: string) {
    return this.admin.archiveCafeCustomer(id);
  }

  @Put('cafe/customers/:customerId/prices/:itemId')
  setCafePrice(
    @Param('customerId', ParseUUIDPipe) customerId: string,
    @Param('itemId', ParseUUIDPipe) itemId: string,
    @Body() input: SetCafePriceDto,
  ) {
    return this.admin.setCafePrice(customerId, itemId, input);
  }

  @Get('kitchen/overview')
  kitchenOverview() {
    return this.admin.kitchenOverview();
  }

  @Get('conflicts')
  conflicts() {
    return this.admin.conflicts();
  }

  @Post('conflicts/:id/decision')
  decideConflict(
    @Param('id', ParseUUIDPipe) id: string,
    @Req() request: AdminRequest,
    @Body() input: DecideConflictDto,
  ) {
    return this.admin.decideConflict(id, request.user.id, input);
  }
}
