import { ArrayMaxSize, ArrayMinSize, IsArray, IsInt, IsUUID, Max, Min, ValidateNested } from 'class-validator';
import { Type } from 'class-transformer';
class RecipeComponentDto { @IsUUID('4') ingredientItemId!: string; @IsInt() @Min(1) @Max(Number.MAX_SAFE_INTEGER) quantityScaled!: number; }
export class SaveRecipeDto {
  @IsInt() @Min(1) @Max(Number.MAX_SAFE_INTEGER) outputScaled!: number;
  @IsInt() @Min(0) expectedVersion!: number;
  @IsArray() @ArrayMinSize(1) @ArrayMaxSize(100) @ValidateNested({ each: true }) @Type(() => RecipeComponentDto)
  components!: RecipeComponentDto[];
}
