import { Body, Controller, Post, Get, Req, Headers } from '@nestjs/common';
import { Public } from '../common/public.decorator';
import { AuthService } from './auth.service';
import { LoginDto } from './login.dto';
import { SignupDto } from './signup.dto';

@Controller('auth')
export class AuthController {
  constructor(private readonly authService: AuthService) {}

  @Get('me')
  session(@Req() request: { user: { id: string } }) {
    return this.authService.session(request.user.id);
  }

  @Post('desktop-authorization')
  desktopAuthorization(@Req() request: { user: { id: string } }, @Headers('x-device-id') deviceId: string,
    @Headers('x-device-secret') credential: string) {
    return this.authService.desktopAuthorization(request.user.id, deviceId, credential);
  }

  @Public()
  @Post('signup')
  signup(@Body() input: SignupDto): Promise<{ status: 'PENDING_PERMISSION' }> {
    return this.authService.signup(input);
  }

  @Public()
  @Post('login')
  login(@Body() input: LoginDto): Promise<{ access_token: string; token_type: 'Bearer'; status: string }> {
    return this.authService.login(input);
  }
}
