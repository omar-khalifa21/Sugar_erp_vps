import { Body, Controller, Post } from '@nestjs/common';
import { Public } from '../common/public.decorator';
import { AuthService } from './auth.service';
import { LoginDto } from './login.dto';

@Controller('auth')
export class AuthController {
  constructor(private readonly authService: AuthService) {}

  @Public()
  @Post('login')
  login(@Body() input: LoginDto): Promise<{ access_token: string; token_type: 'Bearer' }> {
    return this.authService.login(input);
  }
}
